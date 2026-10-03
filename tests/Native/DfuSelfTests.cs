using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 sealed class EmulatedDfu:IDfuUsb {
  internal int FlashSize=32768,Page=1024,ReportedSize=32768,State=2,Error,Alignment=4;internal uint CapacityAddress=0x1ffff7cc;internal List<DfuSector> Sectors;internal bool Corrupt,Disconnected,Stuck,ReadProtected,FailErase;internal string Geometry;
  internal List<uint> Erased=new List<uint>(),Writes=new List<uint>();internal bool Left;internal byte[] Flash=Enumerable.Repeat((byte)0x77,2097152).ToArray();uint Address=0x08000000;byte[] Pending;ushort Block;
  public void Select(byte iface,byte alternate){if(iface!=0||alternate!=0)throw new Exception("Unexpected interface");}
  public void Dispose(){}
  public byte[] Control(byte type,byte request,ushort value,ushort index,byte[] data,int length){
   if(type==0x80&&request==6){if(value==0x0100){var desc=new byte[18];desc[0]=18;desc[1]=1;desc[8]=0x83;desc[9]=4;desc[10]=0x11;desc[11]=0xdf;return desc;}
    if(value==0x0200){byte[] desc={9,2,27,0,1,1,0,0x80,50,9,4,0,0,0,0xfe,1,2,1,9,0x21,3,0,0,0,8,0x1a,1};return desc.Take(length).ToArray();}
    if(value==0x0301){byte[] s=Encoding.Unicode.GetBytes(Geometry??("@Internal Flash /0x08000000/"+(FlashSize/Page).ToString("D3")+"*"+(Page/1024).ToString("D3")+"Kg"));return new[]{(byte)(s.Length+2),(byte)3}.Concat(s).ToArray();}
    throw new Exception("Unexpected descriptor");
   }
   if(index!=0)throw new Exception("Wrong interface index");
   if(type==0x21){
    if(request==4){Error=0;State=2;return data;}if(request==6){if(State!=5&&State!=9&&State!=2)throw new Exception("Abort state");State=2;return data;}
    if(request!=1||State!=2)throw new Exception("Unexpected DFU output state");
    if(data.Length==0){if(Address!=0x08000000)throw new Exception("Wrong leave address");Left=true;State=7;return data;}
    if(value==0&&(data.Length!=5||data[0]!=0x21&&data[0]!=0x41))throw new Exception("Forbidden command including mass erase/unprotect");
    if(value!=0&&value!=2)throw new Exception("Unexpected data block number");
    Pending=data;Block=value;State=3;return data;
   }
   if(type!=0xa1)throw new Exception("Unexpected request type");
   if(request==3){
    if(Left&&Disconnected)throw new IOException("Disconnected after manifestation");
    if(State==3){State=4;if(Block==0){uint at=U.V32(Pending,1);if(Pending[0]==0x21)Address=at;else{if(FailErase){State=10;Error=4;}else{var sector=Sectors?.SingleOrDefault(s=>s.Address==at);if(at<0x08000000||at>=0x08000000+FlashSize||(Sectors==null?at%(uint)Page!=0:sector==null))throw new Exception("Erase outside allowed flash");Erased.Add(at);DfuTestMemory.FillCompat(Flash,at-0x08000000,(uint)(sector?.Size??Page));}}}
     else{if(Pending.Length%Alignment!=0||Address%Alignment!=0)throw new Exception("Unaligned write");for(int i=0;i<Pending.Length;i++)if(Flash[(int)(Address-0x08000000)+i]!=255)throw new Exception("Write before erase");Writes.Add(Address);System.Array.Copy(Pending,0,Flash,(int)(Address-0x08000000),Pending.Length);}}
    else if(State==4&&!Stuck)State=5;
    return new byte[]{(byte)Error,1,0,0,(byte)State,0};
   }
   if(request==2&&State==2&&value==2){State=9;if(ReadProtected)throw new IOException("Read protection");if(Address==(CapacityAddress&~3u)){var valueBytes=new byte[4];System.Array.Copy(BitConverter.GetBytes((ushort)(ReportedSize/1024)),0,valueBytes,(int)(CapacityAddress&3u),2);return valueBytes.Take(length).ToArray();}if(Address<0x08000000||Address+length>0x08000000+FlashSize)throw new Exception("Invalid read");var part=Flash.Skip((int)(Address-0x08000000)).Take(length).ToArray();if(Corrupt&&Writes.Count>0)part[0]^=1;return part;}
   throw new Exception("Unexpected DFU input state");
  }
 }
 static class DfuTestMemory {internal static void FillCompat(byte[] data,uint start,uint length){for(uint i=start;i<start+length;i++)data[i]=255;}}
 static class DfuSelfTests {
  internal static Obj Run(){var results=new Obj();byte[] image=Enumerable.Range(0,3001).Select(n=>(byte)n).ToArray();
   Func<string,Obj> info=mcu=>new Obj{{"mcu",mcu},{"sha256",U.Hash(image)},{"flash_size",mcu=="stm32f042x6"?32768:131072},{"page_size",mcu=="stm32f042x6"?1024:2048},{"application_start",0x08002000}};
   foreach(string mcu in new[]{"stm32f042x6","stm32f072xb"}){var board=new EmulatedDfu{FlashSize=mcu=="stm32f042x6"?32768:131072,Page=mcu=="stm32f042x6"?1024:2048,ReportedSize=mcu=="stm32f042x6"?32768:131072};var result=new DfuProtocol(board,_=>{},_=>{}).Install(image,info(mcu));if(!Json.B(result,"verified")||!board.Left||board.Writes.Count!=2||!board.Flash.Take(image.Length).SequenceEqual(image)||board.Flash[0x2000]!=255||board.Flash[0x4000]!=0x77)throw new Exception("DFU success/preservation failure");results[mcu]=true;}
   foreach(string failure in new[]{"geometry","capacity","read-protection","erase","readback","busy","hash"}){var board=new EmulatedDfu();var metadata=info("stm32f042x6");if(failure=="geometry")board.Geometry="@Internal Flash /0x08000000/064*002Kg";if(failure=="capacity")board.ReportedSize=65536;if(failure=="read-protection")board.ReadProtected=true;if(failure=="erase")board.FailErase=true;if(failure=="readback")board.Corrupt=true;if(failure=="busy")board.Stuck=true;if(failure=="hash")metadata["sha256"]="changed";bool caught=false;try{new DfuProtocol(board,_=>{},_=>{}).Install(image,metadata);}catch{caught=true;}if(!caught||board.Left)throw new Exception("DFU failure reset/write gate: "+failure);if(failure!="readback"&&board.Writes.Count!=0)throw new Exception("Writes after failed prerequisite");if(new[]{"geometry","capacity","read-protection","busy","hash"}.Contains(failure)&&board.Erased.Count!=0)throw new Exception("Erased before identification");results[failure+"_blocked"]=true;}
   foreach(var profile in Stm32DfuProfile.All){int kb=profile.Capacities.Last();string geometry;
    if(profile.Geometry=="f4"){geometry="@Internal Flash /0x08000000/004*016Kg,001*064Kg,"+(Math.Min(kb,1024)-128)/128+"*128Kg";if(kb==128)geometry="@Internal Flash /0x08000000/004*016Kg,001*064Kg";if(kb==2048)geometry+="/0x08100000/004*016Kg,001*064Kg,007*128Kg";}
    else{int size=profile.Geometry=="1k"?1:profile.Geometry=="2k"?2:128;geometry="@Internal Flash /0x08000000/"+(kb/size)+"*"+size+"Kg";}
    var sectors=Stm32DfuProfile.Parse(geometry);uint app=0x08000000+(uint)profile.Offsets.Last();var board=new EmulatedDfu{Geometry=geometry,FlashSize=kb*1024,ReportedSize=kb*1024,Sectors=sectors,CapacityAddress=profile.CapacityAddress,Alignment=profile.WriteUnit};var metadata=new Obj{{"mcu",profile.Mcu},{"application_start",app},{"sha256",U.Hash(image)}};
    var result=new DfuProtocol(board,_=>{},_=>{}).Install(image,metadata);var plan=Stm32DfuProfile.ErasePlan(sectors,image.Length,app);
    if(!Json.B(result,"verified")||!board.Left||!board.Erased.SequenceEqual(plan.Select(s=>s.Address)))throw new Exception("Expanded DFU plan failed: "+profile.Mcu);
    foreach(var sector in sectors.Except(plan))if(board.Flash[sector.Address-0x08000000]!=0x77)throw new Exception("Unselected sector erased");results[profile.Mcu+"_layout"]=true;
   }
   Stm32DfuProfile.For("stm32f429xx").Check(Stm32DfuProfile.Parse("@Internal Flash /0x08000000/4*16Kg,1*64Kg,3*128Kg/0x08080000/4*16Kg,1*64Kg,3*128Kg"),1048576);results["f429_1mb_dual_bank"]=true;
   var fullInfo=info("stm32f042x6");fullInfo["application_start"]=0x08000000;fullInfo["replace_bootloader"]=true;var fullBoard=new EmulatedDfu();new DfuProtocol(fullBoard,_=>{},_=>{}).Install(image,fullInfo);if(!fullBoard.Left||!fullBoard.Flash.Take(image.Length).SequenceEqual(image)||fullBoard.Erased.Contains(0x08002000))throw new Exception("Full image start/erase regression");results["full_firmware_from_flash_base"]=true;
   var fullCorrupt=new EmulatedDfu{Corrupt=true};bool corruptBlocked=false;try{new DfuProtocol(fullCorrupt,_=>{},_=>{}).Install(image,fullInfo);}catch{corruptBlocked=true;}if(!corruptBlocked||fullCorrupt.Left)throw new Exception("Full firmware readback failure rebooted");results["full_readback_failure_no_reboot"]=true;
   fullInfo.Remove("replace_bootloader");bool baseBlocked=false;try{new DfuProtocol(new EmulatedDfu(),_=>{},_=>{}).Install(image,fullInfo);}catch{baseBlocked=true;}if(!baseBlocked)throw new Exception("Default bootloader guard weakened");results["default_base_write_blocked"]=true;
   foreach(string bad in new[]{"@Internal Flash /0x08000000/004*016Kg/0x08000000/004*016Kg","@Internal Flash /0x08001000/128*002Kg","@Internal Flash /0x08000000/032*001Ka","@Internal Flash /0x08000000/999999*128Kg"}){bool caught=false;try{Stm32DfuProfile.Parse(bad);}catch{caught=true;}if(!caught)throw new Exception("Malformed DFU layout accepted");}
   var detach=new EmulatedDfu{Disconnected=true};var detached=new DfuProtocol(detach,_=>{},_=>{}).Install(image,info("stm32f042x6"));if(!Json.B(detached,"verified")||Json.B(detached,"leave_acknowledged"))throw new Exception("Disconnect acknowledgement incorrect");results["disconnect_after_verified_write"]=true;results["invalid_layouts_blocked"]=true;results["physical_device_opened"]=false;results["success"]=true;return results;
  }
 }
}
