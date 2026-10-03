using System;using System.IO;using System.Linq;using System.Text;using System.Text.RegularExpressions;using System.Collections.Generic;using System.Threading;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 interface IDfuUsb:IDisposable {byte[] Control(byte type,byte request,ushort value,ushort index,byte[] data,int length);void Select(byte iface,byte alternate);}
 sealed class DfuProtocol {
  readonly IDfuUsb Usb;readonly Action<string> Log;readonly Action<int> Sleep;byte Interface;int TransferSize;string Memory;
  internal DfuProtocol(IDfuUsb usb,Action<string> log,Action<int> sleep=null){Usb=usb;Log=log;Sleep=sleep??Thread.Sleep;}
  byte[] In(byte request,ushort value,int length)=>Usb.Control(0xa1,request,value,Interface,new byte[0],length);
  void Out(byte request,ushort value,byte[] data)=>Usb.Control(0x21,request,value,Interface,data,data.Length);
  internal void Discover(){
   byte[] device=Usb.Control(0x80,6,0x0100,0,new byte[0],18);if(device.Length!=18||BitConverter.ToUInt16(device,8)!=0x0483||BitConverter.ToUInt16(device,10)!=0xdf11)throw new Exception("ST DFU USB 식별값이 아닙니다.");
   byte[] head=Usb.Control(0x80,6,0x0200,0,new byte[0],9);if(head.Length!=9)throw new Exception("DFU 구성 정보 오류");int total=BitConverter.ToUInt16(head,2);if(total<9||total>16384)throw new Exception("DFU 구성 크기 오류");
   byte[] config=Usb.Control(0x80,6,0x0200,0,new byte[0],total);if(config.Length!=total)throw new Exception("DFU 구성 정보 누락");
   int found=0,transfer=0;byte iface=0,alt=0;string memory="";
   for(int i=0;i<config.Length;){int size=config[i];if(size<2||i+size>config.Length)throw new Exception("잘못된 USB descriptor");
    if(config[i+1]==4&&size>=9&&config[i+5]==0xfe&&config[i+6]==1&&config[i+7]==2){
     byte number=config[i+8];if(number!=0){var raw=Usb.Control(0x80,6,(ushort)(0x0300|number),0x0409,new byte[0],255);if(raw.Length>=2&&raw[0]<=raw.Length&&raw[1]==3){string label=Encoding.Unicode.GetString(raw,2,raw[0]-2);if(label.StartsWith("@Internal Flash",StringComparison.OrdinalIgnoreCase)){found++;iface=config[i+2];alt=config[i+3];memory=label;}}}
    }
    if(config[i+1]==0x21&&size>=9){if((config[i+2]&3)!=3||BitConverter.ToUInt16(config,i+7)!=0x011a)throw new Exception("읽기/쓰기를 지원하는 STM32 DfuSe가 아닙니다.");transfer=BitConverter.ToUInt16(config,i+5);}
    i+=size;
   }
   if(found!=1||transfer<64||transfer>4096||transfer%4!=0)throw new Exception("내장 플래시 DFU 인터페이스가 없거나 모호합니다.");Interface=iface;TransferSize=transfer;Memory=memory;Usb.Select(iface,alt);Idle();Log("DFU: "+Memory+" · transfer "+TransferSize);
  }
  byte[] Status(){var s=In(3,0,6);if(s.Length!=6)throw new Exception("DFU 상태 응답 길이 오류");return s;}
  void Idle(){var s=Status();if(s[4]==10){Out(4,0,new byte[0]);s=Status();}if(s[4]==2)return;if(s[4]!=5&&s[4]!=9)throw new Exception("DFU가 대기 상태가 아닙니다: "+s[4]);Out(6,0,new byte[0]);s=Status();if(s[0]!=0||s[4]!=2)throw new Exception("DFU ABORT 실패");}
  void Poll(){for(int n=0;n<100;n++){var s=Status();if(s[0]!=0||s[4]==10)throw new Exception("DFU 오류 status="+s[0]+" state="+s[4]+". 쓰기 보호 해제를 자동 수행하지 않습니다.");if(s[4]==5)return;if(s[4]!=3&&s[4]!=4)throw new Exception("예상하지 못한 DFU 상태: "+s[4]);int wait=s[1]|s[2]<<8|s[3]<<16;if(wait>5000)throw new Exception("DFU poll timeout 범위 초과");Sleep(Math.Max(1,wait));}throw new TimeoutException("DFU 작업 대기 시간 초과");}
  void Special(byte command,uint address){Idle();Out(1,0,new[]{command}.Concat(BitConverter.GetBytes(address)).ToArray());Poll();}
  internal byte[] Read(uint address,int count){var result=new List<byte>();while(result.Count<count){int size=Math.Min(TransferSize,count-result.Count);Special(0x21,address+(uint)result.Count);Idle();var part=In(2,2,size);if(part.Length!=size)throw new Exception("DFU 읽기 길이가 다릅니다.");result.AddRange(part);}Idle();return result.ToArray();}
  internal Obj Install(byte[] image,Obj info){
   if(U.Hash(image)!=Json.S(info,"sha256"))throw new Exception("BIN snapshot hash mismatch");
   var profile=Stm32DfuProfile.For(Json.S(info,"mcu"));uint app=Convert.ToUInt32(info["application_start"]);bool full=Json.B(info,"replace_bootloader");
   if(image.Length<64||(full?app!=0x08000000:!profile.Offsets.Any(offset=>app==0x08000000+offset)||image.Length>app-0x08000000))throw new Exception("MCU의 기록 시작 주소/영역과 이미지가 일치하지 않습니다.");
   Discover();var sectors=Stm32DfuProfile.Parse(Memory);
   // DFU cannot read DBGMCU peripheral registers (AN3156). Check factory flash
   // capacity plus advertised geometry; physical MCU confirmation remains required.
   byte[] size=Read(profile.CapacityAddress&~3u,4);int flash=BitConverter.ToUInt16(size,(int)(profile.CapacityAddress&3u))*1024;profile.Check(sectors,flash);
   Read(0x08000000,16);Log("DFU 플래시 구성 확인: "+Json.S(info,"mcu")+" · "+flash+" bytes (정확한 칩 ID 판독은 아님)");
   if(TransferSize%profile.WriteUnit!=0)throw new Exception("DFU 전송 크기가 MCU 쓰기 정렬과 맞지 않습니다.");
   byte[] programmed=image.Concat(Enumerable.Repeat((byte)255,(profile.WriteUnit-image.Length%profile.WriteUnit)%profile.WriteUnit)).ToArray();
   if(programmed.Length>flash)throw new Exception("펌웨어가 실제 플래시 용량보다 큽니다. 쓰지 않았습니다.");
   var plan=full?sectors.Where(s=>s.Address<0x08000000+(uint)programmed.Length).ToList():Stm32DfuProfile.ErasePlan(sectors,programmed.Length,app);
   foreach(var sector in plan){Log("DFU ERASE 0x"+sector.Address.ToString("X8")+" +"+sector.Size);Special(0x41,sector.Address);}
   for(int offset=0;offset<programmed.Length;){int sizeBytes=Math.Min(TransferSize,programmed.Length-offset);uint at=0x08000000+(uint)offset;Log("DFU WRITE 0x"+at.ToString("X8")+" +"+sizeBytes);Special(0x21,at);Idle();Out(1,2,programmed.Skip(offset).Take(sizeBytes).ToArray());Poll();offset+=sizeBytes;}
   Log("DFU READBACK "+programmed.Length+" bytes");if(!Read(0x08000000,programmed.Length).SequenceEqual(programmed)||!full&&Read(app,4).Any(b=>b!=255))throw new Exception("DFU 읽기 검증 실패. 재부팅하지 않습니다.");
   Log("DFU 읽기 검증 완료 · "+(full?"Klipper":"Katapult")+" 시작 요청");Special(0x21,0x08000000);Idle();Out(1,0,new byte[0]);
   bool leaveAcknowledged=false;try{var s=Status();if(s[0]!=0)throw new Exception("DFU leave 상태 오류");if(s[4]!=6&&s[4]!=7&&s[4]!=8&&s[4]!=2)throw new Exception("DFU leave state "+s[4]);leaveAcknowledged=true;}catch(IOException){Log("시작 요청 후 USB 연결이 종료됐습니다. 실행 여부는 장치 재조회로 확인하세요.");}
   return new Obj{{"verified",true},{"transferred",true},{"leave_acknowledged",leaveAcknowledged},{"katapult_connected",false},{"image",info}};
  }
 }
}
