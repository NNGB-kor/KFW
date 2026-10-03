using System;using System.IO;using System.Linq;using System.Drawing;using System.Windows.Forms;using System.Threading.Tasks;using System.Collections.Generic;
namespace KlipperWinForms {using Obj=Dictionary<string,object>;
 sealed class WizardUi {
  readonly MainForm owner;
  internal readonly Panel Root=new Panel{Dock=DockStyle.Fill,Padding=new Padding(10)};
  readonly Panel body=new Panel{Dock=DockStyle.Fill};
  readonly Label title=new Label{AutoSize=true,Font=new Font("Malgun Gothic",11,FontStyle.Bold),Margin=new Padding(0,0,0,3)};
  readonly Label steps=new Label{AutoSize=true,UseMnemonic=false,ForeColor=Color.DimGray,Margin=new Padding(0,0,0,6)};
  readonly Label status=new Label{AutoSize=true,MaximumSize=new Size(690,0),Margin=new Padding(0,3,0,3)};
  readonly TableLayoutPanel uploadNotice=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=2,Padding=new Padding(10,6,10,6),Margin=new Padding(0,5,0,5),Visible=false};
  readonly Label uploadNoticeTitle=new Label{AutoSize=true,UseMnemonic=false,Font=new Font("Malgun Gothic",11,FontStyle.Bold),Margin=Padding.Empty};
  readonly Label uploadNoticeDetail=new Label{AutoSize=true,UseMnemonic=false,MaximumSize=new Size(660,0),Margin=new Padding(0,3,0,0)};
  internal readonly Button Back=new Button{Text="이전",AutoSize=true,MinimumSize=new Size(65,27)};
  internal readonly Button Next=new Button{Text="다음",AutoSize=true,MinimumSize=new Size(65,27)};
  readonly Button home=new Button{Text="처음으로",AutoSize=true,MinimumSize=new Size(65,27)};
  internal RadioButton FirstChoice,SecondChoice;
  internal Control QuestionBlock;
  readonly TableLayoutPanel nav=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=5,Padding=new Padding(0,5,0,0)};
  readonly Panel buildNav=new Panel{Dock=DockStyle.Fill,Visible=false};
  Button uploadAction;
  ComboBox versionChoices;
  Label versionNotice;
  readonly Dictionary<string,string> downloadCounts=new Dictionary<string,string>();
  string versionSignature="";
  bool versionPending;
  bool versionUpdating;
  readonly Dictionary<string,string> versionTransfers=new Dictionary<string,string>();
  readonly HashSet<string> failedVersions=new HashSet<string>();
  int windowKind=-1;
  internal bool UseSsh;
  bool uploadPreparing;
  Obj completedState;
  string completedProject="",completedHash="";
  readonly Control[] uploadViews;
  internal readonly TextBox CopyFolder=new TextBox{Dock=DockStyle.Fill};
  readonly Button browse=new Button{Text="폴더 선택",AutoSize=true};
  readonly Label result=new Label{AutoSize=true,MaximumSize=new Size(690,0)};
  readonly ProgressBar progress=new ProgressBar{Dock=DockStyle.Fill,Height=12,Style=ProgressBarStyle.Continuous,Minimum=0,Maximum=100};
  readonly Dictionary<string,Control[]> views=new Dictionary<string,Control[]>();
  readonly Timer timer=new Timer{Interval=200};
  internal int Step;internal string Project="klipper";internal bool KatapultInstalled;
  string exportFolder="";internal string LastExport="";bool copying,buildComplete;
  BuilderPane Pane=>Project=="klipper"?owner.Firmware:owner.Boot;
  bool NavigationLocked=>copying||uploadPreparing||owner.Boot.CancelPath!=""||owner.Firmware.CancelPath!=""||Step==6&&owner.Busy;
  internal bool ReadyForUpdate=>completedState!=null&&ReferenceEquals(completedState,Pane.State)&&completedProject==Project&&LastExport!=""&&File.Exists(LastExport);
  internal WizardUi(MainForm owner){
   this.owner=owner;CopyFolder.Text=Path.Combine(Program.Root,"Out");
   uploadViews=new[]{owner.Tabs.TabPages[2].Controls[0],owner.Tabs.TabPages[3].Controls[0]};
   views["klipper"]=owner.Firmware.WizardViews();views["katapult"]=owner.Boot.WizardViews();
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6};
   for(int i=0;i<6;i++)layout.RowStyles.Add(new RowStyle(i==2?SizeType.Percent:SizeType.AutoSize,i==2?100:0));
   layout.Controls.Add(title,0,0);layout.Controls.Add(steps,0,1);layout.Controls.Add(body,0,2);layout.Controls.Add(status,0,3);
   uploadNotice.RowStyles.Add(new RowStyle(SizeType.AutoSize));uploadNotice.RowStyles.Add(new RowStyle(SizeType.AutoSize));uploadNotice.Controls.Add(uploadNoticeTitle,0,0);uploadNotice.Controls.Add(uploadNoticeDetail,0,1);layout.Controls.Add(uploadNotice,0,4);
   nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=2;i<5;i++)nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));nav.Controls.Add(home,2,0);nav.Controls.Add(Back,3,0);nav.Controls.Add(Next,4,0);
   var footer=new Panel{Dock=DockStyle.Fill,Height=40};footer.Controls.Add(nav);footer.Controls.Add(buildNav);layout.Controls.Add(footer,0,5);Root.Controls.Add(layout);buildNav.Resize+=delegate{LayoutBuildButtons();};
   Back.Click+=delegate{if(NavigationLocked||versionPending)return;Show(Step>=3?Step-1:Step==2&&Project=="klipper"?1:0);};
   home.Click+=delegate{if(!NavigationLocked)Show(0);};
   Next.Click+=async delegate{if(NavigationLocked||versionPending)return;if(Step==0){ChooseProject(FirstChoice.Checked?"klipper":"katapult");return;}if(Step==1){ChooseLoader(SecondChoice.Checked);return;}if(Step==2){await SelectVersion();return;}if(Step==4){if(ValidateBuiltFile())Show(5);return;}if(Step==5){await OpenUploader();return;}if(Step!=3||owner.IsBuilderBusy(Project)||Pane.IsValueEditing)return;if(WarnConfiguration())Show(4);};
   browse.Click+=delegate{using(var dialog=new FolderBrowserDialog{SelectedPath=CopyFolder.Text,Description="완성된 BIN / UF2를 복사할 폴더"})if(dialog.ShowDialog(owner)==DialogResult.OK)CopyFolder.Text=dialog.SelectedPath;};
   Root.Resize+=delegate{if(owner.IsDisposed||owner.Disposing||Root.IsDisposed||Root.Disposing)return;int width=Math.Max(280,Root.ClientSize.Width-28);steps.MaximumSize=status.MaximumSize=result.MaximumSize=Pane.Summary.MaximumSize=new Size(width,0);uploadNoticeTitle.MaximumSize=uploadNoticeDetail.MaximumSize=new Size(width-24,0);};
   timer.Tick+=delegate{Update();};Root.Disposed+=delegate{timer.Dispose();foreach(var pair in views)foreach(var view in pair.Value)view.Dispose();foreach(var view in uploadViews)view.Dispose();};timer.Start();Show(0);
  }
  void SizeWindow(int kind){if(windowKind==kind)return;windowKind=kind;var center=new Point(owner.Left+owner.Width/2,owner.Top+owner.Height/2);owner.MinimumSize=kind>=1?new Size(660,480):new Size(400,260);owner.ClientSize=kind>=1?new Size(720,520):new Size(440,240);if(owner.Visible){var area=Screen.FromControl(owner).WorkingArea;owner.Location=new Point(Math.Max(area.Left,Math.Min(center.X-owner.Width/2,area.Right-owner.Width)),Math.Max(area.Top,Math.Min(center.Y-owner.Height/2,area.Bottom-owner.Height)));}}
  internal void ChooseProject(string project){if(NavigationLocked)return;Project=project;LastExport="";CopyFolder.Text=Path.Combine(Program.Root,"Out");Show(project=="klipper"?1:2);}
  internal void ChooseLoader(bool installed){KatapultInstalled=installed;Show(2);}
  void LayoutBuildButtons(){if(Step!=4&&Step!=6)return;var button=Step==4?Pane.BuildButton:uploadAction;if(button==null)return;button.MinimumSize=new Size(110,30);button.Size=button.GetPreferredSize(Size.Empty);button.Location=new Point((buildNav.ClientSize.Width-button.Width)/2,(buildNav.ClientSize.Height-button.Height)/2);Back.Size=Back.GetPreferredSize(Size.Empty);if(Step==4){Next.Size=Next.GetPreferredSize(Size.Empty);Next.Location=new Point(buildNav.ClientSize.Width-Next.Width-3,(buildNav.ClientSize.Height-Next.Height)/2);Back.Location=new Point(Next.Left-Back.Width-6,Next.Top);}else{home.Size=home.GetPreferredSize(Size.Empty);Back.Location=new Point(buildNav.ClientSize.Width-Back.Width-3,(buildNav.ClientSize.Height-Back.Height)/2);home.Location=new Point(Back.Left-home.Width-6,Back.Top);}}
  internal void Show(int step){
   ClearUploadNotice();
   owner.RestoreWizardUploadButtons();uploadAction=null;owner.Boot.BuildButton.Parent=null;owner.Firmware.BuildButton.Parent=null;nav.Controls.Add(home,2,0);nav.Controls.Add(Back,3,0);nav.Controls.Add(Next,4,0);
   foreach(var view in uploadViews)view.Parent=null;
   foreach(var pair in views)foreach(var view in pair.Value)view.Parent=null;
   foreach(Control reused in new Control[]{CopyFolder,browse,result,progress})reused.Parent=null;
   foreach(Control old in body.Controls.Cast<Control>().ToArray())old.Dispose();
   versionChoices=null;versionNotice=null;versionSignature="";
   Step=step;owner.Tabs.SelectedTab=Pane;Next.Visible=step<6;Back.Visible=step>0;home.Visible=step==6;title.Visible=steps.Visible=step==4||step==6;status.Visible=step==4;SizeWindow(step==6?2:step==3||step==4?1:0);
   nav.Visible=step!=4&&step!=6;buildNav.Visible=step==4||step==6;
   if(step==4){buildNav.Controls.Add(Pane.BuildButton);buildNav.Controls.Add(Back);buildNav.Controls.Add(Next);LayoutBuildButtons();}
   if(step==6){uploadAction=owner.TakeWizardUploadButton(UseSsh,Project);buildNav.Controls.Add(uploadAction);buildNav.Controls.Add(home);buildNav.Controls.Add(Back);LayoutBuildButtons();}
   steps.Text=Project=="klipper"?"Klipper  ›  "+(KatapultInstalled?"Katapult 설치됨":"Katapult 부트로더 없음")+"  ›  빌드":"Katapult  ›  빌드";
   title.Text=step==6?(UseSsh?"SSH 펌웨어 업데이트":"USB 펌웨어 업데이트"):"저장 경로 및 빌드";
   if(step==6)steps.Text=UpdatePathText();
   if(step<2){var center=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=1};center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));center.RowStyles.Add(new RowStyle(SizeType.Percent,100));
    var choices=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Anchor=AnchorStyles.None,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};QuestionBlock=choices;
    choices.Controls.Add(new Label{Text=step==0?"무엇을 설치하시겠습니까?":"보드의 부트로더 상태를 선택하세요.",AutoSize=true,Font=new Font(owner.Font,FontStyle.Bold),Margin=new Padding(0,0,0,10)});
    FirstChoice=new RadioButton{Text=step==0?"Klipper 펌웨어 설치":"Katapult 부트로더 없음",AutoSize=true,Margin=new Padding(0,3,0,3),Checked=step==0?Project=="klipper":!KatapultInstalled};
    SecondChoice=new RadioButton{Text=step==0?"Katapult 부트로더 설치":"Katapult 부트로더 설치됨",AutoSize=true,Margin=new Padding(0,3,0,3),Checked=step==0?Project=="katapult":KatapultInstalled};
    choices.Controls.Add(FirstChoice);choices.Controls.Add(SecondChoice);center.Controls.Add(choices);body.Controls.Add(center);
   }else if(step==5){
    var center=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=1};center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));center.RowStyles.Add(new RowStyle(SizeType.Percent,100));
    var choices=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Anchor=AnchorStyles.None,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};QuestionBlock=choices;
    choices.Controls.Add(new Label{Text="펌웨어 업데이트 방식을 선택하세요.",AutoSize=true,Font=new Font(owner.Font,FontStyle.Bold),Margin=new Padding(0,0,0,10)});
    FirstChoice=new RadioButton{Text="USB로 연결된 메인보드 펌웨어 업데이트",AutoSize=true,Checked=!UseSsh,Margin=new Padding(0,3,0,3)};
    SecondChoice=new RadioButton{Text="SSH에 접속하여 서버에 연결된 메인보드 펌웨어 업데이트",AutoSize=true,Checked=UseSsh,Margin=new Padding(0,3,0,3)};
    choices.Controls.Add(FirstChoice);choices.Controls.Add(SecondChoice);center.Controls.Add(choices);body.Controls.Add(center);
   }else if(step==6){body.Controls.Add(uploadViews[UseSsh?1:0]);}
   else if(step==2){ShowVersionQuestion();}
   else if(step==3){body.Controls.Add(views[Project][0]);nav.Controls.Add(views[Project][2],0,0);Pane.Menu.Focus();}
   else{
    var page=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.Absolute,18));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.Percent,100));
    page.Controls.Add(new Label{Text="완성된 BIN 파일을 복사할 경로",AutoSize=true},0,0);
    var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,AutoSize=true};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));row.Controls.Add(CopyFolder,0,0);row.Controls.Add(browse,1,0);page.Controls.Add(row,0,1);
    page.Controls.Add(progress,0,2);result.Text=LastExport==""?"선택한 경로에 BIN / UF2만 복사합니다. 같은 이름의 이전 결과는 덮어씁니다.":"복사 완료: "+LastExport;page.Controls.Add(result,0,3);page.Controls.Add(views[Project][1],0,4);body.Controls.Add(page);
   }Update();
  }
  internal string UpdatePathText(){
   var path=new List<string>{Project=="klipper"?"Klipper 펌웨어 설치":"Katapult 부트로더 설치"};
   if(Project=="klipper")path.Add(KatapultInstalled?"Katapult 부트로더 설치됨":"Katapult 부트로더 없음");
   path.Add("버전 선택");path.Add("펌웨어 설정");
   path.Add("빌드 완료");path.Add(UseSsh?"SSH 연결":"USB 연결");
   return string.Join("  ›  ",path);
  }
  void ShowVersionQuestion(){
   var center=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=1};center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));center.RowStyles.Add(new RowStyle(SizeType.Percent,100));
   var question=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Anchor=AnchorStyles.None,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};QuestionBlock=question;
   question.Controls.Add(new Label{Text=(Project=="klipper"?"Klipper":"Katapult")+" 펌웨어 버전을 선택하세요.",AutoSize=true,Font=new Font(owner.Font,FontStyle.Bold),Margin=new Padding(0,0,0,8)});
   versionChoices=new ComboBox{Width=350,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,0,0,6)};versionChoices.SelectedIndexChanged+=async delegate{if(!versionUpdating&&versionChoices?.SelectedItem!=null)await PrepareSelectedVersion();};question.Controls.Add(versionChoices);
   versionNotice=new Label{AutoSize=false,Size=new Size(350,52),ForeColor=Color.DimGray,Margin=new Padding(0,0,0,3)};question.Controls.Add(versionNotice);
   center.Controls.Add(question);body.Controls.Add(center);
  }
  void UpdateVersions(){
   if(versionChoices==null)return;
   var versions=Pane.Versions.Items.Cast<Item>().ToArray();string current=Pane.State==null?"":Json.S(Pane.State,"version");string signature=string.Join("|",versions.Select(v=>Json.S(v.Data,"sha")+v.Text+Json.S(v.Data,"date")+Json.S(v.Data,"date_kind")));
   if(signature!=versionSignature){
    string selected=versionChoices.SelectedItem is Item?Json.S(((Item)versionChoices.SelectedItem).Data,"sha"):"";
    versionSignature=signature;versionUpdating=true;try{versionChoices.Items.Clear();foreach(var version in versions){int index=versionChoices.Items.Add(new Item(version.Data,version.Text));if(selected!=""?Json.S(version.Data,"sha")==selected:version.Text==current)versionChoices.SelectedIndex=index;}}finally{versionUpdating=false;}
   }
   bool busy=owner.IsBuilderBusy(Project)||versionPending;versionChoices.Enabled=!busy;
   var item=versionChoices.SelectedItem as Item;string transfer;
   if(busy){string count;transfer="다운로드 중…"+(downloadCounts.TryGetValue(Project,out count)?" ("+count+")":" (목록 확인)");}
   else if(item==null)transfer="버전을 선택하세요.";
   else if(versionTransfers.TryGetValue(Project+":"+Json.S(item.Data,"sha"),out transfer)){}
   else if(item.Text==current&&Pane.State!=null&&Json.S(Pane.State,"startup_warning")!="")transfer=VersionStatus.Notice(Pane.State);
   else transfer=Json.B(item.Data,"config_ready")?"다운로드 완료.":"선택하면 다운로드합니다.";
   versionNotice.Text=VersionDescription(item?.Data,transfer);
  }
  internal static string VersionDescription(Obj version,string transfer){
   if(version==null)return "버전 정보 준비 중\r\n"+transfer;
   DateTimeOffset date;string dated=DateTimeOffset.TryParse(Json.S(version,"date"),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out date)?date.ToString("yyyy.MM.dd")+(Json.S(version,"date_kind")=="release"?" (릴리즈)":" (커밋)"):"날짜 미확인";
   return Versions.Label(version)+"  "+dated+"\r\n"+transfer;
  }
  async Task PrepareSelectedVersion(){
   if(owner.IsBuilderBusy(Project)||versionPending||versionChoices==null)return;var selected=versionChoices.SelectedItem as Item;if(selected==null)return;
   var pane=Pane;string key=Project+":"+Json.S(selected.Data,"sha");versionPending=true;versionTransfers.Remove(key);failedVersions.Remove(key);Update();
   try{await owner.SafeTask(async delegate{try{await owner.Snapshot(pane,"select",new Obj{{"sha",Json.S(selected.Data,"sha")}});versionTransfers[key]="다운로드 완료.";}catch(Exception ex){failedVersions.Add(key);versionTransfers[key]=VersionStatus.Failure(ex);throw;}},"펌웨어 버전 다운로드",new[]{"builder:"+Project});}finally{versionPending=false;Update();}
  }
  async Task SelectVersion(){
   if(owner.IsBuilderBusy(Project)||versionPending||versionChoices==null)return;var selected=versionChoices.SelectedItem as Item;if(selected==null)return;
   string key=Project+":"+Json.S(selected.Data,"sha");bool failed=failedVersions.Contains(key);
   if(failed||Pane.State==null||Json.S(Pane.State,"version")!=selected.Text)await PrepareSelectedVersion();
   failed=failedVersions.Contains(key);
   if(!failed&&Pane.State!=null&&Json.S(Pane.State,"version")==selected.Text)Show(3);
  }
  internal static string ConfigurationWarning(string project,bool installed,Obj state){
   if(state==null)return "설정과 소스 준비가 완료된 뒤 다시 시도하세요.";
   if(Json.S(state,"build_error")!="")return Json.S(state,"build_error");
   if(project!="klipper")return "";
   long offset;if(!long.TryParse(Json.S(Json.D(state,"summary"),"bootloader_offset"),out offset)||offset<0)return "부트로더 오프셋을 확인할 수 없습니다. MCU와 부트로더 설정을 확인하세요.";
   if(installed&&offset==0)return "‘Katapult 부트로더 설치됨’을 선택했지만 부트로더 오프셋이 0입니다. menuconfig의 Bootloader offset을 설치된 Katapult의 애플리케이션 시작 주소에 맞게 지정하세요.";
   return "";
  }
  bool WarnConfiguration(){string warning=ConfigurationWarning(Project,KatapultInstalled,Pane.State);if(warning=="")return true;MessageBox.Show(owner,warning,"부트로더 설정 확인",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
  internal bool CheckBeforeBuild(BuilderPane pane){
   if(pane!=Pane||Step!=4)return false;if(!WarnConfiguration()){Show(3);return false;}
   if(string.IsNullOrWhiteSpace(CopyFolder.Text))throw new Exception("BIN 파일을 복사할 경로를 선택하세요.");
   exportFolder=Path.GetFullPath(CopyFolder.Text);Directory.CreateDirectory(exportFolder);LastExport="";completedState=null;buildComplete=false;progress.Value=0;result.Text="빌드 진행 중 · 성공한 결과만 선택한 경로로 복사합니다.";return true;
  }
  internal async Task BuildFinished(BuilderPane pane,Obj response){
   copying=true;progress.Value=Math.Max(progress.Value,95);Update();try{string destination=exportFolder;LastExport=await Task.Run(()=>Export(Json.S(response,"output"),destination,pane.Project));completedHash=Json.S(Json.D(Json.D(Json.D(response,"report"),"artifacts"),pane.Project+".bin"),"sha256");completedState=pane.State;completedProject=pane.Project;buildComplete=true;progress.Value=100;result.Text="빌드 및 복사 완료: "+LastExport;MainForm.Append(pane.Log,result.Text);}finally{copying=false;Update();}
  }
  internal void BuildFailed(string message){completedState=null;buildComplete=true;result.Text="작업 중단: "+message;}
  bool ValidateBuiltFile(){if(!ReadyForUpdate)return false;if(U.Hash(File.ReadAllBytes(LastExport))==completedHash)return true;completedState=null;MessageBox.Show(owner,"빌드한 파일이 변경되었습니다. 다시 빌드한 뒤 진행하세요.","펌웨어 확인",MessageBoxButtons.OK,MessageBoxIcon.Warning);Update();return false;}
  async Task OpenUploader(){
   if(!ValidateBuiltFile())return;UseSsh=SecondChoice.Checked;uploadPreparing=true;
   try{Show(6);await owner.PrepareWizardUploader(UseSsh,Project,LastExport);}finally{uploadPreparing=false;Update();}
  }
  internal static bool IsUploadAction(string action)=>new[]{"flash","ssh_flash","rp2040_install","ssh_rp2040_install","dfu_install","ssh_dfu_install"}.Contains(action);
  internal void ClearUploadNotice(){uploadNotice.Visible=false;}
  void SetUploadNotice(string heading,string detail,int level){
   uploadNotice.BackColor=level==0?Color.FromArgb(229,245,233):level==1?Color.FromArgb(255,244,206):Color.FromArgb(253,234,234);
   uploadNoticeTitle.ForeColor=uploadNoticeDetail.ForeColor=level==0?Color.FromArgb(24,94,45):level==1?Color.FromArgb(117,78,0):Color.FromArgb(155,30,30);
   uploadNoticeTitle.Text=heading;uploadNoticeDetail.Text=detail;uploadNotice.Visible=Step==6;
  }
  internal void UploadFinished(string action,Obj request,Obj response){
   if(!IsUploadAction(action)||response.ContainsKey("host_key_required"))return;
   bool bootloader=action.Contains("install")&&!Json.B(request,"replace_bootloader"),transferred=Json.B(response,"transferred"),verified=Json.B(response,"verified"),usb=Json.B(response,"application_usb_detected"),katapult=Json.B(response,"katapult_connected");
   string name=bootloader?"Katapult 부트로더 설치":"Klipper 펌웨어 업로드";
   bool confirmed=verified||transferred&&(usb||katapult);
   string detail=verified?"기록한 데이터의 읽기 검증을 마쳤습니다.":usb?"Klipper USB 재인식을 확인했습니다.":katapult?"Katapult 연결을 확인했습니다.":transferred?"전송은 끝났지만 장치 재인식을 확인하지 못했습니다.":"작업 로그와 보드 상태를 확인하세요.";
   if(confirmed)detail+=bootloader?" 오프셋에 맞는 Klipper를 설치하세요.":" 보드 동작을 확인하세요.";
   if(Json.B(response,"service_left_stopped"))detail+=" Klipper 서비스는 중지 상태입니다.";
   if(Json.B(response,"service_restarted"))detail+=" Klipper 서비스를 다시 시작했습니다.";
   SetUploadNotice(confirmed?"✓ "+name+" 완료":transferred?"! 펌웨어 전송 완료 · 장치 확인 필요":"! 업로드 결과 확인 필요",detail,confirmed?0:1);
  }
  internal void UploadFailed(){SetUploadNotice("! 업로드 완료를 확인하지 못했습니다","작업 로그와 보드 상태를 확인하세요.",2);}
  internal void BeginOperation(string project,string action){if(new[]{"startup","refresh","select","build"}.Contains(action))downloadCounts.Remove(project);if(IsUploadAction(action)||action=="check"||action=="ssh_check")ClearUploadNotice();}
  internal bool ReportProgress(string project,string line){
   bool download=line.StartsWith("@@DOWNLOAD_PROGRESS@@",StringComparison.Ordinal),build=line.StartsWith("@@BUILD_PROGRESS@@",StringComparison.Ordinal);if(!download&&!build)return false;
   var pair=line.Substring(download?21:18).Split('/');int done,total;
   if(pair.Length!=2||!int.TryParse(pair[0],out done)||!int.TryParse(pair[1],out total)||done<0||total<done)return true;
   if(download)downloadCounts[project]=done+" / "+total;
   else if(project==Project&&total>0&&Pane.CancelPath!=""&&!buildComplete){int value=5+90*done/total;progress.Value=Math.Max(progress.Value,Math.Min(value,95));result.Text="빌드 진행: "+done+" / "+total+" · "+progress.Value+"%";}
   Update();return true;
  }
  internal static string Export(string source,string destination,string project){
   return FirmwareExport.Copy(source,destination,project);
  }
  void Update(){if(Root.IsDisposed||Root.Disposing||owner.IsDisposed||owner.Disposing)return;UpdateVersions();bool busy=owner.IsBuilderBusy(Project)||copying||versionPending;Next.Enabled=Step<2?!NavigationLocked:Step==2?!busy&&versionChoices?.SelectedItem!=null:Step==4||Step==5?ReadyForUpdate&&!busy&&!NavigationLocked:Pane.State!=null&&!busy&&!Pane.IsValueEditing;Back.Enabled=home.Enabled=!NavigationLocked&&!versionPending;CopyFolder.Enabled=browse.Enabled=!busy;if(Step==6){owner.LayoutWizardUploader(UseSsh);LayoutBuildButtons();}
   status.Text=Step!=4?"":(Project=="klipper"?(KatapultInstalled?"Klipper · Katapult 부트로더 설치됨":"Klipper · Katapult 부트로더 없음"):"Katapult 부트로더 설치")+(busy?" · 작업 중":Pane.State==null?" · 설정 준비 중":" · "+Pane.Summary.Text);
  }
 }
}
