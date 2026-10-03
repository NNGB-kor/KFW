using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Renci.SshNet;
namespace KlipperWinForms {
    using Obj=Dictionary<string,object>;
    static class SshRp2040Install {
        internal static string Source() {using(var stream=typeof(SshRp2040Install).Assembly.GetManifestResourceStream("Rp2040Remote"))using(var reader=new StreamReader(stream))return reader.ReadToEnd();}
        internal static object[] Scan(SshClient ssh,string python) {
            using(var command=ssh.CreateCommand(U.Shell(python)+" -c "+U.Shell(Source())+" --scan")) {
                command.CommandTimeout=TimeSpan.FromSeconds(20);string output=command.Execute();
                if(command.ExitStatus!=0)throw new Exception("원격 BOOTSEL 장치 조회 실패: "+command.Error);
                return (object[])Json.Serializer().DeserializeObject(output);
            }
        }
        internal static Obj Run(SshClient ssh,SftpClient sftp,Obj req,string data,Action<string> log) {
            sftp.OperationTimeout=TimeSpan.FromSeconds(15);
            byte[] bytes=InstallImage.Snapshot(req,out Obj info);if(Json.S(info,"method")!="bootsel")throw new Exception("BOOTSEL용 이미지가 아닙니다.");var selected=Json.D(req,"device");
            string usb=Json.S(selected,"usb_path"),python=Json.S(req,"python"),service=Json.S(req,"service");if(service=="")service="klipper";
            bool bootsel=Json.S(selected,"kind")=="bootsel";string operation=Json.B(info,"replace_bootloader")?"Klipper 펌웨어 업로드":"Katapult 부트로더 설치";
            if(!usb.StartsWith("/sys/devices/") || usb.Contains("..") || python=="" || !Regex.IsMatch(service,@"^[A-Za-z0-9_][A-Za-z0-9_.@-]*$"))throw new Exception("잘못된 원격 설치 대상/서비스 설정입니다.");
            bool manual=Json.B(info,"replace_bootloader")&&Json.S(selected,"vid")=="1d50"&&Json.S(selected,"pid")=="6177";
            if(!bootsel && (Json.S(selected,"vid")!="1d50" || Json.S(selected,"pid")!="614e"&&!manual || !Regex.IsMatch(Json.S(selected,"device"),@"^/dev/serial/by-(id|path)/[^/\r\n]+$")))throw new Exception("Klipper 네이티브 USB 또는 RP2040/RP2350 BOOTSEL 장치를 선택하세요.");
            string temp=SshOperations.Command(ssh,"mktemp -d /tmp/klipper-rp2040-XXXXXXXXXXXX");
            if(!Regex.IsMatch(temp,@"^/tmp/klipper-rp2040-[A-Za-z0-9]+$"))throw new Exception("Invalid remote staging path");
            bool started=false,completed=false,workerExited=false;
            try {
                var config=new Obj{{"selected",selected},{"mcu",Json.S(info,"mcu")},{"replace_bootloader",Json.B(info,"replace_bootloader")},{"directory",temp},{"sha256",U.Hash(bytes)},{"application_start",info["application_start"]},
                    {"communication",Json.S(info,"communication")},{"service",service},{"stop",Json.B(req,"stop_service")}};
                SshOperations.Put(sftp,temp+"/katapult.uf2",bytes);
                string source="cfg = "+"__import__('json').loads("+U.CString(Json.Write(config))+")\n"+Source();
                SshOperations.Put(sftp,temp+"/install.py",Encoding.UTF8.GetBytes(source));
                started=true;string pid=SshServiceRunner.Start(ssh,python,temp+"/install.py",Json.S(req,"password"));
                log(("원격 "+operation)+" PID "+pid+" · "+temp+"/operation.log");
                int offset=0;var output=new StringBuilder();Action drain=()=>{if(!sftp.Exists(temp+"/operation.log"))return;using(var stream=sftp.OpenRead(temp+"/operation.log")){stream.Position=offset;using(var memory=new MemoryStream()){stream.CopyTo(memory);var chunk=memory.ToArray();offset+=chunk.Length;string text=Encoding.UTF8.GetString(chunk);if(text!=""){output.Append(text);log(text);}}}};
                DateTime end=DateTime.UtcNow.AddMinutes(6),nextProbe=DateTime.UtcNow;
                while(DateTime.UtcNow<end) {
                    drain();if(sftp.Exists(temp+"/result.json")) {
                        drain();var result=Json.Read(SshOperations.Read(sftp,temp+"/result.json"));completed=true;
                        string logfile=Path.Combine(data,"ssh-logs",Guid.NewGuid().ToString("N")+".log");Directory.CreateDirectory(Path.GetDirectoryName(logfile));File.WriteAllText(logfile,output.ToString(),Encoding.UTF8);
                        if(Json.B(result,"service_restore_failed"))throw new Exception("펌웨어 작업 후 Klipper 서비스를 다시 시작하지 못했습니다. 서버의 systemd/sudo 상태를 확인하세요. 로그: "+logfile);
                        if(Convert.ToInt32(result["exit_code"])!=0)throw new Exception("RP2040/RP2350 "+operation+" 실패. 보드/서비스 상태를 확인하세요. 로그: "+logfile);
                        result["image"]=info;result["local_log"]=logfile;return result;
                    }
                    if(DateTime.UtcNow>=nextProbe) {
                        using(var probe=ssh.CreateCommand("if [ ! -d /proc/"+pid+" ]; then printf gone; elif [ -r /proc/"+pid+"/cmdline ]; then tr '\\000' '\\n' < /proc/"+pid+"/cmdline; else printf unknown; fi")) {
                            probe.CommandTimeout=TimeSpan.FromSeconds(5);string state=probe.Execute();
                            bool alive=state=="unknown"||state.Split('\n').Any(line=>line==temp+"/install.py");
                            if(!alive&&!sftp.Exists(temp+"/result.json")) {
                                workerExited=true;drain();string folder=Path.Combine(data,"ssh-logs");Directory.CreateDirectory(folder);string file=Path.Combine(folder,Guid.NewGuid().ToString("N")+".log");File.WriteAllText(file,output.ToString(),Encoding.UTF8);
                                throw new Exception("원격 설치 프로세스 "+pid+"가 결과를 남기지 않고 종료됐습니다. 보드/서비스 상태를 확인하세요. 로그: "+file+" · 원격: "+temp);
                            }
                        }
                        nextProbe=DateTime.UtcNow.AddSeconds(3);
                    }
                    Thread.Sleep(300);
                }
                throw new TimeoutException("원격 "+operation+" 대기 시간 초과");
            } catch(Exception ex) {if(started&&!completed&&!workerExited)throw new Exception("원격 설치가 계속 실행 중일 수 있습니다. 재시도 전에 확인하세요: "+temp+"/operation.log\n"+ex.Message,ex);throw;}
            finally {if(completed||!started){foreach(string name in new[]{"katapult.uf2","install.py","operation.log","result.json"})try{sftp.DeleteFile(temp+"/"+name);}catch{}try{sftp.DeleteDirectory(temp+"/mount");}catch{}try{sftp.DeleteDirectory(temp);}catch{}}}
        }
    }
}
