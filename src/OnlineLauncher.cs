using System;using System.Drawing;using System.IO;using System.Diagnostics;using System.Net;using System.Threading.Tasks;using System.Windows.Forms;using DonaldOnline;
// Start window "Donald Duck Online": play alone, create an online game (invite code) or join one.
// The player's own installed game is used as it is; the folder is asked for once and remembered in %APPDATA%\DonaldOnline.
// Option --game <folder> overrides it (two games on one PC for testing).
class OnlineLauncher:Form{
 readonly string root=AppDomain.CurrentDomain.BaseDirectory;string inviteFile;string gameFolder;OnlineSession session;
#if TESTAUTO
 TestDriver driver;
#endif
/* Interval 16 only gave ~33 ticks/s (Windows rounds WM_TIMER to its 15.6 ms clock); 10 gives ~60–100. */readonly Timer timer=new Timer{Interval=10};
 readonly Label title=new Label(),status=new Label(),peer=new Label(),folder=new Label();readonly TextBox code=new TextBox(),playerName=new TextBox();NameOverlay overlay;
 readonly Button solo=new Button(),create=new Button(),join=new Button(),copy=new Button(),choose=new Button(),logs=new Button();
 OnlineLauncher(string game){gameFolder=game;Text="Donald Duck Online";ClientSize=new Size(520,370);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(24,48,96);ForeColor=Color.White;Font=new Font("Segoe UI",10f);
  title.Text="Donald Duck – Quack Attack";title.Font=new Font("Segoe UI",17f,FontStyle.Bold);title.SetBounds(20,14,480,36);
  Style(solo,"Allein spielen",20,64);Style(create,"Online-Spiel erstellen",20,112);Style(join,"Online-Spiel beitreten",20,160);
  var hint=new Label{Text="Einladungscode:",AutoSize=true};hint.SetBounds(260,70,240,22);code.SetBounds(260,94,240,26);code.Font=new Font("Consolas",9f);
  Style(copy,"Code kopieren",260,128);copy.Width=240;copy.Enabled=false;
  var nameHint=new Label{Text="Dein Name:",AutoSize=true};nameHint.SetBounds(260,172,90,22);playerName.SetBounds(350,170,150,26);playerName.MaxLength=16;playerName.Text=AppPaths.Get("PlayerName")??Environment.UserName;playerName.TextChanged+=(s,e)=>{AppPaths.Set("PlayerName",playerName.Text.Trim());if(session!=null)session.LocalName=playerName.Text;};Controls.Add(nameHint);Controls.Add(playerName);
  status.SetBounds(20,214,480,48);status.Text="Wähle, wie du spielen möchtest.";peer.SetBounds(20,264,480,40);peer.ForeColor=Color.FromArgb(255,214,90);
  folder.SetBounds(20,330,250,30);logs.Text="Protokoll";logs.SetBounds(280,326,90,28);logs.FlatStyle=FlatStyle.Flat;logs.Font=new Font("Segoe UI",8.5f);folder.Font=new Font("Segoe UI",8f);folder.ForeColor=Color.FromArgb(180,200,230);choose.Text="Spielordner …";choose.SetBounds(380,326,120,28);choose.FlatStyle=FlatStyle.Flat;choose.Font=new Font("Segoe UI",8.5f);
  Controls.AddRange(new Control[]{title,solo,create,join,hint,code,copy,status,peer,folder,choose,logs});logs.Click+=(s,e)=>{try{Process.Start("explorer.exe",AppPaths.LogFolder);}catch{}};
  solo.Click+=async (s,e)=>await PlaySolo();GameSetup.Progress=msg=>{try{BeginInvoke(new Action(()=>status.Text=msg));}catch{}};create.Click+=async (s,e)=>await Create();join.Click+=async (s,e)=>await Join();choose.Click+=(s,e)=>ChooseFolder();
  copy.Click+=(s,e)=>{try{Clipboard.SetText(code.Text);status.Text="Code kopiert – schick ihn deinem Freund (z. B. per Discord).";}catch{}};
  timer.Tick+=(s,e)=>{if(session==null)return;if(solo.Enabled){Busy(true);join.Enabled=!session.IsHost;}session.Tick();if(session.IsHost&&session.InviteCode!=null&&code.Text!=session.InviteCode)code.Text=session.InviteCode;if(overlay==null)overlay=new NameOverlay();try{overlay.Update(session.GameWindow,session.PeerName,session.PeerFeet,session.Camera);}catch(Exception ex){AppPaths.Log("Namensanzeige: "+ex.Message);}status.Text=session.Status+(session.LastError!=null?"\n"+session.LastError:"");peer.Text=session.PeerInfo;};timer.Start();
  FormClosing+=(s,e)=>{timer.Stop();if(session!=null)session.Dispose();};
  Shown+=(s,e)=>{if(!AppPaths.IsGameFolder(gameFolder))ChooseFolder();ShowFolder();};}
 void Style(Button b,string text,int x,int y){b.Text=text;b.SetBounds(x,y,220,40);b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(250,190,40);b.ForeColor=Color.FromArgb(20,30,60);b.Font=new Font("Segoe UI",10.5f,FontStyle.Bold);}
 void ShowFolder(){bool ok=AppPaths.IsGameFolder(gameFolder);folder.Text=ok?"Spiel: "+gameFolder:"Kein Spielordner gewählt";if(session==null)solo.Enabled=create.Enabled=join.Enabled=ok;}
 // Setup: either the player's installed game (Donald.exe) or set up from his own CD / CD image. Game files are never shipped.
 async void ChooseFolder(){var answer=MessageBox.Show(this,"Ist Donald Duck bei dir schon installiert?\n\nJa = Donald.exe deiner Installation auswählen\nNein = Spiel von deiner eigenen CD oder deinem CD-Abbild (.cue / .bin / .iso) einrichten","Spiel einrichten",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
  if(answer==DialogResult.Yes)PickInstalled();else if(answer==DialogResult.No)await SetupFromDisc();ShowFolder();}
 void PickInstalled(){using(var dialog=new OpenFileDialog{Title="Wo ist dein Donald Duck installiert? Bitte Donald.exe auswählen",Filter="Donald.exe|Donald.exe",CheckFileExists=true}){
   if(dialog.ShowDialog(this)!=DialogResult.OK)return;string dir=Path.GetDirectoryName(dialog.FileName);if(!AppPaths.IsGameFolder(dir)){status.Text="In diesem Ordner fehlen die Spieldaten (Data\\World\\Levels).";return;}
   if(!GameSetup.IsSupportedExe(dialog.FileName))MessageBox.Show(this,"Diese Donald.exe ist eine andere Version als die deutsche Neuauflage, für die der Online-Modus gebaut ist. Es kann sein, dass er damit nicht funktioniert.","Andere Spielversion",MessageBoxButtons.OK,MessageBoxIcon.Warning);
   gameFolder=dir;AppPaths.Set("GameFolder",dir);AppPaths.Log("Spielordner gewählt: "+dir);status.Text="Spielordner gespeichert.";}}
 async Task SetupFromDisc(){string disc=GameSetup.MountedDiscRoot(),image=null;
  if(disc==null){using(var d=new OpenFileDialog{Title="Dein Donald-Duck-CD-Abbild auswählen",Filter="CD-Abbild (*.cue;*.bin;*.iso)|*.cue;*.bin;*.iso"}){if(d.ShowDialog(this)!=DialogResult.OK)return;image=d.FileName;}}
  string target;using(var f=new FolderBrowserDialog{Description="In welchen Ordner soll das Spiel von deiner CD kopiert werden? (ca. 500 MB, darin wird ein Unterordner \"Donald Duck\" angelegt)",SelectedPath=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}){if(f.ShowDialog(this)!=DialogResult.OK)return;target=Path.Combine(f.SelectedPath,"Donald Duck");}
  Busy(true);try{await Task.Run(()=>{if(image!=null){string iso=GameSetup.PrepareDiscImage(image);AppPaths.Set("DiscImage",iso);disc=GameSetup.Mount(iso);}GameSetup.InstallFromDisc(disc,target);});gameFolder=target;AppPaths.Set("GameFolder",target);status.Text="Spiel eingerichtet.";}
  catch(Exception e){AppPaths.Log("Einrichtung fehlgeschlagen: "+e);status.Text="Fehler: "+e.Message;}finally{Busy(false);}}
 // Before each start: dgVoodoo2 (window mode, DirectX compatibility) is set up after asking once.
 async Task Prepare(){if(!GameSetup.NeedsDgVoodoo(gameFolder)||AppPaths.Get("DgVoodoo")=="no")return;
  var a=MessageBox.Show(this,"Damit das alte Spiel unter aktuellem Windows sauber im Fenster läuft, wird dgVoodoo2 (kostenloser DirectX-Wrapper von Dege) einmalig von github.com/dege-diosg geladen und in deinen Spielordner gelegt (DDraw.dll, D3DImm.dll, dgVoodoo.conf).\n\nJetzt einrichten?","Grafik-Kompatibilität",MessageBoxButtons.YesNo,MessageBoxIcon.Question);
  if(a!=DialogResult.Yes){AppPaths.Set("DgVoodoo","no");return;}
  try{await Task.Run(()=>GameSetup.EnsureCompatibility(gameFolder,true));}catch(Exception e){AppPaths.Log("dgVoodoo fehlgeschlagen: "+e);status.Text="dgVoodoo2 konnte nicht eingerichtet werden: "+e.Message;}} void Busy(bool on){solo.Enabled=create.Enabled=join.Enabled=choose.Enabled=!on;}
 async Task PlaySolo(){Busy(true);try{GameSetup.EnsureNoRunningGame(gameFolder);await Prepare();await Task.Run(()=>{GameStarter.EnsureDisc(gameFolder);GameSetup.EnsureCompatibility(gameFolder,false);});GameStarter.GuardOptions(gameFolder);GameSetup.LogEnvironment(gameFolder);var p=GameSetup.Launch(gameFolder);AppPaths.Log("Solo gestartet");status.Text="Spiel startet …";
  string died=await Task.Run(()=>GameSetup.WatchStart(p,15));if(died!=null)throw new Exception(died+"\n\n"+StartHints);status.Text="Spiel läuft. Das Spielfenster kann jetzt benutzt werden.";await Task.Run(()=>p.WaitForExit());GameSetup.LogExit(p,"allein");status.Text="Spiel beendet.";p.Dispose();Busy(false);}catch(Exception e){AppPaths.Log("Solo fehlgeschlagen: "+e);status.Text=e.Message;Problem(e.Message);Busy(false);}}
 const string StartHints="Mögliche Ursachen:\n• Die Spiel-CD ist nicht eingelegt bzw. das CD-Abbild nicht eingebunden.\n• Windows Defender hat etwas blockiert (Ausnahme für den Ordner von DonaldOnline.exe eintragen).\n• Es ist eine andere Version des Spiels als die deutsche Neuauflage.\n\nIm Startfenster auf „Protokoll“ klicken und die neueste Datei an Tim schicken.";
 void Problem(string text){try{MessageBox.Show(this,text,"Donald Duck Online",MessageBoxButtons.OK,MessageBoxIcon.Warning);}catch{}}
 async Task Create(){Busy(true);try{await Prepare();session=new OnlineSession(true,root){LocalName=playerName.Text};status.Text="Internet-Verbindung wird aufgebaut …";string invite=await session.CreateRoom();code.Text=invite;code.ReadOnly=true;copy.Enabled=true;if(inviteFile!=null)try{File.WriteAllText(inviteFile,invite);}catch{}await session.StartGame(gameFolder);}catch(Exception e){AppPaths.Log("Erstellen fehlgeschlagen: "+e);status.Text="Fehler: "+e.Message;Problem(e.Message+"\n\n"+StartHints);if(session!=null){session.Dispose();session=null;}Busy(false);}}
 async Task Join(){
  // While a guest session runs, "beitreten" only switches to the new code (e.g. after the host had to restart his tunnel).
  if(session!=null&&!session.IsHost){try{session.JoinRoom(code.Text);status.Text="Verbinde mit dem neuen Code …";}catch(Exception e){status.Text="Fehler: "+e.Message;}return;}
  Busy(true);try{await Prepare();session=new OnlineSession(false,root){LocalName=playerName.Text};session.JoinRoom(code.Text);await session.StartGame(gameFolder);}catch(Exception e){AppPaths.Log("Beitreten fehlgeschlagen: "+e);status.Text="Fehler: "+e.Message;Problem(e.Message+"\n\n"+StartHints);if(session!=null){session.Dispose();session=null;}Busy(false);}}
 // 1 ms timer resolution so the 16 ms UI timer that drives capture and replica really runs at ~60 Hz.
 [System.Runtime.InteropServices.DllImport("winmm.dll")]static extern uint timeBeginPeriod(uint ms);
 [STAThread]static void Main(string[] args){timeBeginPeriod(1);string game=GameLocation.Find(AppDomain.CurrentDomain.BaseDirectory,args,()=>AppPaths.Get("GameFolder"));if(Array.IndexOf(args,"--check-package")>=0){Console.WriteLine(game);Environment.ExitCode=GameLocation.PackageComplete(AppDomain.CurrentDomain.BaseDirectory,game)?0:1;return;}Application.ThreadException+=(s,e)=>AppPaths.Log("Absturz (UI): "+e.Exception);AppDomain.CurrentDomain.UnhandledException+=(s,e)=>AppPaths.Log("Absturz: "+e.ExceptionObject);AppPaths.Log("Programmstart "+string.Join(" ",args));ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;ServicePointManager.DnsRefreshTimeout=0;Application.EnableVisualStyles();
  string auto=null,autoCode=null;
  for(int i=0;i+1<args.Length;i++){if(args[i]=="--game")game=Path.GetFullPath(args[i+1]);if(args[i]=="--auto"){auto=args[i+1];if(auto=="join"&&i+2<args.Length)autoCode=args[i+2];}}
  var form=new OnlineLauncher(game);for(int i=0;i+1<args.Length;i++)if(args[i]=="--name")form.playerName.Text=args[i+1];
#if TESTAUTO
  // Test automation (TestDriver: launcher-cmd-host.txt / launcher-cmd-guest.txt): --auto host writes the invite code to launcher-invite.txt, --auto join <code> joins.
  // Test: the host rewrites launcher-invite.txt when its code changes, the guest rejoins when the file changes.
  if(auto!=null){string inviteFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher-invite.txt");var watch=new Timer{Interval=1000};watch.Tick+=(s,e)=>{try{if(form.session==null)return;if(auto=="host"&&form.session.InviteCode!=null&&(!File.Exists(inviteFile)||File.ReadAllText(inviteFile)!=form.session.InviteCode))File.WriteAllText(inviteFile,form.session.InviteCode);if(form.driver==null)form.driver=new TestDriver(form.session,auto=="host"?"host":"guest");if(auto=="join"&&File.Exists(inviteFile)){string now=File.ReadAllText(inviteFile).Trim();if(now!=form.code.Text){form.code.Text=now;form.session.JoinRoom(now);}}}catch{}};watch.Start();}
  if(auto!=null)form.Shown+=async (s,e)=>{if(auto=="host"){form.inviteFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher-invite.txt");await form.Create();}else if(auto=="join"){form.code.Text=autoCode;await form.Join();}};
#endif
  Application.Run(form);}
}


