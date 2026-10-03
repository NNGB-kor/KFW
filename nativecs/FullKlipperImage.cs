using System;using System.IO;using System.Linq;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 static class FullKlipperImage {
  internal static Obj Inspect(string path){
   if(!Path.GetFileName(path).Equals("klipper.bin",StringComparison.OrdinalIgnoreCase))throw new Exception("일반 설치에는 No bootloader로 빌드한 klipper.bin을 선택하세요.");
   string dir=Path.GetDirectoryName(Path.GetFullPath(path));if(File.Exists(Path.Combine(dir,"BUILD_INCOMPLETE")))throw new Exception("미완료 빌드입니다.");
   var info=Safety.Inspect(path);var report=FirmwareExport.Report(path);string mcu=Json.S(info,"mcu");bool rp=mcu=="rp2040"||mcu=="rp2350";
   if(!rp)Stm32DfuProfile.For(mcu);
   uint start=rp?0x10000000u:0x08000000u;
   if(Convert.ToUInt32(info["application_start"])!=start||Convert.ToUInt32(info["bootloader_offset"])!=0)throw new Exception("플래시 시작 주소에 설치하려면 오프셋 0 (No bootloader) 펌웨어가 필요합니다.");
   var artifact=Json.D(Json.D(report,"artifacts"),"klipper.bin");
   if(Json.S(report,"project")!="klipper"||Json.S(report,"status")!="success"||Json.S(report,"mcu")!=mcu||Json.S(artifact,"sha256")!=Json.S(info,"sha256")||Convert.ToInt64(artifact["size"])!=Convert.ToInt64(info["size"])||Convert.ToUInt32(Json.D(report,"elf_check")["load_address"])!=start)throw new Exception("Klipper BIN/시작 주소가 빌드 기록과 일치하지 않습니다.");
   if(mcu=="rp2040"){byte[] bytes=File.ReadAllBytes(path);if(bytes.Length<256)throw new Exception("RP2040 boot2 누락");uint crc=0xffffffff;for(int i=0;i<252;i++){crc^=(uint)bytes[i]<<24;for(int n=0;n<8;n++)crc=(crc<<1)^((crc&0x80000000)!=0?0x04c11db7u:0);}if(crc!=U.V32(bytes,252))throw new Exception("RP2040 boot2 체크섬 오류");}
   info["path"]=Path.GetFullPath(path);info["method"]=rp?"bootsel":"dfu";info["replace_bootloader"]=true;info["communication"]=Json.S(report,"communication");
   if(rp){info["bootsel_pid"]=mcu=="rp2040"?3:15;info["board_id"]=mcu=="rp2040"?"RPI-RP2":"RP2350";}
   return info;
  }
  internal static byte[] Snapshot(Obj req,out Obj info){info=Inspect(Json.S(req,"firmware"));byte[] bytes=File.ReadAllBytes(Json.S(info,"path"));if(Json.S(req,"sha256")!=Json.S(info,"sha256")||U.Hash(bytes)!=Json.S(info,"sha256"))throw new Exception("선택 후 BIN이 변경되었습니다.");
   if(Json.S(info,"method")=="dfu")return bytes;
   int count=(bytes.Length+255)/256;uint family=Json.S(info,"mcu")=="rp2040"?0xe48bff56u:0xe48bff59u;
   using(var output=new MemoryStream()){for(int i=0;i<count;i++){byte[] page=Enumerable.Repeat((byte)255,256).ToArray();Array.Copy(bytes,i*256,page,0,Math.Min(256,bytes.Length-i*256));var block=Elf.Block(0x10000000+(uint)i*256,page,(uint)i,(uint)count,family);output.Write(block,0,block.Length);}return output.ToArray();}
  }
 }
 static class InstallImage {internal static byte[] Snapshot(Obj req,out Obj info)=>Json.B(req,"replace_bootloader")?FullKlipperImage.Snapshot(req,out info):KatapultImage.Snapshot(req,out info);}
}
