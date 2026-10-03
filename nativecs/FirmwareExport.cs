using System;using System.IO;using System.Linq;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 static class FirmwareExport {
  internal static string[] Names(string project){if(project=="klipper")return new[]{"klipper.bin","klipper.uf2"};if(project=="katapult")return new[]{"katapult.bin","katapult.uf2","katapult.withclear.uf2","deployer.bin"};throw new ArgumentException("Invalid firmware project");}
  static string Index(string hash)=>Path.Combine(Program.Smoke==""?Path.Combine(Program.Root,"winforms-data"):Path.Combine(Program.Smoke,"data"),"export-index",hash+".json");
  internal static Obj Report(string firmware){
   string dir=Path.GetDirectoryName(Path.GetFullPath(firmware)),report=Path.Combine(dir,"build-report.json");
   if(File.Exists(Path.Combine(dir,"BUILD_INCOMPLETE")))throw new IOException("완료되지 않은 빌드입니다.");
   if(!File.Exists(report)){
    string hash=U.Hash(File.ReadAllBytes(firmware)),index=Index(hash);if(!File.Exists(index))throw new IOException("대응하는 빌드 기록이 없습니다. 원래 컴파일 폴더를 유지하세요.");
    var record=U.Read(index);report=Json.S(record,"report");
    if(File.Exists(Path.Combine(Path.GetDirectoryName(report),"BUILD_INCOMPLETE"))||!File.Exists(report)||U.Hash(File.ReadAllBytes(report))!=Json.S(record,"report_sha256"))throw new IOException("원본 빌드 기록이 없거나 변경되었습니다.");
   }
   return U.Read(report);
  }
  internal static string Copy(string source,string destination,string project){
   source=Path.GetFullPath(source);destination=Path.GetFullPath(destination);
   if(source.TrimEnd(Path.DirectorySeparatorChar).Equals(destination.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new IOException("복사 경로는 컴파일 폴더와 다르게 선택하세요.");
   string reportPath=Path.Combine(source,"build-report.json");var report=U.Read(reportPath);var artifacts=Json.D(report,"artifacts");
   if(Json.S(report,"status")!="success"||Json.S(report,"project")!=project||File.Exists(Path.Combine(source,"BUILD_INCOMPLETE")))throw new IOException("성공한 빌드만 복사할 수 있습니다.");
   var names=Names(project);var files=names.Where(n=>File.Exists(Path.Combine(source,n))).ToArray();
   if(!files.Contains(project+".bin"))throw new IOException("빌드 결과 BIN이 없습니다.");
   var bytes=new Dictionary<string,byte[]>();foreach(string name in files){byte[] data=File.ReadAllBytes(Path.Combine(source,name));if(!artifacts.ContainsKey(name)||Json.S(Json.D(artifacts,name),"sha256")!=U.Hash(data))throw new IOException("복사 전 펌웨어 해시 불일치: "+name);bytes[name]=data;}
   Directory.CreateDirectory(destination);string reportHash=U.Hash(File.ReadAllBytes(reportPath));
   foreach(string name in files){string path=Path.Combine(destination,name),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes[name]);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}U.Write(Index(U.Hash(bytes[name])),new Obj{{"report",reportPath},{"report_sha256",reportHash}});}
   // Only remove known outputs of this project, never unrelated user files.
   foreach(string name in names.Except(files)){string stale=Path.Combine(destination,name);if(File.Exists(stale))File.Delete(stale);}
   return Path.Combine(destination,project+".bin");
  }
 }
}
