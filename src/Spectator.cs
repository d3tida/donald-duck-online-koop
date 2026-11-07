using System;using System.Collections.Generic;
namespace DonaldOnline {
public sealed partial class GameBridge {
 // Spectator mode (Tim, 31.10.): a player without co-op lives watches the partner instead of getting a Game Over.
 // Switching the engine's main actor (g_hNextMainActor 0x5AE49C) to the replica crashed within seconds (0x85653), the
 // replica has no dynamics/brain. Instead the own Donald is hidden, gets no input and is carried along with the partner
 // (absolute-position procedure through the frame hook), so the normal game camera follows the partner.
 public bool Spectating{get;private set;}public bool PeerOutOfLives;uint savedDraw,savedFlags,hiddenLocal,savedColl;
 public void SetSpectator(bool on){if(on==Spectating)return;/* camera writer off at once: a level change in the same tick left it on and it wrote into the freed camera of the old level (guest crash in ntdll while loading the lab, 7.11.) */if(!on)StopPartnerCamera();try{
   if(on){if(remote==0||replicaMem==0)return;savedDraw=U(local+44);savedFlags=U(local+48);hiddenLocal=local;Spectating=true;WU(local+44,0);WU(local+48,savedFlags|0x82);/* no collision, so the hidden Donald pushes nobody and takes or triggers nothing (Tim 6.11.); given back when spectating ends */savedColl=0;if(AppPaths.Get("SpectatorNoCollision")!="0"){uint lp=U(local+4);savedColl=U(lp+20);if(savedColl!=0)WU(lp+20,0);}AppPaths.Log("Zuschauer-Modus an – Kamera folgt dem Partner");}
   else{Spectating=false;/* only the Donald that was hidden gets its values back; after a map change the new Donald is untouched (writing the old flags into it preceded hub crashes) */if(local==hiddenLocal&&U(local)==2){WU(local+44,savedDraw!=0?savedDraw:0xFFFFFFFF);WU(local+48,U(local+48)&~0x82u|(savedFlags&0x82u));if(savedColl!=0){uint lp=U(local+4);if(U(lp+20)==0)WU(lp+20,savedColl);}}savedColl=0;hiddenLocal=0;AppPaths.Log("Zuschauer-Modus aus");}}catch(Exception e){AppPaths.Log("Zuschauer-Modus: "+e.Message);}}
 // While spectating (also after a death reload): keep the own Donald hidden and close behind the partner.
 DateTime followed;static int SpectatorFollowMs{get{int v;return int.TryParse(AppPaths.Get("SpectatorFollowMs"),out v)?v:0;}}
 void KeepSpectating(){if(!Spectating)return;try{if(remote==0||replicaMem==0)return;if(U(local+44)!=0)WU(local+44,0);uint f=U(local+48);if((f&0x82)!=0x82)WU(local+48,f|0x82);
   /* every tick (10 ms) with the smoothed pose of the visible partner: a jump every 120 ms let the hidden Donald fall in between and the camera jerked (Tim, 1.11.) */if(DateTime.UtcNow-followed>TimeSpan.FromMilliseconds(SpectatorFollowMs)){float[] at=ReplicaShownAt;if(at!=null){followed=DateTime.UtcNow;PlaceAt(at[0],at[1],SpectatorHeight(at[2]));/* facing of the partner too, so the game camera looks the same way as his (Tim 6.11.: camera like when playing) */try{if(displayPose!=null&&AppPaths.Get("SpectatorTurn")!="0"){byte[] rot=new byte[36];Array.Copy(displayPose,16,rot,0,36);foreach(uint m in new[]{U(local+32),U(local+36)})if(m>65536)W(m+16,rot);/* the dynamics copy its current matrix back into the SO every frame, so the facing also goes into its previous/current matrix (DNM base block +0x78 / +0xD0, rotation +16) */uint dyn=U(U(U(local+4)+8));if(dyn>65536){W(dyn+0x78+16,rot);W(dyn+0xD0+16,rot);}}}catch{}}}}catch{}}
 // Height of the hidden Donald: the partner's height once it has been steady for 0.4 s (walking, standing, a ledge);
 // during the partner's jumps the hidden Donald stays where he is: putting him into the air every frame and letting him
 // drop back made the camera bob up and down (measured 4.11.).
 float steadyZ=float.NaN;DateTime steadySince;
 float SpectatorHeight(float partnerZ){DateTime now=DateTime.UtcNow;if(float.IsNaN(steadyZ)||Math.Abs(partnerZ-steadyZ)>0.2f){steadyZ=partnerZ;steadySince=now;}if((now-steadySince).TotalMilliseconds>=400){groundZ=partnerZ;return partnerZ+0.05f;}/* during a jump the last steady height (without collision the hidden Donald would sink) */return float.IsNaN(groundZ)?partnerZ+0.05f:groundZ+0.05f;}
 float groundZ=float.NaN;
 // Called by the session: decides when this player becomes a spectator.
 // Only in the same level as the partner and never in the hub: the hidden Donald was once carried to the partner's
 // brush coordinates inside the hub (crash in the frame hook).
 public bool PeerSameMap;
 uint unsharedSeen;
 // Spectator camera (Tim 6.11.: "die echte Kamera übertragen"): the playing partner sends the global matrix of his game
 // camera; the spectator writes it, smoothed, into his own camera after the engine has updated all actors (hook at
 // 0x40B5E3, right after 0x40AF70 and before 0x40C2F0/rendering), so his camera AI keeps running but is overruled.
 static readonly byte[] CamHookOriginal={0x83,0xC4,0x0C,0xE8,0x05,0x0D,0x00,0x00};uint camHookMem;byte[] camShown;
 [System.Runtime.InteropServices.DllImport("kernel32.dll")]static extern IntPtr GetModuleHandle(string n);[System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr m,string n);
 /* 7.11.: the camera is now taken and shown per game frame inside the hook. Before, the launcher (10 ms timer) read and wrote it
    between frames (about 18 ms): wrong sample times on the sender and uneven steps on the spectator, measured 30 jerks in 15 s.
    Layout of the hook page: +0x200 spectator write on, +0x204 camera SO, +0x208 world, +0x20C play mode, +0x210 active sample slot,
    +0x214 frame stamping on, +0x300 shown camera, slots +0x400/+0x700 (n, 8 QPC times, 8 cameras), +0xA00 frame stamp
    (sequence, camera, QPC), code at +0xB00. */
 // The own Donald and its frame time from the camera hook's stamp (null when not available).
 uint localStampSeen;public bool LocalFrame(out byte[] matrix,out long timeMs,out bool fresh){matrix=null;timeMs=0;fresh=false;try{if(camHookMem==0||AppPaths.Get("PoseStamp")=="0")return false;uint s1=U(camHookMem+0xA00);byte[] q=R(camHookMem+0xA60,8);byte[] m=R(camHookMem+0xA80,88);uint s2=U(camHookMem+0xA00);if(s1==0||(s1&1)!=0||s1!=s2)return false;bool any=false;for(int i=4;i<16;i++)if(m[i]!=0){any=true;break;}if(!any)return false;
  long qpc=BitConverter.ToInt64(q,0);double ms=Pose.Clock.Elapsed.TotalMilliseconds-(System.Diagnostics.Stopwatch.GetTimestamp()-qpc)*1000.0/System.Diagnostics.Stopwatch.Frequency;if(ms<0)return false;fresh=s1!=localStampSeen;localStampSeen=s1;matrix=m;timeMs=(long)Math.Round(ms);return true;}catch{return false;}}
 public double CameraMsOut; public int CameraLagOut;
 public byte[] CameraOut{get{CameraLagOut=0;CameraMsOut=0;try{if(Spectating)return null;CameraMatrix();if(cameraSO==0)return null;EnsureCamStamp();if(camHookMem!=0)for(int t=0;t<3;t++){uint s1=U(camHookMem+0xA00);byte[] blk=R(camHookMem+0xA08,96);uint s2=U(camHookMem+0xA00);if(s1==0||(s1&1)!=0||s1!=s2)continue;long q=BitConverter.ToInt64(blk,88);double camMs=Pose.Clock.Elapsed.TotalMilliseconds-(System.Diagnostics.Stopwatch.GetTimestamp()-q)*1000.0/System.Diagnostics.Stopwatch.Frequency;double lag=(long)Pose.Clock.Elapsed.TotalMilliseconds-camMs;if(lag<-2000||lag>2000)break;CameraLagOut=(int)Math.Round(lag);CameraMsOut=camMs;var cam=new byte[88];Array.Copy(blk,0,cam,0,88);return cam;}return R(U(cameraSO+36),88);}catch{return null;}}}
 public DateTime TrackCloneUntil;void TrackCloneTick(){try{if(camHookMem!=0)WU(camHookMem+0x218,DateTime.UtcNow<TrackCloneUntil&&ReplicaStillValid()?replicaSO:0u);}catch{}}
 void EnsureCamStamp(){try{InstallCamHook();if(camHookMem==0)return;TrackCloneTick();WU(camHookMem+0x208,world);WU(camHookMem+0x20C,playMode);WU(camHookMem+0x214,1);}catch{}}
 // Test: the last camera the game actually rendered, with its frame time (QPC).
 public bool CamFrame(out uint seq,out long qpc,out float[] pos){seq=0;qpc=0;pos=null;try{if(camHookMem==0)return false;uint s1=U(camHookMem+0xA00);byte[] blk=R(camHookMem+0xA08,116);uint s2=U(camHookMem+0xA00);if(s1==0||(s1&1)!=0||s1!=s2)return false;seq=s1;qpc=BitConverter.ToInt64(blk,88);pos=new[]{BitConverter.ToSingle(blk,4),BitConverter.ToSingle(blk,8),BitConverter.ToSingle(blk,12),BitConverter.ToSingle(blk,104),BitConverter.ToSingle(blk,108),BitConverter.ToSingle(blk,112)};return true;}catch{return false;}}
 void InstallCamHook(){if(camHookMem!=0)return;byte[] now=R(0x40B5E3,8);if(BitConverter.ToString(now)!=BitConverter.ToString(CamHookOriginal)){AppPaths.Log("Kamera-Haken: unbekannter Code");return;}
  uint qpcFn=(uint)GetProcAddress(GetModuleHandle("kernel32.dll"),"QueryPerformanceCounter").ToInt64();if(qpcFn<65536){AppPaths.Log("Kamera-Haken: kein QPC");return;}
  uint mem=(uint)VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)4096,0x3000,0x40).ToInt64();if(mem<65536)return;uint flag=mem+0x200,so=mem+0x204,buf=mem+0x300,seq=mem+0xA00,code=mem+0xB00;
  W(mem,new byte[0xB00]);
  var b=new List<byte>();Action<byte[]> B=x=>b.AddRange(x);Action<uint> I=x=>b.AddRange(BitConverter.GetBytes(x));
  var toOut=new List<int>();var toStamp=new List<int>();
  Action<byte> JccOut=cc=>{B(new byte[]{0x0F,cc});toOut.Add(b.Count);I(0);};Action<byte> JccStamp=cc=>{B(new byte[]{0x0F,cc});toStamp.Add(b.Count);I(0);};
  Action<List<int>> Here=list=>{foreach(int at in list){int rel=b.Count-(at+4);var rb=BitConverter.GetBytes(rel);for(int i=0;i<4;i++)b[at+i]=rb[i];}list.Clear();};
  B(new byte[]{0x83,0xC4,0x0C,0x9C,0x60});
  /* only while playing in the world the values were taken for (ntdll heap crash after "both out", 6.11.) */
  B(new byte[]{0xA1});I(0x5AEEF0);B(new byte[]{0x3B,0x05});I(mem+0x208);JccOut(0x85);
  B(new byte[]{0x0F,0xB6,0x05});I(0x5AE2A0);B(new byte[]{0x3B,0x05});I(mem+0x20C);JccOut(0x85);
  // spectator: interpolate the partner's camera for this frame's time and write it into the own camera
  B(new byte[]{0x83,0x3D});I(flag);B(new byte[]{0x01});JccStamp(0x85);
  B(new byte[]{0x8B,0x1D});I(so);B(new byte[]{0x85,0xDB});JccStamp(0x84);B(new byte[]{0x83,0x3B,0x02});JccStamp(0x85);/* and only while it is still the engine's active camera */B(new byte[]{0xA1});I(0x5AE4A0);B(new byte[]{0x39,0xD8});JccStamp(0x85);
  B(new byte[]{0x8B,0x35});I(mem+0x210);B(new byte[]{0x85,0xF6});JccStamp(0x84);
  B(new byte[]{0x83,0xEC,0x08,0x54,0xB8});I(qpcFn);B(new byte[]{0xFF,0xD0,0x8B,0x04,0x24,0x83,0xC4,0x08});
  B(new byte[]{0x8B,0x0E,0x8D,0x7E,0x04,0x31,0xD2});
  int loop=b.Count;B(new byte[]{0x8D,0x5A,0x02,0x39,0xCB,0x7D});int f1=b.Count;B(new byte[]{0});B(new byte[]{0x89,0xC3,0x2B,0x5C,0x97,0x04,0x78});int f2=b.Count;B(new byte[]{0});B(new byte[]{0x42});B(new byte[]{0xEB,(byte)(loop-(b.Count+2))});
  b[f1]=(byte)(b.Count-f1-1);b[f2]=(byte)(b.Count-f2-1);
  B(new byte[]{0x89,0xC3,0x2B,0x1C,0x97,0x79,0x02,0x31,0xDB});
  B(new byte[]{0x8B,0x4C,0x97,0x04,0x2B,0x0C,0x97,0x85,0xC9});JccStamp(0x8E);
  B(new byte[]{0x39,0xCB,0x7E,0x02,0x89,0xCB});
  B(new byte[]{0x51,0x53,0xDB,0x04,0x24,0xDA,0x74,0x24,0x04,0x83,0xC4,0x08});
  B(new byte[]{0x6B,0xD2,0x58,0x8D,0x74,0x16,0x40,0x8D,0x7E,0x58,0xBB});I(buf);B(new byte[]{0x8B,0x07,0x89,0x03,0xB9,0x04,0,0,0});
  int ll=b.Count;B(new byte[]{0xD9,0x04,0x0F,0xD8,0x24,0x0E,0xD8,0xC9,0xD8,0x04,0x0E,0xD9,0x1C,0x0B,0x83,0xC1,0x04,0x83,0xF9,0x58});B(new byte[]{0x7C,(byte)(ll-(b.Count+2))});
  B(new byte[]{0xDD,0xD8});
  B(new byte[]{0x8B,0x1D});I(so);
  foreach(byte off in new byte[]{0x24,0x20}){B(new byte[]{0x8B,0x7B,off,0x85,0xFF,0x74,0x0D,0xBE});I(buf);B(new byte[]{0xB9,0x16,0,0,0,0xFC,0xF3,0xA5});}
  B(new byte[]{0x53,0xB8});I(0x45D530);B(new byte[]{0xFF,0xD0,0x83,0xC4,0x04});
  // every frame: copy the camera the game will render now, with its time, for sending and for measuring
  Here(toStamp);
  B(new byte[]{0x83,0x3D});I(mem+0x214);B(new byte[]{0x01});JccOut(0x85);
  B(new byte[]{0x8B,0x1D});I(0x5AE4A0);B(new byte[]{0x85,0xDB});JccOut(0x84);B(new byte[]{0x83,0x3B,0x02});JccOut(0x85);
  B(new byte[]{0x8B,0x73,0x24,0x85,0xF6});JccOut(0x84);
  B(new byte[]{0xFF,0x05});I(seq);B(new byte[]{0xBF});I(mem+0xA08);B(new byte[]{0xB9,0x16,0,0,0,0xFC,0xF3,0xA5,0x68});I(mem+0xA60);B(new byte[]{0xB8});I(qpcFn);B(new byte[]{0xFF,0xD0});/* test measurement: where the partner Donald is drawn this frame (+0x218 = clone SO, copy of its position to +0xA70) */B(new byte[]{0x8B,0x1D});I(mem+0x218);B(new byte[]{0x85,0xDB,0x74,0x16,0x8B,0x73,0x20,0x85,0xF6,0x74,0x0F,0x83,0xC6,0x04,0xBF});I(mem+0xA70);B(new byte[]{0xB9,0x03,0,0,0,0xF3,0xA5});/* the own Donald as drawn this frame (+0xA80), sent with the frame time so the partner can interpolate without the launcher tick in the timing (7.11.) */B(new byte[]{0x8B,0x1D});I(0x5AE498);B(new byte[]{0x85,0xDB,0x74,0x13,0x8B,0x73,0x24,0x85,0xF6,0x74,0x0C,0xBF});I(mem+0xA80);B(new byte[]{0xB9,0x16,0,0,0,0xF3,0xA5});B(new byte[]{0xFF,0x05});I(seq);
  Here(toOut);
  B(new byte[]{0x61,0x9D,0xB8});I(0x40C2F0);B(new byte[]{0xFF,0xD0,0x68});I(0x40B5EB);B(new byte[]{0xC3});
  if(b.Count>0x500)throw new Exception("Kamera-Haken zu lang");W(code,b.ToArray());camHookMem=mem;Jump(0x40B5E3,code,8);AppPaths.Log("Kamera-Haken installiert ("+b.Count+" Bytes bei "+code.ToString("X8")+", PID "+Pid+")");}
 void ClearCamHook(){if(camHookMem==0)return;try{WU(camHookMem+0x200,0);WU(camHookMem+0x214,0);CodeWrite(0x40B5E3,CamHookOriginal);}catch{}camHookMem=0;}
 const double CamDelayMs=110;int camSlot;
 // Called every tick while spectating: keeps the last received cameras with their render times; the hook picks per frame.
 void FollowPartnerCamera(byte[] cam,double senderCamMs){if(cam==null||cam.Length!=88||AppPaths.Get("SpectatorCamera")=="0")return;try{InstallCamHook();if(camHookMem==0)return;CameraMatrix();if(cameraSO==0)return;
   double nowMs=Pose.Clock.Elapsed.TotalMilliseconds;long nowQ=System.Diagnostics.Stopwatch.GetTimestamp();
   if(camHist.Count==0||Math.Abs(camHist[camHist.Count-1].Item1-senderCamMs)>3){if(camHist.Count>0&&senderCamMs<camHist[camHist.Count-1].Item1)camHist.Clear();camHist.Add(Tuple.Create(senderCamMs,(byte[])cam.Clone()));if(camHist.Count>8)camHist.RemoveAt(0);double off=nowMs-senderCamMs;camOffset=double.IsNaN(camOffset)||off<camOffset?off:camOffset+0.02*(off-camOffset);}
   else return;
   int n=camHist.Count;uint slot=camHookMem+(camSlot==0?0x400u:0x700u);camSlot^=1;var data=new byte[0x40+8*88];
   Func<double,int> Q=ms=>unchecked((int)(nowQ+(long)((ms+camOffset+InterpolationDelay-nowMs)*System.Diagnostics.Stopwatch.Frequency/1000.0)));
   int cnt=Math.Max(2,n);Array.Copy(BitConverter.GetBytes(cnt),0,data,0,4);
   for(int i=0;i<cnt;i++){int k=Math.Min(i,n-1);int tq=Q(camHist[k].Item1)+(i>=n?1+i:0);Array.Copy(BitConverter.GetBytes(tq),0,data,4+i*4,4);Array.Copy(camHist[k].Item2,0,data,0x40+i*88,88);}
   W(slot,data);WU(camHookMem+0x210,slot);TrackCloneTick();camShown=cam;WU(camHookMem+0x204,cameraSO);WU(camHookMem+0x208,world);WU(camHookMem+0x20C,playMode);WU(camHookMem+0x214,1);WU(camHookMem+0x200,1);}catch{}}
 public void SpectatorCamera(Pose peer){if(Spectating&&peer!=null&&peer.Level==level&&peer.Camera!=null)FollowPartnerCamera(peer.Camera,peer.Time-peer.CameraLag);else if(camShown!=null)StopPartnerCamera();}
 readonly List<Tuple<double,byte[]>> camHist=new List<Tuple<double,byte[]>>();double camOffset=double.NaN;
 static bool SameBytes(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
 void StopPartnerCamera(){camShown=null;camHist.Clear();camOffset=double.NaN;if(camHookMem!=0)try{WU(camHookMem+0x200,0);WU(camHookMem+0x210,0);}catch{}}
 public void UpdateSpectator(){try{if(replicaMem!=0){uint n=U(replicaMem+0x1320);if(n!=unsharedSeen){if(n>unsharedSeen)AppPaths.Log("Geteilten Animationspuffer getrennt ("+n+")");unsharedSeen=n;}}else unsharedSeen=0;}catch{}bool hub=level!=null&&level.Equals("MapMonde",StringComparison.OrdinalIgnoreCase);if(Spectating&&(hub||!PeerSameMap)){SetSpectator(false);return;}if(OutOfLives&&!PeerOutOfLives&&PeerSameMap&&!hub&&!Spectating&&remote!=0&&replicaMem!=0&&AppPaths.Get("Spectator")!="0")SetSpectator(true);KeepSpectating();}
 // A death reload can be so short that the mod never sees the loading state, so the kill hook's "kept" count counts too.
 public DateTime LastReloadAt;/* the shared Game Over leaves after a short wait instead of the 15 s after a death reload (Tim 7.11.: too long until the lab) */public bool StableForGameOver{get{try{LogKillRestores();return level!=null&&playMode!=0&&R(0x5AE2A0,1)[0]==playMode&&(DateTime.UtcNow-worldReadyAt).TotalSeconds>2&&(DateTime.UtcNow-LastReloadAt).TotalSeconds>GameOverWait;}catch{return false;}}}static double GameOverWait{get{double v;return double.TryParse(AppPaths.Get("GameOverWait"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v)?v:8;}}/* 3 s crashed once in ntdll (7.11.), 8 s: 13 of 13 Game Overs clean */
 public bool StablePlaying{get{try{LogKillRestores();return level!=null&&playMode!=0&&R(0x5AE2A0,1)[0]==playMode&&(DateTime.UtcNow-worldReadyAt).TotalSeconds>5&&(DateTime.UtcNow-LastReloadAt).TotalSeconds>StableAfterReload/* the clone being switched off far away (not in the world) no longer blocks: it kept a guest from following the host out of "trail" for good (Tim 4.11.) */;}catch{return false;}}}
 // Leaving a level shortly after a death (the kept clone is still being taken back into the world) crashed the next
 // level; after 30 s of calm it did not (1.11. experiment). Also the clone must be back in the world list.
 static int StableAfterReload{get{int v;return int.TryParse(AppPaths.Get("StableAfterReload"),out v)?v:15;}}
 // Scripts of other actors read the brain of every Donald (crash at 0x48E465 with the clone's brain 0, e.g. when lives
 // run out). The clone gets a stand-in brain in mod memory: its mind points to the own Donald's AI model and designer
 // memory (read-only use by others), intelligence and reflex are empty; StdGame NoAI (0x10000) keeps the engine from
 // running it.
 // The engine re-initialises every brain on a reload (handler 0x418A80, AI reinit): with the empty stand-in that
 // crashed at 0x4862E0. This guard skips the handler for the clone's perso (stub: compare [esi]=perso, return).
 uint brainGuardMem;static readonly byte[] BrainOriginal={0x8B,0x46,0x0C,0x85,0xC0,0x75,0x2D};
 void InstallBrainGuard(){if(brainGuardMem!=0)return;byte[] now=R(0x418AB4,7);if(BitConverter.ToString(now)!=BitConverter.ToString(BrainOriginal)){AppPaths.Log("Gehirn-Schutz: unbekannter Code");return;}
  brainGuardMem=(uint)VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)4096,0x3000,0x40).ToInt64();if(brainGuardMem<65536){brainGuardMem=0;return;}uint cell=brainGuardMem+0x100;
  var b=new List<byte>();Action<byte[]> B=x=>b.AddRange(x);Action<uint> I=x=>b.AddRange(BitConverter.GetBytes(x));
  B(new byte[]{0x3B,0x35});I(cell);B(new byte[]{0x75,0x04,0x5F,0x5E,0x5B,0xC3});B(new byte[]{0x8B,0x46,0x0C,0x85,0xC0,0x0F,0x85});I(unchecked(0x418AE8u-(brainGuardMem+(uint)b.Count+4)));B(new byte[]{0xE9});I(unchecked(0x418ABBu-(brainGuardMem+(uint)b.Count+4)));
  W(brainGuardMem,b.ToArray());var j=new List<byte>{0xE9};j.AddRange(BitConverter.GetBytes(unchecked(brainGuardMem-(0x418AB4u+5))));j.AddRange(new byte[]{0x90,0x90});CodeWrite(0x418AB4,j.ToArray());AppPaths.Log("Gehirn-Schutz installiert");}
 void SetBrainGuard(uint perso){if(brainGuardMem!=0)try{WU(brainGuardMem+0x100,perso);}catch{}}
 void ClearBrainGuard(){if(brainGuardMem==0)return;try{WU(brainGuardMem+0x100,0);CodeWrite(0x418AB4,BrainOriginal);}catch{}brainGuardMem=0;} // On every death reload the engine re-initialises all actors' components (0x4065F0(perso, mode), e.g. resetting the
 // 3D data to its initial objects table). The clone shares tables with the own Donald, so resetting it damaged Donald's
 // data; the damage only surfaced as heap crashes when Donald moved later (1.11.: deaths then walking = crash, deaths
 // then standing still = no crash). The guard skips the whole re-initialisation for the clone.
 uint initGuardMem;static readonly byte[] InitOriginal={0x53,0x8B,0x5C,0x24,0x08,0x55};
 void InstallInitGuard(){if(initGuardMem!=0)return;byte[] now=R(0x4065F0,6);if(BitConverter.ToString(now)!=BitConverter.ToString(InitOriginal)){AppPaths.Log("Init-Schutz: unbekannter Code");return;}
  initGuardMem=(uint)VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)4096,0x3000,0x40).ToInt64();if(initGuardMem<65536){initGuardMem=0;return;}uint cell=initGuardMem+0x100;
  var b=new List<byte>();Action<byte[]> B=x=>b.AddRange(x);Action<uint> I=x=>b.AddRange(BitConverter.GetBytes(x));
  B(new byte[]{0x8B,0x44,0x24,0x04,0x85,0xC0,0x74,0x0A,0x3B,0x05});I(cell);B(new byte[]{0x75,0x02,0xFF,0x05});I(cell+4);B(new byte[]{0xC3});
  // (count is incremented before the ret; layout: test eax / jz orig / cmp / jne orig / inc [cell+4] / ret)
  b.Clear();B(new byte[]{0x8B,0x44,0x24,0x04,0x85,0xC0,0x74,0x0F,0x3B,0x05});I(cell);B(new byte[]{0x75,0x07,0xFF,0x05});I(cell+4);B(new byte[]{0xC3});B(InitOriginal);B(new byte[]{0xE9});I(unchecked(0x4065F6u-(initGuardMem+(uint)b.Count+4)));
  W(initGuardMem,b.ToArray());var j=new List<byte>{0xE9};j.AddRange(BitConverter.GetBytes(unchecked(initGuardMem-(0x4065F0u+5))));j.Add(0x90);CodeWrite(0x4065F0,j.ToArray());AppPaths.Log("Init-Schutz installiert");}
 void SetInitGuard(uint perso){if(initGuardMem!=0)try{WU(initGuardMem+0x100,perso);}catch{}}
 void ClearInitGuard(){if(initGuardMem==0)return;try{WU(initGuardMem+0x100,0);CodeWrite(0x4065F0,InitOriginal);}catch{}initGuardMem=0;} bool GiveFakeBrain(){try{InstallBrainGuard();if(brainGuardMem==0)return false;SetBrainGuard(replicaPerso);uint b=replicaMem+0x3000,mind=b+0x10,intel=b+0x40,reflex=b+0xC0;fakeBrains.Add(b);W(b,new byte[0x140]);uint own=U(U(local+4)+12);if(own<65536)return false;uint ownMind=U(own);if(ownMind<65536)return false;
   WU(b,mind);WU(mind,U(ownMind));/* no intelligence/reflex: the engine's AI entry points (0x469EB0/0x469EE0) return at once, readers test them for 0 */WU(mind+4,0);WU(mind+8,0);WU(mind+12,U(ownMind+12));uint std=U(replicaPerso+4);WU(std+36,U(std+36)|0x10000u);WU(replicaPerso+12,b);return true;}catch(Exception e){AppPaths.Log("Ersatz-Gehirn: "+e.Message);return false;}} bool pendingRefill;public void RefillOnNextLevel(){pendingRefill=true;}
 void RefillIfPending(){if(!pendingRefill)return;if(Spectating&&InHub)SetSpectator(false);if(InHub){/* remembered through the hub: the next level starts with one life (Tim 6.11.) */if(OutOfLives||LastLife)reviveOne=true;OutOfLives=false;LastLife=false;return;}if(LivesAddress==0)return;pendingRefill=false;if(Spectating)SetSpectator(false);if(AppPaths.Get("Refill")=="0"){LastLife=false;OutOfLives=false;AppPaths.Log("Test: Leben nicht aufgefüllt");return;}/* Tim 6.11.: the spectator comes back in the next part with one life; a player still alive on his last life keeps it (both out goes through the lab with 3 lives) */if(OutOfLives||LastLife||reviveOne)RefillLives(1);reviveOne=false;}bool reviveOne;
}
}
