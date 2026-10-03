using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Management;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
namespace KlipperWinForms {
    using Obj=Dictionary<string,object>;
    static class Rp2040Install {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetVolumeNameForVolumeMountPoint(string path,StringBuilder name,int length);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out uint node,string id,uint flags);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out uint parent,uint node,uint flags);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern int CM_Get_Device_IDW(uint node,StringBuilder text,int length,uint flags);
        static string UsbAncestor(string id) {
            if(CM_Locate_DevNodeW(out uint node,id,0)!=0) return "";
            for(int i=0;i<12;i++) {
                var text=new StringBuilder(1024);
                if(CM_Get_Device_IDW(node,text,text.Capacity,0)!=0) break;
                string value=text.ToString();
                if(value.StartsWith("USB\\VID_2E8A&PID_0003\\",StringComparison.OrdinalIgnoreCase)||value.StartsWith("USB\\VID_2E8A&PID_000F\\",StringComparison.OrdinalIgnoreCase)) return value;
                if(CM_Get_Parent(out uint parent,node,0)!=0) break; node=parent;
            }
            return "";
        }
        internal static bool IsBootsel(Obj p)=>Json.S(p,"kind")=="bootsel";
        internal static bool IsKatapult(Obj p)=>p!=null&&Json.S(p,"vid")=="7504"&&Json.S(p,"pid")=="24951";
        internal static bool CanInstall(Obj p)=>p!=null && (IsBootsel(p) || Json.S(p,"vid")=="7504" && Json.S(p,"pid")=="24910");
        internal static string VolumeMcu(string root) {
            string info=Path.Combine(root,"INFO_UF2.TXT");
            if(!File.Exists(info) || new FileInfo(info).Length>8192)throw new InvalidDataException("BOOTSEL 볼륨 정보를 확인하지 못했습니다.");
            var ids=File.ReadAllLines(info).Select(line=>line.Trim()).Where(line=>line.StartsWith("Board-ID:")).ToArray();
            if(ids.Length!=1)throw new InvalidDataException("BOOTSEL Board-ID가 모호합니다.");
            return ids[0]=="Board-ID: RPI-RP2"?"rp2040":ids[0]=="Board-ID: RP2350"?"rp2350":throw new InvalidDataException("지원하지 않는 BOOTSEL Board-ID입니다.");
        }
        internal static void CheckTarget(Obj info,Obj drive){if(Json.S(info,"mcu")!=Json.S(drive,"mcu")||Convert.ToInt32(info["bootsel_pid"])!=Convert.ToInt32(drive["pid"]))throw new Exception("UF2 MCU와 BOOTSEL 장치가 다릅니다. RP2040/RP2350을 서로 바꿔 쓸 수 없습니다.");}
        internal static List<Obj> BootselDrives() {
            var result=new List<Obj>();
            using(var search=new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
            using(var disks=search.Get()) foreach(ManagementObject disk in disks) using(disk) {
                string usb=UsbAncestor(Convert.ToString(disk["PNPDeviceID"]));
                if(usb=="") continue;
                string location=SerialFlash.Location(usb); if(location=="") continue;
                using(var partitions=disk.GetRelated("Win32_DiskPartition")) foreach(ManagementObject partition in partitions) using(partition)
                using(var volumes=partition.GetRelated("Win32_LogicalDisk")) foreach(ManagementObject volume in volumes) using(volume) {
                    string root=Convert.ToString(volume["DeviceID"])+"\\";
                    string mcu;int pid=usb.IndexOf("PID_000F",StringComparison.OrdinalIgnoreCase)>=0?15:3;
                    try { mcu=VolumeMcu(root);if((mcu=="rp2350")!=(pid==15))continue; } catch(IOException) {continue;} catch(UnauthorizedAccessException){continue;}
                    var volumeName=new StringBuilder(128);if(!GetVolumeNameForVolumeMountPoint(root,volumeName,volumeName.Capacity))continue;
                    result.Add(new Obj{{"kind","bootsel"},{"mcu",mcu},{"device",root},{"volume",volumeName.ToString()},{"location",location},{"usb_instance",usb},{"vid",0x2e8a},{"pid",pid},
                        {"label",root+" | "+mcu.ToUpperInvariant()+" BOOTSEL"},{"details",mcu.ToUpperInvariant()+" BOOTSEL | "+location}});
                }
            }
            return result;
        }
        internal static List<Obj> Devices()=>SerialFlash.Ports().Concat(BootselDrives()).ToList();
        internal static Obj Match(IEnumerable<Obj> candidates,string location) {
            if(location=="") throw new Exception("USB 물리 위치가 없어 자동 설치할 수 없습니다.");
            var matches=candidates.Where(p=>Json.S(p,"location")==location).ToList();
            if(matches.Count>1) throw new Exception("동일 USB 위치의 대상이 여러 개입니다.");
            return matches.SingleOrDefault();
        }
        internal static Obj Run(Obj request,Action<string> log) {
            if(!Monitor.TryEnter(SerialFlash.Gate)) throw new Exception("다른 업로드가 진행 중입니다.");
            try {
                byte[] image=InstallImage.Snapshot(request,out Obj info);
                if(Json.S(info,"method")!="bootsel")throw new Exception("BOOTSEL용 이미지가 아닙니다.");bool full=Json.B(info,"replace_bootloader");
                var selected=Json.D(request,"port"); string location=Json.S(selected,"location");
                bool manual=full&&IsKatapult(selected);
                if((!CanInstall(selected)&&!manual) || location=="") throw new Exception("Klipper 네이티브 USB 또는 RP2040/RP2350 BOOTSEL 장치를 선택하세요. CH340은 지원하지 않습니다.");
                Obj drive=null;
                if(IsBootsel(selected)) {
                    drive=Match(BootselDrives(),location);
                    if(drive==null || Json.S(drive,"usb_instance")!=Json.S(selected,"usb_instance") || Json.S(drive,"device")!=Json.S(selected,"device")) throw new Exception("선택한 BOOTSEL 장치가 변경되었습니다.");
                    CheckTarget(info,drive);
                } else if(manual){
                    var actual=Match(SerialFlash.Ports(),location);
                    if(!IsKatapult(actual)||Json.S(actual,"device")!=Json.S(selected,"device")||Json.S(actual,"serial_number")!=Json.S(selected,"serial_number"))throw new Exception("선택한 Katapult USB 장치가 변경되었습니다.");
                    drive=BootselEntry.WaitManual(()=>{var found=Match(BootselDrives(),location);if(found!=null)CheckTarget(info,found);return found;},log);
                } else {
                    var actual=Match(SerialFlash.Ports(),location);
                    if(actual==null || !CanInstall(actual) || Json.S(actual,"device")!=Json.S(selected,"device") || Json.S(actual,"serial_number")!=Json.S(selected,"serial_number")) throw new Exception("선택한 USB COM 장치가 변경되었습니다.");
                    log("1200 baud 요청: "+Json.S(actual,"device"));
                    var port=new SerialPort(Json.S(actual,"device"),1200){DtrEnable=true,ReadTimeout=1000,WriteTimeout=1000};DateTime? katapultSince=null;
                    drive=BootselEntry.Enter(()=>{log("Serial 포트 열기 (1200 baud)");port.Open();Thread.Sleep(100);log("DTR 해제 요청");port.DtrEnable=false;},()=>port.Dispose(),()=>{
                        var found=Match(BootselDrives(),location);
                        if(found!=null){CheckTarget(info,found);return found;}
                        if(SerialFlash.Ports().Any(p=>Json.S(p,"location")==location&&Json.S(p,"vid")=="7504"&&Json.S(p,"pid")=="24951")) {
                            if(!katapultSince.HasValue)katapultSince=DateTime.UtcNow;
                            else if((DateTime.UtcNow-katapultSince.Value).TotalSeconds>=5)throw new Exception("기존 Katapult로 진입했습니다. 재설치하려면 보드를 BOOTSEL 모드로 전환하세요. UF2는 쓰지 않았습니다.");
                        }else katapultSince=null;
                        return null;
                    },log);
                }
                if(drive==null) throw new Exception("동일 USB 위치에서 RP2040/RP2350 BOOTSEL을 찾지 못했습니다. UF2는 쓰지 않았습니다.");
                string bootIdentity=Json.S(drive,"usb_instance");
                drive=BootselReadiness.Wait(()=>{var found=Match(BootselDrives(),location);if(found==null)return null;if(Json.S(found,"usb_instance")!=bootIdentity)throw new Exception("대기 중 다른 BOOTSEL 장치로 교체됐습니다.");CheckTarget(info,found);if(VolumeMcu(Json.S(found,"volume"))!=Json.S(info,"mcu"))throw new Exception("BOOTSEL MCU 불일치");return found;},log);
                var checkedDrive=Match(BootselDrives(),location);
                if(checkedDrive==null || Json.S(checkedDrive,"device")!=Json.S(drive,"device") || Json.S(checkedDrive,"usb_instance")!=Json.S(drive,"usb_instance") || Json.S(checkedDrive,"volume")!=Json.S(drive,"volume")) throw new Exception("전송 직전 BOOTSEL 장치가 변경되었습니다.");
                CheckTarget(info,checkedDrive);string root=Json.S(drive,"volume");if(VolumeMcu(root)!=Json.S(info,"mcu"))throw new Exception("전송 직전 BOOTSEL MCU가 변경되었습니다.");
                log((full?"Klipper":"Katapult")+" UF2 전송: "+root+" · "+location);
                try { using(var target=new FileStream(Path.Combine(root,"katapult.uf2"),FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){target.Write(image,0,image.Length);target.Flush(true);} }
                catch(Exception ex) {throw new Exception("UF2 전송 결과가 불확실합니다. 보드 상태를 확인하세요. 자동 재전송하지 않습니다.",ex);}
                log(full?"Klipper UF2 전송 완료. 기록 시작 주소: 0x10000000.":"Katapult UF2 전송 완료. 오프셋에 맞는 Klipper 펌웨어를 설치하세요.");
                bool verified=false;
                if(Json.S(info,"communication")=="USB") {
                    DateTime deadline=DateTime.UtcNow.AddSeconds(25);
                    while(DateTime.UtcNow<deadline) {
                        string expectedPid=full?"24910":"24951";
                        var port=Match(SerialFlash.Ports().Where(p=>Json.S(p,"vid")=="7504"&&Json.S(p,"pid")==expectedPid),location);
                        if(port!=null) {
                            if(full){verified=true;break;}
                            Thread.Sleep(1000);
                            var current=Match(SerialFlash.Ports(),location);
                            if(current==null || Json.S(current,"device")!=Json.S(port,"device") || Json.S(current,"pid")!="24951") throw new Exception("설치 후 장치가 변경되었습니다.");
                            try {
                                using(var serial=new SerialPort(Json.S(port,"device"),250000){ReadTimeout=250,WriteTimeout=3000,DtrEnable=true}) {
                                    serial.Open();using(var link=new ComLink(serial)) {
                                        var reply=SerialFlash.Command(link,0x11);
                                        if(reply.Length<13 || Encoding.UTF8.GetString(reply,12,reply.Length-12).Split('\0')[0]!=Json.S(info,"mcu") || U.V32(reply,4)!=Convert.ToUInt32(info["application_start"])) throw new Exception("설치 후 Katapult MCU/주소가 일치하지 않습니다.");
                                        verified=true;break;
                                    }
                                }
                            } catch(UnauthorizedAccessException){} catch(IOException){}
                        }
                        Thread.Sleep(500);
                    }
                }
                log(full?(verified?"Klipper USB 재인식 확인 (펌웨어 전체 읽기/동작 검증은 아님).":"UF2 전송 완료 / Klipper 재인식 미확인. 보드 상태를 확인하세요."):(verified?"Katapult "+Json.S(info,"mcu")+" 연결 및 애플리케이션 주소 확인 완료 (플래시 전체 읽기 검증은 아님).":"UF2 전송은 완료됐지만 Katapult 실행 확인은 하지 못했습니다. 통신 설정과 보드 상태를 확인하세요."));
                return new Obj{{"transferred",true},{"katapult_connected",!full&&verified},{"application_usb_detected",full&&verified},{"image",info}};
            } finally {Monitor.Exit(SerialFlash.Gate);}
        }
    }
}
