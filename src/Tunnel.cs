using System;using System.Diagnostics;using System.IO;using System.Text.RegularExpressions;using System.Threading;using System.Net;
namespace DonaldOnline {
public sealed class Tunnel:IDisposable {
 Process process;readonly ManualResetEvent ready=new ManualResetEvent(false);public string Endpoint{get;private set;}
 // cloudflared output goes to %APPDATA%\DonaldOnline\logs\cloudflared.log.
 public Tunnel(string executable,int port){var start=new ProcessStartInfo(executable,"tunnel --no-autoupdate --protocol http2 --url http://127.0.0.1:"+port){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=Path.GetDirectoryName(executable)};process=new Process{StartInfo=start};string log=Path.Combine(AppPaths.LogFolder,"cloudflared.log");DataReceivedEventHandler read=(s,e)=>{if(e.Data==null)return;lock(ready)try{File.AppendAllText(log,e.Data+Environment.NewLine);}catch{}var match=Regex.Match(e.Data,@"https://[a-z0-9-]+\.trycloudflare\.com");if(match.Success){Endpoint=match.Value;}if(Endpoint!=null&&e.Data.Contains("Registered tunnel connection"))ready.Set();};process.ErrorDataReceived+=read;process.OutputDataReceived+=read;process.Start();process.BeginErrorReadLine();process.BeginOutputReadLine();}
 public bool HasExited{get{try{return process==null||process.HasExited;}catch{return true;}}}
 public bool Wait(int milliseconds){Stopwatch watch=Stopwatch.StartNew();if(!ready.WaitOne(milliseconds))return false;while(watch.ElapsedMilliseconds<milliseconds){try{if(Dns.GetHostAddresses(new Uri(Endpoint).Host).Length>0)return true;}catch{}Thread.Sleep(1000);}return false;}public void Dispose(){try{if(process!=null&&!process.HasExited){process.Kill();process.WaitForExit(3000);}}catch{}if(process!=null)process.Dispose();ready.Dispose();}
}
}
