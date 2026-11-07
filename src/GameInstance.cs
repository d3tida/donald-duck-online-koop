using System;
using System.Diagnostics;
using System.IO;
namespace DonaldOnline {
public static partial class GameInstance {
 public static void EnsureNotRunning(string folder) {
  string wanted=MutexTag(Path.Combine(folder,"Donald.exe"));
  foreach(Process p in Process.GetProcessesByName("Donald")) using(p) {
   string other=null;
   try{other=MutexTag(p.MainModule.FileName);}catch{}
   if(other!=null && wanted!=null && other!=wanted)continue;
   string text="Donald Duck läuft bereits (PID "+p.Id+"). Bitte die vorhandene Spielinstanz zuerst beenden und dann erneut starten.";
   if(p.MainWindowHandle==IntPtr.Zero)text+="\n\nDie vorhandene Instanz hat kein sichtbares Fenster. Im Task-Manager kann Donald.exe unter Details beendet werden.";
   throw new InvalidOperationException(text);
  }
 }
 static string MutexTag(string file){try{using(var f=File.OpenRead(file)){f.Position=0xA02C0;byte[] b=new byte[10];if(f.Read(b,0,b.Length)!=b.Length)return null;return Convert.ToBase64String(b);}}catch{return null;}}
}
}
