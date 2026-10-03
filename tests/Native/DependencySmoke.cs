using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using System.Text;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 static class DependencySmoke {
  internal static Obj Run(){
   string lib=Path.GetFullPath(Path.Combine(Program.Root,"lib"))+Path.DirectorySeparatorChar;
   var loaded=new Obj();
   foreach(string file in Directory.GetFiles(lib,"*.dll")){
    // Load by identity, not LoadFrom: exercise the executable's real probing policy.
    var assembly=Assembly.Load(AssemblyName.GetAssemblyName(file));assembly.GetTypes();
    if(!Path.GetFullPath(assembly.Location).StartsWith(lib,StringComparison.OrdinalIgnoreCase))throw new Exception("DLL loaded outside lib: "+assembly.FullName);
    loaded[assembly.GetName().Name]=assembly.Location;
   }
   byte[] raw=Encoding.UTF8.GetBytes("library relocation check");
   var deflater=new ICSharpCode.SharpZipLib.Zip.Compression.Deflater();deflater.SetInput(raw);deflater.Finish();byte[] buffer=new byte[256];int length=deflater.Deflate(buffer);
   if(!U.Inflate(buffer.Take(length).ToArray(),0).SequenceEqual(raw))throw new Exception("Compression round trip failed");
   using(var connection=new Renci.SshNet.PasswordConnectionInfo("127.0.0.1",22,"test","test"))
   using(var client=new Renci.SshNet.SshClient(connection)){if(client.IsConnected)throw new Exception("Unexpected SSH connection");}
   System.Windows.Forms.Application.EnableVisualStyles();System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
   // Construct the real wizard without showing it: Shown starts online preparation.
   using(var form=new MainForm()){form.EnableWizard();if(form.Wizard.Step!=0||form.ClientSize.Width!=440)throw new Exception("Wizard initialization failed");}
   return new Obj{{"success",true},{"loaded_from_lib",loaded},{"compression_roundtrip",true},{"ssh_client_created",true},{"wizard_constructed",true},{"network_connected",false},{"physical_hardware_opened",false}};
  }
 }
}
