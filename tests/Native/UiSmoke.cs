using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace KlipperWinForms {
    using Obj = Dictionary<string, object>;
    sealed partial class MainForm {
        void SaveScreenshot(string name) {
            bool wasBusy = DiagnosticBusy; SetBusy(false); Refresh();
            using(var bitmap = new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(Path.Combine(Program.Smoke,name),ImageFormat.Png); }
            SetBusy(wasBusy);
        }
        async Task WaitMenuAction() {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while(Busy && DateTime.UtcNow < deadline) await Task.Delay(10);
            if(Busy) throw new Exception("Menu action timed out");
            if(Application.OpenForms.Count != 1) throw new Exception("Menu editing opened a child window");
        }
        static void SelectMenu(BuilderPane pane, Func<Obj,bool> predicate) {
            for(int i=0;i<pane.Menu.Items.Count;i++) if(predicate(((Item)pane.Menu.Items[i]).Data)) { pane.Menu.SelectedIndex=i; return; }
            throw new Exception("Menu item not found in " + pane.Project + ": " + string.Join("; ",pane.Menu.Items.Cast<Item>().Select(i=>Json.S(i.Data,"name")+" | "+Json.S(i.Data,"label"))));
        }
        async Task MenuSmoke() {
            Directory.CreateDirectory(Program.Smoke);
            var report = new Obj { {"scope","Configuration menu UI; no downloads, firmware build or serial operations"} };
            try {
                foreach(var pane in new[]{Boot,Firmware}) {
                    Tabs.SelectedTab = pane;
                    await Snapshot(pane,"init"); await Snapshot(pane,"reset");
                    SelectMenu(pane, r=>Json.S(r,"kind")=="choice" && Json.S(r,"label").Contains("Architecture"));
                    string choiceId = Json.S(((Item)pane.Menu.SelectedItem).Data,"id");
                    pane.Menu.SendMenuKey(Keys.Enter); await WaitMenuAction();
                    if(pane.Stack.Count!=1 || pane.Menu.Items.Cast<Item>().Any(i=>Json.S(i.Data,"kind")!="choice_option")) throw new Exception("Choice did not open inside the menu");
                    if(!pane.Menu.Items.Cast<Item>().Any(i=>i.Text.StartsWith("(X) "))) throw new Exception("Choice marker missing");
                    SaveScreenshot(pane.Project+"-choice.png");
                    SelectMenu(pane,r=>Json.S(r,"name")=="MACH_STM32");
                    pane.Menu.SendMenuKey(Keys.Space); await WaitMenuAction();
                    if(pane.Stack.Count!=1 || Json.S(Json.D(pane.State,"summary"),"architecture")!="stm32") throw new Exception("Space did not apply the choice in place");
                    pane.Menu.SendMenuKey(Keys.Enter); await WaitMenuAction();
                    if(pane.Stack.Count!=0 || Json.S(((Item)pane.Menu.SelectedItem).Data,"id")!=choiceId) throw new Exception("Choice return did not restore the parent cursor");
                    pane.Menu.SendMenuDoubleClick(); await WaitMenuAction();
                    SelectMenu(pane,r=>Json.S(r,"name")=="MACH_RPXXXX");
                    pane.Menu.SendMenuKey(Keys.Escape); await WaitMenuAction();
                    if(Json.S(Json.D(pane.State,"summary"),"architecture")!="stm32") throw new Exception("Escape applied an unconfirmed choice");
                    await Snapshot(pane,"reset");
                    bool hasAdvancedToggle = pane.Menu.Items.Cast<Item>().Any(i=>Json.S(i.Data,"name")=="LOW_LEVEL_OPTIONS");
                    SelectMenu(pane,r=>Json.S(r,"name")== (hasAdvancedToggle ? "LOW_LEVEL_OPTIONS" : "ENABLE_LED"));
                    pane.Menu.SendMenuKey(Keys.Space); await WaitMenuAction();
                    if(!hasAdvancedToggle) { pane.Menu.SendMenuKey(Keys.Space); await WaitMenuAction(); }
                    SelectMenu(pane,r=>Json.S(r,"label")=="USB ids");
                    pane.Menu.SendMenuKey(Keys.Enter); await WaitMenuAction();
                    SelectMenu(pane,r=>Json.S(r,"name")=="USB_VENDOR_ID");
                    string original = Json.S(((Item)pane.Menu.SelectedItem).Data,"value");
                    pane.Menu.SendMenuDoubleClick(); await WaitMenuAction();
                    if(!pane.IsValueEditing) throw new Exception("Value editor did not open inline");
                    pane.ValueInput.Text="0x1234"; SaveScreenshot(pane.Project+"-value.png");
                    pane.ValueInput.SendMenuKey(Keys.Escape); await WaitMenuAction();
                    if(pane.IsValueEditing || Json.Rows(pane.State,"rows").Any(r=>Json.S(r,"name")=="USB_VENDOR_ID" && Json.S(r,"value")!=original)) throw new Exception("Value cancel changed the configuration");
                    pane.Menu.SendMenuKey(Keys.Enter); await WaitMenuAction();
                    pane.ValueInput.Text="0x1234"; pane.ValueInput.SendMenuKey(Keys.Enter); await WaitMenuAction();
                    if(pane.IsValueEditing || !Json.Rows(pane.State,"rows").Any(r=>Json.S(r,"name")=="USB_VENDOR_ID" && Json.S(r,"value")=="0x1234")) throw new Exception("Inline value was not applied");
                    pane.Menu.SendMenuKey(Keys.Enter); await WaitMenuAction();
                    pane.ValueInput.Text="not-a-hex-number"; pane.ValueInput.SendMenuKey(Keys.Enter); await WaitMenuAction();
                    if(!pane.IsValueEditing || pane.EditError=="") throw new Exception("Validation error was not shown inline");
                    SaveScreenshot(pane.Project+"-validation.png");
                    pane.ValueInput.SendMenuKey(Keys.Escape); await WaitMenuAction();
                    pane.Menu.SendMenuKey(Keys.Left); await WaitMenuAction();
                    if(pane.Stack.Count!=0) throw new Exception("Left arrow did not return to the parent menu");
                    pane.Menu.SendMenuKey(Keys.F1); await WaitMenuAction();
                    SaveScreenshot(pane.Project+"-help.png"); pane.HelpView.SendMenuKey(Keys.Escape); await WaitMenuAction();
                    if(!pane.Menu.Visible) throw new Exception("Escape did not close inline help");
                    await Snapshot(pane,"reset");
                    report[pane.Project] = new Obj { {"choice_keyboard_and_double_click",true}, {"space_apply_enter_return",true}, {"escape_cancels_unconfirmed_choice",true}, {"inline_value_apply_cancel",true}, {"inline_validation_error",true}, {"inline_help",true}, {"no_child_forms",true} };
                }
                report["success"]=true;
            } catch(Exception ex) { report["success"]=false; report["error"]=ex.ToString(); Environment.ExitCode=1; }
            finally { File.WriteAllText(Path.Combine(Program.Smoke,"report.json"),Json.Write(report),Encoding.UTF8); SetBusy(false); Close(); }
        }
        async Task Smoke() {
            var report = new Obj(); Directory.CreateDirectory(Program.Smoke);
            try {
                SetBusy(true);
                await Snapshot(Boot,"startup"); await Snapshot(Firmware,"startup");
                report["katapult_startup"] = new Obj{{"latest_selected",Json.B(Boot.State,"latest_selected")},{"assets_ready",Json.B(Boot.State,"assets_ready")},{"version",Json.S(Boot.State,"version")},{"folder",Json.S(Boot.State,"download_folder")},{"warning",Json.S(Boot.State,"startup_warning")}};
                report["klipper_startup"] = new Obj{{"latest_selected",Json.B(Firmware.State,"latest_selected")},{"assets_ready",Json.B(Firmware.State,"assets_ready")},{"version",Json.S(Firmware.State,"version")},{"folder",Json.S(Firmware.State,"download_folder")},{"warning",Json.S(Firmware.State,"startup_warning")}};
                if(Firmware.Versions.SelectedItem == null || Boot.Versions.SelectedItem == null) throw new Exception("Version display missing");
                report["framework"] = ".NET Framework 4.8"; report["runtime"] = Environment.Version.ToString(); report["tabs"] = Tabs.TabPages.Cast<TabPage>().Select(p => p.Text).ToArray();
                Tabs.SelectedIndex = 3; SaveScreenshot("ssh-uploader.png");
                report["ssh_password_masked"] = SshPassword.UseSystemPasswordChar;
                report["ssh_upload_disabled_without_selection"] = !SshFlash.Enabled;
                Tabs.SelectedTab = Firmware;
                await Snapshot(Firmware,"reset");
                await Snapshot(Firmware,"set",new Obj{{"name","LOW_LEVEL_OPTIONS"},{"value","y"}});
                var group = Json.Rows(Firmware.State,"rows").First(r => Json.S(r,"label") == "USB ids");
                Firmware.Stack.Push(Json.S(group,"id")); Firmware.ShowMenu();
                await Snapshot(Firmware,"set",new Obj{{"name","USB_VENDOR_ID"},{"value","0x1d51"}});
                if(!Json.Rows(Firmware.State,"rows").Any(r => Json.S(r,"name") == "USB_VENDOR_ID" && Json.S(r,"value") == "0x1d51")) throw new Exception("USB edit failed");
                SaveScreenshot("usb-ids.png"); report["usb_menu_edit"] = true; Firmware.Back();
                await Snapshot(Firmware,"reset"); await Snapshot(Firmware,"set",new Obj{{"name","RPXXXX_FLASH_START_4000"},{"value","y"}});
                await Build(Firmware); if(Inspection == null || UploadFile.Text != Path.Combine(Firmware.ResultFolder,"klipper.bin")) throw new Exception("Build to uploader failed");
                report["klipper_output"] = Firmware.ResultFolder; SaveScreenshot("klipper-build.png");
                string klipperBin = UploadFile.Text;
                Tabs.SelectedTab = Boot; await Snapshot(Boot,"reset"); await Snapshot(Boot,"set",new Obj{{"name","RPXXXX_FLASH_START_4000"},{"value","y"}}); await Build(Boot);
                if(UploadFile.Text != klipperBin) throw new Exception("Bootloader replaced uploader file"); report["katapult_output"] = Boot.ResultFolder; SaveScreenshot("katapult-build.png");
                Tabs.SelectedIndex = 2; SaveScreenshot("uploader.png");
                report["no_port_selected"] = Ports.SelectedItem == null; report["upload_enabled_without_port"] = FlashButton.Enabled; report["hardware_written"] = false;
                Tabs.SelectedTab = Firmware; var old = Json.Rows(Firmware.State,"versions").First(v => Json.S(v,"ref") == "v0.12.0");
                await Snapshot(Firmware,"select",new Obj{{"sha",Json.S(old,"sha")}});
                if(Inspection != null || UploadFile.Text != "") throw new Exception("Stale firmware retained");
                await Snapshot(Firmware,"reset"); await Build(Firmware); report["older_output"] = Firmware.ResultFolder;
                report["full_version"] = Json.S(Firmware.State,"version"); report["success"] = true;
            } catch(Exception ex) { report["success"] = false; report["error"] = ex.ToString(); Environment.ExitCode = 1; }
            finally { File.WriteAllText(Path.Combine(Program.Smoke,"report.json"),Json.Write(report),Encoding.UTF8); SetBusy(false); Close(); }
        }
    }
}
