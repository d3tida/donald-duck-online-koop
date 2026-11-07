using System;using System.Text;
namespace DonaldOnline {
public sealed partial class GameBridge {
 // Lives live in a designer variable of the actor "World/GlobalActorModel/global" (offset 0x1C4, found 31.10. with
 // MemScan/DsgFind: 3,2,1,0 over three deaths; the HUD actor RAC_Lives1_I1 only mirrors it). The game's own Game Over
 // sequence left the engine in a state in which the clone crashed the hub every time. In co-op the mod therefore keeps
 // the real counter at 1 or more and counts the co-op lives itself: at 0 the player is on his last life, the next death
 // makes him a spectator instead of a Game Over (OnlineSession decides what happens then).
 uint globalActor;string globalLevel;public bool LastLife{get;private set;}public bool OutOfLives{get;private set;}int livesSeenDeaths;
 public int Lives{get{uint at=LivesAddress;return at==0?-1:(int)U(at);}}
 uint LivesAddress{get{try{if(globalActor==0||globalLevel!=level||U(globalActor)!=2){globalActor=FindActor("World/GlobalActorModel/global");globalLevel=level;}if(globalActor==0)return 0;uint p=U(globalActor+4),brain=U(p+12);if(brain<65536)return 0;uint dm=U(U(brain)+12);if(dm<65536)return 0;uint buf=U(dm+8);return buf<65536?0:buf+0x1C4;}catch{return 0;}}}
 uint FindActor(string wanted){foreach(uint root in new[]{U(0x5AEEF0),U(0x5AEEF4),U(0x5AEEEC),U(0x5AEEE8)}){uint f=FindIn(root,0,wanted);if(f!=0)return f;}return 0;}
 uint FindIn(uint parent,int depth,string wanted){if(parent<65536||depth>3)return 0;uint at=U(parent+8),n=U(parent+16);for(uint i=0;i<Math.Min(n,3000u)&&at>65536;i++){if(U(at)==2){uint st=U(U(at+4)+4);if(Name(0,U(st))+"/"+Name(1,U(st+4))+"/"+Name(2,U(st+8))==wanted)return at;}else{uint f=FindIn(at,depth+1,wanted);if(f!=0)return f;}at=U(at+20);}return 0;}
 DateTime livesChecked;
 // Called while playing (about twice a second): never let the real counter reach 0 while co-op runs.
 void GuardLives(){if(!CoopLivesActive||DateTime.UtcNow-livesChecked<TimeSpan.FromMilliseconds(300)||DateTime.UtcNow<livesGraceUntil)return;livesChecked=DateTime.UtcNow;if(InHub)return;uint at=LivesAddress;if(at==0)return;int lives=(int)U(at);
  // The real counter never goes below 2: at 0 the game already marks a Game Over internally even though it does not
  // leave the level at once, and the hub entered afterwards ran its Game Over sequence (hub crash after "both out").
  if(lives<=1){if(!LastLife){LastLife=true;AppPaths.Log("Letztes Leben – beim nächsten Tod Zuschauer statt Game Over");}else if(!OutOfLives){OutOfLives=true;AppPaths.Log("Keine Leben mehr – Zuschauer-Modus");}WU(at,2);}}
 // The hub has no deaths and its global actor does not hold the lives the levels use.
 bool InHub{get{return level!=null&&level.Equals("MapMonde",StringComparison.OrdinalIgnoreCase);}}
 public void TestSetStars(int n){uint at=LivesAddress;if(at!=0)W(at-1,new[]{(byte)n});AppPaths.Log("[Test] Sterne gesetzt: "+n);}
 public void TestSetLives(int n){uint at=LivesAddress;if(at!=0)WU(at,(uint)n);AppPaths.Log("[Test] Leben gesetzt: "+n);}
 public bool CoopLivesActive=true;
 // New lives for both after the level was finished together or after a shared "Game Over".
 DateTime livesGraceUntil;
 public void RefillLives(int lives){LastAction="Leben auffuellen";LastLife=false;OutOfLives=false;livesGraceUntil=DateTime.UtcNow.AddSeconds(5);uint at=LivesAddress;if(at!=0)WU(at,(uint)lives);AppPaths.Log("Leben aufgefüllt: "+lives);}
}
}
