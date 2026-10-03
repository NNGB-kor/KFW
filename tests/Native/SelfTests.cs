using System;using System.IO;using System.Linq;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 sealed class EmulatedLink:IFlashLink {
  public string Mcu;public uint Address;public bool Corrupt;public List<byte> Commands=new List<byte>();Dictionary<uint,byte[]> memory=new Dictionary<uint,byte[]>();Queue<byte[]> received=new Queue<byte[]>();
  public void Write(byte[] data){if(SerialFlash.Crc(data.Skip(2).Take(data.Length-6))!=BitConverter.ToUInt16(data,data.Length-4))throw new Exception("Request CRC");byte cmd=data[2];Commands.Add(cmd);byte[] payload;
   switch(cmd){case 0x11:payload=BitConverter.GetBytes(0x00010100).Concat(BitConverter.GetBytes(Address)).Concat(BitConverter.GetBytes(64)).Concat(System.Text.Encoding.UTF8.GetBytes(Mcu+"\0emulated-v1\0")).ToArray();break;
    case 0x12:uint at=U.V32(data,4);memory[at]=data.Skip(8).Take(64).ToArray();payload=BitConverter.GetBytes(at);break;
    case 0x13:payload=BitConverter.GetBytes(1);break;
    case 0x14:uint location=U.V32(data,4);byte[] block=(byte[])memory[location].Clone();if(Corrupt)block[0]^=1;payload=BitConverter.GetBytes(location).Concat(block).ToArray();break;
    case 0x15:payload=new byte[0];break;
    default:throw new Exception("Unknown emulator command");
   }var body=BitConverter.GetBytes((uint)cmd).Concat(payload).ToList();while(body.Count%4!=0)body.Add(0);received.Enqueue(SerialFlash.Frame(0xa0,body.ToArray()));
  }
  public byte[] ReadFrame()=>received.Dequeue();public void Discard()=>received.Clear();public void Dispose(){}
 }
 static class SelfTests {
  public static Obj Protocol(string file){var info=Safety.Inspect(file);var bytes=File.ReadAllBytes(file);var good=new EmulatedLink{Mcu=Json.S(info,"mcu"),Address=Convert.ToUInt32(info["application_start"])};SerialFlash.Transfer(good,bytes,info,false,_=>{});if(!good.Commands.Contains(0x12)||!good.Commands.Contains(0x14)||!good.Commands.Contains(0x15))throw new Exception("Success protocol incomplete");var check=new EmulatedLink{Mcu=good.Mcu,Address=good.Address};SerialFlash.Transfer(check,bytes,info,true,_=>{});if(check.Commands.Contains(0x12)||check.Commands.Contains(0x15))throw new Exception("Check-only wrote/reset");foreach(var failed in new[]{new EmulatedLink{Mcu="wrong-mcu",Address=good.Address},new EmulatedLink{Mcu=good.Mcu,Address=good.Address+16384},new EmulatedLink{Mcu=good.Mcu,Address=good.Address,Corrupt=true}}){bool caught=false;try{SerialFlash.Transfer(failed,bytes,info,false,_=>{});}catch{caught=true;}if(!caught||failed.Commands.Contains(0x15)||!failed.Corrupt&&failed.Commands.Contains(0x12))throw new Exception("Unsafe emulator outcome");}return new Obj{{"success",true},{"upload_and_readback",true},{"mcu_mismatch_blocked",true},{"offset_mismatch_blocked",true},{"readback_failure_no_reset",true},{"check_only_no_write",true},{"physical_device_opened",false}};}
 }
}
