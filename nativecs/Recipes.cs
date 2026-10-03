using System;using System.IO;using System.Text;using System.Linq;using System.Collections.Generic;using System.Text.RegularExpressions;
namespace KlipperWinForms {
 sealed class Recipes {
  Dictionary<string,string> values=new Dictionary<string,string>();HashSet<string> immediate=new HashSet<string>();
  public Recipes(Dictionary<string,string> initial){foreach(var p in initial){values[p.Key]=p.Value;immediate.Add(p.Key);}}
  public string Get(string name)=>Expand("$("+name+")");
  public string Expand(string text,int depth=0){if(depth>60)throw new Exception("Recursive Make variable");while(text.Contains("$(")){string result=Regex.Replace(text,@"\$\(([^()]*)\)",m=>{string key=m.Groups[1].Value;if(key.StartsWith("patsubst ")){var match=Regex.Match(key,"^patsubst\\s+\"%\",\\s*%,\\s*(.*)$");if(!match.Success)throw new Exception("Unsupported Make expression "+key);return match.Groups[1].Value.Trim().Trim('"');}if(key.StartsWith("shell echo ")){string s=key.Substring(11),upper=" | tr a-z A-Z | tr X x";if(s.EndsWith(upper))return s.Substring(0,s.Length-upper.Length).Trim().Trim('"').ToUpperInvariant().Replace('X','x');if(s.IndexOfAny("|;&`$".ToCharArray())>=0)throw new Exception("Unsupported shell expression");return s.Trim().Trim('"');}if(key.Contains(" ")||key.Contains(","))throw new Exception("Unsupported Make expression: "+key);if(!values.ContainsKey(key))return "";return immediate.Contains(key)?values[key]:Expand(values[key],depth+1);});if(result==text)throw new Exception("Malformed Make variable: "+text);text=result;}return text;}
  public void Read(string text){foreach(var raw in Regex.Replace(text,@"\\\r?\n\s*"," ").Split('\n')){if(raw.StartsWith("\t")||raw.Trim()==""||raw.TrimStart().StartsWith("#"))continue;var m=Regex.Match(raw,@"^(.+?)\s*(\+=|:=|\?=|=)\s*(.*)$");if(!m.Success){if(raw.Contains(":"))continue;throw new Exception("Unsupported Make line: "+raw);}string key=Expand(m.Groups[1].Value.Trim()),op=m.Groups[2].Value,v=m.Groups[3].Value.Split('#')[0].Trim();if(op=="?="&&values.ContainsKey(key))continue;if(op=="+="&&values.ContainsKey(key))values[key]+=" "+(immediate.Contains(key)?Expand(v):v);else{values[key]=op==":="?Expand(v):v;if(op==":=")immediate.Add(key);else immediate.Remove(key);}}}
  public static Recipes For(KConfig k){var init=k.Symbols.ToDictionary(s=>"CONFIG_"+s.Key,s=>s.Value.Value);init["OUT"]="@OUT@/";foreach(var key in new[]{"CFLAGS","CFLAGS_klipper.elf","CFLAGS_katapult.elf","CFLAGS_deployer.elf","src-y","deployer-y"})init[key]="";init["dirs-y"]="src";var r=new Recipes(init);r.immediate.Remove("src-y");r.immediate.Remove("deployer-y");r.Read(File.ReadAllText(Path.Combine(k.Source,"src/Makefile")));r.Read(File.ReadAllText(Path.Combine(k.Source,"src",k.Get("BOARD_DIRECTORY"),"Makefile")));return r;}
  public static List<string> Flags(KConfig k,Recipes r,string output){string text=Regex.Replace(File.ReadAllText(Path.Combine(k.Source,"Makefile")),@"\\\r?\n\s*"," ");text=string.Join("\n",text.Split('\n').Where(l=>Regex.IsMatch(l,@"^CFLAGS\s*[+:?]?="))).Replace("$(call cc-option,$(CC),-Wtype-limits,)","-Wtype-limits");var common=new Recipes(new Dictionary<string,string>{{"OUT","@OUT@/"},{"CONFIG_OPTIMIZE_FLAG",k.Get("OPTIMIZE_FLAG")}});common.Read(text);return U.Split(common.Get("CFLAGS")+" "+r.Get("CFLAGS")).Select(v=>(v.StartsWith("-flto")?"-flto=1":v).Replace("@OUT@",output)).ToList();}
  public static List<string> Link(Recipes r,string target,string output){
   var flags=U.Split(r.Get("CFLAGS_"+target+".elf")).Select(x=>x.Replace("@OUT@",output)).ToList();
   // newlib may introduce libgcc references (e.g. ffs -> __ffssi2) after libgcc
   // was scanned. Group explicit runtime libraries so ld can resolve both directions.
   // Klipper relies on its upstream LTO/runtime archive ordering. Restrict the
   // Katapult -O0/newlib fix to Katapult and its deployer.
   if(target!="klipper"&&!flags.Contains("-Wl,--start-group")){
    int first=flags.FindIndex(x=>x.StartsWith("-l")&&x.Length>2),last=flags.FindLastIndex(x=>x.StartsWith("-l")&&x.Length>2);
    if(first>=0&&last>first){flags.Insert(last+1,"-Wl,--end-group");flags.Insert(first,"-Wl,--start-group");}
   }
   return flags;
  }
  public static string Linker(Recipes r,string target){var flags=Link(r,target,"@OUT@");int i=flags.IndexOf("-T");if(i<0||!flags[i+1].StartsWith("@OUT@/src/")||!flags[i+1].EndsWith(".ld"))throw new Exception("Unsupported linker source");return flags[i+1].Substring(6,flags[i+1].Length-9)+".lds.S";}
  public static string Stage(KConfig k,Recipes r){var m=Regex.Match(File.ReadAllText(Path.Combine(k.Source,"src/rp2040/Makefile")),@"--script=([^\s]+/)boot_stage2.ld");if(!m.Success)throw new Exception("Missing RP2040 stage2 recipe");return r.Expand(m.Groups[1].Value);}
 }
}
