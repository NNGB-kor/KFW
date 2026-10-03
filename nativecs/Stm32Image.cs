using System;using System.IO;using System.Linq;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 static class Stm32Image {
  internal static Obj Inspect(string path){
   if(Path.GetFileName(path)!="katapult.bin")throw new Exception("STM32 DFU 설치에는 빌드 폴더의 katapult.bin을 선택하세요.");
   string dir=Path.GetDirectoryName(Path.GetFullPath(path));if(File.Exists(Path.Combine(dir,"BUILD_INCOMPLETE")))throw new Exception("완료되지 않은 빌드입니다.");
   var report=FirmwareExport.Report(path);string mcu=Json.S(report,"mcu");
   if(Json.S(report,"status")!="success"||Json.S(report,"project")!="katapult")throw new Exception("성공한 Katapult 빌드 기록이 필요합니다.");
   var profile=Stm32DfuProfile.For(mcu);
   if(new FileInfo(path).Length>131072)throw new Exception("Katapult가 최대 부트로더 영역을 초과합니다.");
   byte[] bytes=File.ReadAllBytes(path);var entry=Json.D(Json.D(report,"artifacts"),"katapult.bin");string hash=U.Hash(bytes);
   if(hash!=Json.S(entry,"sha256")||bytes.Length!=Convert.ToInt64(entry["size"]))throw new Exception("BIN이 빌드 기록과 다릅니다.");
   uint app=checked((uint)U.Number(Json.S(report,"application_address")));
   if(!profile.Offsets.Any(o=>app==0x08000000+o)||bytes.Length<64||bytes.Length>app-0x08000000||Convert.ToUInt32(Json.D(report,"elf_check")["load_address"])!=0x08000000)throw new Exception("Katapult 주소/예약 영역이 올바르지 않습니다.");
   uint stack=U.V32(bytes,0),reset=U.V32(bytes,4);
   if(stack<=profile.RamStart||stack>profile.RamStart+profile.RamSize||stack%4!=0||(reset&1)==0||(reset&~1u)<0x08000000||(reset&~1u)>=0x08000000+bytes.Length)throw new Exception("STM32 벡터 테이블이 올바르지 않습니다.");
   return new Obj{{"mcu",mcu},{"method","dfu"},{"path",Path.GetFullPath(path)},{"sha256",hash},{"size",bytes.Length},{"application_start",app},{"erase_geometry",profile.Geometry},{"capacity_register",profile.CapacityAddress},{"write_unit",profile.WriteUnit},{"communication",Json.S(report,"communication")},{"version",Json.S(report,"source_version")}};
  }
 }
 static class KatapultImage {
  internal static Obj Inspect(string path){var info=Path.GetExtension(path).Equals(".uf2",StringComparison.OrdinalIgnoreCase)?Rp2040Image.Inspect(path):Stm32Image.Inspect(path);if(!info.ContainsKey("method"))info["method"]="bootsel";return info;}
  internal static byte[] Snapshot(Obj req,out Obj info){info=Inspect(Json.S(req,"firmware"));byte[] data=File.ReadAllBytes(Json.S(info,"path"));if(Json.S(req,"sha256")!=Json.S(info,"sha256")||U.Hash(data)!=Json.S(info,"sha256"))throw new Exception("선택 후 설치 파일이 변경되었습니다.");return data;}
 }
}
