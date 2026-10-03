using System;using System.IO;using System.Linq;using System.Text;using System.ComponentModel;using System.Collections.Generic;using System.Runtime.InteropServices;using Microsoft.Win32;using Microsoft.Win32.SafeHandles;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 sealed class WinDfuUsb:IDfuUsb {
  [StructLayout(LayoutKind.Sequential)]struct DevInfo{public int Size;public Guid ClassGuid;public uint DevInst;public IntPtr Reserved;}
  [StructLayout(LayoutKind.Sequential)]struct IfInfo{public int Size;public Guid ClassGuid;public uint Flags;public IntPtr Reserved;}
  [StructLayout(LayoutKind.Sequential,Pack=1)]struct Setup{public byte Type,Request;public ushort Value,Index,Length;}
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode)]static extern IntPtr SetupDiGetClassDevs(ref Guid guid,IntPtr enumerator,IntPtr hwnd,uint flags);
  [DllImport("setupapi.dll")]static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr dev,ref Guid guid,uint index,ref IfInfo info);
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode)]static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set,ref IfInfo info,IntPtr detail,uint size,out uint required,ref DevInfo dev);
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode)]static extern bool SetupDiGetDeviceInstanceId(IntPtr set,ref DevInfo info,StringBuilder id,uint size,out uint required);
  [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint create,uint flags,IntPtr template);
  [DllImport("winusb.dll",SetLastError=true)]static extern bool WinUsb_Initialize(SafeFileHandle file,out IntPtr usb);
  [DllImport("winusb.dll",SetLastError=true)]static extern bool WinUsb_ControlTransfer(IntPtr usb,Setup setup,byte[] data,uint length,out uint transferred,IntPtr overlapped);
  [DllImport("winusb.dll",SetLastError=true)]static extern bool WinUsb_SetCurrentAlternateSetting(IntPtr usb,byte alternate);
  [DllImport("winusb.dll",SetLastError=true)]static extern bool WinUsb_SetPipePolicy(IntPtr usb,byte pipe,uint policy,uint length,ref uint value);
  [DllImport("winusb.dll")]static extern bool WinUsb_Free(IntPtr usb);
  SafeFileHandle File;IntPtr Usb;
  static string InterfacePath(string instance,IEnumerable<string> guids){foreach(string text in guids){if(!Guid.TryParse(text,out Guid guid))continue;IntPtr set=SetupDiGetClassDevs(ref guid,IntPtr.Zero,IntPtr.Zero,0x12);if(set==new IntPtr(-1))continue;try{for(uint i=0;;i++){var iface=new IfInfo{Size=Marshal.SizeOf(typeof(IfInfo))};if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref iface))break;var dev=new DevInfo{Size=Marshal.SizeOf(typeof(DevInfo))};SetupDiGetDeviceInterfaceDetail(set,ref iface,IntPtr.Zero,0,out uint required,ref dev);if(required<8||required>65536)continue;IntPtr buffer=Marshal.AllocHGlobal((int)required);try{Marshal.WriteInt32(buffer,IntPtr.Size==8?8:6);if(!SetupDiGetDeviceInterfaceDetail(set,ref iface,buffer,required,out required,ref dev))continue;var id=new StringBuilder(1024);if(SetupDiGetDeviceInstanceId(set,ref dev,id,1024,out uint n)&&id.ToString().Equals(instance,StringComparison.OrdinalIgnoreCase))return Marshal.PtrToStringUni(IntPtr.Add(buffer,4));}finally{Marshal.FreeHGlobal(buffer);}}}finally{SetupDiDestroyDeviceInfoList(set);}}return "";}
  internal static List<Obj> Devices(){var rows=new List<Obj>();using(var root=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB")){if(root==null)return rows;foreach(string name in root.GetSubKeyNames().Where(n=>n.StartsWith("VID_0483&PID_DF11",StringComparison.OrdinalIgnoreCase)))using(var group=root.OpenSubKey(name))foreach(string serial in group.GetSubKeyNames())using(var key=group.OpenSubKey(serial))using(var parameters=key.OpenSubKey("Device Parameters")){
   string id="USB\\"+name+"\\"+serial;var raw=parameters?.GetValue("DeviceInterfaceGUIDs");var guids=raw as string[]??new[]{Convert.ToString(raw)};string path=InterfacePath(id,guids);string location=SerialFlash.Location(id);if(location=="")continue;
   // Present-only devnode lookup prevents stale registry entries from appearing.
   if(!Present(id))continue;
   rows.Add(new Obj{{"kind","dfu"},{"device",id},{"instance",id},{"usb_path",path},{"location",location},{"vid",0x0483},{"pid",0xdf11},{"serial_number",serial},{"label","STM32 DFU | "+serial},{"details",location+" | "+(path==""?"WinUSB 드라이버 연결 필요":"WinUSB")}});
  }}return rows;}
  [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern int CM_Locate_DevNodeW(out uint node,string id,uint flags);
  static bool Present(string id)=>CM_Locate_DevNodeW(out uint node,id,0)==0;
  internal WinDfuUsb(Obj selected){var matches=Devices().Where(p=>Json.S(p,"instance")==Json.S(selected,"instance")&&Json.S(p,"location")==Json.S(selected,"location")).ToList();if(matches.Count!=1)throw new Exception("선택한 DFU 장치가 변경됐습니다.");string path=Json.S(matches[0],"usb_path");if(path=="")throw new Exception("STM32 DFU 장치에 WinUSB 드라이버가 필요합니다. 드라이버를 자동 변경하지 않습니다.");File=CreateFile(path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);if(File.IsInvalid||!WinUsb_Initialize(File,out Usb)){Dispose();throw new Exception("DFU WinUSB 열기 실패. 해당 STM32 BOOTLOADER 장치의 WinUSB 드라이버와 점유 상태를 확인하세요.");}uint timeout=5000;WinUsb_SetPipePolicy(Usb,0,3,4,ref timeout);}
  public byte[] Control(byte type,byte request,ushort value,ushort index,byte[] data,int length){byte[] buffer=(type&128)!=0?new byte[length]:data;var setup=new Setup{Type=type,Request=request,Value=value,Index=index,Length=checked((ushort)length)};if(!WinUsb_ControlTransfer(Usb,setup,buffer,(uint)length,out uint count,IntPtr.Zero))throw new IOException("WinUSB control 실패: "+new Win32Exception(Marshal.GetLastWin32Error()).Message);if((type&128)==0&&count!=length)throw new IOException("USB 전송 길이 불일치");return buffer.Take((int)count).ToArray();}
  public void Select(byte iface,byte alternate){if(iface!=0)throw new Exception("DFU interface 0만 지원합니다.");if(!WinUsb_SetCurrentAlternateSetting(Usb,alternate))throw new IOException("DFU alternate 선택 실패");}
  public void Dispose(){if(Usb!=IntPtr.Zero){WinUsb_Free(Usb);Usb=IntPtr.Zero;}File?.Dispose();}
 }
}
