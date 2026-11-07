using System;using System.Collections.Generic;
namespace DonaldOnline {
public sealed partial class GameBridge {
 // After a death or checkpoint the engine reloads the level and takes the clone out of the world, but its memory stays
 // in the level's object pool until the next map change. Creating a new clone after every death filled that pool and
 // crashed the game (allocation returned NULL at Donald.exe+0x5F055 / allocator errors at 0x438A8, 0x43ACD).
 // So the old clone is remembered and hung back into the world if it is still intact and detached; at most two new
 // clones are made per map as a last resort.
 uint orphan,orphanPerso;string orphanMap,spawnMap;int spawnsThisMap;bool spawnLimitLogged;
 // Crash at Donald.exe+0x18F12 (reproduced 31.10. in "brush", both games): when the engine re-initialises the actors
 // (handler 0x418E80 of the 3D-data component, modes 3/4), it fetches each actor's saved start matrix with 0x44AE40
 // and copies 88 bytes from it. A clone made at runtime has no saved copy, the lookup returns NULL and the copy
 // crashes. Patched in place (same 18 bytes): pop the two arguments, and copy only when the lookup found something.
 static readonly byte[] ReinitOriginal={0x8B,0x4D,0x00,0x8B,0xF0,0x83,0xC4,0x08,0x8D,0x79,0x18,0xB9,0x16,0x00,0x00,0x00,0xF3,0xA5};
 static readonly byte[] ReinitGuarded={0x59,0x59,0x85,0xC0,0x74,0x0C,0x96,0x8B,0x7D,0x00,0x83,0xC7,0x18,0x6A,0x16,0x59,0xF3,0xA5};
 void InstallReinitGuard(){byte[] now=R(0x418F02,18);if(BitConverter.ToString(now)==BitConverter.ToString(ReinitGuarded))return;if(BitConverter.ToString(now)!=BitConverter.ToString(ReinitOriginal)){AppPaths.Log("Reinit-Schutz: unbekannter Code, nicht installiert");return;}CodeWrite(0x418F02,ReinitGuarded);AppPaths.Log("Reinit-Schutz installiert");}
 // Diagnosis of the "always" object slots used by the clone function 0x40C4E0 (count 0x4A8EC8, slots 0x4A8ED8 stride
 // 0x48, component storage 0x4A8EDC with 14 entries per slot, used flags 0x4A8EE0).
 public string AlwaysInfo(){try{uint n=U(0x4A8EC8),slots=U(0x4A8ED8),store=U(0x4A8EDC),used=U(0x4A8EE0);var s=new System.Text.StringBuilder("Always n="+n+" slots="+slots.ToString("X")+" store="+store.ToString("X")+" used="+used.ToString("X"));
  for(uint i=0;i<Math.Min(n,8u);i++){s.Append(" ["+i+": u="+U(used+i*4).ToString("X")+" s14="+U(slots+i*0x48+0x14).ToString("X")+" c=");for(uint k=0;k<14;k++){uint p=U(store+(i*14+k)*4);if(p!=0)s.Append(k+":"+p.ToString("X")+(Readable(p)?"":"!")+",");}s.Append("]");}return s.ToString();}catch(Exception e){return "Always ? "+e.Message;}}
 // Death crashes (0x5F055 out of memory, 0x438A8 heap), reproduced 31.10. in "brush": the clone function 0x40C4E0
 // keeps per-slot component storage (0x4A8EDC, 14 entries per slot) and allocates the missing ones (Dynam 2,
 // CollSet 5, SectInfo 8) in the running level. A respawn resets that level memory, but the storage pointers stay,
 // so the next always-object in that slot (the new clone or an engine effect) writes into memory the engine hands
 // out again. The entries our clone allocated are therefore remembered and cleared as soon as the level reloads.
 // Clones per map; settings.txt MaxClones overrides (tests). Even with the storage fix a second clone after a respawn
 // ran the level memory full within seconds (0x5F055/0x43B48, 3 test runs); without it many deaths stayed stable.
 int MaxClonesPerMap{get{int v;return int.TryParse(AppPaths.Get("MaxClones"),out v)&&v>0?v:6;}}
 uint[] storeBefore;uint storeBase;readonly List<KeyValuePair<uint,uint>> cloneStorage=new List<KeyValuePair<uint,uint>>();
 void NoteStorageBefore(){try{uint n=U(0x4A8EC8);storeBase=U(0x4A8EDC);if(n==0||n>256||storeBase<65536){storeBefore=null;return;}byte[] raw=R(storeBase,(int)n*14*4);storeBefore=new uint[n*14];for(int i=0;i<storeBefore.Length;i++)storeBefore[i]=BitConverter.ToUInt32(raw,i*4);}catch{storeBefore=null;}}
 void NoteStorageAfter(){try{if(storeBefore==null||U(0x4A8EDC)!=storeBase)return;byte[] raw=R(storeBase,storeBefore.Length*4);for(int i=0;i<storeBefore.Length;i++){uint now=BitConverter.ToUInt32(raw,i*4);if(storeBefore[i]==0&&now!=0)cloneStorage.Add(new KeyValuePair<uint,uint>(storeBase+(uint)i*4,now));}if(cloneStorage.Count>0)AppPaths.Log("Klon hat "+cloneStorage.Count+" Speicherteile neu angelegt");}catch{}storeBefore=null;}
 void ForgetCloneStorage(){if(cloneStorage.Count==0)return;int cleared=0;try{WithGameThreadsPaused(()=>{foreach(var e in cloneStorage)if(U(e.Key)==e.Value){WU(e.Key,0);cleared++;}});}catch{}AppPaths.Log("Neuladen: "+cleared+" Speicherteile des Klons verworfen");cloneStorage.Clear();}
 // Real cause of the death crashes: the replica runs without its Dynam/Brain/CollSet pointers (Perso+8/+12/+20 set to 0
 // so the engine does not move it). When the engine kills always-objects on a reload (0x40C8E0), it releases Dynam
 // only through Perso+8, so the clone's dynamics data was never freed, and each new clone allocated it again until the
 // level memory ran out (0x5F055) or the heap broke (0x438A8/0x43B48). This hook puts the three pointers back right
 // before the engine kills our clone, so the engine frees everything as for its own objects.
 // Stub data at +0x400: perso, dynam, brain, collset; +0x110 count of restores.
 uint killHookMem;static readonly byte[] KillOriginal={0x8B,0x44,0x24,0x04,0x8B,0x15,0xD8,0x8E,0x4A,0x00};
 void InstallKillHook(){byte[] now=R(0x40C8E0,10);if(BitConverter.ToString(now)!=BitConverter.ToString(KillOriginal)){AppPaths.Log("Entfern-Haken: unbekannter Code, nicht installiert");return;}
  killHookMem=(uint)VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)4096,0x3000,0x40).ToInt64();if(killHookMem<65536){killHookMem=0;return;}uint data=killHookMem+0x400;
  var b=new List<byte>();Action<byte[]> B=x=>b.AddRange(x);Action<uint> I=x=>b.AddRange(BitConverter.GetBytes(x));
  var at=new Dictionary<string,int>();var fix=new List<KeyValuePair<int,string>>();Action<string> L=n=>at[n]=b.Count;Action<byte,string> S=(op,n)=>{if(op==0xEB)b.Add(0xE9);else{b.Add(0x0F);b.Add((byte)(op+0x10));}fix.Add(new KeyValuePair<int,string>(b.Count,n));I(0);};
  // pushad; eax = slot = SO of the always object; edx = SO; eax = perso; ours?
  B(new byte[]{0x60,0x8B,0x44,0x24,0x24,0x2D});I(0x10000);B(new byte[]{0x6B,0xC0,0x48,0x03,0x05});I(0x4A8ED8);B(new byte[]{0x8B,0xD0,0x8B,0x40,0x04,0x85,0xC0});S(0x74,"orig");
  B(new byte[]{0x3B,0x05});I(data);S(0x75,"orig");
  // On a map change the clone is always left alone (every kill of a clone that lived through reloads broke the heap).
  // map changed since arming? ([data+0x1C] = address of the ChangeLevel counter, [data+0x14] = its value when armed)
  B(new byte[]{0x8B,0x0D});I(data+0x1C);B(new byte[]{0x85,0xC9});S(0x74,"changed");B(new byte[]{0x8B,0x09,0x3B,0x0D});I(data+0x14);S(0x75,"changed");
  // (no map change) only the kill-all loop of a reload is skipped; a deliberate destroy (0x406A40) kills it normally
  B(new byte[]{0x81,0x7C,0x24,0x20});I(0x40CA9F);S(0x75,"killplain");
  // death/checkpoint reload: keep the clone alive, nothing is freed or allocated
  B(new byte[]{0xFF,0x05});I(data+0x18);B(new byte[]{0x61,0xC3});
  L("changed");B(new byte[]{0x83,0x3D});I(data+0x20);B(new byte[]{0});S(0x74,"restore");B(new byte[]{0x83,0x3D});I(data+0x20);B(new byte[]{3});S(0x74,"killplain");B(new byte[]{0x83,0x3D});I(data+0x20);B(new byte[]{1});S(0x74,"unlinked");
  // map change: unlink the clone from its parent list (the new level must not find it), do not kill it, disarm
  B(new byte[]{0x8B,0x4A,0x1C,0x85,0xC9});S(0x74,"unlinked");B(new byte[]{0x56,0x57,0x8B,0x72,0x14,0x8B,0x7A,0x18,0x85,0xFF});S(0x74,"nprev");B(new byte[]{0x89,0x77,0x14});S(0xEB,"pdone");L("nprev");B(new byte[]{0x89,0x71,0x08});L("pdone");
  B(new byte[]{0x85,0xF6});S(0x74,"nnext");B(new byte[]{0x89,0x7E,0x18});S(0xEB,"ndone");L("nnext");B(new byte[]{0x89,0x79,0x0C});L("ndone");
  B(new byte[]{0xFF,0x49,0x10,0xC7,0x42,0x14,0,0,0,0,0xC7,0x42,0x18,0,0,0,0,0xC7,0x42,0x1C,0,0,0,0,0x5F,0x5E});L("unlinked");
  B(new byte[]{0xC7,0x05});I(data);I(0);B(new byte[]{0xFF,0x05});I(data+0x24);B(new byte[]{0x61,0xC3});
  // default on map changes: the engine kills the clone as it is (components stay switched off, so nothing it may
  // already have released on a death reload is freed twice); this also takes it out of all engine lists
  L("killplain");B(new byte[]{0xC7,0x05});I(data);I(0);B(new byte[]{0xFF,0x05});I(data+0x24);S(0xEB,"orig");
  // KeepCloneOnMapChange=0: give the components back and let the engine kill it
  L("restore");B(new byte[]{0x8B,0x0D});I(data+4);B(new byte[]{0x89,0x48,0x08,0x8B,0x0D});I(data+8);B(new byte[]{0x89,0x48,0x0C,0x8B,0x0D});I(data+12);B(new byte[]{0x89,0x48,0x14,0xC7,0x05});I(data);I(0);B(new byte[]{0xFF,0x05});I(data+0x10);
  L("orig");foreach(var p in fix){byte[] rel=BitConverter.GetBytes(at[p.Value]-(p.Key+4));for(int k=0;k<4;k++)b[p.Key+k]=rel[k];}
  B(new byte[]{0x61});B(KillOriginal);B(new byte[]{0xE9});I(unchecked(0x40C8EAu-(killHookMem+(uint)b.Count+4)));
  W(killHookMem,b.ToArray());var jump=new List<byte>{0xE9};jump.AddRange(BitConverter.GetBytes(unchecked(killHookMem-(0x40C8E0u+5))));jump.AddRange(new byte[]{0x90,0x90,0x90,0x90,0x90});CodeWrite(0x40C8E0,jump.ToArray());AppPaths.Log("Entfern-Haken installiert");}
 // Called when the replica takes the components away (armed) or gives them back itself (disarmed).
 void ArmKillHook(uint perso,uint[] components){if(killHookMem==0)return;try{if(perso==0){WU(killHookMem+0x400,0);return;}WU(killHookMem+0x404,components[0]);WU(killHookMem+0x408,components[1]);WU(killHookMem+0x40C,components[2]);WU(killHookMem+0x414,MapChangeCount);WU(killHookMem+0x41C,levelHookMem!=0&&!RenewClone?levelHookMem+0x208:0);WU(killHookMem+0x420,RenewClone?(AppPaths.Get("RenewKill")=="plain"?3u:0u):MapChangeMode);WU(killHookMem+0x400,perso);}catch{}}
 // Default (tested 31.10., 3 cycles brush<->MapMonde, ~18 deaths, no crash): the engine never kills our clone, neither on
 // a death reload nor on a map change. Killing it - with or without its components - corrupted the heap (double free /
 // stale lists) and a new clone after a reload crashed by the 3rd time. CloneMode=renew / KeepCloneOnMapChange=0 for tests.
 // MapChangeKill: keep (default, 1) = left behind untouched (8 map changes in a row fine), plain (3) = engine kills it
 // unchanged (crashed while loading after Game Over), unlink (2) and restore (0) crashed too.
 static uint MapChangeMode{get{switch(AppPaths.Get("MapChangeKill")){case "plain":return 3;case "unlink":return 2;case "restore":return 0;default:return 1;}}}
 static bool RenewClone{get{return AppPaths.Get("CloneMode")=="renew";}}static bool KeepCloneOnMapChange{get{return !RenewClone&&AppPaths.Get("KeepCloneOnMapChange")!="0";}}
 uint lastKillRestores;void LogKillRestores(){if(killHookMem==0)return;try{uint n=U(killHookMem+0x410);if(n!=lastKillRestores){lastKillRestores=n;AppPaths.Log("Spiel hat den zweiten Donald sauber entfernt ("+n+")");}uint k=U(killHookMem+0x418);if(k!=lastKeeps){lastKeeps=k;LastReloadAt=DateTime.UtcNow;AppPaths.Log("Zweiter Donald beim Neuladen behalten ("+k+")");}uint u=U(killHookMem+0x424);if(u!=lastUnlinks){lastUnlinks=u;AppPaths.Log("Zweiter Donald vom Spiel normal entfernt ("+u+")");}}catch{}}uint lastKeeps,lastUnlinks;
 void ClearKillHook(){if(killHookMem==0)return;try{WU(killHookMem+0x400,0);CodeWrite(0x40C8E0,KillOriginal);}catch{}killHookMem=0;}
 // Diagnosis of the engine's memory channels (allocator 0x443850): table [0x5B9128], 8 bytes per channel, +4 = heap
 // {+4 end, +8 free list (-1 = only bump), +C top}. Free list nodes: [0] size in dwords, [4] next.
 public string HeapInfo(){try{var s=new System.Text.StringBuilder("Heap["+process.Id+"] blk="+R(0x5C4088,1)[0]+" noalloc="+R(0x565E60,1)[0]);foreach(uint m in new uint[]{10}){uint baseAt=U(0x5B8C00+m*4);if(baseAt<65536)continue;for(uint blk=0;blk<8;blk++){uint h=baseAt+blk*16;try{uint end=U(h+4),free=U(h+8),top=U(h+12);if(end<65536||top<65536)continue;long bump=(long)end-top;long freeBytes=0;int nodes=0;if(free!=0xFFFFFFFF){uint at=free;while(at>65536&&nodes<20000){freeBytes+=(long)U(at)*4;at=U(at+4);nodes++;}}s.Append(" m"+m+"b"+blk+":"+(bump/1024)+"k"+(free!=0xFFFFFFFF?"+"+(freeBytes/1024)+"k/"+nodes:""));}catch{s.Append(" m"+m+"b"+blk+":?");}}}return s.ToString();}catch(Exception e){return "Heap ? "+e.Message;}}
 // A clone that was left behind on a map change keeps its address in engine lists that outlive the map. The hub is
 // loaded to the same addresses every time, so a new clone in the same slot met those stale entries (heap crashes in
 // the hub 1-8 s after the clone, 31.10.). Slots whose address was used by an earlier clone are therefore skipped.
 readonly HashSet<uint> usedCloneSO=new HashSet<uint>();readonly List<uint> heldSlots=new List<uint>();
 void HoldUsedSlots(){try{uint n=U(0x4A8EC8),slots=U(0x4A8ED8),used=U(0x4A8EE0);if(n==0||n>256||slots<65536||used<65536)return;
   /* never hold the last free slots: without a fresh one the game would have none at all */int free=0,fresh=0;for(uint i=0;i<n;i++){if(U(used+i*4)!=0||U(slots+i*0x48+0x14)!=0)continue;free++;if(!usedCloneSO.Contains(slots+i*0x48))fresh++;}if(fresh==0||free<4){if(usedCloneSO.Count>0)AppPaths.Log("Klon-Plätze: keine frischen mehr frei ("+free+" frei), benutzte werden wiederverwendet");usedCloneSO.Clear();return;}
   WithGameThreadsPaused(()=>{for(uint i=0;i<n;i++){uint so=slots+i*0x48;if(U(used+i*4)!=0||U(so+0x14)!=0)continue;if(!usedCloneSO.Contains(so))break;WU(used+i*4,local);heldSlots.Add(used+i*4);}});
   if(heldSlots.Count>0)AppPaths.Log(heldSlots.Count+" schon benutzte Klon-Plätze übersprungen");}catch(Exception e){AppPaths.Log("Klon-Plätze: "+e.Message);}}
 void ReleaseHeldSlots(){if(heldSlots.Count==0)return;try{WithGameThreadsPaused(()=>{foreach(uint at in heldSlots)if(U(at)==local)WU(at,0);});}catch{}heldSlots.Clear();}
 bool Readable(uint a){try{R(a,4);return true;}catch{return false;}}
 // After the game itself left a level (Game Over, exit question) the hub plays its intro (Gyro's hand carries Donald in);
 // a clone made during that sequence was followed by a heap crash within a second (31.10.). Then wait longer.
 public int NextSpawnDelay=2500;public void DelayNextSpawn(int ms){NextSpawnDelay=ms;}
 // The game's own way back into the hub (Game Over, native exit) plays that intro; our own transfers skip it.
 // Test aid: settings ReplicaStartDelay=<ms> keeps the new clone untouched for a while after it was made.
 int ReplicaStartDelay(){int v;return int.TryParse(AppPaths.Get("ReplicaStartDelay"),out v)?v:0;}
 bool carriedLogged;DateTime freeSince=DateTime.MaxValue;
 // After the game itself brought us back to the hub (Game Over, own exit), cloning there kept crashing (heap). A clean
 // reload of the hub by the mod (like every other transfer, which never crashed) comes first: WantsHubReload.
 public bool HubReloadPending;public bool WantsHubReload{get{try{return HubReloadPending&&CurrentMapFile().Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&(DateTime.UtcNow-worldReadyAt).TotalMilliseconds>8000;}catch{return false;}}}
 bool ownTransfer;int SpawnDelayNow(){int ms=NextSpawnDelay;if(HubReloadPending)return int.MaxValue;/* while Donald is carried (child of the hand, not of the world) no clone is made *//* carried = hidden by the game during a warp (SO flag 0x2); standing on a pad also makes the pad his parent, and after a level Donald stands on the level portal in the overworld, which kept the second Donald from appearing for good (Tim 5.11.) */try{if((U(local+48)&2)!=0&&U(local+28)!=world){if(!carriedLogged){carriedLogged=true;AppPaths.Log("Donald wird gerade getragen – zweiter Donald wartet");}freeSince=DateTime.MaxValue;return int.MaxValue;}if(freeSince==DateTime.MaxValue){freeSince=DateTime.UtcNow;if(carriedLogged)AppPaths.Log("Donald steht wieder selbst");}if(carriedLogged&&DateTime.UtcNow-freeSince<TimeSpan.FromSeconds(3))return int.MaxValue;}catch{}carriedLogged=false;/* off by default (5.11.): leaving a level the normal way arrives in MapMonde too (world overworld); the reload then hid the partner and sent Tim to the lab. The game-over case it was made for no longer happens (co-op lives). */try{if(!ownTransfer&&CurrentMapFile().Equals("MapMonde",StringComparison.OrdinalIgnoreCase)&&AppPaths.Get("HubReload")=="1"){HubReloadPending=true;return int.MaxValue;}}catch{}return ms;}
 // The clone function copies every component of the live Donald (ACP_Ray2: HIE_tdstEngineObject = 3dData, StdGame,
 // Dynam, Brain, CineInfo, CollSet, MSWay, MSLight, SectInfo, Micro, MSPrtSrc, MSSound, AnimEffect, MSMagnet). Right
 // after a Game Over Donald still plays the hub sequence, and the copy took over cinematic, sound and particle data
 // that the game frees shortly after (heap crashes 1-3 s after the clone). The replica only needs to be drawn, so these
 // extra components are switched off as well (settings KeepReplicaExtras=1 to keep them).
 static readonly uint[] ExtraOffsets={16,24,28,36,40,44,52};
 void StripReplicaExtras(){try{var s=new System.Text.StringBuilder("Komponenten Donald/Kopie:");uint lp=U(local+4);for(uint k=0;k<14;k++)s.Append(" "+k+":"+U(lp+k*4).ToString("X")+"/"+U(replicaPerso+k*4).ToString("X"));AppPaths.Log(s.ToString());
  if(AppPaths.Get("KeepReplicaExtras")=="1")return;foreach(uint o in ExtraOffsets)WU(replicaPerso+o,0);}catch(Exception e){AppPaths.Log("Komponenten: "+e.Message);}} public float[] TestPosition(){return LocalPosition();}
 public string ReplicaState{get{try{if(remote==0)return "kein Partner-Donald";bool inWorld=false;uint at=U(world+8);for(uint i=0,n=Math.Min(U(world+16),3000u);i<n&&at>65536;i++){if(at==remote){inWorld=true;break;}at=U(at+20);}byte[] g=R(U(remote+36)+4,12);return "Partner-Donald "+remote.ToString("X8")+(inWorld?" in Welt":" NICHT in Welt, parent="+U(remote+28).ToString("X8")+" world="+world.ToString("X8"))+" hook="+(replicaMem!=0)+" draw="+U(remote+44).ToString("X")+" @"+BitConverter.ToSingle(g,0).ToString("F1")+","+BitConverter.ToSingle(g,4).ToString("F1")+","+BitConverter.ToSingle(g,8).ToString("F1");}catch(Exception e){return "Partner-Donald ? "+e.Message;}}}
 public string SpawnInfo{get{return "Klone in dieser Karte: "+spawnsThisMap+(remote!=0?", Partner-Donald aktiv":"");}}
 // Off by default: with one clone per map the respawns without clearing were the configuration that stayed stable.
 static bool ClearStorageOnRespawn{get{return AppPaths.Get("ClearCloneStorage")=="1";}}
 uint playChangeCount;bool reloadHandled,mapChangePending;
 DateTime heapLogged;
 // The engine sometimes parks the clone in another list of the dynamic world (seen: world+0xA8, "inactive"); it is then
 // not drawn. It is moved back into the active world list (game threads paused while the links change).
 DateTime worldChecked;int moves;
 // Off unless settings KeepInWorld=1: moving it while the engine handles that list preceded a heap crash (31.10. 16:51).
 void KeepInWorld(){if(AppPaths.Get("KeepInWorld")!="1")return;if(remote==0||replicaMem==0||DateTime.UtcNow-worldChecked<TimeSpan.FromMilliseconds(500))return;worldChecked=DateTime.UtcNow;
  try{uint parent=U(remote+28);if(parent==world||parent<65536)return;uint so=remote;
   WithGameThreadsPaused(()=>{uint p=U(so+28);if(p==world||p<65536)return;uint next=U(so+20),prev=U(so+24);if(prev>65536)WU(prev+20,next);else WU(p+8,next);if(next>65536)WU(next+24,prev);else WU(p+12,prev);WU(p+16,U(p+16)-1);
    uint last=U(world+12),count=U(world+16);WU(so+20,0);WU(so+24,last);WU(so+28,world);if(last>65536)WU(last+20,so);else WU(world+8,so);WU(world+12,so);WU(world+16,count+1);});
   if(++moves<=5||moves%20==0)AppPaths.Log("Zweiten Donald aus Liste "+parent.ToString("X8")+" zurück in die Welt geholt ("+moves+")");}catch(Exception e){AppPaths.Log("Zurückholen fehlgeschlagen: "+e.Message);}} void NotePlaying(){CheckHeap();reloadIsMapChange=false;GuardLives();RefillIfPending();if(remote!=0&&replicaMem!=0){uint p=0;try{p=U(remote+4);}catch{}if(p!=replicaPerso){AppPaths.Log("Spiel hat den zweiten Donald entfernt – wird neu erzeugt");ClearReplica(false);remote=0;return;}}if(remote!=0){NextSpawnDelay=2500;ownTransfer=false;}LogKillRestores();KeepInWorld();if(AppPaths.Get("HeapLog")=="1"&&DateTime.UtcNow-heapLogged>TimeSpan.FromSeconds(2)){heapLogged=DateTime.UtcNow;AppPaths.Log(HeapInfo());}uint now=MapChangeCount;if(now!=playChangeCount)mapChangePending=true;playChangeCount=now;reloadHandled=false;}
 // Only a reload without a ChangeLevel call (death, checkpoint) keeps the level's storage array; on a map change the
 // engine frees that array itself and must find it untouched.
 // Every cached actor address is dropped on a reload: a map loaded again under the same name puts other objects at
 // those addresses (the lives refill once wrote into the old global actor of the hub).
 void KeepOrphan(){/* the camera hook must not write into the old level's camera while loading */StopPartnerCamera();globalActor=0;cameraSO=0;LastReloadAt=DateTime.UtcNow;if(!reloadHandled){reloadHandled=true;if(levelHookMem!=0&&!mapChangePending&&MapChangeCount==playChangeCount&&ClearStorageOnRespawn)ForgetCloneStorage();else{cloneStorage.Clear();if(mapChangePending||MapChangeCount!=playChangeCount){/* new map: the old clone is gone, its perso address may be reused by an engine object */ArmKillHook(0,null);keptPerso=0;keptComponents=null;if(OutOfLives||LastLife)pendingRefill=true;reloadIsMapChange=true;ownClones.Clear();/* the skipped slots only matter inside one level (reload after a death); kept over map changes they used up all 40 Always slots after about 38 level changes and the next clone crashed the game (7.11.) */usedCloneSO.Clear();WatchHeap();}}mapChangePending=false;playChangeCount=MapChangeCount;}
  // Levels load to the same addresses every time: after a map change the old clone's address may belong to any object
  // of the new level. Then nothing is remembered (writing into such an object as if it were the clone broke the heap).
  if(reloadIsMapChange){orphan=0;return;}AppPaths.Log("Zustände vor dem Neuladen:"+AnimHistory(6000));if(remote!=0)AppPaths.Log("Neuladen, "+AlwaysInfo()+" "+HeapInfo());try{if(remote!=0&&Actor(remote)){orphan=remote;orphanPerso=U(remote+4);orphanMap=level;}}catch{orphan=0;}}
 bool reloadIsMapChange;
 // Our own clones of the current map (SO -> perso); only these may ever be taken up again after a death reload.
 readonly Dictionary<uint,uint> ownClones=new Dictionary<uint,uint>();
 void TryRelinkOrphan(){if(orphan==0||orphanMap!=level)return;uint so=orphan;orphan=0;uint known;if(!ownClones.TryGetValue(so,out known)||known!=orphanPerso){AppPaths.Log("Alter zweiter Donald unbekannt – nicht wiederverwendet");return;}
  try{if(!Actor(so)||U(so+4)!=orphanPerso){AppPaths.Log("Alter zweiter Donald nicht mehr gültig");return;}uint parent=U(so+28);
   // Only a detached clone may be linked again; if the engine moved it into another list, leave it alone.
   if(parent!=0&&parent!=world){AppPaths.Log("Alter zweiter Donald hängt an "+parent.ToString("X8")+" – nicht wiederverwendet");return;}
   // After a reload the world list is rebuilt; the old parent pointer alone does not mean the clone is still in it.
   if(parent==world){uint at=U(world+8);for(uint i=0,n=Math.Min(U(world+16),3000u);i<n&&at>65536;i++){if(at==so){remote=so;AppPaths.Log("Alter zweiter Donald ist noch in der Welt");return;}at=U(at+20);}}
   WithGameThreadsPaused(()=>{uint last=U(world+12),count=U(world+16);WU(so+20,0);WU(so+24,last);WU(so+28,world);if(last>65536)WU(last+20,so);else WU(world+8,so);WU(world+12,so);WU(world+16,count+1);});
   remote=so;AppPaths.Log("Alten zweiten Donald wieder in die Welt gehängt");}catch(Exception e){AppPaths.Log("Wiedereinhängen fehlgeschlagen: "+e.Message);}}
}
}
