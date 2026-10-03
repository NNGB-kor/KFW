using System;using System.IO;using System.Diagnostics;using System.Threading;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 static class BootselEntry {
  // A serial disconnect is not proof of success. Only the caller's physical-location
  // and MCU checked probe can complete entry. Never repeat the reboot request.
  internal static Obj Enter(Action request,Action close,Func<Obj> probe,Action<string> log,Func<double> clock=null,Action<int> sleep=null){
   string serialError=null;
   try{request();}
   catch(IOException ex){serialError=ex.Message;log("1200 baud 요청 중 Serial 단절: "+ex.Message+" · BOOTSEL 전환 여부를 확인합니다.");}
   finally{try{close();}catch(IOException ex){serialError=ex.Message;log("Serial 포트 닫기 중 단절: "+ex.Message+" · 새 BOOTSEL 장치 확인이 필요합니다.");}}
   return Wait(probe,log,30,serialError,clock,sleep);
  }
  internal static Obj WaitManual(Func<Obj> probe,Action<string> log,Func<double> clock=null,Action<int> sleep=null){
   log("Katapult는 BOOTSEL 자동 전환을 지원하지 않습니다. BOOTSEL 버튼을 누른 채 USB를 같은 포트에 다시 연결하세요. 120초 동안 기다립니다. 아직 기록하지 않습니다.");
   return Wait(probe,log,120,null,clock,sleep);
  }
  static Obj Wait(Func<Obj> probe,Action<string> log,int seconds,string serialError,Func<double> clock,Action<int> sleep){
   var watch=Stopwatch.StartNew();clock=clock??(()=>watch.Elapsed.TotalSeconds);sleep=sleep??Thread.Sleep;
   double begin=clock(),lastLog=begin;
   log("Serial → BOOTSEL 전환 대기 · 같은 USB 위치의 저장 장치를 찾습니다 (최대 "+seconds+"초).");
   while(clock()-begin<seconds){
    Obj found=probe();if(found!=null){log("같은 USB 위치의 BOOTSEL 확인 · 볼륨 준비 확인으로 이동합니다.");return found;}
    if(clock()-lastLog>=5){log("BOOTSEL 장치 대기 중 · Serial 재요청/펌웨어 기록 없음");lastLog=clock();}sleep(500);
   }
   throw new TimeoutException("동일 USB 위치에서 BOOTSEL을 찾지 못했습니다. 펌웨어를 기록하지 않았습니다."+(serialError==null?"":" Serial 오류: "+serialError));
  }
 }
}
