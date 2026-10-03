using System;using System.IO;using System.Linq;using System.Text;using System.Threading.Tasks;using System.Collections.Generic;using System.Windows.Forms;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 sealed partial class MainForm {
  async Task LockSmoke(){Directory.CreateDirectory(Program.Smoke);var report=new Obj();var gates=new List<TaskCompletionSource<bool>>();var pending=new List<Task>();
   Action<bool,string> expect=(ok,message)=>{if(!ok)throw new Exception(message);};
   Func<string,string[],TaskCompletionSource<bool>> hold=(name,keys)=>{var gate=new TaskCompletionSource<bool>();gates.Add(gate);pending.Add(SafeTask(()=>gate.Task,name,keys));return gate;};
   Func<string,string,Button> button=(side,label)=>(side=="ssh"?SshInputs:UploadInputs).OfType<Button>().First(b=>b.Text==label);
   try {
    await Snapshot(Boot,"init");await Snapshot(Firmware,"init");
    var boot=hold("Katapult 준비 중",new[]{"builder:katapult"});var firmware=hold("Klipper 준비 중",new[]{"builder:klipper"});
    expect(!Boot.Versions.Enabled&&!Firmware.Versions.Enabled&&SshHost.Enabled&&Ports.Enabled,"Startup blocks unrelated controls");
    expect(Boot.Menu.Enabled&&Boot.Help.Enabled&&Boot.Log.Enabled&&Boot.Output.Enabled,"Readonly navigation/output selection disabled during preparation");
    Tabs.SelectedIndex=3;SaveScreenshot("ssh-during-startup.png");report["ssh_usable_during_source_preparation"]=true;
    boot.SetResult(true);await pending[0];expect(Boot.Versions.Enabled&&!Firmware.Versions.Enabled,"Completing one project unlocked another");
    var ssh=hold("SSH 업로드 중",new[]{"ssh-connection","ssh-device","ssh-service","ssh-klipper-file"});
    expect(!SshHost.Enabled&&!SshDevices.Enabled&&!SshStop.Enabled&&!button("ssh","BIN 선택").Enabled,"In-flight SSH inputs enabled");
    expect(button("ssh","UF2 / BIN 선택").Enabled&&Ports.Enabled&&LocalUploadViews.Enabled&&SshUploadViews.Enabled&&SshLog.Enabled,"SSH blocks unrelated inputs/navigation/logs");
    bool duplicateRan=false;await SafeTask(()=>{duplicateRan=true;return Task.FromResult(0);},"duplicate",new[]{"ssh-device"});expect(!duplicateRan,"Duplicate device job started");
    var file=hold("다른 파일 확인 중",new[]{"ssh-katapult-file"});expect(!button("ssh","UF2 / BIN 선택").Enabled,"Image lock missing");file.SetResult(true);await pending.Last();expect(!SshHost.Enabled&&button("ssh","UF2 / BIN 선택").Enabled,"Image completion unlocked SSH action");
    var local=hold("로컬 장치 조회 중",new[]{"local-device"});expect(!Ports.Enabled&&button("local","BIN 선택").Enabled&&button("local","UF2 / BIN 선택").Enabled,"Device refresh blocks independent file selection");
    try{await SafeTask(()=>{throw new Exception("expected lock failure");},"파일 오류",new[]{"local-katapult-file"});}catch(Exception ex){expect(ex.Message=="expected lock failure","Unexpected failure");}
    expect(button("local","UF2 / BIN 선택").Enabled&&!Ports.Enabled&&!SshHost.Enabled,"Failure cleanup released another operation");
    report["duplicate_device_jobs_blocked"]=true;report["per_file_and_device_locks"]=true;report["failure_releases_only_own_locks"]=true;
    firmware.SetResult(true);await pending[1];
    // A real cached firmware build must not overwrite the selected uploader image.
    Inspection=new Obj{{"mcu","selected-existing"},{"application_start",0x10004000},{"version","existing"},{"sha256","snapshot"}};UploadFile.Text="existing-selection.bin";LocalImageRevision++;
    await SafeTask(async delegate{await Build(Firmware);},"Klipper 실제 빌드",new[]{"builder:klipper"});
    expect(UploadFile.Text=="existing-selection.bin"&&Json.S(Inspection,"mcu")=="selected-existing","Build overwrote an in-flight uploader selection");
    report["build_during_upload_preserves_selected_file"]=true;report["actual_build_output"]=Firmware.ResultFolder;
    string cancelDir=Path.Combine(Program.Smoke,"cancel-test");Directory.CreateDirectory(cancelDir);Boot.CancelPath=Path.Combine(cancelDir,Guid.NewGuid()+".cancel");Firmware.CancelPath=Path.Combine(cancelDir,Guid.NewGuid()+".cancel");
    Cancel(Boot);expect(File.Exists(Boot.CancelPath)&&!File.Exists(Firmware.CancelPath),"Cancel affected both projects");Boot.CancelPath=Firmware.CancelPath="";ApplyBusyStates();report["independent_build_cancel"]=true;
    string settings=Path.Combine(DataRoot,"klipper.json");string previous=File.ReadAllText(settings);try{File.WriteAllText(settings,"{partial");var result=await Task.Run(()=>new NativeEngine(new Obj{{"project","klipper"},{"data",DataRoot},{"action","dfu_test"}},_=>{}).Run());expect(Json.B(result,"success"),"Readonly action read changing builder settings");}finally{File.WriteAllText(settings,previous);}
    report["readonly_actions_independent_of_builder_settings"]=true;
    local.SetResult(true);ssh.SetResult(true);await Task.WhenAll(pending);expect(!Busy&&Ports.Enabled&&SshHost.Enabled&&Firmware.Versions.Enabled,"Resources not released");
    report["success"]=true;report["physical_device_opened"]=false;
   }catch(Exception ex){report["success"]=false;report["error"]=ex.ToString();Environment.ExitCode=1;}
   finally{foreach(var gate in gates)gate.TrySetResult(true);try{await Task.WhenAll(pending);}catch{}SetBusy(false);U.Write(Path.Combine(Program.Smoke,"report.json"),report);Close();}
  }
 }
}
