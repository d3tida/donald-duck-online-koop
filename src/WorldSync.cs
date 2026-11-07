using System;using System.Collections.Generic;using System.Text;
namespace DonaldOnline {
public sealed partial class GameBridge {
 // Shared world (Tim, 31.10.): defeated enemies, destroyed objects and collected items should be gone for both players.
 // Actors are identified across both games by "family/model/instance" (instance names are unique in a level).
 // First step: a scan of all actors with their StdGame state (ACP_Ray2 GAM_tdstStandardGame: HP +33, custom bits +36,
 // misc flags +41 with 0x01 = DesactivateAtAll) to find out which field changes when something is taken or killed.
 public struct ActorState{public uint SO;public byte HP,Misc;public uint Custom,Draw,Flags,Parent;}
 public Dictionary<string,ActorState> ScanActors(){var r=new Dictionary<string,ActorState>();/* dynamic world, inactive dynamic world, father sector (same layout as ACP_Ray2: inactive = dynamic - 0x0C, father = dynamic - 0x10) */foreach(uint root in new[]{U(0x5AEEF0),U(0x5AEEE4),U(0x5AEEE0)})ScanIn(root,0,r);return r;}
 void ScanIn(uint parent,int depth,Dictionary<string,ActorState> into){if(parent<65536||depth>3)return;uint at=U(parent+8),n=U(parent+16);for(uint i=0;i<Math.Min(n,3000u)&&at>65536;i++){
   if(U(at)==2){if(at!=local&&at!=remote)try{uint p=U(at+4),st=U(p+4);string key;if(!keyCache.TryGetValue(st,out key)){key=Name(0,U(st))+"/"+Name(1,U(st+4))+"/"+Name(2,U(st+8));keyCache[st]=key;}byte[] s=R(st+33,9);if(!into.ContainsKey(key))into[key]=new ActorState{SO=at,HP=s[0],Custom=BitConverter.ToUInt32(s,3),Misc=R(st+41,1)[0],Draw=U(at+44),Flags=U(at+48),Parent=parent};}catch{}}
   else ScanIn(at,depth+1,into);at=U(at+20);}}
 Dictionary<string,ActorState> scanBefore;readonly Dictionary<uint,string> keyCache=new Dictionary<uint,string>();
 // Shared world (4.11.): an actor that goes from active (misc 0x04) to "deactivated at all" (0x01) was collected, destroyed
 // or defeated; the engine then also clears 0x04 (seen with stars: 0x2E -> 0x2B). Both games report such events as
 // 32-bit hashes of "family/model/instance"; the other game deactivates the same actor with exactly these two flag
 // changes (verified 4.11.: the star disappears) and keeps it deactivated, also after a death reload restored it.
 string worldLevel;Dictionary<string,ActorState> worldPrev;readonly HashSet<uint> goneLocal=new HashSet<uint>(),goneRemote=new HashSet<uint>();readonly List<uint> goneSend=new List<uint>();DateTime worldScanAt;int goneCursor;
 /* not shared: system/UI actors, portals, the level end and checkpoints (each player keeps his own); of Toys, warp_boss and Lives only the collectible models below (Tim 5.11.: everything collected counts for both) */static readonly string[] WorldFamiliesSkipped={"World","GenCamera","Icon","Numbers","WarpPad","Finish","chekpoint","Bubble","Camera"};
 static bool Shareable(string key){string family=key.Split('/')[0];if(family.StartsWith("Gen",StringComparison.OrdinalIgnoreCase))return false;foreach(string f in WorldFamiliesSkipped)if(family.Equals(f,StringComparison.OrdinalIgnoreCase))return false;if((family=="Toys"||family=="warp_boss"||family=="Lives")&&!Collectible(key))return false;return true;}
 // Items the partner collected are brought onto the own Donald, so the game collects them itself with its own pickup
 // script (counters, toys, boss puzzle piece, progress then count in both games). Enemies and other objects are only
 // switched off.
 static bool Collectible(string key){string[] k=key.Split('/');if(k.Length<2)return false;string family=k[0],model=k[1];return family=="it_small_star"||family=="it_red_star"||family=="milky"||model=="RAC_AllToys"||model=="IBK_WarpBoss_Lvl"||model=="RAC_OneLife";}
 readonly Dictionary<uint,DateTime> bringSince=new Dictionary<uint,DateTime>();
 void BringToOwnDonald(ActorState a){byte[] me=R(U(local+36)+4,12);foreach(uint m in new[]{U(a.SO+32),U(a.SO+36)})if(m>65536)W(m+4,me);uint st=U(U(a.SO+4)+4);byte misc=R(st+41,1)[0];/* AlwaysActive, so its pickup check runs although it stands in a far, switched-off sector */if((misc&0x40)==0)W(st+41,new byte[]{(byte)(misc|0x40)});}
 // Pickup animations (Tim 5.11.: the partner only saw the item vanish at the end). Every tick the animation state
 // (3D data +8) of the items is read; a change is sent at once as item hash + state index, and the partner's frame
 // hook puts its copy of the item into the same state (0x4105D0, as for the second Donald), so the falling book or the
 // puzzle piece sequence plays there too. Switching it off still follows when the engine deactivates it.
 readonly Dictionary<uint,uint> itemSO=new Dictionary<uint,uint>(),itemLastState=new Dictionary<uint,uint>();readonly List<uint[]> stateSend=new List<uint[]>();readonly Dictionary<ulong,int> stateAppliedAt=new Dictionary<ulong,int>();readonly HashSet<uint> stateLogged=new HashSet<uint>();
 /* not the boss puzzle piece: it changes its state when a player comes near (portal look), and copying that showed the boss portal in the other game although nobody had the piece (Tim 5.11.) *//* only books and toys: copying a star's pickup state ran its pickup script without a collector and crashed the guest at 0x484F99 (6.11.) */static bool Animated(string key){return key.StartsWith("MagicBook/",StringComparison.Ordinal)||key.Contains("/RAC_AllToys/");}
 public uint[] ItemStatesOut{get{if(stateSend.Count==0)return null;var r=new List<uint>();for(int i=Math.Max(0,stateSend.Count-4);i<stateSend.Count;i++){r.Add(stateSend[i][0]);r.Add(stateSend[i][1]);}return r.ToArray();}}
 void ItemStateTick(Pose peer){FlushItemStates();int now=Environment.TickCount;
  foreach(var kv in itemSO){uint h=kv.Key,so=kv.Value,st;try{if(U(so)!=2)continue;st=U(U(U(so+4))+8);}catch{continue;}uint last;if(!itemLastState.TryGetValue(h,out last)){itemLastState[h]=st;continue;}if(st==last||st<65536)continue;itemLastState[h]=st;int idx;try{idx=States(so).IndexOf(st);}catch{continue;}NoteOwnStateChange(h,so,idx,now);if(idx>=0)stateSend.Add(new uint[]{h,(uint)idx,(uint)now});}
  stateSend.RemoveAll(x=>now-(int)x[2]>2000);
  if(peer==null||peer.Level!=level)return;
  try{ClaimTick(peer);}catch{}
  if(peer.ItemStates==null)return;
  for(int i=0;i+1<peer.ItemStates.Length;i+=2){uint h=peer.ItemStates[i],idx=peer.ItemStates[i+1];ulong k=((ulong)h<<32)|idx;int at;if(stateAppliedAt.TryGetValue(k,out at)&&now-at<3000)continue;uint so;if(!itemSO.TryGetValue(h,out so))continue;
   /* never force the partner's state into an item the own Donald is picking up / hitting himself (7.11.: both took the
      same teddy, the forced state hung that game), and not into a toy the partner claimed: it was switched off here */
   if(localClaimAt.ContainsKey(h)||goneLocal.Contains(h)){stateAppliedAt[k]=now;continue;}
   try{if(U(so)!=2)continue;List<uint> states=States(so);if(idx>=states.Count)continue;uint st=states[(int)idx];stateAppliedAt[k]=now;if(U(U(U(so+4))+8)==st)continue;itemLastState[h]=st;replayedAt[h]=now;QueueItemState(so,st);if(stateLogged.Add(h))AppPaths.Log("Welt: Einsammel-Animation vom Partner abgespielt ("+ItemName(h)+")");}catch{}}}
 // Item claims (Tim 7.11.: "wenn beide gleichzeitig ein Item einsammeln (Teddy) hängt sich bei einem das Spiel auf"). Every
 // item may be collected only once. A state change of a toy or book that the mod did not set, with the own Donald next
 // to it, is the own pickup (toy) or hit (book): it is sent as a claim at once, long before the engine switches the item
 // off at the end of its pickup sequence. The host decides: whoever's claim it saw first gets the item. The other game
 // switches a claimed toy off immediately (no pickup animation there; toy and progress still count for both through the
 // progress bits). If both had already started their own pickup (claims crossing on the network), neither game touches
 // its item: each own sequence plays to its end, the progress words are ORed, so nothing hangs and nothing counts twice.
 readonly Dictionary<uint,int> localClaimAt=new Dictionary<uint,int>(),replayedAt=new Dictionary<uint,int>();readonly List<uint> localClaims=new List<uint>(),guestGrants=new List<uint>();readonly HashSet<uint> partnerClaims=new HashSet<uint>();readonly Dictionary<uint,bool> claimHostFirst=new Dictionary<uint,bool>();readonly Dictionary<uint,string> itemKey=new Dictionary<uint,string>();int stateLogs;
 static bool Vanishes(string key){return key!=null&&key.Contains("/RAC_AllToys/");}
 string ItemName(uint h){string k;return itemKey.TryGetValue(h,out k)?k:h.ToString("X8");}
 float ClaimRadius{get{float r;return float.TryParse(AppPaths.Get("ClaimRadius"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out r)&&r>0?r:4f;}}
 float DistanceToOwn(uint so){try{byte[] a=R(U(local+36)+4,12),b=R(U(so+36)+4,12);float dx=BitConverter.ToSingle(a,0)-BitConverter.ToSingle(b,0),dy=BitConverter.ToSingle(a,4)-BitConverter.ToSingle(b,4),dz=BitConverter.ToSingle(a,8)-BitConverter.ToSingle(b,8);return (float)Math.Sqrt(dx*dx+dy*dy+dz*dz);}catch{return float.MaxValue;}}
 public uint[] ClaimsOut{get{if(localClaims.Count==0)return null;return localClaims.GetRange(Math.Max(0,localClaims.Count-4),Math.Min(4,localClaims.Count)).ToArray();}}
 public uint[] GrantsOut{get{if(!StarIsHost||guestGrants.Count==0)return null;return guestGrants.GetRange(Math.Max(0,guestGrants.Count-4),Math.Min(4,guestGrants.Count)).ToArray();}}
 public bool ClaimedHere(uint h){return localClaimAt.ContainsKey(h);}
 void NoteOwnStateChange(uint h,uint so,int idx,int now){int rp;bool echo=replayedAt.TryGetValue(h,out rp)&&now-rp<1500;float d=DistanceToOwn(so);string key=ItemName(h);
  if(stateLogs++<40)AppPaths.Log("Welt: Zustand "+key+" -> "+idx+(echo?" (nach Partner-Zustand)":"")+", Abstand "+d.ToString("F1",System.Globalization.CultureInfo.InvariantCulture));
  if(echo||d>ClaimRadius||localClaimAt.ContainsKey(h)||goneLocal.Contains(h))return;
  localClaimAt[h]=now;localClaims.Add(h);bool contested=partnerClaims.Contains(h);if(StarIsHost&&!claimHostFirst.ContainsKey(h))claimHostFirst[h]=true;
  AppPaths.Log("Welt: "+key+" hier "+(key.StartsWith("MagicBook/",StringComparison.Ordinal)?"getroffen":"eingesammelt")+" – Anspruch an den Partner"+(contested?" (Partner war schon dran, eigene Animation läuft zu Ende)":""));}
 void ClaimTick(Pose peer){if(peer.Claims==null)return;foreach(uint h in peer.Claims){/* an item not scanned here yet (other sector) is tried again next tick */if(partnerClaims.Contains(h)||!itemKey.ContainsKey(h))continue;partnerClaims.Add(h);string key=ItemName(h);bool mine=localClaimAt.ContainsKey(h);
   if(StarIsHost){bool hostFirst;if(!claimHostFirst.TryGetValue(h,out hostFirst)){hostFirst=false;claimHostFirst[h]=false;guestGrants.Add(h);}
    if(mine){AppPaths.Log("Welt: "+key+" – beide gleichzeitig, "+(hostFirst?"Host":"Gast")+" war zuerst; beide Animationen laufen zu Ende, zählt einmal");continue;}}
   else if(mine){bool guestFirst=peer.Grants!=null&&Array.IndexOf(peer.Grants,h)>=0;AppPaths.Log("Welt: "+key+" – beide gleichzeitig, "+(guestFirst?"Gast":"Host")+" war zuerst; beide Animationen laufen zu Ende, zählt einmal");continue;}
   uint so;if(Vanishes(key)&&itemSO.TryGetValue(h,out so)){try{if(U(so)!=2)continue;uint st=U(U(so+4)+4);byte m=R(st+41,1)[0];if((m&1)==0)W(st+41,new byte[]{(byte)((m|1)&~4)});goneLocal.Add(h);AppPaths.Log("Welt: "+key+" hat der Partner eingesammelt – hier sofort ausgeblendet");}catch{}}
   else AppPaths.Log("Welt: "+key+" – Partner war zuerst");}}
 static uint WorldHash(string key){uint h=2166136261;foreach(char c in key){h^=c;h*=16777619;}return h==0?1:h;}
 public bool WorldSyncOn{get{return AppPaths.Get("WorldSync")!="0";}}
 public int WorldEvents{get{return goneSend.Count;}}
 // Up to 8 per packet: the 4 newest and 4 more in turn, so every event reaches the partner although only the last pose counts.
 public uint[] WorldEventsOut(){if(!WorldSyncOn||goneSend.Count==0)return new uint[0];var r=new List<uint>();for(int i=goneSend.Count-1;i>=0&&r.Count<4;i--)r.Add(goneSend[i]);for(int k=0;k<4&&goneSend.Count>r.Count;k++){uint h=goneSend[goneCursor++%goneSend.Count];if(!r.Contains(h))r.Add(h);}return r.ToArray();}
 // HUD digits (Tim 6.11.: lives and stars must show the real co-op values): the number actors RAC_Lives1/10 and RAC_Stars1/10
 // keep the digit in designer variable 0 and are only updated by the game's own pickup/death scripts, so a star count taken
 // over from the host or the lives kept at 2 by the guard showed wrong values. The mod writes the digits itself.
 Dictionary<string,uint> hudDigits=new Dictionary<string,uint>();string hudLevel;DateTime hudAt,hudScanAt;
 public int ShownLives{get{if(Spectating||OutOfLives)return 0;if(LastLife)return 1;return Lives;}}
 void HudTick(){if(InHub)return;if(DateTime.UtcNow-hudAt<TimeSpan.FromMilliseconds(250))return;hudAt=DateTime.UtcNow;
  if(hudLevel!=level||(hudDigits.Count<4&&DateTime.UtcNow-hudScanAt>TimeSpan.FromSeconds(3)))HudScan();
  uint lv=LivesAddress;if(lv==0)return;int lives=ShownLives;int stars=R(lv-1,1)[0];if(lives<0)return;
  SetDigit("RAC_Lives1_I1",lives%10);SetDigit("RAC_Lives10_I1",(lives/10)%10);SetDigit("RAC_Stars1_I1",stars%10);SetDigit("RAC_stars10_I1",(stars/10)%10);}
 public string DigitInfo(){var s=new StringBuilder();HudScan();foreach(var e in hudDigits){try{uint so=e.Value;var st=States(so);uint cur=U(U(U(so+4))+8);uint brain=U(U(so+4)+12);uint dm=U(U(brain)+12);uint buf=U(dm+8);s.Append(" "+e.Key+"=dsg"+U(buf)+"/st"+st.IndexOf(cur)+"of"+st.Count);}catch{}}s.Append(" Leben "+Lives+" gezeigt "+ShownLives);return s.ToString();}
 void HudScan(){hudLevel=level;hudScanAt=DateTime.UtcNow;hudDigits.Clear();foreach(var e in ScanActors()){string k=e.Key;int s=k.LastIndexOf('/');string n=s<0?k:k.Substring(s+1);if(k.StartsWith("Numbers/",StringComparison.Ordinal)&&(n=="RAC_Lives1_I1"||n=="RAC_Lives10_I1"||n=="RAC_Stars1_I1"||n=="RAC_stars10_I1"))hudDigits[n]=e.Value.SO;}}
 // The digit shown is the number actor's animation state (10 states, not in digit order); learned from what the game itself
 // sets (digit variable + state) and kept for all levels, 7.11.: all ten learned in test runs, 9 = the one state left.
 static readonly Dictionary<int,int> digitState=new Dictionary<int,int>{{0,0},{1,2},{2,3},{3,8},{4,1},{5,4},{6,5},{7,6},{8,9},{9,7}};Dictionary<string,DateTime> digitWritten=new Dictionary<string,DateTime>();Dictionary<string,int> digitCand=new Dictionary<string,int>();
 void SetDigit(string n,int v){uint so;if(!hudDigits.TryGetValue(n,out so))return;try{if(U(so)!=2)return;uint brain=U(U(so+4)+12);if(brain<65536)return;uint dm=U(U(brain)+12);if(dm<65536)return;uint buf=U(dm+8);if(buf<65536)return;
  List<uint> states=States(so);if(states.Count!=10)return;uint cur=U(U(U(so+4))+8);int curIdx=states.IndexOf(cur);int game=(int)U(buf);DateTime w;
  if((!digitWritten.TryGetValue(n,out w)||DateTime.UtcNow-w>TimeSpan.FromSeconds(2))&&game>=0&&game<=9&&curIdx>=0&&!digitState.ContainsKey(game)&&!digitState.ContainsValue(curIdx)){int seen;if(digitCand.TryGetValue(n,out seen)&&seen==game*100+curIdx){digitState[game]=curIdx;AppPaths.Log("HUD: Ziffer "+game+" = Zustand "+curIdx);}else digitCand[n]=game*100+curIdx;}
  if(AppPaths.Get("HudSync")=="0")return;int want;if(!digitState.TryGetValue(v,out want))return;/* unknown digit state: leave the game's own display */
  if(game!=v||curIdx!=want){digitWritten[n]=DateTime.UtcNow;if(game!=v)WU(buf,(uint)v);if(curIdx!=want&&replicaMem!=0&&StablePlaying)QueueItemState(so,states[want]);}}catch{}} public void WorldTick(Pose peer){if(!WorldSyncOn||level==null||playMode==0)return;try{if(R(0x5AE2A0,1)[0]!=playMode)return;}catch{return;}
  if(worldLevel!=level){worldLevel=level;worldPrev=null;goneLocal.Clear();goneRemote.Clear();goneSend.Clear();keyCache.Clear();bringSince.Clear();starsCounted.Clear();starsKept=-1;itemSO.Clear();itemLastState.Clear();stateSend.Clear();stateAppliedAt.Clear();stateLogged.Clear();itemStateQueue.Clear();goneCursor=0;localClaimAt.Clear();replayedAt.Clear();localClaims.Clear();guestGrants.Clear();partnerClaims.Clear();claimHostFirst.Clear();itemKey.Clear();stateLogs=0;}
  try{HudTick();}catch{}
  if(peer!=null&&peer.Level==level&&peer.Gone!=null)foreach(uint h in peer.Gone)goneRemote.Add(h);/* off by default: the byte counts up step by step on screen, each step was taken for a collected star and the host added them up to 100+ (extra lives, Tim 4.11.) */if(AppPaths.Get("ProgressSync")!="0")try{ProgressTick(peer);}catch{}if(AppPaths.Get("TimerSync")!="0")try{TimerTick(peer);}catch{}try{KeepStarsOverDeath();}catch{}if(AppPaths.Get("StarSync")=="1")try{StarTick(peer);}catch{}else if(!StarIsHost&&!InHub&&peer!=null&&peer.Level==level&&peer.StarsEarned>=0&&peer.StarsEarned<=255){/* the host's star count is the shared one (Tim 6.11.: both must show the same); it is only taken over when it is higher, or when it stayed the same for 1.5 s (the host counts a star the guest took a moment later; a counter that only displays steps is never read here) */try{uint lives=LivesAddress;if(lives!=0){int v=R(lives-1,1)[0];if(peer.StarsEarned!=hostStarsSeen){hostStarsSeen=peer.StarsEarned;hostStarsAt=DateTime.UtcNow;}if(v!=peer.StarsEarned&&(peer.StarsEarned>v||DateTime.UtcNow-hostStarsAt>TimeSpan.FromSeconds(1.5))){W(lives-1,new[]{(byte)peer.StarsEarned});if(starSyncLevel!=level){starSyncLevel=level;AppPaths.Log("Sterne vom Host übernommen: "+v+" -> "+peer.StarsEarned);}}}}catch{}}
  if(AppPaths.Get("ItemAnimations")!="0")try{ItemStateTick(peer);}catch{}
  if(DateTime.UtcNow<worldScanAt)return;/* 100 ms (Tim 6.11.: destroyed things appeared late at the partner) */worldScanAt=DateTime.UtcNow.AddMilliseconds(100);Dictionary<string,ActorState> now;try{now=ScanActors();}catch{return;}
  foreach(var e in now){if(!Shareable(e.Key))continue;var a=e.Value;uint h=WorldHash(e.Key);ActorState b;if(Animated(e.Key)){itemSO[h]=a.SO;itemKey[h]=e.Key;}
   if(worldPrev!=null&&worldPrev.TryGetValue(e.Key,out b)&&(b.Misc&5)==4&&(a.Misc&1)!=0&&goneLocal.Add(h)){goneSend.Add(h);AppPaths.Log("Welt: "+e.Key+" weg (hier)");}
   /* also the own ones: a death reload brings collected and destroyed objects back in the dead player's game, they stay gone (Tim 6.11.) */else if((a.Misc&1)==0&&(goneRemote.Contains(h)||goneLocal.Contains(h))){try{uint st=U(U(a.SO+4)+4);byte m=R(st+41,1)[0];int claimAt;if((m&1)!=0){}
    /* the own pickup of this item is still running (both took it at once): switching it off now could leave the own Donald stuck in it; the engine switches it off itself at the end (30 s at most) */
    else if(localClaimAt.TryGetValue(h,out claimAt)&&Environment.TickCount-claimAt<30000){}
    /* stars, lives and the puzzle piece have no claim (no state change on pickup): when the partner's "gone" comes while the
       own Donald touches the item, it is the own pickup at the same moment; it is left to the game (counts once, its own
       sequence ends normally) and only switched off when the Donald has moved away without taking it */
    else if(!Vanishes(e.Key)&&Collectible(e.Key)&&!goneLocal.Contains(h)&&DistanceToOwn(a.SO)<(e.Key.Contains("/IBK_WarpBoss_Lvl/")?2.5f:1.5f)){}
    else if(Collectible(e.Key)&&AppPaths.Get("BringItems")=="1"){DateTime since;if(!bringSince.TryGetValue(h,out since)){since=DateTime.UtcNow;bringSince[h]=since;goneLocal.Add(h);AppPaths.Log("Welt: "+e.Key+" vom Partner eingesammelt – wird hier mit eingesammelt");}
     /* after 4 s (not collected, or brought back by a death reload) it is only switched off */if(DateTime.UtcNow-since<TimeSpan.FromSeconds(4))BringToOwnDonald(a);else{W(st+41,new byte[]{(byte)((m|1)&~4)});if(DateTime.UtcNow-since<TimeSpan.FromSeconds(5))AppPaths.Log("Welt: "+e.Key+" ließ sich hier nicht einsammeln – nur ausgeblendet");}}
    else{W(st+41,new byte[]{(byte)((m|1)&~4)});goneLocal.Add(h);AppPaths.Log("Welt: "+e.Key+" weg (vom Partner)");/* bringing the item to the own Donald did not make the game collect it (5.11.); a small star the partner took counts here directly: +1 on the star counter (one event per star, not the on-screen count) */if(e.Key.StartsWith("it_small_star/",StringComparison.Ordinal)&&starsCounted.Add(h))AddStars(1);}}catch{}}}
  worldPrev=now;}
 // Diagnosis: after every map change the engine heap (module 10, block 0) is checked on each capture for 30 s; the first
 // broken free list is logged together with the last mod action, to find who corrupts it.
 public string LastAction="";DateTime heapWatchUntil;bool heapBroken;
 public void WatchHeap(){heapWatchUntil=DateTime.UtcNow.AddSeconds(30);heapBroken=false;}
 public int GamePid{get{return process.Id;}}
 // Test aid: SO, perso components and the start of the 3D data of an actor, to recognise structures in debugger dumps.
 public uint AnimArrayField(uint so){try{uint p3d=U(U(so+4));return p3d<65536?0:p3d+0x78;}catch{return 0;}}
 // Test aid: allocation state (no-alloc byte 0x565E60, heap channel bytes) and free space of module 10 block 0.
 public string AllocInfo(){try{var s=new StringBuilder("noalloc="+R(0x565E60,1)[0]+" ch="+R(0x4A6294,1)[0]+"/"+R(0x5C4088,1)[0]);uint h=U(0x5B8C00+40);uint at=U(h+8);long total=0,max=0;int n=0;while(at>65536&&at!=0xFFFFFFFF&&n<20000){uint size=U(at)*4;total+=size;if(size>max)max=size;at=U(at+4);n++;}s.Append(" frei "+total/1024+" KB in "+n+" Bloecken, groesster "+max/1024+" KB, top "+((long)U(h+4)-(long)U(h+12))/1024+" KB");return s.ToString();}catch(Exception e){return "? "+e.Message;}}
 // Test aid: pointers into the engine heap that the clone shares with the own Donald (per component, first 0x120 bytes).
 public string SharedPointers(uint clone,uint own){var s=new StringBuilder();try{uint pc=U(clone+4),po=U(own+4);for(uint c=0;c<14;c++){uint a=U(pc+c*4),b=U(po+c*4);if(a<65536||b<65536||a==b)continue;byte[] x=R(a,0x120),y=R(b,0x120);for(int k=0;k<0x120;k+=4){uint v=BitConverter.ToUInt32(x,k);if(v==BitConverter.ToUInt32(y,k)&&v>=0x03000000&&v<0x04000000)s.Append(" k"+c+"+"+k.ToString("X")+"="+v.ToString("X"));}}}catch(Exception e){s.Append(" ? "+e.Message);}return s.ToString();}
 // Test aid: ring buffer of the last 4096 engine frees (0x45F070(&ptr)): [ring] = count, entries at ring+16: ptr, caller.
 public uint FreeRing;
 public uint InstallFreeLog(){if(FreeRing!=0)return FreeRing;byte[] now=R(0x45F070,5);if(BitConverter.ToString(now)!="56-8B-74-24-08")return 0;uint mem=(uint)VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)0x10000,0x3000,0x40).ToInt64();if(mem<65536)return 0;uint ring=mem+0x1000;var b=new List<byte>();Action<byte[]> B=x=>b.AddRange(x);Action<uint> I=x=>b.AddRange(BitConverter.GetBytes(x));
  B(new byte[]{0x50,0x51,0x52,0x8B,0x44,0x24,0x10,0x8B,0x00,0x8B,0x0D});I(ring);B(new byte[]{0x81,0xE1});I(0xFFF);B(new byte[]{0x89,0x04,0xCD});I(ring+16);B(new byte[]{0x8B,0x54,0x24,0x0C,0x89,0x14,0xCD});I(ring+20);B(new byte[]{0xFF,0x05});I(ring);B(new byte[]{0x5A,0x59,0x58,0x56,0x8B,0x74,0x24,0x08,0xE9});I(unchecked(0x45F075u-(mem+(uint)b.Count+4)));
  W(mem,b.ToArray());var j=new List<byte>{0xE9};j.AddRange(BitConverter.GetBytes(unchecked(mem-(0x45F070u+5))));CodeWrite(0x45F070,j.ToArray());FreeRing=ring;return ring;}
 public string FieldHeaders(){var s=new StringBuilder();try{uint p=U(U(local+4));foreach(uint o in new uint[]{0x78,0x9C,0xA0,0xBC}){uint v=U(p+o);s.Append(" +"+o.ToString("X")+"="+v.ToString("X")+(v>65536?" hdr "+U(v-4).ToString("X"):""));}}catch(Exception e){s.Append(e.Message);}return s.ToString();}
 // Experiment (shared world): permanently deactivate an actor the way a collected star looks (misc flag 0x01).
 public string Deactivate(string key){var all=ScanActors();ActorState a;if(!all.TryGetValue(key,out a))return "nicht gefunden";uint st=U(U(a.SO+4)+4);byte m=R(st+41,1)[0];byte n=(byte)((m|1)&~4);W(st+41,new byte[]{n});return "misc "+m.ToString("X")+" -> "+n.ToString("X");}
 public string GlobalDsgDump(){uint at=LivesAddress;if(at==0)return "?";byte[] b=R(at-0x1C4,0x400);var s=new StringBuilder();for(int i=0;i<b.Length;i+=4)s.Append(BitConverter.ToInt32(b,i)+(i%64==60?"\n":" "));return s.ToString();}
 // Star counter (4.11.): a byte in the global DsgVar buffer right before the lives (+0x1C3; +3 after collecting three
 // small stars). The host's counter is the shared one: the guest counts what it collected itself in this level and sends
 // that total, the host adds the difference and sends its counter value, which the guest takes over. (Adding each other's
 // gains on both sides counted again after death reloads, where stars come back and the counter changes: 9 vs 7.)
 int starLast=-1,starsEarned,partnerStarsApplied=-1;string starLevel;
 /* 5.11.: the host sends its star count; the guest takes it over once per level (save games start with different counts), afterwards only single star events count */
 public int StarsOut{get{if(AppPaths.Get("StarSync")=="1")return StarIsHost?starLast:starsEarned;if(!StarIsHost)return -1;try{uint lives=LivesAddress;return lives==0?-1:R(lives-1,1)[0];}catch{return -1;}}}
 string starSyncLevel;int hostStarsSeen=-1;DateTime hostStarsAt;public bool StarIsHost;
 void StarTick(Pose peer){uint lives=LivesAddress;if(lives==0)return;uint at=lives-1;if(starLevel!=level){starLevel=level;starLast=-1;starsEarned=0;partnerStarsApplied=-1;}int v=R(at,1)[0];
  if(!StarIsHost&&starLast>=0&&v>starLast)starsEarned+=v-starLast;starLast=v;
  if(peer==null||peer.Level!=level)return;
  if(StarIsHost){if(partnerStarsApplied<0||peer.StarsEarned<partnerStarsApplied){partnerStarsApplied=peer.StarsEarned;return;}if(peer.StarsEarned>partnerStarsApplied){int add=peer.StarsEarned-partnerStarsApplied;int nv=Math.Min(255,v+add);W(at,new[]{(byte)nv});starLast=nv;partnerStarsApplied=peer.StarsEarned;AppPaths.Log("Welt: +"+add+" Stern(e) vom Gast, jetzt "+nv);}}
  /* guest: shows the host value plus its own gains the host has not counted yet (the host echoes how much of the guest total it added), so the shown number never drops back for a moment */
  else if(peer.StarsEarned>=0&&peer.StarsEarned<=255){int pending=peer.StarsAck>=0?Math.Max(0,starsEarned-peer.StarsAck):0;int target=Math.Min(255,peer.StarsEarned+pending);if(target!=v){W(at,new[]{(byte)target});starLast=target;}}}
 public int StarsAckOut{get{return StarIsHost?partnerStarsApplied:-1;}}
 public string AllActors(){var s=new StringBuilder();foreach(var e in ScanActors()){string pos="?";try{byte[] q=R(U(e.Value.SO+36)+4,12);pos=BitConverter.ToSingle(q,0).ToString("F1")+" "+BitConverter.ToSingle(q,4).ToString("F1")+" "+BitConverter.ToSingle(q,8).ToString("F1");}catch{}s.Append("\n  "+e.Key+" @ "+pos+" misc "+e.Value.Misc.ToString("X"));}return s.ToString();}
 // Test aid: move the own Donald by writing the position into his local and global matrix (and the dynamics matrices).
 public string MoveOwn(float x,float y,float z){var pos=new byte[12];Array.Copy(BitConverter.GetBytes(x),0,pos,0,4);Array.Copy(BitConverter.GetBytes(y),0,pos,4,4);Array.Copy(BitConverter.GetBytes(z),0,pos,8,4);uint lm=U(local+32),gm=U(local+36);W(lm+4,pos);W(gm+4,pos);string s="lokal+global";try{uint dyn=U(U(U(local+4)+8));if(dyn>65536){W(dyn+0x78+4,pos);W(dyn+0xD0+4,pos);s+="+dynamik";}}catch{}return s;}
 readonly HashSet<uint> starsCounted=new HashSet<uint>();/* once per star and level, also when a death reload brings it back */
 // Level progress (5.11., found by diffing the global DsgVar buffer while Tim collected the three toys and the boss
 // puzzle piece in "trail"): bit fields at +0x08/+0x0C (toys 0x10000/0x20000/0x40000, piece 0x1000 and +0x0B=1), +0x18
 // and per-toy bytes at +0x1BC. Both games send these words; each ORs the partner's bits into its own, so progress
 // gained by either player counts for both (union, bits are only ever added).
 static readonly uint[] ProgressOffsets={0x08,0x0C,0x18,0x1BC};
 public uint[] ProgressOut{get{try{if(InHub)return null;uint lives=LivesAddress;if(lives==0)return null;uint b=lives-0x1C4;var r=new uint[ProgressOffsets.Length];for(int i=0;i<r.Length;i++)r[i]=U(b+ProgressOffsets[i]);return r;}catch{return null;}}}
 void ProgressTick(Pose peer){if(InHub||peer==null||peer.Level!=level||peer.Progress==null||peer.Progress.Length!=ProgressOffsets.Length)return;uint lives=LivesAddress;if(lives==0)return;uint b=lives-0x1C4;for(int i=0;i<ProgressOffsets.Length;i++){uint mine=U(b+ProgressOffsets[i]),v=mine|peer.Progress[i];if(v!=mine){WU(b+ProgressOffsets[i],v);AppPaths.Log("Fortschritt vom Partner übernommen: +"+ProgressOffsets[i].ToString("X")+" "+mine.ToString("X8")+" -> "+v.ToString("X8"));}}}
 // Test aid: DsgVar buffers (first 0x80 bytes) of all magic books, to find what the hit changes (timer, 5.11.).
 public string BookDsg(){var s=new StringBuilder();foreach(var e in ScanActors()){if(!e.Key.StartsWith("MagicBook/",StringComparison.Ordinal))continue;try{uint brain=U(U(e.Value.SO+4)+12);if(brain<65536){s.Append("\n  "+e.Key+" ohne Gehirn");continue;}uint dm=U(U(brain)+12),buf=dm<65536?0:U(dm+8);s.Append("\n  "+e.Key+" misc "+e.Value.Misc.ToString("X")+" state "+U(U(U(e.Value.SO+4))+8).ToString("X")+" :");if(buf>65536){byte[] b=R(buf,0x80);for(int i=0;i<0x80;i+=4)s.Append(" "+BitConverter.ToUInt32(b,i).ToString("X"));}}catch{}}return s.ToString();}
 // Book timer (6.11., read in "trail": hitting a magic book sets global DsgVar +0x94 = 1 (running), +0x98 = seconds left
 // (4,3,2,1) and +0x1B8 = 0x10000 (shown); all three go back to 0 at the end). A running timer is sent and the partner,
 // whose own timer is not running, shows the same values; when it ends there it is set back to 0.
 static readonly uint[] TimerOffsets={0x94,0x98,0x1B8};bool timerMirrored;
 public uint[] TimerOut{get{try{if(InHub||timerMirrored)return null;uint lives=LivesAddress;if(lives==0)return null;uint b=lives-0x1C4;if(U(b+0x94)==0)return null;var r=new uint[3];for(int i=0;i<3;i++)r[i]=U(b+TimerOffsets[i]);return r;}catch{return null;}}}
 void TimerTick(Pose peer){if(InHub)return;uint lives=LivesAddress;if(lives==0)return;uint b=lives-0x1C4;bool peerRunning=peer!=null&&peer.Level==level&&peer.Timer!=null&&peer.Timer.Length==3;
  if(peerRunning){if(!timerMirrored&&U(b+0x94)!=0)return;if(!timerMirrored)AppPaths.Log("Welt: Buch-Timer vom Partner läuft auch hier");timerMirrored=true;for(int i=0;i<3;i++)if(U(b+TimerOffsets[i])!=peer.Timer[i])WU(b+TimerOffsets[i],peer.Timer[i]);}
  else if(timerMirrored){timerMirrored=false;foreach(uint o in TimerOffsets)WU(b+o,0);AppPaths.Log("Welt: Buch-Timer des Partners beendet");}}
 // Death reload: the star counter falls back with the level; the count from before the death is restored, because the
 // stars collected since then stay gone (see above) and could not be collected again.
 int starsKept=-1;DateTime starsReloadSeen;
 void KeepStarsOverDeath(){if(InHub)return;uint lives=LivesAddress;if(lives==0)return;int v=R(lives-1,1)[0];if(LastReloadAt!=starsReloadSeen){starsReloadSeen=LastReloadAt;if(starsKept>v){W(lives-1,new[]{(byte)starsKept});AppPaths.Log("Sterne nach dem Neuladen wiederhergestellt: "+v+" -> "+starsKept);return;}}starsKept=v;}
 // Test aids (6.11.): HUD number actors and a fast camera trace.
 public string HudDsg(){var s=new StringBuilder();foreach(var e in ScanActors()){if(!e.Key.StartsWith("Numbers/",StringComparison.Ordinal)&&!e.Key.StartsWith("Lives/",StringComparison.Ordinal))continue;try{uint brain=U(U(e.Value.SO+4)+12);if(brain<65536)continue;uint dm=U(U(brain)+12),buf=dm<65536?0:U(dm+8);s.Append("\n  "+e.Key+" :");if(buf>65536){byte[] b=R(buf,0x40);for(int i=0;i<0x40;i+=4)s.Append(" "+BitConverter.ToUInt32(b,i).ToString("X"));}}catch{}}uint lv=LivesAddress;s.Append("\n  echte Leben "+(lv==0?"?":U(lv).ToString())+" Sterne "+(lv==0?"?":R(lv-1,1)[0].ToString()));return s.ToString();}
 public float[] CameraPos(){try{CameraMatrix();if(cameraSO==0)return null;byte[] g=R(U(cameraSO+36)+4,12);return new[]{BitConverter.ToSingle(g,0),BitConverter.ToSingle(g,4),BitConverter.ToSingle(g,8)};}catch{return null;}}
 void AddStars(int n){uint lives=LivesAddress;if(lives==0)return;uint at=lives-1;int v=R(at,1)[0];W(at,new[]{(byte)Math.Min(255,v+n)});}
 public uint ReadU(uint at){try{return U(at);}catch{return 0;}}
 public uint OwnAnimArray(){try{return local==0?0:U(U(U(local+4))+0x78);}catch{return 0;}}
 public string DescribeActor(uint so){try{var s=new StringBuilder("SO "+so.ToString("X")+" perso");uint p=U(so+4);s.Append(" "+p.ToString("X")+" comps");for(uint k=0;k<56;k+=4)s.Append(" "+U(p+k).ToString("X"));uint d=U(p);s.Append(" 3d");for(uint k=0;k<0xB0;k+=4)s.Append(" "+U(d+k).ToString("X"));return s.ToString();}catch(Exception e){return "? "+e.Message;}}
 public uint[] HeapNodes(){var r=new List<uint>();try{uint h=U(0x5B8C00+40);uint at=U(h+8);for(int i=0;i<3&&at>65536&&at!=0xFFFFFFFF;i++){r.Add(at);at=U(at+4);}}catch{}return r.ToArray();}
 void CheckHeap(){if(heapBroken||DateTime.UtcNow>heapWatchUntil||AppPaths.Get("HeapWatch")!="1")return;try{uint h=U(0x5B8C00+40);uint end=U(h+4),free=U(h+8),top=U(h+12);if(free==0xFFFFFFFF)return;uint at=free;int n=0;while(at!=0&&n<20000){if(at<65536||at>end){heapBroken=true;AppPaths.Log("HEAP KAPUTT nach Aktion '"+LastAction+"' – Knoten "+n+" Zeiger "+at.ToString("X8"));return;}uint size=U(at);if(size==0||size>0x4000000){heapBroken=true;AppPaths.Log("HEAP KAPUTT nach Aktion '"+LastAction+"' – Knoten "+n+" Größe "+size.ToString("X"));return;}at=U(at+4);n++;}}catch{}}
 // Test aid: logs what changed since the previous call.
 public string ScanDiff(){var now=ScanActors();var s=new StringBuilder("Akteure "+now.Count);if(scanBefore!=null){foreach(var e in scanBefore){ActorState b;if(!now.TryGetValue(e.Key,out b)){s.Append("\n  weg: "+e.Key);continue;}var a=e.Value;if(a.HP!=b.HP||a.Misc!=b.Misc||a.Custom!=b.Custom||a.Draw!=b.Draw||a.Flags!=b.Flags||a.Parent!=b.Parent)s.Append("\n  "+((a.Misc&1)==0&&(b.Misc&1)!=0?"DEAKTIVIERT ":"")+e.Key+" hp "+a.HP+">"+b.HP+" misc "+a.Misc.ToString("X")+">"+b.Misc.ToString("X")+" cb "+a.Custom.ToString("X")+">"+b.Custom.ToString("X")+" draw "+a.Draw.ToString("X")+">"+b.Draw.ToString("X")+" fl "+a.Flags.ToString("X")+">"+b.Flags.ToString("X")+(a.Parent!=b.Parent?" parent "+a.Parent.ToString("X")+">"+b.Parent.ToString("X"):""));}foreach(var e in now)if(!scanBefore.ContainsKey(e.Key))s.Append("\n  neu: "+e.Key);}scanBefore=now;return s.ToString();}
}
}
