using System;using System.Diagnostics;using System.IO;using System.Runtime.InteropServices;using System.Text;using System.Threading;using System.Threading.Tasks;
namespace DonaldOnline {
// One player's side of an online game: the own Donald.exe (GameBridge) plus the partner over RoomWire.
// The host runs the RoomServer behind a Cloudflare quick tunnel, the guest polls it. The game logic is the same on
// both sides (DevController did it for two local games): portals, level exit, replica, follow fallback.
public sealed class OnlineSession:IDisposable {
 readonly bool isHost;readonly string toolsFolder;GameBridge bridge;Process game;RoomServer server;Tunnel tunnel;
 volatile Pose outgoing,incoming;volatile bool end;bool exitLogged;string endpoint,token;long receivedAt;Thread loop;
 bool transfer;string localMap;DateTime localMapSince;DateTime mismatchSince=DateTime.MaxValue;
 // Every change of status, error or partner connection goes to the log (AppPaths.Log).
 string status,lastError,peerInfo;string Who{get{return isHost?"[Host] ":"[Gast] ";}}
 public string Status{get{return status;}private set{if(value!=status){status=value;AppPaths.Log(Who+"Status: "+value);}}}
 public string LastError{get{return lastError;}private set{if(value!=lastError){lastError=value;if(value!=null)AppPaths.Log(Who+"Fehler: "+value);}}}
 public string PeerInfo{get{return peerInfo;}private set{if(value!=peerInfo){peerInfo=value;AppPaths.Log(Who+value);}}}
 public string InviteCode{get;private set;}
 public bool IsHost{get{return isHost;}}
 // Player names: the own one is sent with every pose, the partner's is shown over his Donald (NameOverlay).
 public string LocalName="";public string PeerName{get;private set;}
 public IntPtr GameWindow{get{try{if(game==null||game.HasExited)return IntPtr.Zero;game.Refresh();return game.MainWindowHandle;}catch{return IntPtr.Zero;}}}
 public float[] PeerFeet{get;private set;}public float[] Camera{get;private set;}
 public bool Connected{get{return Peer()!=null;}}
 public OnlineSession(bool host,string tools){isHost=host;toolsFolder=tools;
#if TESTAUTO
  {string lag=AppPaths.Get("TestLagMs");if(!string.IsNullOrEmpty(lag)){string[] lp=lag.Split('-');int lo,hi;if(int.TryParse(lp[0],out lo)){hi=lo;if(lp.Length>1)int.TryParse(lp[1],out hi);RoomClient.TestLagMin=Math.Max(0,lo/2);RoomClient.TestLagMax=Math.Max(0,hi/2);AppPaths.Log("[Test] künstliche Verzögerung "+lo+"-"+hi+" ms pro Anfrage");}}}
#endif
  AppPaths.Log("==== Neue Sitzung als "+(host?"Host":"Gast")+", Version "+typeof(OnlineSession).Assembly.GetName().Version+", Windows "+Environment.OSVersion.Version+(Environment.Is64BitOperatingSystem?" 64-Bit":" 32-Bit"));Status="Bereit";PeerInfo="Noch kein Mitspieler";}

 // Host: local room plus public HTTPS tunnel; the invite code carries the tunnel name and the room secret.
 public async Task<string> CreateRoom(){server=new RoomServer();token=server.Token;Status="Internet-Verbindung wird aufgebaut …";
  string cloudflared=FindTool("cloudflared.exe");if(cloudflared==null){Status="Lade Cloudflare-Tunnel (einmalig) …";cloudflared=await Task.Run(()=>AppPaths.DownloadCloudflared());}
  cloudflaredPath=cloudflared;await OpenTunnel();return InviteCode;}
 string cloudflaredPath;bool reopening;
 // Opens (or re-opens after it died) the Cloudflare tunnel; a new tunnel has a new name, so the invite code changes.
 async Task OpenTunnel(){if(tunnel!=null)tunnel.Dispose();tunnel=new Tunnel(cloudflaredPath,server.Port);if(!await Task.Run(()=>tunnel.Wait(120000)))throw new Exception("Die Internet-Verbindung (Cloudflare) konnte nicht aufgebaut werden.");
  string sub=new Uri(tunnel.Endpoint).Host.Replace(".trycloudflare.com","");InviteCode=sub+"."+token;AppPaths.Log("Tunnel: "+tunnel.Endpoint);endpoint=tunnel.Endpoint;Status="Raum offen – Einladungscode an deinen Freund schicken";}

 // Guest: the code is "<tunnel name>.<64 hex secret>".
 public void JoinRoom(string code){code=(code??"").Trim();int dot=code.LastIndexOf('.');if(dot<1)throw new Exception("Der Einladungscode ist unvollständig.");
  string sub=code.Substring(0,dot),secret=code.Substring(dot+1).ToLowerInvariant();
  if(!System.Text.RegularExpressions.Regex.IsMatch(sub,"^[a-z0-9-]{3,80}$")||!System.Text.RegularExpressions.Regex.IsMatch(secret,"^[0-9a-f]{64}$"))throw new Exception("Der Einladungscode ist ungültig.");
  endpoint="https://"+sub+".trycloudflare.com";token=secret;AppPaths.Log("Beitritt zu "+endpoint);Interlocked.Exchange(ref receivedAt,0);if(loop==null){loop=new Thread(ClientLoop){IsBackground=true};loop.Start();}Status="Verbinde mit dem Raum …";}

 // Local test without tunnel (both players on one PC).
 public void UseLoopback(OnlineSession hostSession){endpoint="http://127.0.0.1:"+hostSession.server.Port;token=hostSession.token;loop=new Thread(ClientLoop){IsBackground=true};loop.Start();}
 public void OpenLocalRoom(){server=new RoomServer();token=server.Token;InviteCode="(lokal)";Status="Lokaler Raum offen";}

 string FindTool(string name){foreach(string dir in new[]{toolsFolder,Path.Combine(toolsFolder,"tools"),AppDomain.CurrentDomain.BaseDirectory,AppPaths.Data}){string p=Path.Combine(dir,name);if(File.Exists(p))return p;}return null;}

 // Starts the own game (mounting the CD image if needed) and attaches once the engine is ready.
 public async Task StartGame(string gameFolder){string exe=Path.Combine(gameFolder,"Donald.exe");AppPaths.Log("Spielordner: "+gameFolder+(File.Exists(exe)?" (Donald.exe "+new FileInfo(exe).Length+" Bytes)":" (Donald.exe fehlt)"));if(!File.Exists(exe))throw new Exception("Donald.exe nicht gefunden: "+gameFolder);
  // The game refuses to start next to another running copy and closes at once.
  foreach(Process other in Process.GetProcessesByName("Donald"))if(SamePath(other,exe))throw new Exception("Donald Duck läuft schon (PID "+other.Id+"). Bitte das Spiel zuerst beenden und dann erneut versuchen.");
  await Task.Run(()=>GameStarter.EnsureDisc(gameFolder));GameStarter.GuardOptions(gameFolder);Status="Spiel wird gestartet …";
  GameSetup.EnsureCompatibility(gameFolder,false);GameSetup.LogEnvironment(gameFolder);game=GameSetup.Launch(gameFolder);
  for(int i=0;i<1800;i++){if(game.HasExited)throw new Exception("Das Spiel hat sich direkt nach dem Start wieder beendet (Code "+game.ExitCode+").");if(GameBridge.CanStartSession(game.Id))break;if(i%10==0){GameStarter.Restore(game);GameStarter.PressCdRetry(game);}await Task.Delay(100);}
  if(!GameBridge.CanStartSession(game.Id))throw new Exception("Das Spiel ist nicht rechtzeitig bereit geworden.");AppPaths.Log("Spiel bereit, PID "+game.Id);
  // Online play goes straight into the hub (no menus); both players then meet in Gyro's lab.
  bridge=new GameBridge(game.Id);bridge.BeginLevelTransfer("MapMonde");transfer=true;Status=isHost?"Spiel läuft – warte auf Mitspieler":"Spiel läuft – verbinde …";}

 static bool SamePath(Process p,string exe){try{return string.Equals(Path.GetFullPath(p.MainModule.FileName),Path.GetFullPath(exe),StringComparison.OrdinalIgnoreCase);}catch{return true;}}
 // One HTTPS request through the tunnel takes a whole round trip (often 60–200 ms). A single sequential loop therefore
 // delivered only 5–15 poses per second with large gaps, which made the partner stutter and lag behind. Six loops
 // overlap their requests (measured: 5–15 → 32–37 partner updates/s); the newest pose (sender clock) wins.
 int ClientLoops=6;readonly object incomingGate=new object();
 void ClientLoop(){int n;if(int.TryParse(AppPaths.Get("NetLoops"),out n)&&n>=1&&n<=8)ClientLoops=n;System.Net.ServicePointManager.DefaultConnectionLimit=Math.Max(System.Net.ServicePointManager.DefaultConnectionLimit,16);for(int i=1;i<ClientLoops;i++){int delay=i*60/ClientLoops;new Thread(()=>{Thread.Sleep(delay);ClientWorker();}){IsBackground=true}.Start();}ClientWorker();}
 void ClientWorker(){while(!end){try{Pose p=outgoing;if(p!=null&&endpoint!=null){Pose reply=RoomClient.Exchange(endpoint,token,p);Interlocked.Increment(ref requests);if(reply!=null){lock(incomingGate){Pose old=incoming;if(old==null||old.Session!=reply.Session||reply.Time>=old.Time||DateTime.UtcNow.Ticks-Interlocked.Read(ref receivedAt)>TimeSpan.FromSeconds(1).Ticks)incoming=reply;}Interlocked.Exchange(ref receivedAt,DateTime.UtcNow.Ticks);}}LastError=null;}catch(Exception e){LastError=e.Message;Thread.Sleep(500);}Thread.Sleep(10);}}
 // Every 20 s the log gets how many new partner poses arrived per second and the current smoothing delay.
 long statsLastTime,tickTicks;int requests,ticks;int statsPoses;DateTime statsSince=DateTime.UtcNow;
 void NetStats(Pose peer){if(peer!=null&&peer.Time!=statsLastTime){statsLastTime=peer.Time;statsPoses++;}double s=(DateTime.UtcNow-statsSince).TotalSeconds;if(s<20)return;if(statsPoses>0)AppPaths.Log("Netz: "+(statsPoses/s).ToString("0.0")+" Partner-Updates/s, Glättung "+bridge.SmoothingDelay.ToString("0")+" ms"+(isHost?"":", "+(Interlocked.Exchange(ref requests,0)/s).ToString("0.0")+" Anfragen/s ("+ClientLoops+" parallel)")+", Takt "+(ticks/s).ToString("0")+"/s à "+(ticks==0?0:tickTicks*1000.0/System.Diagnostics.Stopwatch.Frequency/ticks).ToString("0.0")+" ms");ticks=0;tickTicks=0;statsPoses=0;statsSince=DateTime.UtcNow;}
 Pose Peer(){if(isHost)return server==null?null:server.Remote;return DateTime.UtcNow.Ticks-Interlocked.Read(ref receivedAt)<TimeSpan.FromSeconds(3).Ticks?incoming:null;}

 // Called every ~16 ms from the UI timer.
 public void Tick(){if(bridge==null)return;long t0=System.Diagnostics.Stopwatch.GetTimestamp();try{if(game!=null&&game.HasExited){if(!exitLogged){exitLogged=true;GameSetup.LogExit(game,(isHost?"Host":"Gast")+", Karte "+(localMap??"?")+", "+bridge.SpawnInfo);}Status="Das Spiel wurde beendet.";return;}
  if(transfer){if(bridge.PollLevelTransfer())transfer=false;else return;}
  if(pendingGo!=null&&(bridge.StablePlaying||GameOverLeaving&&bridge.StableForGameOver)){string m=pendingGo;Go(m);return;}
  /* both games read the same CD image; during a level load one of them sometimes reports "CDROM not found!" - the game's own retry then works (6.11.: it only happened at start before) */if(++cdCheck%60==0)try{GameStarter.PressCdRetry(game);}catch{}
  Pose me=bridge.Capture();if(me!=null){me.Name=Pose.CleanName(LocalName);me.Gone=bridge.WorldEventsOut();bridge.StarIsHost=isHost;me.StarsEarned=bridge.StarsOut;me.StarsAck=bridge.StarsAckOut;me.Progress=bridge.ProgressOut;me.ItemStates=bridge.ItemStatesOut;me.Claims=bridge.ClaimsOut;me.Grants=bridge.GrantsOut;me.Timer=bridge.TimerOut;me.Camera=bridge.CameraOut;me.CameraLag=bridge.CameraMsOut>0?(int)Math.Max(-30000,Math.Min(30000,Math.Round(me.Time-bridge.CameraMsOut))):bridge.CameraLagOut;if(DateTime.UtcNow<gameOverUntil)me.Look|=0x400;}if(isHost){if(me!=null&&server!=null)server.Local=me;}else outgoing=me;Pose peer=Peer();NetStats(peer);
  if(peer!=null){everConnected=true;codeRenewed=false;lostSince=DateTime.MaxValue;}else if(everConnected&&lostSince==DateTime.MaxValue)lostSince=DateTime.UtcNow;
  PeerInfo=PeerText(me,peer);UpdateTitle(PortalHint(me,peer));
  if(isHost&&tunnel!=null&&tunnel.HasExited&&!reopening){reopening=true;AppPaths.Log("Tunnel beendet – baue neu auf");Status="Internet-Verbindung neu aufbauen …";Task.Run(async()=>{try{await OpenTunnel();codeRenewed=true;}catch(Exception e){LastError=e.Message;}finally{reopening=false;}});}
  // Leaving a level via the game's own ChangeMap: held back until the partner asks for the same map.
  if(me==null||me.MapFile!=localMap){localMap=me==null?null:me.MapFile;localMapSince=DateTime.UtcNow;}
  bridge.SetLeaveVeto(me!=null&&!me.MapFile.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&bridge.AllowedMap(me.MapFile)&&DateTime.UtcNow-localMapSince>TimeSpan.FromSeconds(3)&&peer!=null);
  // The veto stays on even when the partner only watches: when lives run out the game itself asks for the hub (its
  // Game Over), and letting that through was the hub crash after "both out of lives" (1.11.). A watching partner
  // cannot ask for the same map, so then any request except the hub is followed at once.
  if(bridge.LeaveRequest!=null&&bridge.LeaveRequest.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&(bridge.OutOfLives||bridge.LastLife)){AppPaths.Log("Game-Over-Wunsch des Spiels verworfen");bridge.ClearLeave();}
  if(bridge.LeaveRequest!=null&&peer!=null&&(peer.Look&0x200)!=0&&!bridge.LeaveRequest.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&bridge.AllowedMap(bridge.LeaveRequest)){string map=bridge.LeaveRequest;bridge.ClearLeave();Go(map);return;}
  /* level end (4.11., Tim could not leave "trail"): one player confirms the exit, the partner stands right next to him and need not confirm too: after 0.6 s (so the partner sees the request) both go */
  if(peer!=null&&me!=null&&peer.MapFile==me.MapFile&&PlayersNear(me,peer)){string mine=bridge.LeaveRequest;if(mine!=null&&!mine.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&bridge.AllowedMap(mine)){if(nearLeaveSince==DateTime.MinValue){nearLeaveSince=DateTime.UtcNow;AppPaths.Log("Level-Ausgang nach "+mine+": Partner steht daneben, beide gehen");}else if(DateTime.UtcNow-nearLeaveSince>TimeSpan.FromMilliseconds(600)){nearLeaveSince=DateTime.MinValue;bridge.ClearLeave();Go(mine);return;}}
   else if(mine==null&&!string.IsNullOrEmpty(peer.LeaveMap)&&!peer.LeaveMap.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&bridge.AllowedMap(peer.LeaveMap)){AppPaths.Log("Partner verlässt das Level nach "+peer.LeaveMap+" – gehe mit");Go(peer.LeaveMap);return;}}
  if(bridge.LeaveRequest==null)nearLeaveSince=DateTime.MinValue;
  if(bridge.LeaveRequest!=null&&peer!=null&&peer.LeaveMap==bridge.LeaveRequest&&bridge.AllowedMap(bridge.LeaveRequest)){string map=bridge.LeaveRequest;bridge.ClearLeave();if(map.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&AppPaths.Get("HubReload")=="1")bridge.HubReloadPending=true;Go(map);return;}
  if(bridge.WantsHubReload){bridge.HubReloadPending=false;AppPaths.Log("Labor nach Spielende sauber neu laden");Go("MapMonde");return;}
  // Level exit portal (both on it, one pressed Space).
  // The exit question did not work in this game within 11 s. Leaving alone would split the pair: only the host goes
  // (the guest follows him), or a player whose partner already left or only watches.
  if(bridge.WantsHub){bridge.WantsHub=false;bridge.ClearLeave();bool partnerGone=peer==null||me==null||peer.MapFile!=me.MapFile||(peer.Look&0x200)!=0;if(isHost||partnerGone){Go("MapMonde");return;}AppPaths.Log("Level-Ausgang: warte auf den Host");}
  // Safety net when the maps differ: the one left behind in MapMonde (lab/overworld) follows the partner into his level
  // after 6 s (Tim 5.11.: nobody is sent back to the lab); otherwise the guest follows the host after 12 s.
  {if(peer==null||me==null||peer.MapFile==me.MapFile||peer.MapFile=="")mismatchSince=DateTime.MaxValue;else if(mismatchSince==DateTime.MaxValue)mismatchSince=DateTime.UtcNow;
   bool leftBehind=me!=null&&peer!=null&&me.MapFile.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&!peer.MapFile.Equals("MapMonde",StringComparison.OrdinalIgnoreCase);
   if(peer!=null&&me!=null&&bridge.AllowedMap(peer.MapFile)&&!(GameOverLeaving&&!peer.MapFile.Equals("MapMonde",StringComparison.OrdinalIgnoreCase))&&((leftBehind&&DateTime.UtcNow-mismatchSince>TimeSpan.FromSeconds(6))||(!isHost&&!peer.MapFile.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&DateTime.UtcNow-mismatchSince>TimeSpan.FromSeconds(12)))){mismatchSince=DateTime.MaxValue;AppPaths.Log("Partner ist in "+peer.MapFile+" – komme nach");Go(peer.MapFile);return;}}
  // Spectator (no co-op lives left): follow the partner into the next level; if both are out, both go to the hub.
  bridge.PeerOutOfLives=peer!=null&&(peer.Look&0x200)!=0;try{VideoTick(me,peer);}catch(Exception ve){AppPaths.Log("Bildübertragung: "+ve.Message);}bridge.PeerSameMap=peer!=null&&me!=null&&peer.MapFile==me.MapFile;
  /* shared Game Over decided by the host: the watching guest gets 3 lives too, not the one life of a spectator after a finished level (6.11.) */if(peer!=null&&(peer.Look&0x400)!=0){if(!gameOverTaken&&(bridge.OutOfLives||bridge.LastLife||bridge.Spectating)){gameOverTaken=true;gameOverSeenAt=DateTime.UtcNow;AppPaths.Log("Gemeinsames Game Over, 3 Leben");bridge.RefillLives(3);}/* the guest goes to the lab himself: when he died last he never became a spectator, so the spectator follow did not take him (7.11.) */if(!isHost&&me!=null&&DateTime.UtcNow-gameOverSeenAt<TimeSpan.FromSeconds(20)&&!me.MapFile.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&bridge.AllowedMap("MapMonde")){if(pendingGo==null)AppPaths.Log("Game Over: Gast geht ins Labor");Go("MapMonde");return;}}else if(peer!=null)gameOverTaken=false;
  /* also right after spectating ended: the spectator check may switch it off in the same moment the partner leaves (Game Over, 7.11.) */if(bridge.Spectating)lastSpectatingAt=DateTime.UtcNow;
  /* the partner finished the level and came out at the level portal; our own change lands at the map's start (the lab), so the former spectator is put next to him (Tim 7.11.) */if(joinPartnerMap!=null){if(DateTime.UtcNow>joinPartnerUntil)joinPartnerMap=null;else if(peer!=null&&me!=null&&me.MapFile==joinPartnerMap&&peer.MapFile==joinPartnerMap&&peer.Matrix!=null&&bridge.StablePlaying&&!bridge.Spectating){float px=BitConverter.ToSingle(peer.Matrix,4),py=BitConverter.ToSingle(peer.Matrix,8),pz=BitConverter.ToSingle(peer.Matrix,12);double dx=BitConverter.ToSingle(peer.Matrix,4)-BitConverter.ToSingle(me.Matrix,4),dy=BitConverter.ToSingle(peer.Matrix,8)-BitConverter.ToSingle(me.Matrix,8);try{if(dx*dx+dy*dy>9){bridge.MoveOwn(px+1.2f,py,pz+0.3f);AppPaths.Log("Neben den Partner gesetzt ("+px.ToString("0.0")+", "+py.ToString("0.0")+")");}joinPartnerMap=null;}catch{/* second Donald not ready yet, next tick */}}}
  if(peer!=null&&me!=null&&(bridge.Spectating||DateTime.UtcNow-lastSpectatingAt<TimeSpan.FromSeconds(6))&&peer.MapFile!=""&&peer.MapFile!=me.MapFile&&!(GameOverLeaving&&!peer.MapFile.Equals("MapMonde",StringComparison.OrdinalIgnoreCase))/* during a shared Game Over only towards the lab, never back into the level the partner is just leaving (7.11.) */&&bridge.AllowedMap(peer.MapFile)){if(pendingGo==null)AppPaths.Log("Zuschauer folgt dem Partner nach "+peer.MapFile);joinPartnerMap=peer.MapFile;joinPartnerUntil=DateTime.UtcNow.AddSeconds(40);Go(peer.MapFile);return;}
  if(isHost&&AppPaths.Get("BothOutGo")!="0"&&peer!=null&&me!=null&&bridge.OutOfLives&&bridge.PeerOutOfLives&&peer.MapFile==me.MapFile&&!me.MapFile.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)){gameOverUntil=DateTime.UtcNow.AddSeconds(30);gameOverGoAt=DateTime.UtcNow;if(pendingGo==null){AppPaths.Log("Beide ohne Leben – gemeinsam ins Labor");/* lives back before leaving, so the hub is entered like after a normal level */bridge.RefillLives(3);}Go("MapMonde");return;}
  try{bridge.WorldTick(peer);}catch{}if(peer!=null)bridge.Apply(peer);else bridge.HidePeer();bridge.UpdateSpectator();try{bridge.SpectatorCamera(peer);}catch{}
  PeerName=peer==null?null:(peer.Name==""?"Mitspieler":peer.Name);PeerFeet=peer==null?null:bridge.ReplicaShownAt;/* without a second Donald (after a death) the name still floats over the partner's real position */if(PeerFeet==null&&peer!=null&&me!=null&&bridge.RemoteActor==0&&peer.MapFile==me.MapFile&&peer.Matrix!=null)PeerFeet=new[]{BitConverter.ToSingle(peer.Matrix,4),BitConverter.ToSingle(peer.Matrix,8),BitConverter.ToSingle(peer.Matrix,12)};Camera=PeerFeet==null?null:bridge.CameraMatrix();
  Status=bridge.Status;}catch(Exception e){LastError=e.Message;}finally{ticks++;tickTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-t0;}}
 bool everConnected,codeRenewed;DateTime lostSince=DateTime.MaxValue;string lastTitle;
 string PeerText(Pose me,Pose peer){if(peer==null){if(reopening)return "Internet-Verbindung wird neu aufgebaut …";if(codeRenewed)return "Neuer Einladungscode erstellt – bitte deinem Freund schicken (Code kopieren).";if(!everConnected)return isHost?"Warte auf Mitspieler – Einladungscode schicken":"Warte auf Antwort vom Raum …";double s=(DateTime.UtcNow-lostSince).TotalSeconds;return s<30?"Verbindung zum Mitspieler unterbrochen – verbinde automatisch neu …":isHost?"Mitspieler nicht mehr erreichbar. Er kann mit dem Code jederzeit wieder beitreten.":"Raum nicht erreichbar. Neuen Code vom Host einfügen und erneut „Online-Spiel beitreten“ klicken.";}
  return (peer.Name!=""?peer.Name:"Mitspieler")+" verbunden – "+(peer.MapFile==""?"lädt":peer.MapFile)+(PortalHint(me,peer)!=null?" – "+PortalHint(me,peer):"");}
 // Tells a player standing alone on a portal (or whose partner does) what is missing.
 string PortalHint(Pose me,Pose peer){if(me==null||peer==null||peer.MapFile!=me.MapFile)return null;/* level end / card: the game wants to leave and waits for the partner (4.11.) */if(!string.IsNullOrEmpty(me.LeaveMap)&&!me.LeaveMap.Equals("MapMonde",StringComparison.OrdinalIgnoreCase))return "Levelende erreicht – dein Partner muss zu dir kommen";if(!string.IsNullOrEmpty(peer.LeaveMap)&&!peer.LeaveMap.Equals("MapMonde",StringComparison.OrdinalIgnoreCase))return "Dein Partner ist am Levelende – lauf zu ihm";if(peer.Portal>=0&&me.Portal==peer.Portal)return "Ihr steht beide auf dem Portal – Leertaste!";if(peer.Portal>=0)return "Partner wartet an einem Portal";if(me.Portal>=0)return "Warte auf deinen Partner am Portal";return null;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool SetWindowText(IntPtr h,string text);
 void UpdateTitle(string hint){string title="Donald Duck"+(hint!=null?" – "+hint:"");if(title==lastTitle||game==null)return;try{game.Refresh();if(game.MainWindowHandle!=IntPtr.Zero&&SetWindowText(game.MainWindowHandle,title))lastTitle=title;}catch{}}
 int cdCheck;DateTime nearLeaveSince=DateTime.MinValue;
 string joinPartnerMap;DateTime joinPartnerUntil;
 // Spectator picture (7.11.): capture while the partner watches, overlay while watching (VideoStream.cs).
 FrameCapture capture;VideoOverlay overlay;IntPtr videoWindow;DateTime videoWindowAt;long lastUploaded,lastFetched;readonly object videoGate=new object();
 DateTime partnerWatchedAt=DateTime.MinValue;IntPtr VideoWindow{get{return videoWindow;}}
 void VideoTick(Pose me,Pose peer){if(DateTime.UtcNow-videoWindowAt>TimeSpan.FromSeconds(1)){videoWindowAt=DateTime.UtcNow;videoWindow=GameWindow;}
  if(AppPaths.Get("Video")=="0"){if(capture!=null)capture.Active=false;if(overlay!=null)overlay.Wanted=false;return;}
  bool watchingNow=peer!=null&&me!=null&&(peer.Look&0x200)!=0&&peer.MapFile==me.MapFile;if(watchingNow)partnerWatchedAt=DateTime.UtcNow;/* a short gap (pose loss, reload) does not stop and restart the picture */bool partnerWatches=watchingNow||DateTime.UtcNow-partnerWatchedAt<TimeSpan.FromSeconds(2);bool iWatch=bridge.Spectating;
  if(partnerWatches&&capture==null){capture=new FrameCapture(()=>VideoWindow);if(isHost){if(server!=null)server.FrameSource=()=>{var c=capture;return c==null||!c.Active?null:c.Latest;};}else for(int i=0;i<2;i++)new Thread(UploadWorker){IsBackground=true,Name="Bild senden"}.Start();}
  if(capture!=null)capture.Active=partnerWatches;
  if(iWatch&&overlay==null){overlay=new VideoOverlay(()=>VideoWindow);if(!isHost)for(int i=0;i<3;i++)new Thread(FetchWorker){IsBackground=true,Name="Bild holen"}.Start();}
  if(overlay!=null){overlay.Wanted=iWatch;if(isHost&&iWatch&&server!=null)overlay.Push(server.UpFrame);}}
 void UploadWorker(){while(!end){try{var c=capture;VideoFrame f=c==null||!c.Active?null:c.Latest;bool mine=false;if(f!=null)lock(videoGate){if(f.Seq>lastUploaded){lastUploaded=f.Seq;mine=true;}}if(!mine){Thread.Sleep(3);continue;}if(endpoint!=null)FrameClient.Upload(endpoint,token,f);}catch{Thread.Sleep(200);}}}
 void FetchWorker(){while(!end){try{var o=overlay;if(o==null||!bridge.Spectating||endpoint==null){Thread.Sleep(50);continue;}long after;lock(videoGate)after=lastFetched;VideoFrame f=FrameClient.Fetch(endpoint,token,after);if(f==null)continue;lock(videoGate){if(f.Seq>lastFetched||f.Seq<lastFetched-1000)lastFetched=f.Seq;}o.Push(f);}catch{Thread.Sleep(200);}}} DateTime gameOverUntil,gameOverGoAt=DateTime.MinValue,lastSpectatingAt=DateTime.MinValue;bool gameOverTaken;
 static bool PlayersNear(Pose a,Pose b){try{double d=0;for(int i=1;i<=3;i++){double x=BitConverter.ToSingle(a.Matrix,i*4)-BitConverter.ToSingle(b.Matrix,i*4);d+=x*x;}return Math.Sqrt(d)<6;}catch{return false;}}
 internal GameBridge Bridge{get{return transfer?null:bridge;}}internal void TestGo(string map){Go(map);}
 // A second change right after the first (held-back game request plus the partner's stale LeaveMap) reloaded the
 // map while it was still starting and crashed it (0x438A8, test 31.10.); requests within 5 s are dropped.
 DateTime lastGo=DateTime.MinValue;
 // A change requested while the game is still reloading after a death (e.g. "both out of lives" right at the last
 // death) broke the next level (heap crash in the hub, 1.11.). It waits until the level has been running for 3 s.
 string pendingGo;
 DateTime gameOverSeenAt=DateTime.MinValue;bool GameOverLeaving{get{return DateTime.UtcNow-gameOverGoAt<TimeSpan.FromSeconds(90)||DateTime.UtcNow<gameOverUntil||DateTime.UtcNow-gameOverSeenAt<TimeSpan.FromSeconds(20);}}
 void Go(string map){if(!bridge.StablePlaying&&!(GameOverLeaving&&map.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&bridge.StableForGameOver)){if(pendingGo!=map)AppPaths.Log("Levelwechsel nach "+map+" wartet, bis das Level läuft");pendingGo=map;return;}pendingGo=null;if(DateTime.UtcNow-lastGo<TimeSpan.FromSeconds(5)){AppPaths.Log("Zweiter Levelwechsel nach "+map+" ignoriert");bridge.ClearLeave();return;}lastGo=DateTime.UtcNow;AppPaths.Log("Levelwechsel nach "+map);/* shared Game Over: 3 lives again at the moment of leaving, a death while the change waited for a stable level took one (7.11.) */if(DateTime.UtcNow-gameOverGoAt<TimeSpan.FromSeconds(90)&&map.Equals("MapMonde",StringComparison.OrdinalIgnoreCase)){AppPaths.Log("Game Over: beide mit 3 Leben ins Labor");bridge.RefillLives(3);gameOverUntil=DateTime.UtcNow.AddSeconds(15);}gameOverGoAt=DateTime.MinValue;/* no refill here any more: a spectator comes back with one life in the next level (RefillIfPending), Tim 6.11. */bridge.BeginLevelTransfer(map);transfer=true;if(!isHost)outgoing=null;}

 public void Dispose(){end=true;try{if(capture!=null)capture.Dispose();if(overlay!=null)overlay.Dispose();}catch{}try{if(bridge!=null)bridge.Dispose();}catch{}bridge=null;if(tunnel!=null)tunnel.Dispose();if(server!=null)server.Dispose();}
}

// Shared helpers for starting Donald.exe (from the solo launcher and the dev controller).
public static class GameStarter {
 // The game checks for its CD: an inserted Donald CD is enough, otherwise the image chosen in the start window is mounted.
 // Another launcher (or Windows) may still be busy mounting the image, so a missing CD is only reported after 20 s.
 public static void EnsureDisc(string gameFolder){if(GameSetup.MountedDiscRoot()!=null)return;string iso=AppPaths.FindDiscImage(gameFolder);if(iso==null)for(int i=0;i<40&&GameSetup.MountedDiscRoot()==null;i++)System.Threading.Thread.Sleep(500);if(GameSetup.MountedDiscRoot()!=null)return;if(iso==null)throw new Exception("Bitte die Donald-Duck-CD einlegen oder im Startfenster unter „Spielordner …“ dein CD-Abbild auswählen.");GameSetup.Mount(iso);}
 // Killing a game while it writes Data\Options\Current.cfg leaves a truncated file that crashes the next start.
 public static void GuardOptions(string game){try{string cfg=Path.Combine(game,"Data","Options","Current.cfg");string dir=Path.Combine(AppPaths.Data,"backup",AppPaths.FolderKey(game));Directory.CreateDirectory(dir);string good=Path.Combine(dir,"Current.cfg");if(File.Exists(cfg)&&new FileInfo(cfg).Length>=64)File.Copy(cfg,good,true);else if(File.Exists(good)){if(File.Exists(cfg))File.Copy(cfg,Path.Combine(dir,"Current.cfg.broken-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")),true);File.Copy(good,cfg,true);}}catch{}}
 delegate bool EnumProc(IntPtr h,IntPtr l);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool EnumWindows(EnumProc f,IntPtr l);[System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr p,IntPtr c,string cls,string t);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);[System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);[System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int cmd);
 // A minimized or unfocused game pauses itself before the background hook is installed.
 public static void Restore(Process g){try{if(g==null||g.HasExited)return;g.Refresh();IntPtr h=g.MainWindowHandle;if(h==IntPtr.Zero)return;if(IsIconic(h))ShowWindow(h,9);if(g.MainWindowTitle.EndsWith("pause",StringComparison.OrdinalIgnoreCase)){PostMessage(h,0x1C,(IntPtr)1,IntPtr.Zero);PostMessage(h,0x06,(IntPtr)1,IntPtr.Zero);}}catch{}}
 // "CDROM not found!" / "Donald CD not found" right after mounting: Retry succeeds.
 public static void PressCdRetry(Process g){if(g==null)return;int id=g.Id;EnumWindows((h,l)=>{uint pid;GetWindowThreadProcessId(h,out pid);if(pid==id){var sb=new StringBuilder(64);GetWindowText(h,sb,64);string t=sb.ToString();if(t.StartsWith("CD")&&t.Contains("not found")||t.StartsWith("Donald CD")){IntPtr b=FindWindowEx(h,IntPtr.Zero,"Button",null);if(b!=IntPtr.Zero)PostMessage(b,0xF5,IntPtr.Zero,IntPtr.Zero);}}return true;},IntPtr.Zero);}
}
}
