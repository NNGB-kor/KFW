using System;using System.IO;using System.Diagnostics;using System.Threading;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 static class BootselReadiness {
  internal static Obj Wait(Func<Obj> probe,Action<string> log,Func<double> clock=null,Action<int> sleep=null){
   var watch=Stopwatch.StartNew();clock=clock??(()=>watch.Elapsed.TotalSeconds);sleep=sleep??Thread.Sleep;
   double begin=clock(),since=begin,lastLog=begin;string stable=null;
   log("BOOTSEL 인식 · USB/볼륨 준비를 최소 5초 기다립니다 (최대 45초). 아직 기록하지 않습니다.");
   while(clock()-begin<45){double now=clock();Obj item=null;
    try{item=probe();}catch(IOException){stable=null;}catch(UnauthorizedAccessException){stable=null;}
    if(item!=null){string key=Json.S(item,"usb_instance")+"|"+Json.S(item,"volume")+"|"+Json.S(item,"device");if(stable!=key){stable=key;since=now;}if(now-begin>=5&&now-since>=2){log("BOOTSEL 볼륨 준비 완료 · 한 번만 전송합니다.");return item;}}
    else stable=null;
    if(now-lastLog>=5){log("BOOTSEL 볼륨 준비 대기 중 · 펌웨어 기록 전");lastLog=now;}sleep(1000);
   }
   throw new TimeoutException("BOOTSEL 볼륨이 안정적으로 준비되지 않았습니다. 펌웨어는 기록하지 않았습니다.");
  }
 }
}
