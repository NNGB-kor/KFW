using System;using System.Collections.Generic;using System.Linq;using System.Text.RegularExpressions;
namespace KlipperWinForms {
 sealed class DfuSector {internal uint Address;internal int Size;}
 sealed class Stm32DfuProfile {
  internal string Mcu,Geometry;internal uint CapacityAddress,RamStart;internal int RamSize,WriteUnit;internal int[] Capacities,Offsets;
  static Stm32DfuProfile P(string mcu,string geometry,uint capacity,int ram,int unit,int[] sizes,int[] offsets,uint ramStart=0x20000000)=>new Stm32DfuProfile{Mcu=mcu,Geometry=geometry,CapacityAddress=capacity,RamStart=ramStart,RamSize=ram,WriteUnit=unit,Capacities=sizes,Offsets=offsets};
  static readonly Stm32DfuProfile[] Profiles={
   P("stm32f042x6","1k",0x1ffff7cc,6144,4,new[]{32},new[]{4096,8192}),
   P("stm32f072xb","2k",0x1ffff7cc,16384,4,new[]{128},new[]{4096,8192}),
   P("stm32f207xx","f4",0x1fff7a22,131072,4,new[]{256,512,768,1024},new[]{16384,32768}),
   P("stm32f401xc","f4",0x1fff7a22,65536,4,new[]{128,256,512},new[]{16384,32768}),
   P("stm32f405xx","f4",0x1fff7a22,131072,4,new[]{512,1024},new[]{16384,32768}),
   P("stm32f407xx","f4",0x1fff7a22,131072,4,new[]{512,1024},new[]{16384,32768}),
   P("stm32f429xx","f4",0x1fff7a22,131072,4,new[]{512,1024,2048},new[]{16384,32768}),
   P("stm32f446xx","f4",0x1fff7a22,131072,4,new[]{256,512},new[]{16384,32768}),
   P("stm32g0b0xx","2k",0x1fff75e0,147456,8,new[]{128,256,512},new[]{4096,8192}),
   P("stm32g0b1xx","2k",0x1fff75e0,147456,8,new[]{128,256,512},new[]{4096,8192}),
   P("stm32g431xx","2k",0x1fff75e0,32768,8,new[]{32,64,128},new[]{8192}),
   P("stm32h723xx","128k",0x1ff1e880,131072,32,new[]{512,1024},new[]{131072},0x24000000),
   P("stm32h743xx","128k",0x1ff1e880,131072,32,new[]{1024,2048},new[]{131072},0x24000000),
   P("stm32h750xx","128k",0x1ff1e880,131072,32,new[]{128},new[]{32768},0x24000000)
  };
  internal static IEnumerable<Stm32DfuProfile> All=>Profiles;
  internal static Stm32DfuProfile For(string mcu)=>Profiles.SingleOrDefault(p=>p.Mcu==mcu)??throw new Exception("현재 공통 지원 목록의 ROM USB DFU 모델이 아닙니다: "+mcu);
  internal void Check(List<DfuSector> sectors,int capacity){
   if(!Capacities.Contains(capacity/1024)||capacity%1024!=0||sectors.Sum(s=>(long)s.Size)!=capacity)throw new Exception("공장 플래시 용량/DFU 구성이 선택 MCU와 다릅니다. 쓰지 않았습니다.");
   foreach(var sector in sectors){int expected=Geometry=="1k"?1024:Geometry=="2k"?2048:Geometry=="128k"?131072:0;
    if(Geometry=="f4"){uint bankSpan=Mcu=="stm32f429xx"&&capacity==1048576&&sectors.Any(s=>s.Address==0x08080000&&s.Size==16384)?0x80000u:0x100000u;uint offset=(sector.Address-0x08000000)%bankSpan;expected=offset<0x10000?16384:offset<0x20000?65536:131072;}
    if(sector.Size!=expected||(sector.Address-0x08000000)%expected!=0)throw new Exception("MCU와 DFU 섹터 크기/경계가 다릅니다. 쓰지 않았습니다.");
   }
  }
  internal static List<DfuSector> Parse(string memory){
   string[] fields=memory.Split('/');if(fields.Length<3||fields.Length%2!=1||!fields[0].Trim().Equals("@Internal Flash",StringComparison.OrdinalIgnoreCase))throw new Exception("지원하지 않는 DFU 메모리 구성");
   var sectors=new List<DfuSector>();ulong next=0x08000000;
   for(int i=1;i<fields.Length;i+=2){if(!Regex.IsMatch(fields[i].Trim(),@"^0x[0-9a-fA-F]{8}$")||Convert.ToUInt32(fields[i].Trim().Substring(2),16)!=next)throw new Exception("DFU 플래시 영역의 틈/중복/주소 오류");
    foreach(string segment in fields[i+1].Split(',')){var match=Regex.Match(segment.Trim(),@"^(\d+)\s*\*\s*(\d+)\s*([BKM])g$",RegexOptions.IgnoreCase);if(!match.Success)throw new Exception("DFU 섹터 권한/형식 오류");long count=long.Parse(match.Groups[1].Value),size=long.Parse(match.Groups[2].Value);size*=match.Groups[3].Value.ToUpperInvariant()=="M"?1048576:match.Groups[3].Value.ToUpperInvariant()=="K"?1024:1;
     if(count<1||count>4096||size<1024||size>131072||next+(ulong)(count*size)>0x08200000)throw new Exception("DFU 메모리 구성 범위 초과");
     for(int n=0;n<count;n++){sectors.Add(new DfuSector{Address=(uint)next,Size=(int)size});next+=(uint)size;}
    }
   }
   return sectors;
  }
  internal static List<DfuSector> ErasePlan(List<DfuSector> sectors,int length,uint app){
   if(length<64||app<0x08000000+(uint)length||!sectors.Any(s=>app>=s.Address&&(ulong)app+4<=s.Address+(ulong)s.Size))throw new Exception("Katapult와 애플리케이션 시작점이 실제 플래시에 맞지 않습니다.");
   return sectors.Where(s=>s.Address<0x08000000+(uint)length||app>=s.Address&&(ulong)app<s.Address+(ulong)s.Size).ToList();
  }
 }
}
