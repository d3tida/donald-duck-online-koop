using System;using System.IO;using System.Net;using System.Security.Cryptography;using System.Text;
namespace DonaldOnline {
// Everything the mod stores lives in %APPDATA%\DonaldOnline; the game folder itself is never changed.
public static class AppPaths {
 public static string Data{get{string d=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"DonaldOnline");Directory.CreateDirectory(d);return d;}}
 static string SettingsFile{get{return Path.Combine(Data,"settings.txt");}}
 public static string Get(string key){try{foreach(string line in File.ReadAllLines(SettingsFile)){int eq=line.IndexOf('=');if(eq>0&&line.Substring(0,eq)==key)return line.Substring(eq+1);}}catch{}return null;}
 public static void Set(string key,string value){var lines=new System.Collections.Generic.List<string>();try{foreach(string line in File.ReadAllLines(SettingsFile))if(!line.StartsWith(key+"="))lines.Add(line);}catch{}lines.Add(key+"="+value);/* a second launcher may be writing at the same moment */for(int attempt=0;;attempt++){try{File.WriteAllLines(SettingsFile,lines.ToArray());return;}catch(IOException){if(attempt>=20)return;System.Threading.Thread.Sleep(50);}}}
 public static string FolderKey(string folder){using(var sha=SHA1.Create()){byte[] h=sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(folder).ToLowerInvariant()));return BitConverter.ToString(h,0,6).Replace("-","");}}
 // A game folder is valid when it contains Donald.exe and the level data.
 public static bool IsGameFolder(string folder){return folder!=null&&File.Exists(Path.Combine(folder,"Donald.exe"))&&Directory.Exists(Path.Combine(folder,"Data","World","Levels"));}
 // Optional CD image next to the game (e.g. "CD\Donald.iso" or any *.iso in the game folder).
 public static string FindDiscImage(string gameFolder){foreach(string p in new[]{Path.Combine(gameFolder,"CD","Donald.iso"),Path.Combine(gameFolder,"Donald.iso")})if(File.Exists(p))return p;
  string saved=Get("DiscImage");if(saved!=null&&File.Exists(saved))return saved;return null;}
 // Log file per day in %APPDATA%\DonaldOnline\logs so problems on a friend's PC can be traced afterwards.
 static readonly object logLock=new object();
 public static string LogFolder{get{string d=Path.Combine(Data,"logs");Directory.CreateDirectory(d);return d;}}
 public static void Log(string message){try{lock(logLock)File.AppendAllText(Path.Combine(LogFolder,"DonaldOnline-"+DateTime.Now.ToString("yyyy-MM-dd")+".log"),DateTime.Now.ToString("HH:mm:ss.fff")+"  "+message+Environment.NewLine);}catch{}}
 // cloudflared (Cloudflare, Apache-2.0) is fetched from its official GitHub release the first time someone hosts a game.
 public static string DownloadCloudflared(){string target=Path.Combine(Data,"cloudflared.exe");if(File.Exists(target))return target;
  string arch=Environment.Is64BitOperatingSystem?"amd64":"386";string url="https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-"+arch+".exe";
  string temp=target+".download";using(var web=new WebClient())web.DownloadFile(url,temp);if(new FileInfo(temp).Length<5000000)throw new Exception("Der Download von cloudflared ist unvollständig.");File.Move(temp,target);return target;}
}
}
