using System;using System.Collections.Generic;using System.Text;
namespace DonaldOnline {
public sealed partial class GameBridge {
 // Portal gate for the hub (MapMonde). The blue WarpPads teleport Donald when Space is pressed on them.
 // Every WarpPad loses its Brain until both players stand on the same pad, so nobody can warp alone.
 // When one player presses Space on the shared pad, the press is sent along (WarpSeq) and replayed on the
 // other game, so both run the native suck-in animation and teleport together.
 List<uint> padSO=null;Dictionary<uint,uint> padPerso=new Dictionary<uint,uint>(),padBrain=new Dictionary<uint,uint>();string padLevel;bool padsOpen=true;DateTime padScan,holdOpenUntil;public string GateMode="auto";
 int lastSpace=-1;int peerWarpSeq=int.MinValue;DateTime replayUntil;
 Dictionary<uint,int> padId=new Dictionary<uint,int>();
 // Stable id that is the same in both games regardless of which actors are currently loaded: "IBK_WarpPad_Gyro/IBK_WarpPad_Gyro_I11" -> 11, World_M pads +100.
 // Lab pads 0-99, beams 100+, small blue level portals (WarpPad_Sub) 200+, boss portals 300+ (instance name hash, e.g. "_Forest").
 static int PadId(string key){int at=key.LastIndexOf("_I",StringComparison.Ordinal);int n;if(at<0||!int.TryParse(key.Substring(at+2),out n)||n<0||n>99){n=0;foreach(char c in key)n=(n*31+c)%97;}int kind=key.IndexOf("WbInterf",StringComparison.Ordinal)>=0?500:key.IndexOf("WarpPad_M/",StringComparison.Ordinal)>=0?400:key.IndexOf("WarpBoss",StringComparison.Ordinal)>=0?300:key.IndexOf("WarpPad_Sub",StringComparison.Ordinal)>=0?200:key.IndexOf("World_M",StringComparison.Ordinal)>=0?100:0;return kind+n;}
 static bool IsPortalFamily(string family){return family=="WarpPad"||family=="warp_boss";}
 HashSet<uint> bossPads=new HashSet<uint>(),exitPads=new HashSet<uint>();int warpPortal=-1;DateTime padLevelSince;
 // Level exit (pad IBK_WarpPad_M with the question actor RAC_WbInterf): after both stood on it and one pressed Space,
 // each game runs the game's own exit: Space opens the question, Enter confirms "Yes" right away, and ChangeMap is let
 // through for 15 s. The game then sets its return point itself (overworld of the world, not the lab).
 // If the level did not change after that, WantsHub sends the player back directly as a fallback.
 public bool WantsHub;static bool IsExitPortal(int id){return id>=400&&id<600;}
 DateTime exitAllowUntil;public bool NativeExitActive{get{return DateTime.UtcNow<exitAllowUntil;}}
 void StartNativeExit(){if(NativeExitActive)return;exitAllowUntil=DateTime.UtcNow.AddSeconds(15);replayUntil=DateTime.UtcNow.AddSeconds(2);string startMap=CurrentMapFile();
  /* the question is confirmed invisibly (Tim: leaving like entering); up to three attempts with growing pauses instead of the old fallback that sent the player to the lab after 11 s (4.11.) */
  new System.Threading.Thread(()=>{for(int attempt=0;attempt<3;attempt++){exitAllowUntil=DateTime.UtcNow.AddSeconds(15);try{testInputMode=6;System.Threading.Thread.Sleep(300);testInputMode=0;System.Threading.Thread.Sleep(400+attempt*500);testKey=0x1C;testInputMode=8;System.Threading.Thread.Sleep(300);}finally{testInputMode=0;}
   System.Threading.Thread.Sleep(4000);try{if(CurrentMapFile()!=startMap)return;}catch{return;}AppPaths.Log("Level-Ausgang: Bestätigung "+(attempt+1)+" ohne Wirkung, versuche erneut");}AppPaths.Log("Level-Ausgang: nach 3 Versuchen kein Levelwechsel");}){IsBackground=true}.Start();}
 // Switching pad brains off crashed a level (another script dereferenced the pad Brain), so the gate now holds back
 // the Space key instead (InputGuard.BlockSpace). BrainGate keeps the old variant available for tests.
 public bool BrainGate=false;byte[] spinStart;float[] spinCentre;
 public int Portal{get;private set;}
 public int WarpSeq{get;private set;}
 public float[] LocalPosition(){byte[] a=R(U(local+36)+4,12);return new[]{BitConverter.ToSingle(a,0),BitConverter.ToSingle(a,4),BitConverter.ToSingle(a,8)};}
 public string PortalDebug{get{return padSO==null?"keine Portale":"portal="+Portal+" offen="+padsOpen+" pads="+padSO.Count+" modus="+GateMode+" leertaste="+SpaceState+" warp="+WarpSeq+" apply="+applyPath+" unsichtbar="+LocalHidden+" "+warpDebug;}}
 string applyPath="";bool LocalHidden{get{try{return (U(local+48)&2)!=0;}catch{return false;}}}
 // State of the native action entry 19 (Space): positive = frames held, negative = frames since release.
 public int SpaceState{get{try{return (int)U(inputGuardBase+19*24+12);}catch{return -99;}}}
 void UpdatePortals(){Portal=-1;if(level==null){padSO=null;BlockSpace=false;return;}
  if(padSO==null||padLevel!=level){padSO=new List<uint>();padPerso.Clear();padBrain.Clear();padId.Clear();padKids.Clear();bossPads.Clear();exitPads.Clear();padsOpen=true;padLevel=level;padScan=DateTime.MinValue;padLevelSince=DateTime.UtcNow;WantsHub=false;}
  // Actors keep appearing for a while after the hub loads, so the pad list is refreshed every 2 s.
  if(DateTime.UtcNow-padScan>TimeSpan.FromSeconds(2)){padScan=DateTime.UtcNow;var found=new SortedDictionary<string,uint>(StringComparer.Ordinal);uint at=U(world+8),n=U(world+16);for(uint i=0;i<Math.Min(n,2000)&&at>65536;i++){if(U(at)==2){uint st=U(U(at+4)+4);if(IsPortalFamily(Name(0,U(st)))){string key=Name(1,U(st+4))+"/"+Name(2,U(st+8));/* the golden boss puzzle piece at a level end is no portal: no key may be held back there (Tim 5.11.) */if(key.StartsWith("IBK_WarpBoss_Lvl",StringComparison.Ordinal)){at=U(at+20);continue;}if(!found.ContainsKey(key))found.Add(key,at);}}at=U(at+20);}
   foreach(var e in found){uint so=e.Value;if(padPerso.ContainsKey(so)){SetPad(so,padsOpen);continue;}uint p=U(so+4);padPerso[so]=p;padBrain[so]=U(p+12);padId[so]=PadId(e.Key);if(e.Key.IndexOf("WarpBoss",StringComparison.Ordinal)>=0)bossPads.Add(so);if(padId[so]>=400)exitPads.Add(so);/* only in the old BrainGate variant: without it the brain was taken away here and never given back (SetPad returns at once), so the boss puzzle piece had no AI and could not be collected (Tim 5.11.) */if(BrainGate&&!padsOpen)WU(p+12,0);}padSO=new List<uint>(found.Values);}
  float[] own=LocalPosition();double best=double.MaxValue;
  // The boss portal is much larger than the pads.
  for(int i=0;i<padSO.Count;i++){byte[] pos=R(U(padSO[i]+36)+4,12);double dx=BitConverter.ToSingle(pos,0)-own[0],dy=BitConverter.ToSingle(pos,4)-own[1],dz=BitConverter.ToSingle(pos,8)-own[2];double d=Math.Sqrt(dx*dx+dy*dy),range=bossPads.Contains(padSO[i])?4.5:2.4;if(d<range&&d<best&&Math.Abs(dz)<2.5){best=d;Portal=padId[padSO[i]];}}
  // A real Space press on an open shared portal becomes a warp event for the partner.
  int space=SpaceState;uint fg=0;try{fg=U(inputGuardMem+0x800);}catch{}
  if(space>0&&lastSpace<=0&&fg==1&&testInputMode==0&&DateTime.UtcNow>replayUntil&&Portal>=0&&padsOpen){WarpSeq++;holdOpenUntil=DateTime.UtcNow.AddSeconds(6);warpPortal=Portal;if(IsExitPortal(Portal))StartNativeExit();}
  if(Portal<0)BlockSpace=false;lastSpace=space;}
 public void GatePortals(bool open){if(GateMode=="open")open=true;else if(GateMode=="closed")open=false;else if(!open&&(DateTime.UtcNow<holdOpenUntil||DateTime.UtcNow-padLevelSince<TimeSpan.FromSeconds(8)))open=true;BlockSpace=Portal>=0&&GateMode!="open"&&(IsExitPortal(Portal)||!open);if(padSO==null||open==padsOpen)return;foreach(uint so in padPerso.Keys)SetPad(so,open);padsOpen=open;}
 // Pads can drop out of the loaded actor list and come back, so every known pad is set, not only the visible ones.
 void SetPad(uint so,bool open){if(!BrainGate)return;uint p=padPerso[so];try{if(U(so)!=2||U(so+4)!=p)return;uint want=open&&!(exitPads.Contains(so)&&DateTime.UtcNow-padLevelSince>TimeSpan.FromSeconds(8))?padBrain[so]:0u;if(U(p+12)!=want)WU(p+12,want);}catch{}}
 public void GateFromPeer(Pose peer){bool shared=peer!=null&&Portal>=0&&peer.MapFile==CurrentMapFile()&&(peer.Portal==Portal||(peer.Look&0x200)!=0);/* a spectating partner does not block portals */GatePortals(shared);
  /* a spectator never presses anything for the partner (the replayed Space/Enter opened "leave the level?" for him, Tim 6.11.); he follows the partner's map instead */if(peer!=null&&Spectating){peerWarpSeq=peer.WarpSeq;return;}
  if(peer==null)return;if(peerWarpSeq==int.MinValue){peerWarpSeq=peer.WarpSeq;return;}
  if(peer.WarpSeq!=peerWarpSeq){peerWarpSeq=peer.WarpSeq;/* the partner may have stepped off the pad in the same moment (sucked in): the press also counts when this player stood on the same pad up to 1 s ago (Tim 5.11.: partner did not come along) */bool recent=Portal<0&&lastSharedPortal>=0&&DateTime.UtcNow-lastSharedAt<TimeSpan.FromSeconds(1);AppPaths.Log("Partner hat Portal ausgelöst (gemeinsam="+shared+", kurz vorher="+recent+", offen="+padsOpen+", Eingabe="+testInputMode+")");if((shared||recent)&&testInputMode==0){holdOpenUntil=DateTime.UtcNow.AddSeconds(6);warpPortal=shared?Portal:lastSharedPortal;replayUntil=DateTime.UtcNow.AddSeconds(1);if(IsExitPortal(warpPortal))StartNativeExit();else StartWarpReplay();}}
  if(shared){lastSharedPortal=Portal;lastSharedAt=DateTime.UtcNow;}}
 int lastSharedPortal=-1;DateTime lastSharedAt;
 // Space for the partner's warp, again after 1.5 s and 3 s while this game is still on the same map (one 300 ms press was not always taken).
 void StartWarpReplay(){string map=CurrentMapFile();new System.Threading.Thread(()=>{for(int k=0;k<3;k++){if(k>0){System.Threading.Thread.Sleep(1500);try{if(CurrentMapFile()!=map||R(0x5AE2A0,1)[0]!=playMode)return;}catch{return;}AppPaths.Log("Portal: Leertaste erneut für den Partner-Warp ("+(k+1)+")");}testInputMode=6;System.Threading.Thread.Sleep(300);if(testInputMode==6)testInputMode=0;}}){IsBackground=true}.Start();}
 // Development: behaves like the player pressing Space in this game (counts as a warp event for the partner).
 public void PlayerSpace(){if(Portal>=0&&padsOpen){WarpSeq++;holdOpenUntil=DateTime.UtcNow.AddSeconds(6);warpPortal=Portal;if(IsExitPortal(Portal)){StartNativeExit();return;}}ReplaySpace();}
 void ReplaySpace(){testInputMode=6;var t=new System.Threading.Timer(o=>{if(testInputMode==6)testInputMode=0;});t.Change(300,System.Threading.Timeout.Infinite);replayTimer=t;}
 System.Threading.Timer replayTimer;
 void RestorePortals(){try{GateMode="open";GatePortals(true);}catch{}padSO=null;}
 // During a warp the game hides the real Donald (SO flag Invisible) and the WarpPad animates its own Donald
 // figure as child hierarchy of the pad, which the hand grabs. The partner's replica follows that figure
 // (slightly offset) so the hand visibly takes both, and is hidden while the figure is inside the portal.
 float[] warpDelta;long warpTick;string warpDebug="";Dictionary<uint,uint> padKids=new Dictionary<uint,uint>();HashSet<uint> padKidsBase=new HashSet<uint>();DateTime padKidsAt;
 List<uint> NearPads(float[] own){var near=new List<uint>();foreach(uint so in padSO){try{byte[] q=R(U(so+36)+4,12);double dx=BitConverter.ToSingle(q,0)-own[0],dy=BitConverter.ToSingle(q,4)-own[1];if(dx*dx+dy*dy<16)near.Add(so);}catch{}}return near;}
 bool WarpOverride(Pose pose,out Pose result,out bool hide){result=pose;hide=false;
  if(padSO==null){warpDelta=null;return false;}
  // While Donald is still visible, remember how many children each pad normally has; the figure is the extra child.
  float[] own=LocalPosition();
  // Level portals (small blue ones, boss portal): the local Donald spins down into a vortex. The replica does the same:
  // pulled to the portal centre, spinning faster and faster, shrinking and sinking; then it is hidden.
  if(warpPortal>=200&&warpPortal<400&&!WarpFollow&&DateTime.UtcNow<holdOpenUntil){double t=(DateTime.UtcNow-holdOpenUntil.AddSeconds(-6)).TotalSeconds;
   if(t>=1.7){warpDebug="Kopie im Levelportal versteckt";hide=true;return true;}
   if(t>=0.25){if(spinStart==null){spinStart=(byte[])displayPose.Clone();spinCentre=null;foreach(uint so in padSO)if(padId[so]==warpPortal){byte[] q=R(U(so+36)+4,12);spinCentre=new[]{BitConverter.ToSingle(q,0),BitConverter.ToSingle(q,4),BitConverter.ToSingle(q,8)};}}
    float k=(float)Math.Min(1,(t-0.25)/1.45),ease=k*k;byte[] sm=(byte[])spinStart.Clone();
    float sx=BitConverter.ToSingle(spinStart,4),sy=BitConverter.ToSingle(spinStart,8),sz=BitConverter.ToSingle(spinStart,12),cx=spinCentre==null?sx:spinCentre[0],cy=spinCentre==null?sy:spinCentre[1];
    float pull=Math.Min(1f,k*1.6f);Array.Copy(BitConverter.GetBytes(sx+(cx-sx)*pull),0,sm,4,4);Array.Copy(BitConverter.GetBytes(sy+(cy-sy)*pull),0,sm,8,4);Array.Copy(BitConverter.GetBytes(sz-1.6f*ease),0,sm,12,4);
    double angle=ease*Math.PI*6,c=Math.Cos(angle),s=Math.Sin(angle);float scale=1f-0.85f*ease;
    // Rotate both 3x3 blocks about the vertical (z) axis and shrink the second (rotation*scale) block.
    foreach(int block in new[]{16,52})for(int row=0;row<3;row++){float x=BitConverter.ToSingle(spinStart,block+row*12),y=BitConverter.ToSingle(spinStart,block+row*12+4),z=BitConverter.ToSingle(spinStart,block+row*12+8);float f=block==52?scale:1f;
     Array.Copy(BitConverter.GetBytes((float)(c*x-s*y)*f),0,sm,block+row*12,4);Array.Copy(BitConverter.GetBytes((float)(s*x+c*y)*f),0,sm,block+row*12+4,4);Array.Copy(BitConverter.GetBytes(z*f),0,sm,block+row*12+8,4);}
    warpDebug="Kopie dreht sich ins Portal "+k.ToString("F2");result=new Pose{Level=pose.Level,Sequence=-(++warpTick),Matrix=sm,Animation=pose.Animation,MapFile=pose.MapFile,Portal=pose.Portal,WarpSeq=pose.WarpSeq,Session=pose.Session};return true;}}
  else spinStart=null;
  if((U(local+48)&2)==0){warpDelta=null;return false;}
  if(DateTime.UtcNow>holdOpenUntil.AddSeconds(5)){warpDelta=null;return false;}
  // Default (Tim's wish): the arm visibly takes one Donald; the partner's replica simply disappears with it
  // and reappears next to the local Donald once both have arrived. WarpFollow=true carries it along instead.
  if(!WarpFollow){warpDebug="Kopie waehrend Warp versteckt";hide=true;return true;}
  // The carried figure is a direct child subtree of the pad Donald stands on with Donald's size (about 20 parts;
  // the arm has 5). This also finds the figure coming out of the destination pad after the teleport.
  Dictionary<uint,float[]> parts=null;uint figure=0;
  foreach(uint so in NearPads(own)){uint kid=U(so+8);for(int k=0;k<Math.Min(U(so+16),16)&&kid>65536;k++){var d=new Dictionary<uint,float[]>();WalkAll(kid,0,d);if(d.Count>=15&&d.Count<=30){parts=d;figure=kid;break;}kid=U(kid+20);}if(parts!=null)break;}
  if(parts==null){warpDebug="keine Figur, Kopie versteckt";hide=true;return true;}
  float fx=0,fy=0,fz=0;foreach(float[] v in parts.Values){fx+=v[0];fy+=v[1];fz+=v[2];}fx/=parts.Count;fy/=parts.Count;fz/=parts.Count;warpDebug="figur="+figure.ToString("X8")+" teile="+parts.Count+" @"+fx.ToString("F1")+","+fy.ToString("F1")+","+fz.ToString("F1");
  if(warpDelta==null)warpDelta=new[]{own[0]-fx,own[1]-fy,own[2]-fz};
  // Side offset grows with the lift: inside the portal both come out of the centre, in the air they hang side by side.
  float feetZ=fz+warpDelta[2],lift=Math.Max(0f,Math.Min(1f,(feetZ-own[2])/1.5f));
  byte[] m=(byte[])pose.Matrix.Clone();float[] target={fx+warpDelta[0]+0.9f*lift,fy+warpDelta[1],feetZ};
  for(int i=0;i<3;i++)Array.Copy(BitConverter.GetBytes(target[i]),0,m,4+i*4,4);
  result=new Pose{Level=pose.Level,Sequence=-(++warpTick),Matrix=m,Animation=WarpAnimation>=0?WarpAnimation:pose.Animation,MapFile=pose.MapFile,Portal=pose.Portal,WarpSeq=pose.WarpSeq,Session=pose.Session};return true;}
 // Development: show the replica next to the local Donald playing a chosen Donald state (to pick the grabbed animation).
 // Donald state 10 (arms up, flailing) while the arm carries the replica; picked from a sweep over all 157 states.
 public int WarpAnimation=10;public bool WarpFollow=false;int forcedAnimation=-1;float forcedDx,forcedDy;
 public void ForceReplica(int animation,float dx,float dy){forcedAnimation=animation;forcedDx=dx;forcedDy=dy;}
 public int ReplicaStateCount{get{try{return remote==0?0:States(remote).Count;}catch{return 0;}}}
 Pose ForcedPose(Pose pose){float[] own=LocalPosition();byte[] m=(byte[])pose.Matrix.Clone();Array.Copy(BitConverter.GetBytes(own[0]+forcedDx),0,m,4,4);Array.Copy(BitConverter.GetBytes(own[1]+forcedDy),0,m,8,4);Array.Copy(BitConverter.GetBytes(own[2]),0,m,12,4);return new Pose{Level=pose.Level,Sequence=-(++warpTick),Matrix=m,Animation=forcedAnimation,MapFile=pose.MapFile,Portal=pose.Portal,WarpSeq=pose.WarpSeq,Session=pose.Session};}
 HashSet<uint> probeBase;
 // Development: local Donald's hierarchy/visibility plus actors that appeared since the first probe (to find the warp hand).
 public string WarpProbe(){var s=new StringBuilder();uint p3d=U(U(local+4));float[] pos=LocalPosition();s.Append("local parent="+U(local+28).ToString("X8")+" draw="+U(local+44).ToString("X8")+" flags="+U(local+48).ToString("X8")+" pos="+pos[0].ToString("F2")+","+pos[1].ToString("F2")+","+pos[2].ToString("F2")+" anim="+AnimationIndex(local)+" frame="+(R(p3d+0x74,2)[0]|R(p3d+0x75,1)[0]<<8));
  var now=new HashSet<uint>();uint at=U(world+8),n=U(world+16);for(uint i=0;i<Math.Min(n,2000)&&at>65536;i++){now.Add(at);if(probeBase!=null&&!probeBase.Contains(at)&&U(at)==2){uint st=U(U(at+4)+4);byte[] q=R(U(at+36)+4,12);s.Append(" NEW "+at.ToString("X8")+" "+Name(0,U(st))+"/"+Name(1,U(st+4))+"/"+Name(2,U(st+8))+" @"+BitConverter.ToSingle(q,0).ToString("F2")+","+BitConverter.ToSingle(q,4).ToString("F2")+","+BitConverter.ToSingle(q,8).ToString("F2")+" draw="+U(at+44).ToString("X8")+" child="+U(at+16));}at=U(at+20);}if(probeBase==null)probeBase=now;
  // Children of the local Donald (attached objects) can also change during the warp.
  s.Append(" kids="+U(local+16));
  var mine=new Dictionary<uint,float[]>();WalkAll(local,0,mine);s.Append(" donaldTeile="+mine.Count+" apply="+applyPath+" main="+(U(0x5AE498)==local?"local":U(0x5AE498).ToString("X8"))+" scene="+R(0x5AE2A0,1)[0]+" "+warpDebug);
  if(remote!=0){byte[] rp=R(U(remote+36)+4,12);s.Append(" KOPIE@"+BitConverter.ToSingle(rp,0).ToString("F1")+","+BitConverter.ToSingle(rp,4).ToString("F1")+","+BitConverter.ToSingle(rp,8).ToString("F1")+" draw="+U(remote+44).ToString("X8"));if(replicaMem!=0){byte[] bp=R(U(replicaMem+0x1300)+4,12);byte[] lm=R(replicaMatrix+4,12);s.Append(" PUFFER@"+BitConverter.ToSingle(bp,0).ToString("F1")+","+BitConverter.ToSingle(bp,4).ToString("F1")+","+BitConverter.ToSingle(bp,8).ToString("F1")+" aktiv="+U(replicaMem+0x1308)+" LOKAL@"+BitConverter.ToSingle(lm,0).ToString("F1")+","+BitConverter.ToSingle(lm,4).ToString("F1")+","+BitConverter.ToSingle(lm,8).ToString("F1")+" hook="+BitConverter.ToString(R(0x40A560,5)));}}
  foreach(uint pad in NearPads(pos)){uint kid=U(pad+8);for(int k=0;k<Math.Min(U(pad+16),12)&&kid>65536;k++){var sub=new Dictionary<uint,float[]>();WalkAll(kid,0,sub);float cx=0,cy=0,cz=0;foreach(float[] v in sub.Values){cx+=v[0];cy+=v[1];cz+=v[2];}int c=Math.Max(1,sub.Count);s.Append(" | pad"+padId[pad]+".kid"+k+" "+kid.ToString("X8")+" n="+sub.Count+" @"+(cx/c).ToString("F1")+","+(cy/c).ToString("F1")+","+(cz/c).ToString("F1")+" fl="+U(kid+48).ToString("X"));kid=U(kid+20);}}
  if(pos!=null)return s.ToString();
  // Every super object (any depth) whose global position moved since the previous probe.
  var moved=new Dictionary<uint,float[]>();WalkAll(U(0x5AEEF0),0,moved);if(probePos!=null)foreach(var e in moved){float[] o;if(probePos.TryGetValue(e.Key,out o)){double d=Math.Abs(o[0]-e.Value[0])+Math.Abs(o[1]-e.Value[1])+Math.Abs(o[2]-e.Value[2]);if(d>0.3)s.Append(" MOVED "+e.Key.ToString("X8")+" type="+U(e.Key).ToString("X")+" parent="+U(e.Key+28).ToString("X8")+" "+SoName(e.Key)+" @"+e.Value[0].ToString("F1")+","+e.Value[1].ToString("F1")+","+e.Value[2].ToString("F1")+" draw="+U(e.Key+44).ToString("X8")+" fl="+U(e.Key+48).ToString("X"));}}probePos=moved;return s.ToString();}
 Dictionary<uint,float[]> probePos;
 void WalkAll(uint parent,int depth,Dictionary<uint,float[]> into){if(depth>6)return;uint at=U(parent+8),n=U(parent+16);for(uint i=0;i<Math.Min(n,3000)&&at>65536;i++){try{uint g=U(at+36);if(g>65536){byte[] q=R(g+4,12);into[at]=new[]{BitConverter.ToSingle(q,0),BitConverter.ToSingle(q,4),BitConverter.ToSingle(q,8)};}}catch{}if(U(at+16)>0)WalkAll(at,depth+1,into);at=U(at+20);}}
 string SoName(uint so){try{if(U(so)!=2){uint p=so;for(int up=0;up<12;up++){p=U(p+28);if(p<65536)return "root?";uint t=U(p);if(t==2)return "under:"+SoName(p)+"("+p.ToString("X8")+")";if(p==U(0x5AEEF0))return "under-dynamic-root";}return "deep";}uint st=U(U(so+4)+4);return Name(0,U(st))+"/"+Name(1,U(st+4))+"/"+Name(2,U(st+8));}catch{return "?";}}
 // Lists every actor of the current dynamic world with names, position and components (development aid).
 public string ActorDump(){var log=new StringBuilder();uint root=U(0x5AEEF0);log.AppendLine("map="+CurrentMapFile()+" root="+root.ToString("X8"));DumpChildren(root,0,log);return log.ToString();}
 void DumpChildren(uint parent,int depth,StringBuilder log){if(depth>3)return;uint at=U(parent+8),n=U(parent+16);for(uint i=0;i<Math.Min(n,2000)&&at>65536;i++){uint type=U(at);string line=new string(' ',depth*2)+"SO="+at.ToString("X8")+" type="+type.ToString("X");if(type==2){uint p=U(at+4),st=U(p+4);byte[] pos=R(U(at+36)+4,12);line+=" "+Name(0,U(st))+"/"+Name(1,U(st+4))+"/"+Name(2,U(st+8))+" pos="+BitConverter.ToSingle(pos,0).ToString("F2")+","+BitConverter.ToSingle(pos,4).ToString("F2")+","+BitConverter.ToSingle(pos,8).ToString("F2")+" dyn="+U(p+8).ToString("X8")+" brain="+U(p+12).ToString("X8")+" coll="+U(p+20).ToString("X8")+" draw="+U(at+44).ToString("X8")+" flags="+U(at+48).ToString("X8");}log.AppendLine(line);if(U(at+16)>0&&type!=2)DumpChildren(at,depth+1,log);at=U(at+20);}}
}
}
