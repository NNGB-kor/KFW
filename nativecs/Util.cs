using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
namespace KlipperWinForms {
 using Obj = Dictionary<string,object>;
 static class U {
  public static string Root => Program.Root;
  public static string Assets => Path.Combine(Root,"builder_assets");
  public static Obj Read(string p) => Json.Read(File.ReadAllText(p,Encoding.UTF8));
  public static void Write(string p,object data) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(p))); File.WriteAllText(p,Json.Write(data),new UTF8Encoding(false)); }
  public static string Hash(byte[] data) { using(var h=SHA256.Create()) return Hex(h.ComputeHash(data)); }
  public static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-","").ToLowerInvariant();
  public static string Blob(byte[] b) { using(var h=SHA1.Create()) return Hex(h.ComputeHash(Encoding.ASCII.GetBytes("blob "+b.Length+"\0").Concat(b).ToArray())); }
  public static long Number(string s) { s=s.TrimStart('+'); if(s.StartsWith("-0x",StringComparison.OrdinalIgnoreCase))return -Convert.ToInt64(s.Substring(3),16);if(s.StartsWith("0x",StringComparison.OrdinalIgnoreCase)) return Convert.ToInt64(s.Substring(2),16); return long.Parse(s,System.Globalization.CultureInfo.InvariantCulture); }
  public static string CString(string s) => "\""+s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r")+"\"";
  public static string Slash(string s) => s.Replace('\\','/');
  public static string Quote(string s) => "\""+Regex.Replace(s,@"(\\*)""",m=>m.Groups[1].Value+m.Groups[1].Value+"\\\"").TrimEnd('\\')+new string('\\',s.Reverse().TakeWhile(c=>c=='\\').Count()*2)+"\"";
  public static string Shell(string s) => "'"+s.Replace("'","'\"'\"'")+"'";
  public static List<string> Split(string s) { var result=new List<string>(); var b=new StringBuilder(); char q='\0'; bool escape=false,has=false; foreach(char c in s) { if(escape){b.Append(c);escape=false;has=true;} else if(c=='\\'&&q!='\''){escape=true;has=true;} else if(q!='\0'){if(c==q)q='\0';else b.Append(c);} else if(c=='\''||c=='"'){q=c;has=true;} else if(char.IsWhiteSpace(c)){if(has){result.Add(b.ToString());b.Clear();has=false;}} else {b.Append(c);has=true;} } if(has)result.Add(b.ToString());return result; }
  public static string SafePath(string root,string rel) { string p=Path.GetFullPath(Path.Combine(root,rel.Replace('/',Path.DirectorySeparatorChar)));if(!p.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe path: "+rel);return p; }
  public static void CopyTree(string from,string to) { Directory.CreateDirectory(to);foreach(var f in Directory.GetFiles(from)){File.Copy(f,Path.Combine(to,Path.GetFileName(f)),true);}foreach(var d in Directory.GetDirectories(from)){var n=Path.GetFileName(d);if(n==".git"||n=="__pycache__"||n=="out"||n=="asset-sets")continue;CopyTree(d,Path.Combine(to,n));} }
  public static uint Adler(byte[] b){uint a=1,c=0;foreach(byte v in b){a=(a+v)%65521;c=(c+a)%65521;}return (c<<16)|a;}
  public static byte[] Zlib(byte[] b){using(var m=new MemoryStream()){m.WriteByte(0x78);m.WriteByte(0x9c);using(var z=new DeflateStream(m,CompressionLevel.Optimal,true))z.Write(b,0,b.Length);uint a=Adler(b);for(int i=3;i>=0;i--)m.WriteByte((byte)(a>>(8*i)));return m.ToArray();}}
  public static byte[] Inflate(byte[] b,int start,int limit=1024*1024){var z=new ICSharpCode.SharpZipLib.Zip.Compression.Inflater();z.SetInput(b,start,b.Length-start);using(var m=new MemoryStream()){byte[] chunk=new byte[4096];while(!z.IsFinished){int n=z.Inflate(chunk);if(n==0&&!z.IsFinished)throw new Exception("Incomplete zlib stream");m.Write(chunk,0,n);if(m.Length>limit)throw new Exception("Compressed data too large");}return m.ToArray();}}
  public static uint V32(byte[] b,int p){if(p<0||p+4>b.Length)throw new Exception("Invalid binary range");return BitConverter.ToUInt32(b,p);}
  public static void Put32(byte[] b,int p,uint v){Array.Copy(BitConverter.GetBytes(v),0,b,p,4);}
 }
}
