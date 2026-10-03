using System;using System.IO;using System.Text;using System.Threading;using System.Linq;using System.Collections.Generic;using System.Text.RegularExpressions;using Renci.SshNet;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 sealed class SshDfuUsb:IDfuUsb {
  SshCommand Command;StreamWriter Input;StreamReader Output;IAsyncResult Pending;Action<string> Log;bool CleanupAttempted;Obj Cleanup;
  internal static string Source(){using(var stream=typeof(SshDfuUsb).Assembly.GetManifestResourceStream("DfuRemote"))using(var reader=new StreamReader(stream))return reader.ReadToEnd();}
  internal static object[] Scan(SshClient ssh,string python){string source="import pathlib,json\nr=[]\nfor p in pathlib.Path('/sys/bus/usb/devices').glob('*'):\n try:\n  if (p/'idVendor').read_text().strip()!='0483' or (p/'idProduct').read_text().strip()!='df11': continue\n  q=str(p.resolve());s=(p/'serial').read_text().strip() if (p/'serial').exists() else ''\n  r.append(dict(kind='dfu',device=q,usb_path=q,realpath=q,vid='0483',pid='df11',serial=s,product='STM32 DFU',manufacturer='STMicroelectronics',aliases=[],readable=True,writable=True))\n except OSError: pass\nprint(json.dumps(r))";return (object[])Json.Serializer().DeserializeObject(SshOperations.Command(ssh,U.Shell(python)+" -c "+U.Shell(source)));}
  internal SshDfuUsb(SshClient ssh,string python,string path,Obj config,Action<string> log){Log=log;Command=ssh.CreateCommand(U.Shell(python)+" -B -u "+U.Shell(path));Command.CommandTimeout=TimeSpan.FromMinutes(6);Pending=Command.BeginExecute();Input=new StreamWriter(Command.CreateInputStream(),new UTF8Encoding(false)){AutoFlush=true};Output=new StreamReader(Command.OutputStream,Encoding.UTF8);try{Input.WriteLine(Json.Write(config));var ready=Receive(90000);if(!Json.B(ready,"ready"))throw new IOException("DFU bridge did not become ready");}catch{Dispose();throw;}}
  Obj Receive(int timeout=15000){for(;;){var pending=Output.ReadLineAsync();if(!pending.Wait(timeout))throw new IOException("SSH USB 응답 시간 초과. 재전송하지 말고 보드 상태를 확인하세요.");string line=pending.Result;if(line==null)throw new IOException("SSH USB 연결이 종료됐습니다.");if(line.Length>65536)throw new IOException("SSH USB 응답 크기 오류");var reply=Json.Read(line);if(reply.ContainsKey("event")){Log(Json.S(reply,"event"));continue;}if(reply.ContainsKey("error"))throw new IOException("원격 DFU: "+Json.S(reply,"error"));return reply;}}
  Obj Request(Obj request){Input.WriteLine(Json.Write(request));var reply=Receive();if(!Json.B(reply,"ok"))throw new IOException("SSH USB 요청 실패");return reply;}
  public byte[] Control(byte type,byte request,ushort value,ushort index,byte[] data,int length){var reply=Request(new Obj{{"op","control"},{"type",type},{"request",request},{"value",value},{"index",index},{"data",Convert.ToBase64String(data)},{"length",length}});return Convert.FromBase64String(Json.S(reply,"data"));}
  public void Select(byte iface,byte alternate){Request(new Obj{{"op","select"},{"interface",iface},{"alternate",alternate}});}
  internal Obj Finish(){
   if(Cleanup!=null)return Cleanup;if(CleanupAttempted)throw new IOException("원격 DFU 정리 결과를 확인하지 못했습니다.");CleanupAttempted=true;
   Input?.Dispose();Input=null;
   if(Pending==null||!Pending.AsyncWaitHandle.WaitOne(90000))throw new IOException("원격 DFU 연결 정리/서비스 재시작 확인 시간 초과");
   string line;
   while((line=Output.ReadLine())!=null){var item=Json.Read(line);if(item.ContainsKey("event"))Log(Json.S(item,"event"));if(item.ContainsKey("error"))Log("원격 DFU: "+Json.S(item,"error"));if(Json.B(item,"cleanup_complete"))Cleanup=item;}
   // EndExecute reads/disposes OutputStream; drain our reader's buffered events first.
   Command.EndExecute(Pending);Pending=null;
   if(Cleanup==null)throw new IOException("원격 DFU 서비스 정리 결과가 없습니다. 서버에서 서비스 상태를 확인하세요.");return Cleanup;
  }
  public void Dispose(){try{if(!CleanupAttempted)Finish();}catch(Exception ex){Log("원격 DFU 정리 확인: "+ex.Message);}finally{try{Input?.Dispose();}catch{}Output?.Dispose();Command?.Dispose();}}
  internal static Obj Run(SshClient ssh,SftpClient sftp,Obj req,string data,Action<string> log){byte[] image=InstallImage.Snapshot(req,out Obj info);if(Json.S(info,"method")!="dfu")throw new Exception("STM32 BIN이 아닙니다.");var selected=Json.D(req,"device");string path=Json.S(selected,"usb_path"),python=Json.S(req,"python"),service=Json.S(req,"service");if(service=="")service="klipper";
   if(!path.StartsWith("/sys/devices/")||path.Contains("..")||!Regex.IsMatch(service,@"^[A-Za-z0-9_][A-Za-z0-9_.@-]*$")||python=="")throw new Exception("Invalid DFU target settings");
   string temp=SshOperations.Command(ssh,"mktemp -d /tmp/klipper-dfu-XXXXXXXXXXXX");if(!Regex.IsMatch(temp,@"^/tmp/klipper-dfu-[A-Za-z0-9]+$"))throw new Exception("Invalid staging path");var transcript=new List<string>();Action<string> say=s=>{transcript.Add(s);log(s);};
   try{SshOperations.Put(sftp,temp+"/common.py",Encoding.UTF8.GetBytes(SshRp2040Install.Source()));SshOperations.Put(sftp,temp+"/dfu_usb.py",Encoding.UTF8.GetBytes(Source()));
    var config=new Obj{{"password",Json.S(req,"password")},{"selected",selected},{"replace_bootloader",Json.B(info,"replace_bootloader")},{"python",python},{"service",service},{"stop",Json.B(req,"stop_service")}};
    say("원격 DFU · "+path+" · "+Json.S(info,"mcu"));using(var usb=new SshDfuUsb(ssh,python,temp+"/dfu_usb.py",config,say)){var result=new DfuProtocol(usb,say).Install(image,info);say("DFU BIN 기록/읽기 검증 완료. BOOT0 해제 후 장치 목록을 갱신하세요.");var cleanup=usb.Finish();result["service_restarted"]=Json.B(cleanup,"service_restarted");if(Json.B(cleanup,"service_restore_failed"))throw new IOException("DFU 펌웨어 검증은 완료됐으나 Klipper 서비스를 다시 시작하지 못했습니다. 서버의 systemd/sudo 상태를 확인하세요.");return result;}
   }finally{string logs=Path.Combine(data,"ssh-logs");Directory.CreateDirectory(logs);string file=Path.Combine(logs,Guid.NewGuid().ToString("N")+".log");File.WriteAllLines(file,transcript,Encoding.UTF8);log("로컬 DFU 로그: "+file);foreach(string name in new[]{"common.py","dfu_usb.py"})try{sftp.DeleteFile(temp+"/"+name);}catch{}try{sftp.DeleteDirectory(temp);}catch{}}
  }
 }
}
