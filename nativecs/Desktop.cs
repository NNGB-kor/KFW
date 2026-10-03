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

[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: System.Reflection.AssemblyVersion("1.0.23.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.23.0")]
[assembly: System.Reflection.AssemblyTitle("Klipper Firmware Wizard")]
[assembly: System.Reflection.AssemblyProduct("Klipper Firmware Wizard")]

namespace KlipperWinForms {
    using Obj = Dictionary<string, object>;
    static class Json {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }; }
        public static string Write(object value) { return Serializer().Serialize(value); }
        public static Obj Read(string value) { return (Obj)Serializer().DeserializeObject(value); }
        public static string S(Obj value, string key) { return value.ContainsKey(key) && value[key] != null ? Convert.ToString(value[key]) : ""; }
        public static Obj D(Obj value, string key) { return (Obj)value[key]; }
        public static IEnumerable<Obj> Rows(Obj value, string key) { return ((object[])value[key]).Cast<Obj>(); }
        public static bool B(Obj value, string key) { return value.ContainsKey(key) && Convert.ToBoolean(value[key]); }
    }
    sealed class Item {
        public Obj Data;
        public string Text;
        public Item(Obj data, string text) { Data = data; Text = text; }
        public override string ToString() { return Text; }
    }
    static class Program {
        public static string Root = AppDomain.CurrentDomain.BaseDirectory;
        public static string Smoke = "";
#if DIAGNOSTICS
        public static bool MenuSmokeOnly;
        public static bool LockSmokeOnly;
#endif
        [STAThread] static int Main(string[] args) {
#if DIAGNOSTICS
            if(args.Length==2&&args[0]=="--dependency-smoke-test") {try{U.Write(args[1],DependencySmoke.Run());return 0;}catch(Exception ex){U.Write(args[1],new Obj{{"success",false},{"error",ex.ToString()}});return 1;}}
#endif
            if(args.Length > 0 && args[0] == "--engine") { Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true}); Console.SetError(new StreamWriter(Console.OpenStandardError(),new UTF8Encoding(false)){AutoFlush=true});
                try { var result = new NativeEngine(Json.Read(Console.In.ReadToEnd()), line=>Console.WriteLine(line)).Run(); Console.WriteLine("@@RESULT@@"+Json.Write(new Obj{{"ok",true},{"result",result}})); return 0; }
                catch(Exception ex){Console.Error.WriteLine(ex);Console.WriteLine("@@RESULT@@"+Json.Write(new Obj{{"ok",false},{"error",ex.Message}}));return 1;}
            }
#if DIAGNOSTICS
            if(args.Length > 0 && args[0] == "--kconfig-test") {
                try { var req=Json.Read(Console.In.ReadToEnd());var k=new KConfig(Json.S(req,"source"),"test-version"); if(Json.S(req,"config")!="")k.Load(Json.S(req,"config"));else k.DefaultArm(); if(req.ContainsKey("sets"))foreach(var v in Json.Rows(req,"sets"))k.Set(Json.S(v,"name"),Json.S(v,"value")); Console.WriteLine(Json.Write(k.Symbols.ToDictionary(v=>v.Key,v=>(object)v.Value.Value)));return 0; }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            }
            if (args.Length == 2 && args[0] == "--smoke-test") Smoke = Path.GetFullPath(args[1]);
            if (args.Length == 2 && args[0] == "--menu-smoke-test") { Smoke = Path.GetFullPath(args[1]); MenuSmokeOnly = true; }
            if (args.Length == 2 && args[0] == "--lock-smoke-test") { Smoke = Path.GetFullPath(args[1]); LockSmokeOnly = true; }
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message, "오류"); };
            using (MainForm form = new MainForm()) {if(Smoke==""&&!args.Contains("--classic"))form.EnableWizard();Application.Run(form);}
            return Environment.ExitCode;
        }
    }
    // Treat terminal navigation keys as menu input rather than dialog commands.
    sealed class MenuListBox : ListBox {
        protected override bool IsInputKey(Keys keyData) {
            var key = keyData & Keys.KeyCode;
            return new[] { Keys.Enter, Keys.Escape, Keys.Left, Keys.Right, Keys.Space }.Contains(key) || base.IsInputKey(keyData);
        }
        internal void SendMenuKey(Keys key) { OnKeyDown(new KeyEventArgs(key)); }
        internal void SendMenuDoubleClick() { OnDoubleClick(EventArgs.Empty); }
    }
    sealed class MenuValueBox : TextBox {
        protected override bool IsInputKey(Keys keyData) {
            var key = keyData & Keys.KeyCode;
            return key == Keys.Enter || key == Keys.Escape || base.IsInputKey(keyData);
        }
        internal void SendMenuKey(Keys key) { OnKeyDown(new KeyEventArgs(key)); }
    }
    sealed class BuilderPane : TabPage {
        public readonly string Project;
        public readonly ComboBox Versions = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 375 };
        public readonly TextBox Output = new TextBox { Width = 420 };
        public readonly Button BuildButton = new Button { Text = "펌웨어 빌드", AutoSize = true };
        public readonly Button CancelButton = new Button { Text = "빌드 취소", AutoSize = true, Enabled = false };
        public readonly MenuListBox Menu = new MenuListBox { Dock = DockStyle.Fill, Font = new Font("Courier New", 11), BackColor = Color.FromArgb(187,187,187), BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 27 };
        public readonly TextBox Log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), WordWrap = false };
        public readonly Label Summary = new Label { AutoSize = true, MaximumSize = new Size(940,0) };
        public readonly TextBox Help = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        public readonly Label Crumb = new Label { Text = "(Top)", Dock = DockStyle.Top, Height = 27, BackColor = Color.FromArgb(187,187,0), Font = new Font("Courier New", 11, FontStyle.Bold) };
        readonly Label MenuKeys = new Label { Text = "Enter: Select    Space: Toggle    Esc: Back    ?: Help", Dock = DockStyle.Bottom, Height = 26, BackColor = Color.FromArgb(187,187,0), Font = new Font("Courier New", 10) };
        readonly Panel ValueView = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(187,187,187), Visible = false, Padding = new Padding(20) };
        readonly Label ValuePrompt = new Label { AutoSize = true, Dock = DockStyle.Top, Font = new Font("Courier New", 11, FontStyle.Bold) };
        internal readonly MenuValueBox ValueInput = new MenuValueBox { Dock = DockStyle.Top, Font = new Font("Courier New", 12), BackColor = Color.FromArgb(0,0,187), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        readonly Label ValueError = new Label { Dock = DockStyle.Top, AutoSize = true, ForeColor = Color.DarkRed, Padding = new Padding(0,12,0,12) };
        internal readonly MenuValueBox HelpView = new MenuValueBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(187,187,187), Font = new Font("Courier New",11), Visible = false };
        readonly List<Control> EditorInputs = new List<Control>();
        Obj EditedRow;
        bool ShowingHelp;
        bool RefocusAfterAction;
        internal bool IsValueEditing { get { return EditedRow != null; } }
        internal string EditError { get { return ValueError.Text; } }
        bool OverlayOpen { get { return IsValueEditing || ShowingHelp; } }
        public Obj State;
        public string ResultFolder = "";
        public string CancelPath = "";
        Button OutputBrowse;
        public bool Updating;
        internal Control[] WizardViews(){
            Versions.Width=280;Menu.Font=new Font("Courier New",10);Menu.ItemHeight=22;Crumb.Font=new Font("Courier New",10,FontStyle.Bold);Crumb.Height=23;MenuKeys.Font=new Font("Courier New",9);MenuKeys.Height=22;
            var layout=(TableLayoutPanel)Controls[0];
            var config=new Panel{Dock=DockStyle.Fill};
            var menu=layout.GetControlFromPosition(0,2);var buttons=(FlowLayoutPanel)layout.GetControlFromPosition(0,4);
            var build=new Panel{Dock=DockStyle.Fill};BuildButton.Parent=null;CancelButton.Parent=null;
            var results=buttons.Controls.Cast<Control>().First(c=>c.Text=="결과 폴더 열기");buttons.Controls.Remove(results);results.Dispose();
            var up=buttons.Controls.Cast<Control>().First(c=>c.Text=="상위 메뉴");buttons.Controls.Remove(up);up.Dispose();
            buttons.Dock=DockStyle.None;buttons.WrapContents=false;buttons.Margin=Padding.Empty;
            config.Controls.Add(menu);
            build.Controls.Add(Log);
            return new Control[]{config,build,buttons};
        }
        public readonly Stack<string> Stack = new Stack<string>();
        public readonly List<Control> Inputs = new List<Control>();
        readonly MainForm Owner;
        static FlowLayoutPanel Row() { return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; }
        public BuilderPane(MainForm owner, string project) {
            Owner = owner; Project = project; Text = project == "katapult" ? "Katapult Bootloader" : "Klipper Firmware";
            Versions.DrawMode = DrawMode.OwnerDrawFixed; Versions.ItemHeight = 23;
            Versions.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                e.DrawBackground();
                object item = e.Index >= 0 ? Versions.Items[e.Index] : Versions.SelectedItem;
                if(item != null) TextRenderer.DrawText(e.Graphics,item.ToString(),Versions.Font,e.Bounds,Versions.Enabled ? e.ForeColor : SystemColors.GrayText,TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
            Padding = new Padding(12); BackColor = SystemColors.Control;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 63)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 37));
            Controls.Add(layout);
            var versionRow = Row(); versionRow.Controls.Add(new Label { Text = "펌웨어 버전", AutoSize = true, Margin = new Padding(3,8,8,3) }); versionRow.Controls.Add(Versions);
            AddButton(versionRow, "최신 버전 확인", async delegate { await Owner.Snapshot(this, "refresh"); }); layout.Controls.Add(versionRow,0,0);
            var outputRow = Row(); outputRow.Controls.Add(new Label { Text = "결과 저장 폴더", AutoSize = true, Margin = new Padding(3,8,8,3) });
            Output.Text = Path.Combine(Program.Smoke == "" ? Program.Root : Program.Smoke, project == "katapult" ? "Bootloader" : "Firmware");
            outputRow.Controls.Add(Output); Inputs.Add(Output);
            OutputBrowse=AddButton(outputRow, "폴더 선택", delegate { using(var dialog = new FolderBrowserDialog()) { dialog.SelectedPath = Output.Text; if(dialog.ShowDialog() == DialogResult.OK) Output.Text = dialog.SelectedPath; } return Task.FromResult(0); },true);
            layout.Controls.Add(outputRow,0,1);
            var menuPanel = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
            var content = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(187,187,187) };
            content.Controls.Add(Menu); content.Controls.Add(ValueView); content.Controls.Add(HelpView);
            var editButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            var apply = new Button { Text = "< Apply >", AutoSize = true, FlatStyle = FlatStyle.Flat, Font = Menu.Font };
            var cancel = new Button { Text = "< Cancel >", AutoSize = true, FlatStyle = FlatStyle.Flat, Font = Menu.Font };
            editButtons.Controls.Add(apply); editButtons.Controls.Add(cancel);
            ValueView.Controls.Add(editButtons); ValueView.Controls.Add(ValueError); ValueView.Controls.Add(ValueInput); ValueView.Controls.Add(ValuePrompt);
            EditorInputs.Add(ValueInput); EditorInputs.Add(apply); EditorInputs.Add(cancel);
            apply.Click += delegate { RunMenu(ApplyValue); };
            cancel.Click += delegate { Back(); };
            ValueInput.KeyDown += delegate(object sender, KeyEventArgs e) {
                if(e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; RunMenu(ApplyValue); }
                else if(e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; Back(); }
            };
            HelpView.KeyDown += delegate(object sender, KeyEventArgs e) { if(e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; Back(); } };
            menuPanel.Controls.Add(content); menuPanel.Controls.Add(Crumb); menuPanel.Controls.Add(MenuKeys);
            layout.Controls.Add(menuPanel,0,2); layout.Controls.Add(Help,0,3); Inputs.Add(Menu); Inputs.Add(Versions);
            var buttons = Row();
            AddButton(buttons,"상위 메뉴", delegate { Back(); return Task.FromResult(0); });
            AddButton(buttons,"설정 불러오기", async delegate { using(var d = new OpenFileDialog { Filter = "Config|*.config;*.*" }) if(d.ShowDialog() == DialogResult.OK) await Owner.Snapshot(this,"load",new Obj{{"path",d.FileName}}); });
            AddButton(buttons,"설정 저장", async delegate { using(var d = new SaveFileDialog { Filter = "Config|*.config", FileName = Project + ".config" }) if(d.ShowDialog() == DialogResult.OK) await Owner.Snapshot(this,"export",new Obj{{"path",d.FileName}}); });
            AddButton(buttons,"기본값", async delegate { await Owner.Snapshot(this,"reset"); });
            BuildButton.Text = project == "katapult" ? "부트로더 빌드" : "펌웨어 빌드"; buttons.Controls.Add(BuildButton); Inputs.Add(BuildButton);
            BuildButton.Click += delegate { Owner.Safe(async delegate { await Owner.Build(this); },Text+" 빌드","builder:"+Project); };
            buttons.Controls.Add(CancelButton); CancelButton.Click += delegate { Owner.Cancel(this); };
            AddButton(buttons,"결과 폴더 열기", delegate { string folder=ResultFolder;if(Directory.Exists(folder)) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); return Task.FromResult(0); });
            layout.Controls.Add(buttons,0,4); layout.Controls.Add(Summary,0,5); layout.Controls.Add(Log,0,6);
            Versions.SelectedIndexChanged += delegate { if(!Updating && Versions.SelectedItem != null) Owner.Safe(async delegate { await Owner.Snapshot(this,"select",new Obj{{"sha",Json.S(((Item)Versions.SelectedItem).Data,"sha")}}); },Text+" 버전 선택","builder:"+Project); };
            Menu.DoubleClick += delegate { if(Owner.IsBuilderBusy(Project)) BrowseWhileBusy();else RunMenu(delegate { return Activate(false); }); };
            Menu.KeyDown += delegate(object s, KeyEventArgs e) {
                if(e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.Right) { e.Handled = e.SuppressKeyPress = true; if(Owner.IsBuilderBusy(Project)){if(e.KeyCode!=Keys.Space)BrowseWhileBusy();}else RunMenu(delegate { return Activate(e.KeyCode == Keys.Space); }); }
                else if(e.KeyCode == Keys.Escape || e.KeyCode == Keys.Left) { e.Handled = e.SuppressKeyPress = true; Back(); }
                else if(e.KeyCode == Keys.F1 || e.KeyCode == Keys.OemQuestion && e.Shift) { e.Handled = e.SuppressKeyPress = true; ShowInlineHelp(); }
                else if(e.KeyCode == Keys.Y || e.KeyCode == Keys.N) { e.Handled = e.SuppressKeyPress = true; RunMenu(delegate { return SetBoolean(e.KeyCode == Keys.Y ? "y" : "n"); }); }
            };
            Menu.SelectedIndexChanged += delegate { var item = Menu.SelectedItem as Item; if(item != null) Help.Text = Json.S(item.Data,"label") + "\r\n" + Json.S(item.Data,"help") + "\r\n" + Json.S(item.Data,"name"); };
            Menu.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                if(e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using(var brush = new SolidBrush(selected ? Color.FromArgb(0,0,187) : Color.FromArgb(187,187,187))) e.Graphics.FillRectangle(brush,e.Bounds);
                TextRenderer.DrawText(e.Graphics, Menu.Items[e.Index].ToString(), Menu.Font,e.Bounds,selected ? Color.White : Color.Black,TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
        }
        Button AddButton(FlowLayoutPanel row, string text, Func<Task> action,bool passive=false) { passive=passive||text=="상위 메뉴"||text=="결과 폴더 열기";var b = new Button { Text = text, AutoSize = true }; row.Controls.Add(b); if(!passive)Inputs.Add(b); b.Click += delegate { Owner.Safe(action,Text+" · "+text,passive?new string[0]:new[]{"builder:"+Project}); };return b; }
        public void Render(Obj state) {
            State = state; Updating = true;
            Versions.Items.Clear();
            foreach(var v in Json.Rows(state,"versions")) { int index = Versions.Items.Add(new Item(v,Json.S(v,"label"))); if(Json.S(v,"label") == Json.S(state,"version")) Versions.SelectedIndex = index; }
            Updating = false;
            var summary = Json.D(state,"summary");
            Summary.Text = Json.S(state,"version") + "  |  " + Json.S(summary,"mcu") + "  |  " + Json.S(summary,"communication") + "  |  " + Json.S(summary,"application_address") + "  " + Json.S(state,"build_error");
            if(!OverlayOpen) ShowMenu(); SetBusy(Owner.IsBuilderBusy(Project));
        }
        public void SetBusy(bool busy) {
            foreach(var c in Inputs) c.Enabled = !busy && !OverlayOpen;
            foreach(var c in EditorInputs) c.Enabled = !busy;
            HelpView.Enabled = true;
            Menu.Enabled=State!=null&&!OverlayOpen;
            Output.Enabled=CancelPath==""&&!OverlayOpen;
            if(OutputBrowse!=null)OutputBrowse.Enabled=Output.Enabled;
            CancelButton.Enabled=CancelPath!="";
            BuildButton.Enabled = !busy && !OverlayOpen && State != null && Json.S(State,"build_error") == "";
            if(!busy && RefocusAfterAction) { RefocusAfterAction = false; if(Owner.Tabs.SelectedTab==this){if(IsValueEditing) ValueInput.Focus(); else if(ShowingHelp) HelpView.Focus(); else Menu.Focus();} }
        }
        void BrowseWhileBusy(){var item=Menu.SelectedItem as Item;if(item==null)return;string kind=Json.S(item.Data,"kind");if(kind=="group"||kind=="choice"){Stack.Push(Json.S(item.Data,"id"));ShowMenu();}else ShowInlineHelp();}
        public void ShowMenu(string selectedId = null) {
            if(State == null) return;
            string previous = selectedId ?? (Menu.SelectedItem is Item ? Json.S(((Item)Menu.SelectedItem).Data,"id") : "");
            var rows = Json.Rows(State,"rows").ToList();
            while(Stack.Count > 0 && !rows.Any(r => Json.S(r,"id") == Stack.Peek())) Stack.Pop();
            string parent = Stack.Count > 0 ? Stack.Peek() : "";
            Crumb.Text = "(Top)" + string.Concat(Stack.Reverse().Select(id => " > " + Json.S(rows.First(r => Json.S(r,"id") == id),"label")));
            MenuKeys.Text = "Enter: Select    Space: Toggle    Esc: Back    ?: Help";
            Menu.BeginUpdate(); Menu.Items.Clear();
            var parentRow = rows.FirstOrDefault(r => Json.S(r,"id") == parent);
            if(parentRow != null && Json.S(parentRow,"kind") == "choice") {
                string chosen = Json.S(parentRow,"value");
                foreach(var option in Json.Rows(parentRow,"options")) {
                    string name = Json.S(option,"name");
                    var row = new Obj { {"id",parent + ":" + name}, {"kind","choice_option"}, {"name",name}, {"label",Json.S(option,"label")}, {"help",Json.S(parentRow,"help")}, {"editable",true}, {"value",name == chosen ? "y" : "n"} };
                    int index = Menu.Items.Add(new Item(row,(name == chosen ? "(X) " : "( ) ") + Json.S(option,"label")));
                    if(Json.S(row,"id") == previous || previous == "" && name == chosen) Menu.SelectedIndex = index;
                }
                if(Menu.SelectedIndex < 0) for(int i=0;i<Menu.Items.Count;i++) if(Json.S(((Item)Menu.Items[i]).Data,"name") == chosen) Menu.SelectedIndex = i;
                if(Menu.SelectedIndex < 0 && Menu.Items.Count > 0) Menu.SelectedIndex = 0;
                Menu.EndUpdate(); return;
            }
            foreach(var r in rows.Where(r => Json.S(r,"parent") == parent)) {
                string text = Json.S(r,"label"), kind = Json.S(r,"kind"), value = Json.S(r,"value");
                if(kind == "bool") text = (value == "y" ? "[*] " : "[ ] ") + text;
                else if(kind == "value") text = "(" + value + ") " + text;
                else if(kind == "choice") { var selected = Json.Rows(r,"options").FirstOrDefault(o => Json.S(o,"name") == value); text = "    " + text + " (" + (selected == null ? "" : Json.S(selected,"label")) + ")  --->"; }
                else text = "    " + text + (kind == "group" ? "  --->" : "");
                int index = Menu.Items.Add(new Item(r,text)); if(Json.S(r,"id") == previous) Menu.SelectedIndex = index;
            }
            if(Menu.Items.Count > 0 && Menu.SelectedIndex < 0) Menu.SelectedIndex = 0;
            Menu.EndUpdate();
        }
        public void Back() {
            RefocusAfterAction = Owner.IsBuilderBusy(Project);
            if(OverlayOpen) { string id = EditedRow == null ? null : Json.S(EditedRow,"id"); EditedRow = null; ShowingHelp = false; ValueView.Visible = HelpView.Visible = false; Menu.Visible = true; ShowMenu(id); SetBusy(Owner.IsBuilderBusy(Project)); Menu.Focus(); return; }
            string previous = Stack.Count > 0 ? Stack.Pop() : null; ShowMenu(previous); Menu.Focus();
        }
        void RunMenu(Func<Task> action) {
            Owner.Safe(async delegate {
                try { await action(); }
                catch(Exception ex) { if(IsValueEditing) ValueError.Text = ex.Message; else Help.Text = ex.Message; }
                finally { RefocusAfterAction = true; }
            },Text+" 설정","builder:"+Project);
        }
        void ShowInlineHelp() {
            var item = Menu.SelectedItem as Item; if(item == null) return;
            ShowingHelp = true; HelpView.Text = Json.S(item.Data,"label") + "\r\n\r\n" + (Json.S(item.Data,"help") == "" ? "No help available for this item." : Json.S(item.Data,"help")) + "\r\n\r\n" + Json.S(item.Data,"name");
            Menu.Visible = false; HelpView.Visible = true; HelpView.BringToFront(); Crumb.Text = "Help: " + Json.S(item.Data,"label"); MenuKeys.Text = "Enter / Esc: Back"; SetBusy(Owner.IsBuilderBusy(Project)); HelpView.Focus();
        }
        void BeginValue(Obj row) {
            EditedRow = row; ValueInput.Text = Json.S(row,"value"); ValueError.Text = ""; ValuePrompt.Text = Json.S(row,"label") + "\r\n\r\n";
            Menu.Visible = false; ValueView.Visible = true; ValueView.BringToFront(); Crumb.Text = "Edit: " + Json.S(row,"label"); MenuKeys.Text = "Enter: Apply    Esc: Cancel";
            SetBusy(Owner.IsBuilderBusy(Project)); BeginInvoke(new Action(delegate { ValueInput.Focus(); ValueInput.SelectAll(); }));
        }
        async Task ApplyValue() {
            if(EditedRow == null) return;
            ValueError.Text = "";
            await Owner.Snapshot(this,"set",new Obj{{"name",Json.S(EditedRow,"name")},{"value",ValueInput.Text}});
            Back();
        }
        async Task SetBoolean(string value) {
            var item = Menu.SelectedItem as Item;
            if(item == null || Json.S(item.Data,"kind") != "bool" || !Json.B(item.Data,"editable")) return;
            await Owner.Snapshot(this,"set",new Obj{{"name",Json.S(item.Data,"name")},{"value",value}});
        }
        public async Task Activate(bool stayInChoice = false) {
            Item item = Menu.SelectedItem as Item; if(item == null) return;
            Obj row = item.Data; string kind = Json.S(row,"kind"), name = Json.S(row,"name");
            if(kind == "group" || kind == "choice") { Stack.Push(Json.S(row,"id")); ShowMenu(); return; }
            if(!Json.B(row,"editable")) return;
            if(kind == "choice_option") {
                await Owner.Snapshot(this,"set",new Obj{{"name",name},{"value","y"}});
                if(!stayInChoice) Back();
                return;
            }
            string value = Json.S(row,"value");
            if(kind == "bool") {
                var values = ((object[])row["allowed"]).Select(Convert.ToString).ToList(); value = values[(values.IndexOf(value) + 1) % values.Count];
            } else { BeginValue(row); return; }
            await Owner.Snapshot(this,"set",new Obj{{"name",name},{"value",value}});
        }
    }
    sealed partial class MainForm : Form {
        public readonly string DataRoot;
        public bool Busy=>Work.Busy||DiagnosticBusy;
        readonly UiWorkState Work=new UiWorkState();
        readonly Dictionary<Control,string[]> ControlLocks=new Dictionary<Control,string[]>();
        readonly Dictionary<Guid,string> Calls=new Dictionary<Guid,string>();
        bool DiagnosticBusy;
        int LocalImageRevision;
        string LastStatus="준비";
        public readonly TabControl Tabs = new TabControl { Dock = DockStyle.Fill };
        public BuilderPane Boot, Firmware;
        readonly TextBox UploadFile = new TextBox { Width = 620, ReadOnly = true };
        readonly ComboBox Ports = new ComboBox { Width = 680, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox Mode = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly NumericUpDown Baud = new NumericUpDown { Minimum = 1200, Maximum = 4000000, Value = 250000, Width = 120 };
        readonly CheckBox FullInstall = new CheckBox { Text = "Katapult 제거 후 일반 Klipper 설치 (No bootloader / BOOTSEL·DFU)", AutoSize = true, ForeColor = Color.DarkRed };
        readonly CheckBox SshFullInstall = new CheckBox { Text = "Katapult 제거 후 일반 Klipper 설치 (No bootloader / BOOTSEL·DFU)", AutoSize = true, ForeColor = Color.DarkRed };
        readonly TextBox UploadInfo = new TextBox { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
        readonly TextBox UploadLog = new TextBox { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, Font = new Font("Consolas",9) };
        readonly Label Status = new Label { Dock = DockStyle.Bottom, Height = 28, Text = ".NET Framework 4.8 · Windows native", Padding = new Padding(8,4,0,0) };
        readonly List<Control> UploadInputs = new List<Control>();
        readonly List<Control> SshInputs = new List<Control>();
        readonly TextBox SshHost = new TextBox { Width = 190 };
        readonly NumericUpDown SshPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 22, Width = 72 };
        readonly TextBox SshUser = new TextBox { Width = 135 };
        readonly TextBox SshPassword = new TextBox { Width = 150, UseSystemPasswordChar = true };
        string SshPythonPath = "";
        string SshScriptPath = "";
        readonly TextBox SshFile = new TextBox { Width = 650, ReadOnly = true };
        readonly ComboBox SshDevices = new ComboBox { Width = 880, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 1150 };
        readonly TextBox SshInfo = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        readonly TextBox SshLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas",9), WordWrap = false };
        readonly CheckBox SshStop = new CheckBox { Text = "작업 중 Klipper 서비스 중지 후 복구", AutoSize = true, Checked = true };
        readonly TextBox SshService = new TextBox { Text = "klipper", Width = 130 };
        string LastSshStatus="";
        Button SshConnectButton;
        Obj SshInspection;
        bool SshConnected, SshUpdating, SshInstallReady;
        readonly TextBox KatapultFile = new TextBox { Width = 430, ReadOnly = true };
        readonly TextBox SshKatapultFile = new TextBox { Width = 430, ReadOnly = true };
        Obj KatapultImage, SshKatapultImage;
        Button KatapultInstallButton, SshKatapultInstallButton;
        readonly TabControl LocalUploadViews = new TabControl { Dock = DockStyle.Fill, Height = 200 };
        readonly TabControl SshUploadViews = new TabControl { Dock = DockStyle.Fill, Height = 200 };
        Button SshCheck, SshFlash;
        Button CheckButton, FlashButton;
        bool Ch34xAutoMode;
        Obj Inspection;

        readonly Dictionary<TabControl,Panel> WizardUploadHosts=new Dictionary<TabControl,Panel>();
        readonly Dictionary<TabControl,Control[]> WizardUploadContents=new Dictionary<TabControl,Control[]>();
        readonly Dictionary<Button,Control> WizardUploadButtonHomes=new Dictionary<Button,Control>();
        internal Button TakeWizardUploadButton(bool ssh,string project){var button=project=="klipper"?(ssh?SshFlash:FlashButton):(ssh?SshKatapultInstallButton:KatapultInstallButton);if(!WizardUploadButtonHomes.ContainsKey(button))WizardUploadButtonHomes[button]=button.Parent;return button;}
        internal void RestoreWizardUploadButtons(){foreach(var pair in WizardUploadButtonHomes)if(!pair.Value.IsDisposed)pair.Value.Controls.Add(pair.Key);}
        internal WizardUi Wizard;
        internal void EnableWizard(){Wizard=new WizardUi(this);Tabs.Visible=false;Controls.Add(Wizard.Root);Wizard.Root.BringToFront();Status.Visible=false;Text="Klipper Firmware Wizard 1.0.23";}
        internal async Task PrepareWizardUploader(bool ssh,string project,string binary){
            SelectWizardUploadContent(ssh,project);
            await SafeTask(async delegate{
                if(project=="klipper"){
                    if(ssh){await InspectSsh(binary);SshFullInstall.Checked=false;UpdateSshInfo();UpdateSshButtons();}
                    else{await Inspect(binary);FullInstall.Checked=false;UpdateLocalInfo();UpdateUploadButtons();}
                }else{
                    string uf2=Path.Combine(Path.GetDirectoryName(binary),"katapult.withclear.uf2");
                    await InspectKatapult(File.Exists(uf2)?uf2:binary,ssh);
                }
            },"빌드 결과 연결",new[]{ssh?"ssh-klipper-file":"local-klipper-file",ssh?"ssh-katapult-file":"local-katapult-file"});
            if(!ssh)await SafeTask(async delegate{await RefreshPorts();Append(UploadLog,Ports.SelectedItem is Item?"자동 선택: "+((Item)Ports.SelectedItem).Text:"검색된 장치가 없습니다. 연결 상태를 확인한 뒤 새로고침하세요.");},"USB 장치 자동 검색",new[]{"local-device"});
        }
        bool WizardNoKat=>Wizard!=null&&Wizard.Project=="klipper"&&!Wizard.KatapultInstalled;
        internal bool UseFullUpload(bool ssh)=>Wizard!=null?WizardNoKat&&ZeroOffset(ssh?SshInspection:Inspection):(ssh?SshFullInstall:FullInstall).Checked;
        void SetReplacementVisibility(bool ssh){var checkbox=ssh?SshFullInstall:FullInstall;checkbox.Visible=Wizard==null;if(checkbox.Parent is TableLayoutPanel panel&&panel.GetControlFromPosition(0,3) is Control hint)hint.Visible=Wizard==null;}
        internal static void SelectFirstDevice(ComboBox devices){if(devices.SelectedIndex<0&&devices.Items.Count>0)devices.SelectedIndex=0;}
        internal static OpenFileDialog FirmwareFileDialog(string filter){string folder=Path.Combine(Program.Root,"Out");Directory.CreateDirectory(folder);return new OpenFileDialog{Filter=filter,InitialDirectory=folder,RestoreDirectory=true,CheckFileExists=true};}
        internal void LayoutWizardUploader(bool ssh){
            var tabs=ssh?SshUploadViews:LocalUploadViews;if(!WizardUploadHosts.TryGetValue(tabs,out Panel host))return;
            int width=Math.Max(480,host.Parent.ClientSize.Width-12);
            if(ssh){SshHost.Width=150;SshPort.Width=65;SshUser.Width=95;SshPassword.Width=110;SshService.Width=90;SshDevices.Width=Math.Max(250,width-(SshConnectButton==null?170:SshConnectButton.GetPreferredSize(Size.Empty).Width)-12);SshFile.Width=width-136;SshKatapultFile.Width=width-280;SshStop.Text="작업 중 Klipper 중지 후 복구";}
            else{Ports.Width=width-120;Ports.DropDownWidth=1000;UploadFile.Width=width-136;KatapultFile.Width=width-280;Mode.Width=175;Baud.Width=90;}
            foreach(var content in WizardUploadContents[tabs])foreach(var label in content.Controls.OfType<Label>())label.MaximumSize=new Size(width-24,0);
            host.Padding=new Padding(4);host.Height=tabs.SelectedIndex==0?50:110;
        }
        static bool ZeroOffset(Obj info)=>info!=null&&info.ContainsKey("bootloader_offset")&&Convert.ToInt64(info["bootloader_offset"])==0;
        bool VendorOffset(Obj info)=>Wizard!=null&&Wizard.Project=="klipper"&&!Wizard.KatapultInstalled&&info!=null&&info.ContainsKey("bootloader_offset")&&Convert.ToInt64(info["bootloader_offset"])>0;
        const string VendorUploadNotice="제조사 부트로더용 오프셋이 적용된 BIN입니다. Out의 파일을 보드 제조사가 지정한 방식으로 설치하세요. 이 프로그램의 Katapult/부트로더 제거 업로드는 적용하지 않습니다.";
        internal void SelectWizardUploadContent(bool ssh,string project){
            var tabs=ssh?SshUploadViews:LocalUploadViews;int selected=project=="klipper"?0:1;
            if(!WizardUploadHosts.TryGetValue(tabs,out Panel host)){
                var parent=(TableLayoutPanel)tabs.Parent;var position=parent.GetPositionFromControl(tabs);
                var content=tabs.TabPages.Cast<TabPage>().Select(page=>page.Controls[0]).ToArray();
                host=new Panel{Dock=DockStyle.Fill,Height=tabs.Height,Padding=new Padding(8)};
                WizardUploadHosts[tabs]=host;WizardUploadContents[tabs]=content;
                parent.Controls.Remove(tabs);tabs.Visible=false;parent.Controls.Add(host,position.Column,position.Row);
                host.Disposed+=delegate{tabs.Dispose();};
                if(!ssh&&parent.GetControlFromPosition(0,0) is Label label)label.Text="업데이트할 메인보드를 선택하세요.";
            }
            var contents=WizardUploadContents[tabs];
            for(int i=0;i<contents.Length;i++)if(contents[i].Parent==host)tabs.TabPages[i].Controls.Add(contents[i]);
            tabs.SelectedIndex=selected;host.Controls.Add(contents[selected]);
            LayoutWizardUploader(ssh);if(ssh)UpdateSshInfo();else UpdateLocalInfo();
        }
        public MainForm() {
            Text = "Klipper Firmware Wizard 1.0.23"; Font = new Font("Malgun Gothic",9); Width = 1080; Height = 950; MinimumSize = new Size(870,740); StartPosition = FormStartPosition.CenterScreen;
            DataRoot = Program.Smoke == "" ? Path.Combine(Program.Root,"winforms-data") : Path.Combine(Program.Smoke,"data"); Directory.CreateDirectory(DataRoot);
            Boot = new BuilderPane(this,"katapult"); Firmware = new BuilderPane(this,"klipper");
            Tabs.TabPages.Add(Boot); Tabs.TabPages.Add(Firmware); Tabs.TabPages.Add(Uploader()); Tabs.TabPages.Add(SshUploader()); Controls.Add(Tabs); Controls.Add(Status);
            FormClosing += delegate(object s, FormClosingEventArgs e) { if(Busy) { e.Cancel = true; Cancel(Boot);Cancel(Firmware); Status.Text = "작업 종료를 기다린 뒤 창을 닫으세요. 진행 중: "+Work.Summary; } };
            Shown += async delegate {
#if DIAGNOSTICS
                if(Program.Smoke != "") { if(Program.LockSmokeOnly) await LockSmoke();else if(Program.MenuSmokeOnly) await MenuSmoke(); else await Smoke(); return; }
#endif
                await Task.WhenAll(
                    SafeTask(async delegate{await Snapshot(Boot,"startup");},"Katapult 최신 버전 준비",new[]{"builder:katapult"}),
                    SafeTask(async delegate{await Snapshot(Firmware,"startup");},"Klipper 최신 버전 준비",new[]{"builder:klipper"}),
                    Wizard==null?SafeTask(RefreshPorts,"로컬 장치 조회",new[]{"local-device"}):Task.FromResult(0));
            };
        }
        TabPage SshUploader() {
            var page = new TabPage("SSH Uploader") { Padding = new Padding(14) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            for(int i=0;i<5;i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent,28)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,72)); page.Controls.Add(grid);
            var credentials = SshRow();
            SshField(credentials,"IP / 호스트",SshHost); SshField(credentials,"포트",SshPort); SshField(credentials,"계정 ID",SshUser); SshField(credentials,"패스워드",SshPassword); grid.Controls.Add(credentials,0,0);
            var devices = SshRow();SshDevices.Width=690;devices.Controls.Add(SshDevices);SshInputs.Add(SshDevices);SshConnectButton=AddSshButton(devices,"SSH 접속 / 장치 새로고침",ConnectSsh);grid.Controls.Add(devices,0,2);
            var files = SshRow(); files.Controls.Add(SshFile);
            AddSshButton(files,"BIN 선택",async delegate { using(var d = FirmwareFileDialog("Klipper BIN|*.bin")) if(d.ShowDialog() == DialogResult.OK) await InspectSsh(d.FileName); });
            var service=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=4,RowCount=1};
            for(int i=0;i<3;i++)service.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));service.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));service.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            SshStop.Anchor=SshService.Anchor=AnchorStyles.Left;SshStop.Margin=SshService.Margin=new Padding(3);
            service.Controls.Add(SshStop,0,0);service.Controls.Add(new Label{Text="서비스 이름",AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(8,3,3,3)},1,0);service.Controls.Add(SshService,2,0);
            service.Controls.Add(new Label{Text="SSH 비밀번호로 sudo 인증",AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(10,3,3,3)},3,0);
            SshInputs.Add(SshStop);SshInputs.Add(SshService);Bind(SshService,"ssh-service");grid.Controls.Add(service,0,3);
            var actions = SshRow(); SshCheck = AddSshButton(actions,"원격 보드 호환성 확인",async delegate { await SshBoardAction("ssh_check"); });
            SshFlash = AddSshButton(actions,"BIN 전송 및 펌웨어 업로드",async delegate { await SshBoardAction("ssh_flash"); });
            ConfigureUploadViews(SshUploadViews,files,actions,true);grid.Controls.Add(SshUploadViews,0,4);SshInputs.Add(SshUploadViews);
            grid.Controls.Add(SshInfo,0,5); grid.Controls.Add(SshLog,0,6);
            SshDevices.SelectedIndexChanged += delegate { UpdateSshInfo(); UpdateSshButtons(); };
            SshDevices.SelectionChangeCommitted += delegate { Wizard?.ClearUploadNotice(); };
            Bind(SshDevices,"ssh-device");Bind(SshStop,"ssh-service");
            foreach(var c in new[]{SshHost,SshUser,SshPassword}) c.TextChanged += delegate { InvalidateSsh(); };
            SshPort.ValueChanged += delegate { InvalidateSsh(); };
            string settings = Path.Combine(DataRoot,"ssh-settings.json");
            if(File.Exists(settings)) {
                try { var saved = Json.Read(File.ReadAllText(settings)); SshUpdating = true; SshHost.Text = Json.S(saved,"host"); SshUser.Text = Json.S(saved,"username"); SshPort.Value = Math.Max(1,Math.Min(65535,Convert.ToInt32(saved["port"]))); }
                catch(Exception) { } finally { SshUpdating = false; }
            }
            ReportSshStatus("SSH 접속 후 장치를 조회하세요. 패스워드는 저장하지 않습니다.");UpdateSshButtons(); return page;
        }
        static FlowLayoutPanel SshRow() { return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true }; }
        void SshField(FlowLayoutPanel row,string label,Control field) { row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3,8,3,3) }); row.Controls.Add(field); SshInputs.Add(field);Bind(field,field==SshService?"ssh-service":"ssh-connection"); }
        Button AddSshButton(FlowLayoutPanel row,string label,Func<Task> action) { var button = new Button { Text = label, AutoSize = true }; row.Controls.Add(button); SshInputs.Add(button);var keys=UploadKeys(true,label);Bind(button,keys);button.Click += delegate { Safe(action,"SSH · "+label,keys); }; return button; }
        void ReportSshStatus(string message,bool once=false){if(once&&LastSshStatus==message)return;LastSshStatus=message;Append(SshLog,message);}
        void InvalidateSsh() { if(SshUpdating) return; Wizard?.ClearUploadNotice(); SshConnected = SshInstallReady = false; SshDevices.Items.Clear();ReportSshStatus("접속 정보가 변경되었습니다. SSH 접속을 다시 확인하세요.",true);UpdateSshButtons(); }
        void UpdateSshButtons() {
            if(SshCheck == null) return;
            var selected=SshDevices.SelectedItem as Item; bool bootsel=selected!=null&&new[]{"bootsel","dfu"}.Contains(Json.S(selected.Data,"kind"));
            SshCheck.Enabled = SshFlash.Enabled = !Locked("ssh-connection","ssh-device","ssh-service","ssh-klipper-file") && SshConnected && selected != null && !bootsel && SshInspection != null;
            if(UseFullUpload(true)){SshCheck.Enabled=false;SshFlash.Enabled=!Locked("ssh-connection","ssh-device","ssh-service","ssh-klipper-file")&&SshInstallReady&&selected!=null&&FullTargetMatches(selected.Data,SshInspection,true);}
            SshFlash.Text=UseFullUpload(true)?(ManualBootselTarget(selected?.Data,SshInspection,true)?"BOOTSEL 전환 후 일반 설치":"Katapult 제거 / 일반 설치"):"BIN 전송 및 펌웨어 업로드";
            SshCheck.Visible=Wizard==null;SetReplacementVisibility(true);if(WizardNoKat)SshFlash.Text="펌웨어 업로드";
            if(Wizard!=null)SshFullInstall.Enabled=!Locked("ssh-klipper-file")&&!VendorOffset(SshInspection);
            if(VendorOffset(SshInspection)){SshCheck.Enabled=SshFlash.Enabled=false;SshFlash.Text=WizardNoKat?"펌웨어 업로드":"제조사 방식으로 업데이트";}
            if(SshKatapultInstallButton!=null) SshKatapultInstallButton.Enabled=!Locked("ssh-connection","ssh-device","ssh-service","ssh-katapult-file")&&SshInstallReady&&SshKatapultImage!=null&&selected!=null&&InstallTargetMatches(selected.Data,SshKatapultImage,true);
        }
        Obj SshRequest() { return new Obj{{"host",SshHost.Text.Trim()},{"port",(int)SshPort.Value},{"username",SshUser.Text.Trim()},{"password",SshPassword.Text},{"python",SshPythonPath},{"script",SshScriptPath}}; }
        async Task<Obj> SshCall(string action,Obj request) {
            var result = await Call("klipper",action,request,SshLog);
            if(result.ContainsKey("host_key_required")) {
                var key = Json.D(result,"host_key_required");
                string text = "처음 접속하는 SSH 서버입니다. 서버 키를 확인하고 신뢰할 때만 계속하세요.\r\n\r\n" + Json.S(key,"host") + "\r\n" + Json.S(key,"type") + "\r\n" + Json.S(key,"fingerprint");
                if(MessageBox.Show(this,text,"SSH 서버 키 확인",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2) != DialogResult.Yes) throw new Exception("SSH 접속이 취소되었습니다.");
                request["accepted_fingerprint"] = Json.S(key,"fingerprint"); result = await Call("klipper",action,request,SshLog);
                if(result.ContainsKey("host_key_required")) throw new Exception("SSH 서버 키가 접속 도중 변경되었습니다.");
            }
            return result;
        }
        async Task ConnectSsh() {
            Wizard?.ClearUploadNotice();
            SshConnected = SshInstallReady = false; SshPythonPath = ""; SshScriptPath = ""; SshDevices.Items.Clear(); UpdateSshButtons();
            ReportSshStatus("SSH 접속 및 장치 조회 중…");
            try{
            var result = await SshCall("ssh_connect",SshRequest());
            SshUpdating = true;
            try { SshPythonPath = Json.S(result,"python"); SshScriptPath = Json.S(result,"script"); } finally { SshUpdating = false; }
            foreach(var device in Json.Rows(result,"devices")) {
                string label = Json.S(device,"device") + "  |  " + Json.S(device,"product") + "  |  " + Json.S(device,"realpath");
                SshDevices.Items.Add(new Item(device,label));
            }
            SshDevices.SelectedIndex = -1;SelectFirstDevice(SshDevices);
            SshConnected = Json.B(result,"python_ready") && Json.B(result,"script_exists");
            SshInstallReady = Json.B(result,"python_ready");
            ReportSshStatus(Json.S(result,"hostname") + " · 장치 " + SshDevices.Items.Count + "개 · " + (SshConnected ? "접속 완료 / 장치 목록을 확인하세요" : SshInstallReady ? "접속 완료 · Katapult 설치 가능 / Klipper 업로드는 원격 Katapult 스크립트 필요" : "Linux의 Python/pyserial 설치 확인 필요"));
            Append(SshLog,"서버 키: " + Json.S(result,"fingerprint") + "\r\nPython: " + Json.S(result,"python") + "\r\nKatapult: " + Json.S(result,"script") + "\r\nKlipper 서비스: " + Json.S(result,"klipper_service"));
            if(!Json.B(result,"python_ready")) Append(SshLog,Json.S(result,"python_error"));
            if(SshInstallReady&&!SshConnected) Append(SshLog,"원격 Katapult 스크립트가 없어도 BOOTSEL/DFU 최초 설치는 가능합니다.");
            var saved = SshRequest(); saved.Remove("password"); saved.Remove("python"); saved.Remove("script"); File.WriteAllText(Path.Combine(DataRoot,"ssh-settings.json"),Json.Write(saved),Encoding.UTF8);
            UpdateSshButtons(); UpdateSshInfo();
            }catch(Exception ex){ReportSshStatus("SSH 접속/장치 조회 중단: "+ex.Message);throw;}
        }
        async Task InspectSsh(string path) {
            Wizard?.ClearUploadNotice();
            SshInspection = null; SshFile.Text = path; UpdateSshButtons();
            try { SshInspection = Json.D(await Call("klipper","inspect",new Obj{{"firmware",path}},SshLog),"firmware"); }
            finally { UpdateSshInfo(); UpdateSshButtons(); }
        }
        void UpdateSshInfo() {
            var text = new StringBuilder(); var device = SshDevices.SelectedItem as Item;
            if(device != null) {
                text.AppendLine("선택한 보드: " + Json.S(device.Data,"product") + " / " + Json.S(device.Data,"manufacturer"));
                text.AppendLine("USB 위치: " + Json.S(device.Data,"usb_path"));
                if(device.Data.ContainsKey("aliases")) foreach(var alias in (object[])device.Data["aliases"]) text.AppendLine(Convert.ToString(alias));
                if(!Json.B(device.Data,"readable") || !Json.B(device.Data,"writable")) text.AppendLine("현재 계정의 시리얼 장치 읽기/쓰기 권한을 확인하세요.");
            }
            if(SshInspection != null && SshUploadViews.SelectedIndex==0) text.AppendLine("BIN: " + Json.S(SshInspection,"mcu") + " · " + Json.S(SshInspection,"version") + "\r\nApplication start: 0x" + Convert.ToInt64(SshInspection["application_start"]).ToString("X") + "\r\nSHA256: " + Json.S(SshInspection,"sha256"));
            if(SshUploadViews.SelectedIndex==1 && SshKatapultImage!=null) text.AppendLine(InstallDescription(SshKatapultImage));
            if(SshUploadViews.SelectedIndex==0&&VendorOffset(SshInspection))text.AppendLine(VendorUploadNotice);
            SshInfo.Text = text.ToString();
        }
        async Task SshBoardAction(string action) {
            Wizard?.ClearUploadNotice();
            if(UseFullUpload(true)){if(action=="ssh_flash")await ReplaceBootloader(true);return;}
            var device = SshDevices.SelectedItem as Item;
            if(!SshConnected || device == null || SshInspection == null) throw new Exception("SSH 접속, 보드 및 BIN 선택을 먼저 완료하세요.");
            var request = SshRequest(); request["device"] = device.Data; request["firmware"] = SshFile.Text; request["sha256"] = Json.S(SshInspection,"sha256");
            request["stop_service"] = SshStop.Checked; request["service"] = SshService.Text.Trim();
            SshLog.Clear();
            try {
                var result = await SshCall(action,request);
                Append(SshLog,action == "ssh_flash" ? "원격 업로드 및 읽기 검증 완료" : "원격 보드 호환성 확인 완료 (펌웨어 쓰기 없음)");
                Append(SshLog,"로컬 로그: " + Json.S(result,"local_log"));
                ReportSshStatus("작업 완료 · 보드와 BIN 선택 유지 · 다음 작업에서 장치를 다시 확인합니다.");
            } catch {
                ReportSshStatus("작업 실패 · 선택 유지 · 로그의 보드/서비스 상태를 확인하세요.");
                throw;
            } finally { UpdateSshInfo(); UpdateSshButtons(); }
        }
        TabPage Uploader() {
            var page = new TabPage("Uploader") { Padding = new Padding(14) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            for(int i=0;i<3;i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,30)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,70));
            page.Controls.Add(grid);
            grid.Controls.Add(new Label { Text = "공용 장치 선택 · 아래 탭에서 업로드 종류를 선택하세요.", AutoSize = true },0,0);
            var file = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; file.Controls.Add(UploadFile);
            AddUploadButton(file,"BIN 선택", async delegate { using(var d = FirmwareFileDialog("Firmware BIN|*.bin")) if(d.ShowDialog() == DialogResult.OK) await Inspect(d.FileName); });
            var ports = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; ports.Controls.Add(Ports); UploadInputs.Add(Ports);
            AddUploadButton(ports,"장치 새로고침",RefreshPorts); grid.Controls.Add(ports,0,1);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            Mode.Items.AddRange(new object[]{"자동 부트로더 진입", "수동 (Katapult 모드)", "UART 연결"}); Mode.SelectedIndex = 0; actions.Controls.Add(Mode); actions.Controls.Add(Baud); UploadInputs.Add(Mode); UploadInputs.Add(Baud);
            CheckButton = AddUploadButton(actions,"보드 호환성 확인",async delegate { await BoardAction("check"); }); FlashButton = AddUploadButton(actions,"펌웨어 업로드",async delegate { await BoardAction("flash"); });
            ConfigureUploadViews(LocalUploadViews,file,actions,false);grid.Controls.Add(LocalUploadViews,0,2);UploadInputs.Add(LocalUploadViews);grid.Controls.Add(UploadInfo,0,3);grid.Controls.Add(UploadLog,0,4);
            Ports.SelectedIndexChanged += delegate { UpdateUploadButtons(); var p = Ports.SelectedItem as Item; if(p != null) Status.Text = Json.S(p.Data,"details"); ApplySelectedPortMode(); };
            Ports.SelectionChangeCommitted += delegate { Wizard?.ClearUploadNotice(); };
            Mode.SelectionChangeCommitted += delegate { Wizard?.ClearUploadNotice(); Ch34xAutoMode = false; ApplySelectedPortMode(); };
            Bind(Ports,"local-device");Bind(Mode,"local-device");Bind(Baud,"local-device");
            UpdateUploadButtons(); return page;
        }
        Button AddUploadButton(FlowLayoutPanel row,string text,Func<Task> action) { var b = new Button { Text = text,AutoSize = true }; row.Controls.Add(b); UploadInputs.Add(b);var keys=UploadKeys(false,text);Bind(b,keys); b.Click += delegate { Safe(action,"로컬 · "+text,keys); }; return b; }
        void ConfigureUploadViews(TabControl tabs,Control files,Control actions,bool ssh) {
            var firmware=new TabPage("Klipper 업로드"){Padding=new Padding(8)};var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};for(int i=0;i<4;i++)grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));grid.Controls.Add(files,0,0);grid.Controls.Add(actions,0,1);
            var replace=ssh?SshFullInstall:FullInstall;grid.Controls.Add(replace,0,2);Bind(replace,ssh?"ssh-klipper-file":"local-klipper-file");
            var hint=new Label{Text="기본: Katapult를 통한 Klipper 업데이트. 교체 설치는 ROM 부트 모드가 필요합니다.",AutoSize=true,MaximumSize=new Size(900,0)};grid.Controls.Add(hint,0,3);
            replace.CheckedChanged+=delegate{hint.Text=replace.Checked?"플래시 시작부터 설치합니다. Katapult USB 선택 시 설치 버튼을 누른 뒤 BOOTSEL을 누른 채 같은 USB 포트에 다시 연결하세요.":"기본: Katapult를 통한 Klipper 업데이트. 교체 설치는 ROM 부트 모드가 필요합니다.";UpdateUploadButtons();UpdateSshButtons();};firmware.Controls.Add(grid);
            var boot=new TabPage("Katapult 설치"){Padding=new Padding(8)};boot.Controls.Add(KatapultInstallControls(ssh));tabs.TabPages.Add(firmware);tabs.TabPages.Add(boot);
            tabs.SelectedIndexChanged+=delegate{if(ssh)UpdateSshInfo();else UpdateLocalInfo();};
        }
        Control KatapultInstallControls(bool ssh) {
            var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var row=SshRow();
            row.Controls.Add(ssh?SshKatapultFile:KatapultFile);
            Func<string,Func<Task>,Button> add=(label,action)=>ssh?AddSshButton(row,label,action):AddUploadButton(row,label,action);
            add("UF2 / BIN 선택",async delegate {using(var dialog=FirmwareFileDialog("Katapult 설치|katapult.withclear.uf2;katapult.bin"))if(dialog.ShowDialog()==DialogResult.OK)await InspectKatapult(dialog.FileName,ssh);});
            add("최근 Katapult 빌드",async delegate {if(Boot.ResultFolder=="")throw new Exception("먼저 Katapult를 빌드하세요.");string uf2=Path.Combine(Boot.ResultFolder,"katapult.withclear.uf2");await InspectKatapult(File.Exists(uf2)?uf2:Path.Combine(Boot.ResultFolder,"katapult.bin"),ssh);});
            var install=add("Katapult 설치",async delegate {await InstallKatapult(ssh);});
            if(ssh)SshKatapultInstallButton=install;else KatapultInstallButton=install;
            grid.Controls.Add(row,0,0);grid.Controls.Add(new Label{Text="RP2040/RP2350: BOOTSEL + UF2   |   STM32 F0/F2/F4/G0/G4/H7: DFU + BIN\r\nKlipper 시작 영역을 초기화합니다. 설치 후 오프셋에 맞춘 Klipper를 업로드하세요.",AutoSize=true,MaximumSize=new Size(920,0)},0,1);return grid;
        }
        async Task InspectKatapult(string path,bool ssh) {
            Wizard?.ClearUploadNotice();
            if(ssh){SshKatapultImage=null;SshKatapultFile.Text=path;}else{KatapultImage=null;KatapultFile.Text=path;}
            UpdateUploadButtons();UpdateSshButtons();
            var result=await Call("katapult","katapult_image",new Obj{{"firmware",path}},ssh?SshLog:UploadLog);
            var info=Json.D(result,"image");if(ssh)SshKatapultImage=info;else KatapultImage=info;
            Append(ssh?SshLog:UploadLog,InstallDescription(info));if(ssh)UpdateSshInfo();else UpdateLocalInfo();
            UpdateUploadButtons();UpdateSshButtons();
        }
        async Task InstallKatapult(bool ssh) {
            Wizard?.ClearUploadNotice();
            var selected=(ssh?SshDevices.SelectedItem:Ports.SelectedItem) as Item;
            var info=ssh?SshKatapultImage:KatapultImage;
            if(selected==null||info==null)throw new Exception("설치 대상과 검증된 Katapult 파일을 선택하세요.");
            bool dfu=Json.S(info,"method")=="dfu";
            string warning="대상: "+selected.Text+"\r\n파일: "+Json.S(info,"path")+"\r\n\r\n부트로더 영역을 기록하고 Klipper 시작 영역을 초기화합니다. 설치 후 해당 오프셋에 맞는 Klipper를 업로드해야 합니다."+(ssh?"\r\n작업에서 중지한 Klipper 서비스는 작업 종료 후 다시 시작합니다.":"");
            if(dfu)warning+="\r\n\r\nDFU는 칩 ID를 직접 판독하지 못합니다. 실제 보드 MCU가 "+Json.S(info,"mcu")+"인지 확인하세요. 실제 용량/섹터를 확인하고 부트로더와 앱 시작점이 포함된 섹터를 지웁니다.";
            if(dfu&&Json.S(info,"mcu")=="stm32h750xx")warning+="\r\nH750은 부트로더와 앱 시작점이 같은 섹터에 있어 128KiB 플래시 전체가 지워집니다.";
            if(MessageBox.Show(this,warning,"Katapult 설치 · "+Json.S(info,"mcu"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            var request=ssh?SshRequest():new Obj();request["firmware"]=Json.S(info,"path");request["sha256"]=Json.S(info,"sha256");request[ssh?"device":"port"]=selected.Data;
            var log=ssh?SshLog:UploadLog;log.Clear();
            if(ssh){request["service"]=SshService.Text.Trim();request["stop_service"]=SshStop.Checked;await SshCall(dfu?"ssh_dfu_install":"ssh_rp2040_install",request);ReportSshStatus("Katapult 기록 완료 · 로그에서 검증/실행 확인 결과를 확인하세요. Klipper 설치가 필요합니다.");}
            else {bool completed=false;try{await Call("katapult",dfu?"dfu_install":"rp2040_install",request,log);completed=true;}finally{await RefreshLocalAfterOperation(selected.Data,completed&&Json.S(info,"communication")=="USB"?"24951":"");}}
            Append(log,"Katapult 주소에 맞춘 Klipper BIN을 선택하세요. 설치 후 장치가 선택되지 않았다면 장치 목록을 새로고침하세요.");
        }
        static bool InstallTargetMatches(Obj target,Obj info,bool ssh){if(Json.S(target,"kind")=="bootsel"&&Json.S(target,"mcu")!=""&&Json.S(target,"mcu")!=Json.S(info,"mcu"))return false;if(ssh){string kind=Json.S(target,"kind");return kind==(Json.S(info,"method")=="dfu"?"dfu":"bootsel")||Json.S(target,"vid")=="1d50"&&Json.S(target,"pid")=="614e";}return Json.S(info,"method")=="dfu"?Stm32Install.CanInstall(target):Rp2040Install.CanInstall(target);}
        static bool FullTargetMatches(Obj target,Obj info,bool ssh){if(info==null)return false;string mcu=Json.S(info,"mcu");bool rp=mcu=="rp2040"||mcu=="rp2350";try{if(!rp)Stm32DfuProfile.For(mcu);if(Convert.ToUInt32(info["application_start"])!=(rp?0x10000000u:0x08000000u))return false;}catch{return false;}return ManualBootselTarget(target,info,ssh)||InstallTargetMatches(target,new Obj{{"mcu",mcu},{"method",rp?"bootsel":"dfu"}},ssh);}
        static bool ManualBootselTarget(Obj target,Obj info,bool ssh){return target!=null&&info!=null&&new[]{"rp2040","rp2350"}.Contains(Json.S(info,"mcu"))&&(ssh?Json.S(target,"vid")=="1d50"&&Json.S(target,"pid")=="6177"&&Json.S(target,"usb_path")!="":Rp2040Install.IsKatapult(target)&&Json.S(target,"location")!="");}
        async Task ReplaceBootloader(bool ssh){
            Wizard?.ClearUploadNotice();
            var selected=(ssh?SshDevices.SelectedItem:Ports.SelectedItem) as Item;var inspection=ssh?SshInspection:Inspection;var log=ssh?SshLog:UploadLog;
            if(selected==null||!FullTargetMatches(selected.Data,inspection,ssh))throw new Exception("No bootloader BIN과 일치하는 BOOTSEL/DFU 또는 Klipper USB 장치를 선택하세요.");
            string file=ssh?SshFile.Text:UploadFile.Text;var check=await Call("klipper","full_klipper_image",new Obj{{"firmware",file}},log);var info=Json.D(check,"image");
            if(Json.S(info,"sha256")!=Json.S(inspection,"sha256"))throw new Exception("선택 후 파일이 변경됐습니다. 다시 선택하세요.");bool dfu=Json.S(info,"method")=="dfu";
            string message="대상: "+selected.Text+"\r\nMCU: "+Json.S(info,"mcu")+"\r\n파일: "+file+"\r\n기록 시작: 0x"+Convert.ToUInt32(info["application_start"]).ToString("X8")+"\r\n\r\nKatapult가 덮어써져 제거됩니다. 현재 Katapult에서 ROM 부트 모드로 전환되지 않으면 수동 BOOTSEL/DFU 진입이 필요합니다."+(dfu?"\r\nDFU는 정확한 칩 ID를 읽지 못하므로 실제 MCU를 확인하세요. 이미지가 차지하는 섹터 전체가 지워집니다.":"")+(ssh?"\r\n작업에서 중지한 Klipper 서비스는 작업 종료 후 다시 시작합니다.":"");
            if(WizardNoKat)message=message.Replace("Katapult가 덮어써져 제거됩니다. 현재 Katapult에서 ROM 부트 모드로 전환되지 않으면 수동 BOOTSEL/DFU 진입이 필요합니다.","선택한 시작 주소에 펌웨어를 기록합니다. 보드의 BOOTSEL/DFU 진입이 필요할 수 있습니다.");
            if(ManualBootselTarget(selected.Data,info,ssh))message+="\r\n\r\n확인 후 120초 이내에 BOOTSEL 버튼을 누른 채 같은 USB 포트에 다시 연결하세요. 자동 BOOTSEL 전환이나 Katapult 통신으로의 강제 기록은 하지 않습니다.";
            if(MessageBox.Show(this,message,WizardNoKat?"펌웨어 업로드":"Katapult 제거 후 일반 Klipper 설치",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            var req=ssh?SshRequest():new Obj();req["firmware"]=file;req["sha256"]=Json.S(info,"sha256");req["replace_bootloader"]=true;req[ssh?"device":"port"]=selected.Data;log.Clear();
            if(ssh){req["service"]=SshService.Text.Trim();req["stop_service"]=SshStop.Checked;await SshCall(dfu?"ssh_dfu_install":"ssh_rp2040_install",req);SshFullInstall.Checked=false;ReportSshStatus("일반 Klipper 기록 완료 · 재인식/검증 결과는 로그를 확인하세요.");}else{bool completed=false;try{await Call("klipper",dfu?"dfu_install":"rp2040_install",req,log);FullInstall.Checked=false;completed=true;}finally{await RefreshLocalAfterOperation(selected.Data,completed&&Json.S(info,"communication")=="USB"?"24910":"");}}
            Append(log,(WizardNoKat?"Klipper 펌웨어 업로드":"Katapult 제거 후 Klipper 설치")+" 작업을 마쳤습니다. 장치 목록과 보드 동작을 확인하세요.");
        }
        static string InstallDescription(Obj info)=>"Katapult "+(Json.S(info,"method")=="dfu"?"DFU BIN":"BOOTSEL UF2")+": "+Json.S(info,"mcu")+" · "+Json.S(info,"version")+"\r\nKlipper 시작 주소: 0x"+Convert.ToUInt32(info["application_start"]).ToString("X")+"\r\nSHA256: "+Json.S(info,"sha256");
        void UpdateLocalInfo(){if(LocalUploadViews.SelectedIndex==1)UploadInfo.Text=KatapultImage==null?"Katapult UF2/BIN을 선택하세요.":InstallDescription(KatapultImage);else UploadInfo.Text=Inspection==null?"Klipper BIN을 선택하세요.":"Klipper: "+Json.S(Inspection,"mcu")+" · "+Json.S(Inspection,"version")+"\r\nApplication start: 0x"+Convert.ToUInt32(Inspection["application_start"]).ToString("X")+"\r\nSHA256: "+Json.S(Inspection,"sha256");if(LocalUploadViews.SelectedIndex==0&&VendorOffset(Inspection))Append(UploadInfo,VendorUploadNotice);}
        internal bool Locked(params string[] keys)=>DiagnosticBusy||Work.Locked(keys);
        public bool IsBuilderBusy(string project)=>Locked("builder:"+project);
        void Bind(Control control,params string[] keys){ControlLocks[control]=keys;}
        static string[] UploadKeys(bool ssh,string label){string side=ssh?"ssh":"local";
            if(label=="BIN 선택")return new[]{side+"-klipper-file"};
            if(label=="UF2 / BIN 선택"||label=="최근 Katapult 빌드")return new[]{side+"-katapult-file"};
            if(label.Contains("새로고침"))return ssh?new[]{"ssh-connection","ssh-device"}:new[]{"local-device"};
            string file=label=="Katapult 설치"?"katapult-file":"klipper-file";
            return ssh?new[]{"ssh-connection","ssh-device","ssh-service","ssh-"+file}:new[]{"local-device","local-"+file};
        }
        public void Safe(Func<Task> action,string label,params string[] keys) { var task = SafeTask(action,label,keys); }
        internal async Task SafeTask(Func<Task> action,string label,string[] keys) {
            if(DiagnosticBusy||!Work.Begin(label,keys,out Guid token)){LastStatus="관련 작업이 진행 중입니다.";RefreshWorkStatus();return;}
            ApplyBusyStates();RefreshWorkStatus();
            try { await action(); } catch(Exception ex) { LastStatus=ex.Message;Status.Text = ex.Message; if(Program.Smoke == "") MessageBox.Show(this,ex.Message,"작업 실패",MessageBoxButtons.OK,MessageBoxIcon.Error); else throw; }
            finally { Work.End(token);ApplyBusyStates();RefreshWorkStatus(); }
        }
        void SetBusy(bool value) { DiagnosticBusy=value;ApplyBusyStates(); }
        void ApplyBusyStates(){Boot?.SetBusy(IsBuilderBusy("katapult"));Firmware?.SetBusy(IsBuilderBusy("klipper"));foreach(var pair in ControlLocks)pair.Key.Enabled=!Locked(pair.Value);UpdateUploadButtons();UpdateSshButtons();}
        void RefreshWorkStatus(){Status.Text=Calls.Count>0?"작업 중: "+string.Join(" | ",Calls.Values):Work.Busy?"작업 중: "+Work.Summary:LastStatus;}
        void UpdateUploadButtons() { if(CheckButton == null) return;var selected=Ports.SelectedItem as Item; CheckButton.Enabled = FlashButton.Enabled = !Locked("local-device","local-klipper-file") && Inspection != null && selected != null && !new[]{"bootsel","dfu"}.Contains(Json.S(selected.Data,"kind"));if(UseFullUpload(false)){CheckButton.Enabled=false;FlashButton.Enabled=!Locked("local-device","local-klipper-file")&&selected!=null&&FullTargetMatches(selected.Data,Inspection,false);}FlashButton.Text=UseFullUpload(false)?(ManualBootselTarget(selected?.Data,Inspection,false)?"BOOTSEL 전환 후 일반 설치":"Katapult 제거 / 일반 설치"):"펌웨어 업로드";if(Wizard!=null)FullInstall.Enabled=!Locked("local-klipper-file")&&!VendorOffset(Inspection);if(VendorOffset(Inspection)){CheckButton.Enabled=FlashButton.Enabled=false;FlashButton.Text="제조사 방식으로 업데이트";}SetReplacementVisibility(false);if(WizardNoKat)FlashButton.Text="펌웨어 업로드";CheckButton.Visible=Mode.Visible=Baud.Visible=Wizard==null;Mode.Enabled=Baud.Enabled=!Locked("local-device")&&!UseFullUpload(false);if(KatapultInstallButton!=null)KatapultInstallButton.Enabled=!Locked("local-device","local-katapult-file")&&KatapultImage!=null&&selected!=null&&InstallTargetMatches(selected.Data,KatapultImage,false); }
        void ApplySelectedPortMode() {
            if(Wizard!=null){Ch34xAutoMode=false;Mode.SelectedIndex=0;return;}
            var selected = Ports.SelectedItem as Item;
            bool bridge = selected != null && SerialFlash.IsCh34x(selected.Data);
            if(bridge && (Mode.SelectedIndex == 0 || Ch34xAutoMode)) {
                bool changed = Mode.SelectedIndex != 1 || !Ch34xAutoMode;
                Mode.SelectedIndex = 1; Ch34xAutoMode = true;
                if(changed) {
                    SerialFlash.ResolveMode(selected.Data,"auto",line=>Append(UploadLog,line));
                    Status.Text = Json.S(selected.Data,"device") + " · CH340/CH341 · 수동 연결 모드";
                }
            } else if(!bridge && Ch34xAutoMode) { Mode.SelectedIndex = 0; Ch34xAutoMode = false; }
        }
        public static void Append(TextBox box,string value) { if(!box.IsDisposed) { box.AppendText(value.Replace("\r\n","\n").Replace("\n","\r\n") + "\r\n"); } }
        public async Task<Obj> Call(string project,string action,Obj extra,TextBox log) {
            var req = extra == null ? new Obj() : new Obj(extra); req["project"]=project; req["action"]=action; req["data"]=DataRoot;
            var callId=Guid.NewGuid();Calls[callId]=project+" / "+action;RefreshWorkStatus();
            Wizard?.BeginOperation(project,action);
            IProgress<string> progress = new Progress<string>(line=>{if(Wizard!=null&&Wizard.ReportProgress(project,line))return;if(log!=null)Append(log,line);});
            try { var result=await Task.Run(()=>new NativeEngine(req,line=>progress.Report(line)).Run());Wizard?.UploadFinished(action,req,result);LastStatus="완료: "+project+" / "+action;return result; }
            catch{if(WizardUi.IsUploadAction(action))Wizard?.UploadFailed();throw;}
            finally {Calls.Remove(callId);RefreshWorkStatus();}
        }
        public async Task Snapshot(BuilderPane pane,string action,Obj extra = null) {
            try { pane.Render(await Call(pane.Project,action,extra,pane.Log)); }
            catch { if(pane.State != null) pane.Render(pane.State); throw; }
            string warning = Json.S(pane.State,"startup_warning");
            if(warning != "") { Append(pane.Log,warning); Status.Text = warning; }
        }
        void ClearFirmware() { LocalImageRevision++;Inspection = null; UploadFile.Text = ""; UploadInfo.Text = ""; UpdateUploadButtons(); }
        public async Task Build(BuilderPane pane) {
            if(Wizard!=null&&!Wizard.CheckBeforeBuild(pane))return;
            int revision=LocalImageRevision;
            pane.Log.Clear();
            Directory.CreateDirectory(Path.Combine(DataRoot,"requests")); pane.CancelPath = Path.Combine(DataRoot,"requests",Guid.NewGuid().ToString("N") + ".cancel");ApplyBusyStates();
            try {
                Obj response = await Call(pane.Project,"build",new Obj{{"output",Path.GetFullPath(pane.Output.Text)},{"cancel_file",pane.CancelPath}},pane.Log);
                pane.ResultFolder = Json.S(response,"output");
                if(Wizard!=null)await Wizard.BuildFinished(pane,response);
                if(pane == Firmware&&Wizard==null){if(Program.Smoke!=""&&DiagnosticBusy)await Inspect(Json.S(response,"binary"));else if(revision==LocalImageRevision&&!Locked("local-device","local-klipper-file"))await SafeTask(async delegate{await Inspect(Json.S(response,"binary"));},"새 빌드 BIN 확인",new[]{"local-klipper-file"});else Append(pane.Log,"새 빌드는 완료됐습니다. 현재 업로더 파일 선택을 유지합니다. BIN 선택으로 새 파일을 불러오세요.");}
            } catch(Exception ex){Wizard?.BuildFailed(ex.Message);throw;} finally { if(File.Exists(pane.CancelPath)) File.Delete(pane.CancelPath);pane.CancelPath="";ApplyBusyStates(); }
        }
        public void Cancel(BuilderPane pane) { if(pane.CancelPath != "") { File.WriteAllText(pane.CancelPath,"cancel"); Status.Text = pane.Project+" · 현재 컴파일 작업 종료 후 취소합니다."; } }
        bool ApplyPortList(IEnumerable<Obj> devices,Obj previous,string expectedPid){
            Ports.BeginUpdate();try{
                Ports.Items.Clear();foreach(var port in devices)Ports.Items.Add(new Item(port,Json.S(port,"label")+" | "+Json.S(port,"details")));
                string location=previous==null?"":Json.S(previous,"location");
                var matches=Ports.Items.Cast<Item>().Where(item=>location!=""&&Json.S(item.Data,"location")==location&&(expectedPid==""||Json.S(item.Data,"vid")=="7504"&&Json.S(item.Data,"pid")==expectedPid)).ToArray();
                Ports.SelectedItem=matches.Length==1?matches[0]:null;
            }finally{Ports.EndUpdate();UpdateUploadButtons();}
            return Ports.SelectedItem!=null;
        }
        async Task RefreshPorts() { Wizard?.ClearUploadNotice();var previous=(Ports.SelectedItem as Item)?.Data;var response=await Call("klipper","ports",null,UploadLog);ApplyPortList(Json.Rows(response,"ports"),previous,"");if(Wizard!=null)SelectFirstDevice(Ports); }
        async Task RefreshLocalAfterOperation(Obj previous,string expectedPid){
            // Refresh is read-only and must not turn a completed flash into a reported failure.
            Ports.Items.Clear();UpdateUploadButtons();Append(UploadLog,"작업 후 장치 목록 갱신 · 같은 USB 위치의 새 COM/BOOTSEL/DFU를 확인합니다.");
            try{
                DateTime until=DateTime.UtcNow.AddSeconds(8);
                do{
                    var response=await Call("klipper","ports",null,UploadLog);
                    if(ApplyPortList(Json.Rows(response,"ports"),previous,expectedPid)){Append(UploadLog,"장치 선택 갱신: "+((Item)Ports.SelectedItem).Text);return;}
                    if(DateTime.UtcNow>=until)break;await Task.Delay(750);
                }while(true);
                Append(UploadLog,"같은 USB 위치의 대상이 없거나 여러 개여서 선택을 해제했습니다. 장치 목록을 새로고침하세요.");
            }catch(Exception ex){Ports.Items.Clear();UpdateUploadButtons();Append(UploadLog,"작업 후 장치 조회 실패 (펌웨어 작업 결과와 별개): "+ex.Message);}
        }
        async Task Inspect(string path) {
            Wizard?.ClearUploadNotice();
            ClearFirmware(); UploadFile.Text = path;
            try { var result = await Call("klipper","inspect",new Obj{{"firmware",path}},UploadLog); Inspection = Json.D(result,"firmware"); UploadInfo.Text = "MCU: " + Json.S(Inspection,"mcu") + "\r\nVersion: " + Json.S(Inspection,"version") + "\r\nApplication start: 0x" + Convert.ToInt64(Inspection["application_start"]).ToString("X") + "\r\nBootloader offset: " + Json.S(Inspection,"bootloader_offset") + " bytes\r\nSHA256: " + Json.S(Inspection,"sha256"); }
            catch(Exception ex) { UploadInfo.Text = "BIN 분석 실패: " + ex.Message; Append(UploadLog,UploadInfo.Text); }
            UpdateUploadButtons();if(LocalUploadViews.SelectedIndex==1||VendorOffset(Inspection))UpdateLocalInfo();
        }
        async Task BoardAction(string action) {
            Wizard?.ClearUploadNotice();
            if(UseFullUpload(false)){if(action=="flash")await ReplaceBootloader(false);return;}
            var selected = Ports.SelectedItem as Item; if(Inspection == null || selected == null) throw new InvalidOperationException("검증된 BIN과 COM 보드를 선택하세요.");
            ApplySelectedPortMode();
            var req = new Obj{{"firmware",UploadFile.Text},{"sha256",Json.S(Inspection,"sha256")},{"port",selected.Data},{"mode",new[]{"auto","manual","uart"}[Mode.SelectedIndex]},{"baud",(int)Baud.Value}};
            bool completed=false;try{await Call("klipper",action,req,UploadLog);completed=true;}
            finally{await RefreshLocalAfterOperation(selected.Data,completed&&Json.S(selected.Data,"vid")=="7504"?(action=="check"?"24951":"24910"):"");}
        }
    }
}
