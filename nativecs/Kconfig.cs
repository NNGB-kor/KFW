using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
namespace KlipperWinForms {
 using Obj=Dictionary<string,object>;
 sealed class KNode {
  public string Id,Name="",Kind="",Prompt="",PromptIf="y",Dep="y",Help="";
  public KNode Parent,Choice; public List<KNode> Children=new List<KNode>();
  public List<string[]> Defaults=new List<string[]>(),Selects=new List<string[]>(),Ranges=new List<string[]>();
 }
 sealed class KSymbol { public string Name,Type="",Value="",User;public List<KNode> Nodes=new List<KNode>(); }
 sealed class KConfig {
  public readonly string Source,Version; public string Title="Firmware Configuration";
  public Dictionary<string,KSymbol> Symbols=new Dictionary<string,KSymbol>();
  public List<KNode> Nodes=new List<KNode>();public KNode Top=new KNode{Kind="root",Dep="y"};
  Dictionary<KNode,string> choices=new Dictionary<KNode,string>();
  public KConfig(string source,string version) { Source=source;Version=version;Parse(Path.Combine(source,"src/Kconfig"),Top);Evaluate(); }
  public string Get(string key) => Symbols.TryGetValue(key,out var s)?s.Value:"";
  static string Unquote(string t) {if(t.StartsWith("\"")&&t.EndsWith("\""))return Regex.Unescape(t.Substring(1,t.Length-2));return t;}
  static string Strip(string t){bool quote=false,esc=false;for(int i=0;i<t.Length;i++){char c=t[i];if(esc){esc=false;continue;}if(c=='\\'){esc=true;continue;}if(c=='"')quote=!quote;if(c=='#'&&!quote)return t.Substring(0,i);}return t;}
  static string[] Conditional(string text) {var m=Regex.Match(text,@"^(""(?:\\.|[^""])*""|\S+)(?:\s+if\s+(.+))?$");if(!m.Success)throw new Exception("Unsupported Kconfig value: "+text);return new[]{m.Groups[1].Value,m.Groups[2].Success?m.Groups[2].Value:"y"};}
  KNode Add(KNode parent,string kind,string name="") {var n=new KNode{Id=Nodes.Count.ToString(),Kind=kind,Name=name,Parent=parent};parent.Children.Add(n);Nodes.Add(n);if(kind=="symbol"){if(!Symbols.ContainsKey(name))Symbols[name]=new KSymbol{Name=name};Symbols[name].Nodes.Add(n);if(parent.Kind=="choice")n.Choice=parent;}return n;}
  void Parse(string file,KNode context) {
   var lines=Regex.Replace(File.ReadAllText(file),@"\\\r?\n\s*"," ").Replace("$(VERSION)",Version).Split('\n');KNode current=null;var stack=new Stack<KNode>();stack.Push(context);
   for(int i=0;i<lines.Length;i++){string raw=lines[i].TrimEnd('\r'),s=Strip(raw).Trim();if(s=="")continue;
    if(s=="help"||s=="---help---"){int indent=raw.TakeWhile(char.IsWhiteSpace).Count();var help=new StringBuilder();while(i+1<lines.Length&&(lines[i+1].Trim()==""||lines[i+1].TakeWhile(char.IsWhiteSpace).Count()>indent)){help.AppendLine(lines[++i].Trim());}if(current!=null)current.Help=help.ToString().Trim();continue;}
    int sp=s.IndexOf(' ');string op=sp<0?s:s.Substring(0,sp),arg=sp<0?"":s.Substring(sp+1).Trim();
    switch(op){
     case "VERSION": if(!s.Contains("make-version.sh"))throw new Exception("Unknown Kconfig expansion");break;
     case "mainmenu":Title=Unquote(arg);break;
     case "source":Parse(U.SafePath(Source,Unquote(arg)),stack.Peek());break;
     case "if":current=Add(stack.Peek(),"if");current.Dep=arg;stack.Push(current);break;
     case "menu":current=Add(stack.Peek(),"group");current.Prompt=Unquote(arg);stack.Push(current);break;
     case "choice":current=Add(stack.Peek(),"choice",arg);choices[current]="";stack.Push(current);break;
     case "endif":case "endmenu":case "endchoice":if(stack.Count<=1)throw new Exception("Unbalanced Kconfig");stack.Pop();current=null;break;
     case "config":case "menuconfig":current=Add(stack.Peek(),"symbol",arg);break;
     case "comment":current=Add(stack.Peek(),"comment");current.Prompt=Unquote(arg);break;
     case "depends":current.Dep="("+current.Dep+") && ("+arg.Substring(3)+")";break;
     case "visible":current.PromptIf="("+current.PromptIf+") && ("+arg.Substring(3)+")";break;
     case "bool":case "tristate":case "int":case "hex":case "string":
      if(current.Kind=="symbol")Symbols[current.Name].Type=op;if(arg!=""){var c=Conditional(arg);current.Prompt=Unquote(c[0]);current.PromptIf=c[1];}break;
     case "prompt":var pr=Conditional(arg);current.Prompt=Unquote(pr[0]);current.PromptIf=pr[1];break;
     case "default":current.Defaults.Add(Conditional(arg));break;
     case "select":current.Selects.Add(Conditional(arg));break;
     case "range":var rr=Regex.Match(arg,@"^(\S+)\s+(\S+)(?:\s+if\s+(.+))?$");current.Ranges.Add(new[]{rr.Groups[1].Value,rr.Groups[2].Value,rr.Groups[3].Success?rr.Groups[3].Value:"y"});break;
     case "optional":break;
     default:throw new Exception("Unsupported Kconfig syntax: "+s+" in "+file);
    }
   }
  }
  string Atom(string t) {if(t.StartsWith("\""))return Unquote(t);if(t=="y"||t=="m"||t=="n"||Regex.IsMatch(t,@"^(0[xX][0-9a-fA-F]+|-?\d+)$"))return t;return Get(t);}
  public bool Test(string expression) {if(string.IsNullOrWhiteSpace(expression))return true;var tokens=Regex.Matches(expression,@"""(?:\\.|[^""])*""|&&|\|\||!=|<=|>=|[!()=<>]|[\w.+/-]+").Cast<Match>().Select(m=>m.Value).ToArray();int p=0;
   Func<string,bool> truth=v=>v=="y"||v=="m";Func<string> term=null;Func<bool> and=null,or=null;
   term=()=>{if(p>=tokens.Length)return "n";string t=tokens[p++];if(t=="!")return truth(term())?"n":"y";if(t=="("){bool v=or();if(p>=tokens.Length||tokens[p++]!=")")throw new Exception("Kconfig expression: "+expression);return v?"y":"n";}string left=Atom(t);if(p<tokens.Length&&new[]{"=","!=","<",">","<=",">="}.Contains(tokens[p])){string op=tokens[p++],right=Atom(tokens[p++]);int cmp;try{cmp=U.Number(left).CompareTo(U.Number(right));}catch{cmp=string.CompareOrdinal(left,right);}bool v=op=="="?left==right:op=="!="?left!=right:op=="<"?cmp<0:op==">"?cmp>0:op=="<="?cmp<=0:cmp>=0;return v?"y":"n";}return left;};
   and=()=>{bool v=truth(term());while(p<tokens.Length&&tokens[p]=="&&"){p++;bool x=truth(term());v=v&&x;}return v;};or=()=>{bool v=and();while(p<tokens.Length&&tokens[p]=="||"){p++;bool x=and();v=v||x;}return v;};bool result=or();if(p!=tokens.Length)throw new Exception("Kconfig tokens remain: "+expression);return result;
  }
  bool Active(KNode n) {if(n==null)return true;return Test(n.Dep)&&Active(n.Parent);}
  bool Visible(KNode n) => n.Prompt!=""&&Active(n)&&Test(n.PromptIf);
  public void Evaluate(){
   foreach(var s in Symbols.Values)s.Value=s.Type=="bool"||s.Type=="tristate"?"n":"";
   for(int pass=0;pass<300;pass++){
    var before=Symbols.ToDictionary(x=>x.Key,x=>x.Value.Value);
    foreach(var pair in choices.ToArray()){var c=pair.Key;var opts=c.Children.Where(n=>n.Kind=="symbol"&&Visible(n)).ToList();string pick="";
     if(Visible(c)){pick=opts.FirstOrDefault(n=>Symbols[n.Name].User=="y")?.Name??"";if(pick==""){foreach(var d in c.Defaults)if(Test(d[1])&&opts.Any(n=>n.Name==d[0])){pick=d[0];break;}}if(pick=="")pick=opts.FirstOrDefault()?.Name??"";}
     choices[c]=pick;foreach(var n in c.Children.Where(n=>n.Kind=="symbol"))Symbols[n.Name].Value=n.Name==pick?"y":"n";
    }
    var forced=new HashSet<string>(Symbols.Values.Where(s=>s.Value=="y"||s.Value=="m").SelectMany(s=>s.Nodes.Where(Active).SelectMany(n=>n.Selects.Where(d=>Test(d[1])).Select(d=>d[0]))));
    foreach(var s in Symbols.Values){if(s.Nodes.Any(n=>n.Choice!=null))continue;bool boolean=s.Type=="bool"||s.Type=="tristate";string value=boolean?"n":"";bool active=s.Nodes.Any(Active),visible=s.Nodes.Any(Visible);
     if(active){if(s.User!=null&&visible)value=s.User;else{bool done=false;foreach(var n in s.Nodes){if(!Active(n))continue;foreach(var d in n.Defaults)if(Test(d[1])){value=Atom(d[0]);done=true;break;}if(done)break;}}
      if(s.Type=="int"||s.Type=="hex"){if(value=="")value="0";foreach(var n in s.Nodes.Where(Active))foreach(var r in n.Ranges)if(Test(r[2])){long v=U.Number(value),min=U.Number(Atom(r[0])),max=U.Number(Atom(r[1]));if(v<min||v>max)value=s.Type=="hex"?"0x"+min.ToString("x"):min.ToString();}}
     }s.Value=forced.Contains(s.Name)?"y":value;
    }
    foreach(var s in Symbols.Values.Where(s=>s.Value=="y"||s.Value=="m"))foreach(var n in s.Nodes.Where(Active))foreach(var d in n.Selects)if(Test(d[1])&&Symbols.ContainsKey(d[0]))Symbols[d[0]].Value="y";
    if(Symbols.All(s=>before[s.Key]==s.Value.Value))return;
   }throw new Exception("Kconfig dependencies did not converge");
  }
  public void Set(string name,string value){if(!Symbols.ContainsKey(name))throw new Exception("Unknown setting: "+name);var s=Symbols[name];var old=Symbols.ToDictionary(x=>x.Key,x=>x.Value.User);
   if(s.Type=="hex"||s.Type=="int")U.Number(value);if((s.Type=="bool"||s.Type=="tristate")&&!new[]{"y","n","m"}.Contains(value))throw new Exception("Invalid boolean");
   foreach(var n in s.Nodes.Where(n=>n.Choice!=null))if(value=="y")foreach(var sibling in n.Choice.Children.Where(n=>n.Kind=="symbol"))Symbols[sibling.Name].User=null;
   s.User=value;Evaluate();bool equal=s.Value==value;if(s.Type=="hex")equal=U.Number(s.Value)==U.Number(value);if(!equal){foreach(var kv in old)Symbols[kv.Key].User=kv.Value;Evaluate();throw new Exception("현재 설정 조건에서 적용할 수 없습니다: "+name+"="+value);}}
  public void DefaultArm(){Set(Symbols.ContainsKey("MACH_RPXXXX")?"MACH_RPXXXX":Symbols.ContainsKey("MACH_RP2040")?"MACH_RP2040":"MACH_STM32","y");}
  public void Load(string path){foreach(var s in Symbols.Values)s.User=null;foreach(string l in File.ReadAllLines(path)){var m=Regex.Match(l,@"^CONFIG_(\w+)=(.*)$");if(m.Success&&Symbols.ContainsKey(m.Groups[1].Value))Symbols[m.Groups[1].Value].User=Unquote(m.Groups[2].Value);var n=Regex.Match(l,@"^# CONFIG_(\w+) is not set$");if(n.Success&&Symbols.ContainsKey(n.Groups[1].Value))Symbols[n.Groups[1].Value].User="n";}Evaluate();}
  public string ConfigText(){var b=new StringBuilder();foreach(var s in Symbols.Values){if(s.Type=="")continue;if(s.Value=="n"&&(s.Type=="bool"||s.Type=="tristate")){b.AppendLine("# CONFIG_"+s.Name+" is not set");continue;}if(s.Value==""&&s.Type!="string")continue;b.AppendLine("CONFIG_"+s.Name+"="+(s.Type=="string"?U.CString(s.Value):s.Value));}return b.ToString().Replace("\r\n","\n");}
  public void Save(string p){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(p)));File.WriteAllText(p,ConfigText(),new UTF8Encoding(false));}
  public void Header(string p){var b=new StringBuilder();foreach(var s in Symbols.Values){if(s.Type==""||s.Value==""&&s.Type!="string")continue;string v=s.Value=="y"?"1":s.Value=="n"?"0":s.Type=="string"?U.CString(s.Value):s.Value;b.AppendLine("#define CONFIG_"+s.Name+" "+v);}Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,b.ToString(),new UTF8Encoding(false));}
  public Obj Summary(string project){return new Obj{{"architecture",Get("BOARD_DIRECTORY")},{"mcu",Get("MCU")},{"application_address",Get(project=="katapult"?"LAUNCH_APP_ADDRESS":"FLASH_APPLICATION_ADDRESS")},{"communication",Get("USBCANBUS")=="y"?"USB CAN bridge":Get("CANSERIAL")=="y"?"CAN":Get("USBSERIAL")=="y"?"USB":"UART"},{"deployer",Get("BUILD_DEPLOYER")=="y"}};}
  public object[] Rows(){var rows=new List<Obj>();foreach(var n in Nodes){if(!Visible(n)||n.Choice!=null)continue;var parent=n.Parent;while(parent!=null&&(parent.Kind=="if"||parent.Prompt==""||!Visible(parent)))parent=parent.Parent;var r=new Obj{{"id",n.Id},{"parent",parent?.Id??""},{"name",n.Name},{"label",n.Prompt},{"help",n.Help},{"value",""},{"kind",n.Kind=="choice"?"choice":n.Kind=="comment"?"comment":"group"},{"editable",false}};
    if(n.Kind=="symbol"){var s=Symbols[n.Name];r["kind"]=s.Type=="bool"||s.Type=="tristate"?"bool":"value";r["value"]=s.Value;r["editable"]=true;r["allowed"]=new object[]{"n","y"};}
    if(n.Kind=="choice"){r["value"]=choices[n];r["editable"]=true;r["options"]=n.Children.Where(c=>c.Kind=="symbol"&&Visible(c)).Select(c=>(object)new Obj{{"name",c.Name},{"label",c.Prompt}}).ToArray();}rows.Add(r);
   }return rows.Cast<object>().ToArray();}
 }
}
