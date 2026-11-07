#if TESTAUTO
using System;using System.IO;using System.Windows.Forms;
namespace DonaldOnline {
// Test builds only: drives the own Donald from a command file so two local games can play co-op without a person.
// File launcher-cmd-<role>.txt next to the exe; every new line is one command:
//   walk <0-3> <ms> | jump | space | wander <seconds> | go <map> | pos | stop
sealed class TestDriver {
 readonly OnlineSession session;readonly string file;int done;readonly Timer timer=new Timer{Interval=50};readonly Random random=new Random();
 DateTime walkUntil=DateTime.MinValue,wanderUntil=DateTime.MinValue,nextTurn;int lastLines;
 public TestDriver(OnlineSession session,string role){this.session=session;file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher-cmd-"+role+".txt");try{File.WriteAllText(file,"");}catch{}timer.Tick+=(s,e)=>Step();timer.Start();}
 uint lastRemote;
 // After every new clone in the hub: hardware-watch the engine heap's first free-list nodes (CrashCatcher watch mode).
 void WatchNewClone(GameBridge bridge){uint r=bridge.RemoteActor;if(r==lastRemote)return;lastRemote=r;if(r==0||!"MapMonde".Equals(bridge.LevelKey,StringComparison.OrdinalIgnoreCase))return;uint[] n=bridge.HeapNodes();if(n.Length<1)return;string s=n[0].ToString("X8")+" "+(n[0]+4).ToString("X8")+(n.Length>1?" "+n[1].ToString("X8")+" "+(n[1]+4).ToString("X8"):"");try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"watch-"+bridge.GamePid+".txt"),s);}catch{}AppPaths.Log("[Test] Watch "+s+"\n  lokal "+bridge.DescribeActor(bridge.LocalActor)+"\n  klon "+bridge.DescribeActor(r));}
 // The hub crash (1.11.): the own Donald's animation array (3D data +0x78) lies in a free heap block while still in use.
 // Every new array is hardware-watched so the debugger logs who frees it.
 uint lastArray;
 void WatchOwnArray(GameBridge bridge){uint a=bridge.OwnAnimArray();if(a==lastArray||a<65536)return;lastArray=a;string s=(a-8).ToString("X8")+" "+(a-4).ToString("X8")+" "+a.ToString("X8")+" "+(a+4).ToString("X8");try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"watch-"+bridge.GamePid+".txt"),s);}catch{}AppPaths.Log("[Test] Watch Array "+s+" Karte "+bridge.LevelKey+"\n  lokal "+bridge.DescribeActor(bridge.LocalActor)+(bridge.RemoteActor!=0?"\n  klon "+bridge.DescribeActor(bridge.RemoteActor):""));}
 // Then: the clone's array field gets the own Donald's array; watch both pointer fields to see who writes them.
 uint lastFieldClone;
 void WatchArrayFields(GameBridge bridge){uint r=bridge.RemoteActor;if(r==lastFieldClone)return;lastFieldClone=r;if(r==0)return;uint c=bridge.AnimArrayField(r),l=bridge.AnimArrayField(bridge.LocalActor);if(c==0||l==0)return;string s=c.ToString("X8")+" "+l.ToString("X8");try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"watch-"+bridge.GamePid+".txt"),s);}catch{}AppPaths.Log("[Test] Watch Felder "+s+" Karte "+bridge.LevelKey);}
 uint pointersLogged;DateTime pointersAt;
 void LogSharedPointers(GameBridge bridge){uint r=bridge.RemoteActor;if(r==0){pointersLogged=0;return;}if(r!=pointersLogged){pointersLogged=r;pointersAt=DateTime.UtcNow;AppPaths.Log("[Test] Gemeinsame Zeiger neu "+bridge.LevelKey+":"+bridge.SharedPointers(r,bridge.LocalActor));}else if(pointersAt!=DateTime.MinValue&&DateTime.UtcNow-pointersAt>TimeSpan.FromSeconds(2)){pointersAt=DateTime.MinValue;AppPaths.Log("[Test] Gemeinsame Zeiger +2s "+bridge.LevelKey+":"+bridge.SharedPointers(r,bridge.LocalActor));}}
 uint sharedLogged;
 void DetectShared(GameBridge bridge){uint r=bridge.RemoteActor;if(r==0)return;uint c=bridge.AnimArrayField(r),l=bridge.AnimArrayField(bridge.LocalActor);if(c==0||l==0)return;uint cv=bridge.ReadU(c),lv=bridge.ReadU(l);if(cv!=0&&cv==lv&&cv!=sharedLogged){sharedLogged=cv;AppPaths.Log("[Test] GETEILT Array "+cv.ToString("X8")+" Karte "+bridge.LevelKey+" letzte Aktion "+bridge.LastAction+" | "+bridge.ReplicaState);}}
 DateTime allocLogged;string lastAlloc="";
 void LogAlloc(GameBridge bridge){bool hub="MapMonde".Equals(bridge.LevelKey,StringComparison.OrdinalIgnoreCase);if(DateTime.UtcNow-allocLogged<TimeSpan.FromSeconds(hub?1:10))return;allocLogged=DateTime.UtcNow;string a=bridge.AllocInfo();AppPaths.Log("[Test] Speicher "+bridge.LevelKey+" "+a);}
 DateTime trackUntil;string trackFile;
 void Track(GameBridge bridge){if(DateTime.UtcNow>trackUntil)return;try{float[] p=bridge.TestPosition();if(p!=null)File.AppendAllText(trackFile,DateTime.UtcNow.ToString("HH:mm:ss.fff")+" "+p[0].ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" "+p[1].ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" "+p[2].ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+(bridge.Spectating?" S":" -")+(bridge.ReplicaShownAt!=null?" "+bridge.ReplicaShownAt[0].ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" "+bridge.ReplicaShownAt[1].ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" "+bridge.ReplicaShownAt[2].ToString("F3",System.Globalization.CultureInfo.InvariantCulture):"")+"\n");}catch{}}
 bool ringDone;DateTime headersAt;
 void Step(){var bridge=session.Bridge;if(bridge==null)return;if(!ringDone&&bridge.LevelKey!=null){ringDone=true;try{uint ring=bridge.InstallFreeLog();File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ring-"+bridge.GamePid+".txt"),ring.ToString("X8"));AppPaths.Log("[Test] Freigabe-Protokoll "+ring.ToString("X8"));}catch(Exception e){AppPaths.Log("[Test] Freigabe-Protokoll: "+e.Message);}}Track(bridge);DetectShared(bridge);if(DateTime.UtcNow>headersAt){headersAt=DateTime.UtcNow.AddSeconds(7);AppPaths.Log("[Test] Felder"+bridge.FieldHeaders());}
  try{string[] lines=File.Exists(file)?File.ReadAllLines(file):new string[0];if(lines.Length<lastLines)done=0;lastLines=lines.Length;while(done<lines.Length){string line=lines[done++].Trim();if(line.Length>0)Run(bridge,line);}}catch(Exception ex){AppPaths.Log("[Test] Fehler: "+ex.Message);}
  if(walkUntil!=DateTime.MinValue&&DateTime.UtcNow>walkUntil){walkUntil=DateTime.MinValue;bridge.ClearTestInput();}
  if(wanderUntil!=DateTime.MinValue){if(DateTime.UtcNow>wanderUntil){wanderUntil=DateTime.MinValue;bridge.ClearTestInput();AppPaths.Log("[Test] wander fertig "+Where(bridge));}
   else if(DateTime.UtcNow>nextTurn){nextTurn=DateTime.UtcNow.AddMilliseconds(500+random.Next(1500));if(random.Next(6)==0)bridge.OverrideJump();else bridge.OverrideDirection(random.Next(4));}}}
 void Run(GameBridge bridge,string line){string[] a=line.Split(' ');AppPaths.Log("[Test] > "+line);
  switch(a[0]){
   case "walk":bridge.OverrideDirection(int.Parse(a[1]));walkUntil=DateTime.UtcNow.AddMilliseconds(int.Parse(a[2]));break;
   case "jump":bridge.OverrideJump();walkUntil=DateTime.UtcNow.AddMilliseconds(200);break;
   case "space":bridge.OverrideAction(true);walkUntil=DateTime.UtcNow.AddMilliseconds(300);break;
   case "wander":wanderUntil=DateTime.UtcNow.AddSeconds(double.Parse(a[1],System.Globalization.CultureInfo.InvariantCulture));nextTurn=DateTime.UtcNow;break;
   case "go":session.TestGo(a[1]);break;
   case "stop":walkUntil=wanderUntil=DateTime.MinValue;bridge.ClearTestInput();break;
   case "pos":AppPaths.Log("[Test] "+Where(bridge));break;
   case "track":trackUntil=DateTime.UtcNow.AddSeconds(double.Parse(a[1],System.Globalization.CultureInfo.InvariantCulture));trackFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"track-"+(file.Contains("host")?"host":"guest")+".txt");File.WriteAllText(trackFile,"");break;
   case "kill":AppPaths.Log("[Test] kill "+a[1]+": "+bridge.Deactivate(a[1]));break;
   case "books":AppPaths.Log("[Test] BUECHER"+bridge.BookDsg());break;
   case "hud":AppPaths.Log("[Test] HUD"+bridge.HudDsg());break;
   case "camtrack":{double secs=double.Parse(a[1],System.Globalization.CultureInfo.InvariantCulture);bridge.TrackCloneUntil=DateTime.UtcNow.AddSeconds(secs+1);string cf=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cam-"+(file.Contains("host")?"host":"guest")+".txt");File.WriteAllText(cf,"");var br=bridge;new System.Threading.Thread(()=>{var ci=System.Globalization.CultureInfo.InvariantCulture;var sw=System.Diagnostics.Stopwatch.StartNew();var sb=new System.Text.StringBuilder();uint last=0;long q0=0;/* per rendered frame from the camera hook (7.11.) */while(sw.Elapsed.TotalSeconds<secs){uint s;long q;float[] c;if(br.CamFrame(out s,out q,out c)&&s!=last){if(q0==0)q0=q;last=s;sb.Append(((q-q0)*1000.0/System.Diagnostics.Stopwatch.Frequency).ToString("F2",ci)+" "+c[0].ToString("F4",ci)+" "+c[1].ToString("F4",ci)+" "+c[2].ToString("F4",ci)+" "+c[3].ToString("F4",ci)+" "+c[4].ToString("F4",ci)+" "+c[5].ToString("F4",ci)+" "+s+(br.Spectating?" S":"")+"\n");}System.Threading.Thread.Sleep(0);System.Threading.Thread.SpinWait(500);}File.AppendAllText(cf,sb.ToString());}){IsBackground=true}.Start();break;}
   case "dsg":AppPaths.Log("[Test] DSG\n"+bridge.GlobalDsgDump());break;
   case "leave":bridge.TestLeave(a[1]);break;case "digits":AppPaths.Log("[Test] Ziffern"+bridge.DigitInfo());break;case "stars":bridge.TestSetStars(int.Parse(a[1]));break;case "lives":bridge.TestSetLives(int.Parse(a[1]));break;
   case "all":AppPaths.Log("[Test] ALLE"+bridge.AllActors());break;
   case "mv":AppPaths.Log("[Test] mv "+bridge.MoveOwn(float.Parse(a[1],System.Globalization.CultureInfo.InvariantCulture),float.Parse(a[2],System.Globalization.CultureInfo.InvariantCulture),float.Parse(a[3],System.Globalization.CultureInfo.InvariantCulture)));break;
   case "dump":AppPaths.Log("[Test] DUMP\n"+bridge.ActorDump());break;
   case "tp":bridge.PlaceAt(float.Parse(a[1],System.Globalization.CultureInfo.InvariantCulture),float.Parse(a[2],System.Globalization.CultureInfo.InvariantCulture),float.Parse(a[3],System.Globalization.CultureInfo.InvariantCulture));break;
   case "scan":AppPaths.Log("[Test] "+bridge.ScanDiff());break;}}
 static string Where(GameBridge bridge){try{float[] p=bridge.TestPosition();return "Karte "+bridge.LevelKey+" Pos "+(p==null?"?":p[0].ToString("F1")+","+p[1].ToString("F1")+","+p[2].ToString("F1"))+" "+bridge.SpawnInfo+" | "+bridge.ReplicaState+" | Leben "+bridge.Lives+(bridge.LastLife?" (letztes)":"")+(bridge.OutOfLives?" ZUSCHAUER":"");}catch(Exception e){return "? "+e.Message;}}
}
}
#endif
