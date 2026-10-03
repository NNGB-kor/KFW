using System;using System.IO;using System.Linq;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 sealed class NativeEngine {
  Obj Request,Settings,Version;Versions Store;KConfig Model;Action<string> Log;string Project,Data,SettingsFile,Profile,Source;
  public NativeEngine(Obj request,Action<string> log){Request=request;Log=log;Project=Json.S(request,"project");if(Project=="")Project="klipper";if(Project!="klipper"&&Project!="katapult")throw new Exception("Invalid project");Store=new Versions(Project,log);Data=Json.S(request,"data");if(Data=="")Data=Path.Combine(U.Root,"winforms-data");Directory.CreateDirectory(Data);SettingsFile=Path.Combine(Data,Project+".json");Settings=new Obj();}
  void Load(){
   string name=Json.S(Settings,"version");var candidates=new List<string>();if(name!="")candidates.Add(U.SafePath(Store.Root,name));
   var catalog=Store.Catalog(false);if(catalog.ContainsKey("latest"))candidates.Add(Store.Folder(Json.D(catalog,"latest")));
   candidates.AddRange(Directory.GetDirectories(Store.Root).OrderByDescending(Directory.GetLastWriteTimeUtc));
   Source=candidates.FirstOrDefault(path=>File.Exists(Path.Combine(path,"version.json")));
   if(Source==null)throw new IOException("다운로드된 "+Project+" 소스가 없습니다. 인터넷 연결을 확인하고 프로그램을 다시 실행하세요.");
   Version=U.Read(Path.Combine(Source,"version.json"));Model=new KConfig(Source,Versions.Label(Version));Profile=Path.Combine(Data,"profiles",Project,Json.S(Version,"sha")+".config");if(File.Exists(Profile))Model.Load(Profile);else Model.DefaultArm();
  }
  void Save(){Model.Save(Profile);Settings["version"]=Path.GetFileName(Source);U.Write(SettingsFile,Settings);}
  Obj ConfigSummary(){var result=Model.Summary(Project);if(Project=="klipper"){string start=Model.Get("FLASH_APPLICATION_ADDRESS"),flash=Model.Get("BOARD_DIRECTORY")=="rp2040"?"0x10000000":Model.Get("FLASH_BOOT_ADDRESS");result["bootloader_offset"]=Model.Get("RP2040_HAVE_STAGE2")=="y"?"0":start!=""&&flash!=""?(U.Number(start)-U.Number(flash)).ToString():"";}return result;}
  Obj Snapshot()=>new Obj{{"version",Model.Version},{"versions",Store.All().Select(v=>{var copy=new Obj(v);copy["label"]=Versions.Label(v);return (object)copy;}).ToArray()},{"rows",Model.Rows()},{"summary",ConfigSummary()},{"profile",Profile},{"title",Model.Title},{"build_error",Build.Unsupported(Model,Project)}};
  public Obj Run(){string action=Json.S(Request,"action");
#if DIAGNOSTICS
if(action=="diagnostic_exports")return TestExports.Run(Json.S(Request,"output"));
if(action=="uart_entry_test")return UartEntryTests.Run();if(action=="dfu_test")return DfuSelfTests.Run();if(action=="protocol_test")return SelfTests.Protocol(Json.S(Request,"firmware"));
#endif
if(action=="full_klipper_image")return new Obj{{"image",FullKlipperImage.Inspect(Json.S(Request,"firmware"))}};if(action=="katapult_image")return new Obj{{"image",KatapultImage.Inspect(Json.S(Request,"firmware"))}};if(action=="dfu_install")return Stm32Install.Run(Request,Log);if(action=="rp2040_image")return new Obj{{"image",Rp2040Image.Inspect(Json.S(Request,"firmware"))}};if(action=="rp2040_install")return Rp2040Install.Run(Request,Log);if(action=="inspect")return new Obj{{"firmware",Safety.Inspect(Json.S(Request,"firmware"))}};if(action=="ports")return new Obj{{"ports",Rp2040Install.Devices().Concat(WinDfuUsb.Devices()).Cast<object>().ToArray()}};if(action=="check"||action=="flash")return SerialFlash.Run(Request,Log);if(action.StartsWith("ssh_"))return SshOperations.Run(Request,Data,Log);
   Settings=File.Exists(SettingsFile)?U.Read(SettingsFile):new Obj();
   if(action=="startup"||action=="refresh"){string stage="catalog";try{var catalog=Store.Catalog(true);var latest=Json.D(catalog,"latest");Log("최신 버전: "+Versions.Label(latest));stage="download";Source=Store.Prepare(latest);Settings["version"]=Path.GetFileName(Source);}catch(Exception ex){try{Load();}catch{throw new VersionStageException(stage,"첫 실행에 필요한 "+Project+" 소스를 받지 못했습니다. 인터넷/GitHub 상태를 확인한 뒤 프로그램을 다시 실행하세요.",ex);}Save();var fallback=Snapshot();fallback["latest_selected"]=false;fallback["assets_ready"]=false;fallback["notice_stage"]=stage;fallback["startup_warning"]=(stage=="catalog"?"저장된 소스를 사용합니다. 최신 여부는 확인하지 못했습니다. ":"저장된 소스를 사용합니다. 새 소스 받기를 완료하지 못했습니다. ")+ex.Message;return fallback;}Load();Save();string warning="";try{Build.Ensure(Store,Version,Model,Log);}catch(Exception ex){warning="소스 다운로드는 완료됐으나 빌드 준비 확인이 필요합니다: "+ex.Message;}var result=Snapshot();result["latest_selected"]=true;result["assets_ready"]=warning=="";result["notice_stage"]=warning==""?"":"build";result["startup_warning"]=warning;result["download_folder"]=Source;return result;}
   if(action=="select"){var v=Store.All().First(x=>Json.S(x,"sha")==Json.S(Request,"sha"));try{Settings["version"]=Path.GetFileName(Store.Prepare(v));}catch(Exception ex){throw new VersionStageException("download","선택한 버전의 소스 준비를 완료하지 못했습니다.",ex);}}
   try{Load();}catch(Exception ex){if(action=="select")throw new VersionStageException("configuration","소스 다운로드는 완료됐으나 설정을 읽지 못했습니다.",ex);throw;}if(action=="reset"){Model=new KConfig(Source,Versions.Label(Version));Model.DefaultArm();}if(action=="set")Model.Set(Json.S(Request,"name"),Json.S(Request,"value"));if(action=="load")Model.Load(Json.S(Request,"path"));if(action=="export")Model.Save(Json.S(Request,"path"));Save();if(action=="config_dump")return Model.Symbols.ToDictionary(x=>x.Key,x=>(object)x.Value.Value);
   if(action=="build"){string folder=Path.Combine(Json.S(Request,"output"),Model.Version,Model.Get("MCU")+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));return Build.Run(Store,Version,Model,folder,Json.S(Request,"cancel_file"),Log);}return Snapshot();
  }
 }
}
