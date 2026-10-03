using System;using System.Linq;using System.IO;using System.IO.Ports;using System.Threading;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 static class Stm32Install {
  internal static bool CanInstall(Obj p)=>p!=null&&(Json.S(p,"kind")=="dfu"||Json.S(p,"vid")=="7504"&&Json.S(p,"pid")=="24910");
  internal static Obj Run(Obj request,Action<string> log){if(!Monitor.TryEnter(SerialFlash.Gate))throw new Exception("다른 업로드가 진행 중입니다.");try{
   byte[] image=InstallImage.Snapshot(request,out Obj info);if(Json.S(info,"method")!="dfu")throw new Exception("STM32 DFU BIN이 아닙니다.");var selected=Json.D(request,"port");string location=Json.S(selected,"location");if(!CanInstall(selected)||location=="")throw new Exception("STM32 DFU 또는 Klipper 네이티브 USB 장치를 선택하세요.");
   Obj target=null;if(Json.S(selected,"kind")=="dfu"){target=Rp2040Install.Match(WinDfuUsb.Devices(),location);if(target==null||Json.S(target,"instance")!=Json.S(selected,"instance"))throw new Exception("DFU 대상이 변경되었습니다.");}else{
    var actual=Rp2040Install.Match(SerialFlash.Ports(),location);if(actual==null||!CanInstall(actual)||Json.S(actual,"device")!=Json.S(selected,"device")||Json.S(actual,"serial_number")!=Json.S(selected,"serial_number"))throw new Exception("USB 장치가 변경되었습니다.");
    log("1200 baud/DTR: "+Json.S(actual,"device"));using(var serial=new SerialPort(Json.S(actual,"device"),1200){DtrEnable=true}){serial.Open();Thread.Sleep(100);try{serial.DtrEnable=false;}catch(IOException){}}
   }
   DateTime end=DateTime.UtcNow.AddSeconds(30);while(target==null&&DateTime.UtcNow<end){target=Rp2040Install.Match(WinDfuUsb.Devices(),location);if(target!=null)break;if(SerialFlash.Ports().Any(p=>Json.S(p,"location")==location&&Json.S(p,"pid")=="24951"))throw new Exception("기존 Katapult로 진입했습니다. STM32 ROM DFU로 수동 진입하세요. 쓰지 않았습니다.");Thread.Sleep(500);}
   if(target==null)throw new Exception("동일 USB 위치에서 STM32 DFU를 찾지 못했습니다. BOOT0/DFU 진입 상태를 확인하세요.");
   using(var usb=new WinDfuUsb(target)){var result=new DfuProtocol(usb,log).Install(image,info);log("DFU 기록/읽기 검증 완료. BOOT0를 해제하고 장치 목록을 갱신하세요.");return result;}
  }finally{Monitor.Exit(SerialFlash.Gate);}}
 }
}
