using System;using System.Diagnostics;using System.IO;using System.IO.Compression;using System.Net;using System.Runtime.InteropServices;using System.Text;using System.Text.RegularExpressions;
namespace DonaldOnline {
// Makes a player's own copy of Donald Duck Quack Attack (German re-release) run on current Windows, the same way it was
// set up on Tim's PC, without shipping any game files:
//  - CD image: .cue/.bin (MODE1/2352) is converted to an ISO in %APPDATA%\DonaldOnline, .iso is used directly, then mounted.
//  - Game files: copied from the player's own disc (folders "Donald" and "Data") into a folder of his choice.
//  - ubi.ini: the game reads C:\Windows\UbiSoft\ubi.ini, which Windows redirects to %LOCALAPPDATA%\VirtualStore.
//  - dgVoodoo2 (DirectX wrapper by Dege, freeware): downloaded from its official GitHub release; window mode via dgVoodoo.conf.
//  - Kernel32 lookup fix: patched in memory when the game starts (Launch), the exe on disk stays original.
public static class GameSetup {
 public const uint SupportedTimestamp=971704687; public static void EnsureNoRunningGame(string folder){GameInstance.EnsureNotRunning(folder);}
 public static Action<string> Progress=s=>{};
 static void Say(string s){AppPaths.Log("Einrichtung: "+s);Progress(s);}

 public static bool IsSupportedExe(string exe){try{byte[] b=File.ReadAllBytes(exe);int pe=BitConverter.ToInt32(b,0x3C);return BitConverter.ToUInt32(b,pe+8)==SupportedTimestamp;}catch{return false;}}

 // ---- CD image ----
 public static string PrepareDiscImage(string path){string ext=Path.GetExtension(path).ToLowerInvariant();
  if(ext==".iso")return path;
  string bin=path;if(ext==".cue"){string cue=File.ReadAllText(path);var m=Regex.Match(cue,"FILE\\s+\"([^\"]+)\"");if(!m.Success)throw new Exception("Die .cue-Datei nennt keine .bin-Datei.");bin=Path.Combine(Path.GetDirectoryName(path),m.Groups[1].Value);if(!Regex.IsMatch(cue,"MODE1/2352"))throw new Exception("Dieses CD-Abbild hat ein unbekanntes Format.");}
  if(!File.Exists(bin))throw new Exception("Die .bin-Datei fehlt: "+bin);
  string iso=Path.Combine(AppPaths.Data,"Donald.iso");if(File.Exists(iso)&&new FileInfo(iso).Length==new FileInfo(bin).Length/2352*2048)return iso;
  Say("CD-Abbild wird umgewandelt (einmalig) …");string temp=iso+".part";
  using(var src=File.OpenRead(bin))using(var dst=File.Create(temp)){byte[] sector=new byte[2352];long total=src.Length/2352,done=0;int n;
   while((n=src.Read(sector,0,2352))==2352){if(sector[15]!=1)throw new Exception("Unerwartetes CD-Sektorformat.");dst.Write(sector,16,2048);if(++done%20000==0)Say("CD-Abbild wird umgewandelt … "+(done*100/total)+" %");}}
  if(File.Exists(iso))File.Delete(iso);File.Move(temp,iso);return iso;}
 public static string MountedDiscRoot(){foreach(DriveInfo d in DriveInfo.GetDrives())try{if(d.DriveType==DriveType.CDRom&&d.IsReady&&d.VolumeLabel.Equals("DONALD",StringComparison.OrdinalIgnoreCase))return d.RootDirectory.FullName;}catch{}return null;}
 public static string Mount(string iso){string root=MountedDiscRoot();if(root!=null)return root;Say("CD-Abbild wird eingebunden …");
  var ps=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -Command \"try { Mount-DiskImage -ImagePath '"+iso.Replace("'","''")+"' -ErrorAction Stop | Out-Null } catch { exit 1 }\""){UseShellExecute=false,CreateNoWindow=true};
  using(Process p=Process.Start(ps)){if(!p.WaitForExit(30000)||p.ExitCode!=0)throw new Exception("Das CD-Abbild konnte nicht eingebunden werden.");}
  for(int i=0;i<40;i++){root=MountedDiscRoot();if(root!=null)return root;System.Threading.Thread.Sleep(250);}throw new Exception("Das eingebundene CD-Abbild ist keine Donald-Duck-CD.");}

 // ---- Game files from the player's own disc ----
 public static void InstallFromDisc(string discRoot,string target){if(!File.Exists(Path.Combine(discRoot,"Donald","Donald.exe"))||!Directory.Exists(Path.Combine(discRoot,"Data")))throw new Exception("Auf der CD fehlen die Ordner Donald und Data.");
  Directory.CreateDirectory(target);Say("Spieldateien werden von deiner CD kopiert …");CopyTree(Path.Combine(discRoot,"Donald"),target);CopyTree(Path.Combine(discRoot,"Data"),Path.Combine(target,"Data"));
  // Files from a CD are read-only.
  foreach(string f in Directory.GetFiles(target,"*",SearchOption.AllDirectories))try{File.SetAttributes(f,FileAttributes.Normal);}catch{}Say("Spieldateien kopiert.");}
 static void CopyTree(string from,string to){Directory.CreateDirectory(to);foreach(string f in Directory.GetFiles(from))File.Copy(f,Path.Combine(to,Path.GetFileName(f)),true);foreach(string d in Directory.GetDirectories(from))CopyTree(d,Path.Combine(to,Path.GetFileName(d)));}

 // ---- Compatibility ----
 public static void EnsureCompatibility(string gameFolder,bool allowDownload){EnsureUbiIni();EnsureDgVoodoo(gameFolder,allowDownload);}
 public static bool NeedsDgVoodoo(string gameFolder){return !File.Exists(Path.Combine(gameFolder,"DDraw.dll"));}
 // The game reads C:\Windows\UbiSoft\ubi.ini (for a normal user Windows redirects it to the VirtualStore copy) and quits
 // silently without a [Donald] section. Other old Ubisoft games (e.g. Rayman 2) create that file without our section,
 // so the section is added to whichever file exists; the real file is also tried for players running as administrator.
 static void EnsureUbiIni(){string store=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VirtualStore","Windows","UbiSoft","ubi.ini");string real=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"UbiSoft","ubi.ini");
  string effective=File.Exists(store)?store:File.Exists(real)?real:null;string text=effective==null?"":File.ReadAllText(effective,Encoding.Default);
  AppPaths.Log("ubi.ini: "+(effective??"keine")+(text.IndexOf("[Donald]",StringComparison.OrdinalIgnoreCase)>=0?" (mit [Donald])":" (ohne [Donald])"));
  if(text.IndexOf("[Donald]",StringComparison.OrdinalIgnoreCase)>=0)return;
  string merged=(text.Length>0&&!text.EndsWith("\n")?text+"\r\n":text)+Resource("ubi.ini");
  Directory.CreateDirectory(Path.GetDirectoryName(store));File.WriteAllText(store,merged,Encoding.Default);
  try{Directory.CreateDirectory(Path.GetDirectoryName(real));File.WriteAllText(real,merged,Encoding.Default);AppPaths.Log("ubi.ini auch in "+real+" geschrieben");}catch{}
  Say("Spiel-Einstellungen (ubi.ini) eingetragen.");}
 // After the game ended: exit code and, for a crash, the faulting module and offset from the Windows event log
 // (Windows writes it a moment later), so a tester's log says where it crashed.
 public static void LogExit(Process p,string context){System.Threading.Tasks.Task.Run(()=>{string code="unbekannt";uint raw=0;try{raw=unchecked((uint)p.ExitCode);code="0x"+raw.ToString("X8");}catch{}
  string where="";if(raw>=0xC0000000u)try{System.Threading.Thread.Sleep(4000);using(var log=new EventLog("Application")){for(int i=log.Entries.Count-1,seen=0;i>=0&&seen<200;i--,seen++){var e=log.Entries[i];if(e.TimeGenerated<DateTime.Now.AddMinutes(-3))break;if(e.InstanceId!=1000||e.Message.IndexOf("Donald.exe",StringComparison.OrdinalIgnoreCase)<0)continue;
   var mod=Regex.Matches(e.Message,@":\s*([^\s|]+\.(?:exe|dll))",RegexOptions.IgnoreCase);var off=Regex.Match(e.Message,@"offset:\s*(0x[0-9a-fA-F]+)",RegexOptions.IgnoreCase);where=" – Absturz in "+(mod.Count>1?mod[1].Groups[1].Value:"?")+" bei "+(off.Success?off.Groups[1].Value:"?");break;}}}catch{}
  AppPaths.Log("Spiel beendet ("+context+"), Code "+code+where);});}
 // Waits a little after the start: an old game that dies at once would otherwise just "not open".
 public static string WatchStart(Process p,int seconds){for(int i=0;i<seconds*10;i++){if(p.HasExited){string code="unbekannt";try{code=p.ExitCode.ToString();}catch{}string why="Das Spiel hat sich direkt nach dem Start wieder beendet (Code "+code+").";AppPaths.Log(why);return why;}System.Threading.Thread.Sleep(100);}p.Refresh();if(p.MainWindowHandle==IntPtr.Zero)return "Das Spiel läuft im Hintergrund, hat aber kein Spielfenster geöffnet. Bitte das Protokoll zur Fehlersuche weitergeben.";AppPaths.Log("Spielfenster geöffnet, PID "+p.Id);return null;}
 // One line about the environment for the log, to see a friend's setup afterwards.
 public static void LogEnvironment(string gameFolder){try{string exe=Path.Combine(gameFolder,"Donald.exe");bool admin=new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
  AppPaths.Log("Umgebung: Spiel="+gameFolder+" exeOK="+File.Exists(exe)+" Version="+(File.Exists(exe)&&IsSupportedExe(exe)?"passend":"anders/fehlt")+" Daten="+Directory.Exists(Path.Combine(gameFolder,"Data","World","Levels"))+" dgVoodoo="+!NeedsDgVoodoo(gameFolder)+" CD="+(MountedDiscRoot()??"keine")+" Abbild="+(AppPaths.FindDiscImage(gameFolder)??"keins")+" Admin="+admin+" Windows="+Environment.OSVersion.Version);}catch(Exception e){AppPaths.Log("Umgebung: "+e.Message);}}
 static void EnsureDgVoodoo(string gameFolder,bool allowDownload){if(!NeedsDgVoodoo(gameFolder))return;if(!allowDownload)return;
  Say("Lade dgVoodoo2 (Grafik-Kompatibilität) von GitHub …");string zip=Path.Combine(AppPaths.Data,"dgVoodoo2.zip");
  if(!File.Exists(zip))using(var web=new WebClient()){web.Headers.Add("User-Agent","DonaldOnline");string json=web.DownloadString("https://api.github.com/repos/dege-diosg/dgVoodoo2/releases/latest");
   string url=null;foreach(Match m in Regex.Matches(json,"\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.zip)\""))if(m.Groups[1].Value.IndexOf("dbg",StringComparison.OrdinalIgnoreCase)<0&&m.Groups[1].Value.IndexOf("API",StringComparison.Ordinal)<0){url=m.Groups[1].Value;break;}
   if(url==null)throw new Exception("dgVoodoo2-Download nicht gefunden.");AppPaths.Log("dgVoodoo2: "+url);web.DownloadFile(url,zip);}
  using(var archive=ZipFile.OpenRead(zip)){foreach(string name in new[]{"DDraw.dll","D3DImm.dll"}){ZipArchiveEntry entry=null;foreach(var e in archive.Entries)if(e.FullName.Replace('\\','/').EndsWith("MS/x86/"+name,StringComparison.OrdinalIgnoreCase))entry=e;if(entry==null)throw new Exception(name+" fehlt im dgVoodoo2-Paket.");entry.ExtractToFile(Path.Combine(gameFolder,name),true);}}
  File.WriteAllText(Path.Combine(gameFolder,"dgVoodoo.conf"),Resource("dgVoodoo.conf"),Encoding.ASCII);Say("dgVoodoo2 eingerichtet (Fenstermodus).");}
 static string Resource(string name){using(var s=typeof(GameSetup).Assembly.GetManifestResourceStream("DonaldOnline."+name))using(var r=new StreamReader(s))return r.ReadToEnd();}

 // ---- Start the game with the in-memory compatibility fix ----
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct STARTUPINFO{public int cb;public string r1,desktop,title;public int x,y,w,h,cx,cy,attr,flags;public short show,r2;public IntPtr r3,hin,hout,herr;}
 [StructLayout(LayoutKind.Sequential)]struct PROCESS_INFORMATION{public IntPtr process,thread;public int pid,tid;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateProcess(string app,string cmd,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string dir,ref STARTUPINFO si,out PROCESS_INFORMATION pi);
 [DllImport("kernel32.dll")]static extern uint ResumeThread(IntPtr t);[DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
 [DllImport("kernel32.dll")]static extern bool ReadProcessMemory(IntPtr p,IntPtr a,byte[] b,UIntPtr n,out UIntPtr d);[DllImport("kernel32.dll")]static extern bool WriteProcessMemory(IntPtr p,IntPtr a,byte[] b,UIntPtr n,out UIntPtr d);
 [DllImport("kernel32.dll")]static extern bool VirtualProtectEx(IntPtr p,IntPtr a,UIntPtr n,uint r,out uint o);[DllImport("kernel32.dll")]static extern bool FlushInstructionCache(IntPtr p,IntPtr a,UIntPtr n);
 // The original scans memory for kernel32 (a Windows 9x trick) at 0x448E05; replaced by GetModuleHandleA("KERNEL32.dll").
 static readonly byte[] ScanCode={0x00,0x10,0x00,0x00,0x50,0xE8,0x11,0xFF,0xFF,0xFF,0x83,0xC4,0x0C},FixedCode={0x6E,0x5F,0x5C,0x00,0xFF,0x15,0xC0,0x54,0x5C,0x00,0x83,0xC4,0x04};
 public static Process Launch(string gameFolder){EnsureNoRunningGame(gameFolder);string exe=Path.Combine(gameFolder,"Donald.exe");var si=new STARTUPINFO();si.cb=Marshal.SizeOf(si);PROCESS_INFORMATION pi;
  if(!CreateProcess(exe,"\""+exe+"\"",IntPtr.Zero,IntPtr.Zero,false,4,IntPtr.Zero,gameFolder,ref si,out pi))throw new Exception("Donald.exe konnte nicht gestartet werden (Fehler "+Marshal.GetLastWin32Error()+").");
  Process started=null;try{started=Process.GetProcessById(pi.pid);IntPtr retainedHandle=started.Handle;var at=new IntPtr(0x448E05);byte[] now=new byte[13];UIntPtr n;if(ReadProcessMemory(pi.process,at,now,(UIntPtr)13,out n)){
    if(BitConverter.ToString(now)==BitConverter.ToString(ScanCode)){uint old;VirtualProtectEx(pi.process,at,(UIntPtr)13,0x40,out old);WriteProcessMemory(pi.process,at,FixedCode,(UIntPtr)13,out n);uint unused;VirtualProtectEx(pi.process,at,(UIntPtr)13,old,out unused);FlushInstructionCache(pi.process,at,(UIntPtr)13);AppPaths.Log("Kompatibilitäts-Fix im Speicher gesetzt.");}
    else if(BitConverter.ToString(now)==BitConverter.ToString(FixedCode))AppPaths.Log("Donald.exe enthält den Kompatibilitäts-Fix bereits.");else AppPaths.Log("Kompatibilitäts-Fix: unbekannte Bytes "+BitConverter.ToString(now));}}
  finally{ResumeThread(pi.thread);CloseHandle(pi.thread);CloseHandle(pi.process);}
  AppPaths.Log("Spielprozess gestartet, PID "+started.Id);return started;}
}
}


