using System.IO;using System.Collections.Generic;
namespace KlipperWinForms {
 static class TestExports {
  internal static Dictionary<string,object> Run(string directory){
   Directory.CreateDirectory(directory);
   File.WriteAllText(Path.Combine(directory,"runner.py"),SshServiceRunner.Script("/tmp/kfw-test","/usr/bin/python3","/dev/serial/by-id/fixture","klipper",true,true));
   File.WriteAllText(Path.Combine(directory,"rp2040_install.py"),SshRp2040Install.Source());
   File.WriteAllText(Path.Combine(directory,"dfu_usb.py"),SshDfuUsb.Source());
   return new Dictionary<string,object>{{"success",true}};
  }
 }
}
