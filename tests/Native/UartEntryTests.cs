using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 static class UartEntryTests {
  sealed class Link:IFlashLink {
   internal readonly EmulatedLink Board=new EmulatedLink{Mcu="stm32f407xx",Address=0x08008000};
   internal bool Awake,NeverRespond,BadCrc,IoFailure,WriteFailure;internal int Requests,Probes;bool pending;
   public void Write(byte[] data){
    if(WriteFailure)throw new TimeoutException("write stalled");
    if(data.SequenceEqual(Encoding.ASCII.GetBytes("~ \x1c Request Serial Bootloader!! ~"))){Requests++;Awake=!NeverRespond;return;}
    if(data[2]==0x11)Probes++;if(Awake){Board.Write(data);pending=true;}
   }
   public byte[] ReadFrame(){if(IoFailure)throw new IOException("port gone");if(!pending||NeverRespond)throw new TimeoutException("no reply");pending=false;var data=Board.ReadFrame();if(BadCrc)data[data.Length-4]^=1;return data;}
   public void Discard(){pending=false;Board.Discard();}public void Dispose(){}
  }
  internal static Obj Run(){
   var results=new Obj();byte[] data=Enumerable.Range(0,150).Select(n=>(byte)n).ToArray();
   var info=new Obj{{"mcu","stm32f407xx"},{"application_start",0x08008000},{"sha256",U.Hash(data)}};
   foreach(string name in new[]{"already_katapult","enter_from_klipper","no_response","wrong_mcu","wrong_offset","bad_crc","port_io_failure","port_write_timeout","identity_changed"}){
    var link=new Link{Awake=name!="enter_from_klipper"&&name!="no_response"&&name!="identity_changed",NeverRespond=name=="no_response",BadCrc=name=="bad_crc",IoFailure=name=="port_io_failure",WriteFailure=name=="port_write_timeout"};
    if(name=="wrong_mcu")link.Board.Mcu="rp2040";if(name=="wrong_offset")link.Board.Address+=16384;
    bool succeeded=false;int identities=0;
    try{var reply=SerialFlash.ConnectUart(link,()=>{identities++;if(name=="identity_changed")throw new Exception("changed target");},_=>{},_=>{});var result=SerialFlash.Transfer(link,data,info,false,_=>{},reply);succeeded=Json.B(result,"verified");}catch(Exception){ }
    bool expected=name=="already_katapult"||name=="enter_from_klipper";
    if(succeeded!=expected||link.Requests!=(name=="enter_from_klipper"||name=="no_response"?1:0)||!expected&&link.Board.Commands.Contains(0x12))throw new Exception("Unsafe UART case: "+name);
    if(name=="already_katapult"&&link.Probes!=1||name=="enter_from_klipper"&&(identities!=2||link.Probes!=3))throw new Exception("Unexpected connection attempts: "+name);
    results[name]=new Obj{{"success",succeeded},{"entry_requests",link.Requests},{"connect_attempts",link.Probes},{"firmware_written",link.Board.Commands.Contains(0x12)}};
   }
   return new Obj{{"success",true},{"cases",results},{"physical_device_opened",false}};
  }
 }
}
