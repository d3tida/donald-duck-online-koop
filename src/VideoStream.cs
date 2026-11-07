using System;using System.Collections.Generic;using System.Diagnostics;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Net;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;
namespace DonaldOnline {
// Spectator picture (Tim 7.11.: "eine echte Übertragung"): while the partner watches, the playing side takes its own game
// window picture (PrintWindow with PW_RENDERFULLCONTENT, works even when the window is covered), sends it as JPEG through
// the room connection, and the spectator shows it over his own game window, a few frames buffered for an even picture.
public sealed class VideoFrame{public long Seq;public long Time;public long Qpc;public byte[] Jpeg;}

public sealed class FrameCapture:IDisposable{
 [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
 [StructLayout(LayoutKind.Sequential)]struct RECT{public int L,T,R,B;}
 readonly Func<IntPtr> window;volatile bool active,disposed;long seq;public volatile VideoFrame Latest;
 public int Fps=30,Quality=55;public double Scale=0.8;
 // statistics for the log
 long statBytes;int statFrames;double statCaptureMs;DateTime statSince=DateTime.UtcNow;
 public FrameCapture(Func<IntPtr> window){this.window=window;int v;if(int.TryParse(AppPaths.Get("VideoFps"),out v)&&v>=5&&v<=60)Fps=v;if(int.TryParse(AppPaths.Get("VideoQuality"),out v)&&v>=20&&v<=95)Quality=v;double s;if(double.TryParse(AppPaths.Get("VideoScale"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out s)&&s>=0.3&&s<=1)Scale=s;new Thread(Run){IsBackground=true,Name="Bildaufnahme"}.Start();}
 public bool Active{get{return active;}set{if(value!=active){active=value;AppPaths.Log(value?"Bildübertragung an: Partner schaut zu ("+Fps+" Bilder/s, Qualität "+Quality+")":"Bildübertragung aus");if(!value)Latest=null;}}}
 void Run(){ImageCodecInfo jpeg=null;foreach(var c in ImageCodecInfo.GetImageEncoders())if(c.FormatID==ImageFormat.Jpeg.Guid)jpeg=c;var ps=new EncoderParameters(1);ps.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,(long)Quality);
  Bitmap raw=null,small=null;double next=0;
  while(!disposed){try{if(!active){Thread.Sleep(40);next=0;continue;}double now=Pose.Clock.Elapsed.TotalMilliseconds;if(now<next){Thread.Sleep(Math.Max(1,(int)(next-now)));continue;}next=next==0||now-next>200?now+1000.0/Fps:next+1000.0/Fps;
   IntPtr h=window();if(h==IntPtr.Zero||IsIconic(h)){Thread.Sleep(100);continue;}RECT r;if(!GetClientRect(h,out r))continue;int w=r.R-r.L,hh=r.B-r.T;if(w<64||hh<48||w>4096||hh>4096){Thread.Sleep(100);continue;}
   if(raw==null||raw.Width!=w||raw.Height!=hh){if(raw!=null)raw.Dispose();raw=new Bitmap(w,hh,PixelFormat.Format24bppRgb);}
   double t0=Pose.Clock.Elapsed.TotalMilliseconds;long q0=Stopwatch.GetTimestamp();bool ok;using(var g=Graphics.FromImage(raw)){IntPtr dc=g.GetHdc();try{ok=PrintWindow(h,dc,3);}finally{g.ReleaseHdc(dc);}}if(!ok)continue;
   Bitmap src=raw;if(Scale<0.999){int sw=Math.Max(64,(int)(w*Scale)),sh=Math.Max(48,(int)(hh*Scale));if(small==null||small.Width!=sw||small.Height!=sh){if(small!=null)small.Dispose();small=new Bitmap(sw,sh,PixelFormat.Format24bppRgb);}using(var g=Graphics.FromImage(small)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.Bilinear;g.DrawImage(raw,0,0,sw,sh);}src=small;}
   byte[] data;using(var m=new MemoryStream()){src.Save(m,jpeg,ps);data=m.ToArray();}
   Latest=new VideoFrame{Seq=++seq,Time=(long)Math.Round(t0),Qpc=q0,Jpeg=data};
   statFrames++;statBytes+=data.Length;statCaptureMs+=Pose.Clock.Elapsed.TotalMilliseconds-t0;double secs=(DateTime.UtcNow-statSince).TotalSeconds;if(secs>=20){AppPaths.Log("Bildübertragung: "+(statFrames/secs).ToString("0.0")+" Bilder/s, "+(statBytes*8/secs/1000000).ToString("0.0")+" Mbit/s, "+(statFrames==0?0:statBytes/statFrames/1024)+" KB/Bild, Aufnahme+JPEG "+(statFrames==0?0:statCaptureMs/statFrames).ToString("0.0")+" ms");statFrames=0;statBytes=0;statCaptureMs=0;statSince=DateTime.UtcNow;}
  }catch(Exception e){AppPaths.Log("Bildaufnahme: "+e.Message);Thread.Sleep(500);}}
  if(raw!=null)raw.Dispose();if(small!=null)small.Dispose();}
 public void Dispose(){disposed=true;}
}

// Transport through the room (host: RoomServer keeps the frames; guest: HTTPS requests through the tunnel).
public static class FrameClient{
 static HttpWebRequest Request(string endpoint,string token,string path,string method){Uri uri=new Uri(endpoint);if(uri.Scheme!="https"&&!(uri.Scheme=="http"&&uri.IsLoopback))throw new ArgumentException("HTTPS required");var req=(HttpWebRequest)WebRequest.Create(new Uri(uri,path+token));req.Method=method;req.Timeout=4000;req.ReadWriteTimeout=4000;req.KeepAlive=true;req.Proxy=null;req.ServicePoint.Expect100Continue=false;return req;}
 public static void Upload(string endpoint,string token,VideoFrame f){var req=Request(endpoint,token,"/frame/","POST");req.ContentType="image/jpeg";req.Headers["X-Seq"]=f.Seq.ToString();req.Headers["X-Time"]=f.Time.ToString();req.Headers["X-Qpc"]=f.Qpc.ToString();req.ContentLength=f.Jpeg.Length;using(var s=req.GetRequestStream())s.Write(f.Jpeg,0,f.Jpeg.Length);using(var r=(HttpWebResponse)req.GetResponse()){}}
 public static VideoFrame Fetch(string endpoint,string token,long after){var req=Request(endpoint,token,"/frame/","GET");req.Headers["X-After"]=after.ToString();using(var r=(HttpWebResponse)req.GetResponse()){if(r.StatusCode!=HttpStatusCode.OK)return null;long seq,time;if(!long.TryParse(r.Headers["X-Seq"],out seq)||!long.TryParse(r.Headers["X-Time"],out time))return null;if(r.ContentLength<4||r.ContentLength>FrameLimit)return null;var b=new byte[r.ContentLength];using(var s=r.GetResponseStream()){int off=0;while(off<b.Length){int n=s.Read(b,off,b.Length-off);if(n<=0)throw new EndOfStreamException();off+=n;}}if(b[0]!=0xFF||b[1]!=0xD8)return null;long qpc;long.TryParse(r.Headers["X-Qpc"],out qpc);return new VideoFrame{Seq=seq,Time=time,Qpc=qpc,Jpeg=b};}}
 public const int FrameLimit=600000;
}

// The spectator's view: a borderless window owned by the own game window, covering its client area, not taking focus
// or clicks. Frames are shown by sender time, a fixed delay after the earliest possible arrival (like the camera).
public sealed class VideoOverlay:IDisposable{
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out RECT r);[DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr h,ref POINT p);
 [DllImport("user32.dll")]static extern IntPtr SetWindowLong(IntPtr h,int i,IntPtr v);[DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);[DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
 [StructLayout(LayoutKind.Sequential)]struct RECT{public int L,T,R,B;}[StructLayout(LayoutKind.Sequential)]struct POINT{public int X,Y;}
 sealed class View:Form{public Image Picture;public View(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;BackColor=Color.Black;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque,true);Text="Donald Online – Zuschauer";}
  protected override bool ShowWithoutActivation{get{return true;}}
  protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x08000000/*NOACTIVATE*/|0x80/*TOOLWINDOW*/|0x20/*TRANSPARENT*/|0x80000/*LAYERED*/;return cp;}}
  [DllImport("user32.dll")]static extern bool SetLayeredWindowAttributes(IntPtr h,uint key,byte alpha,uint flags);
  protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);SetLayeredWindowAttributes(Handle,0,255,2);}
  protected override void OnPaint(PaintEventArgs e){var p=Picture;if(p==null){e.Graphics.Clear(Color.Black);return;}e.Graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.Bilinear;e.Graphics.DrawImage(p,0,0,ClientSize.Width,ClientSize.Height);}}
 readonly Func<IntPtr> window;View view;volatile bool wanted,disposed;readonly object gate=new object();readonly List<VideoFrame> queue=new List<VideoFrame>();double offset=double.NaN;long shownSeq;DateTime lastArrival=DateTime.MinValue;IntPtr owner=IntPtr.Zero;
 public double DelayMs=90;
 // statistics
 int statShown,statLate,statAgeN;double statAge;DateTime statSince=DateTime.UtcNow;
 public VideoOverlay(Func<IntPtr> window){this.window=window;double d;if(double.TryParse(AppPaths.Get("VideoDelay"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out d)&&d>=0&&d<=1000)DelayMs=d;var t=new Thread(()=>{view=new View();new Thread(Render){IsBackground=true,Name="Bildanzeige"}.Start();var timer=new System.Windows.Forms.Timer{Interval=50};timer.Tick+=(s,e)=>Place();timer.Start();Application.Run(view);}){IsBackground=true,Name="Zuschauerbild"};t.SetApartmentState(ApartmentState.STA);t.Start();}
 public bool Wanted{set{if(value!=wanted){wanted=value;AppPaths.Log(value?"Zuschauerbild: warte auf Bilder des Partners":"Zuschauerbild aus");if(!value)lock(gate){queue.Clear();offset=double.NaN;}}}}
 // Visible = frames are arriving; otherwise the old camera view below stays.
 public bool Showing{get{return wanted&&DateTime.UtcNow-lastArrival<TimeSpan.FromMilliseconds(800);}}
 public long LastSeq{get{lock(gate)return queue.Count>0?queue[queue.Count-1].Seq:shownSeq;}}
 public void Push(VideoFrame f){if(f==null||!wanted)return;lock(gate){long last=queue.Count>0?queue[queue.Count-1].Seq:shownSeq;if(f.Seq<=last){if(f.Seq<last-1000){queue.Clear();shownSeq=0;offset=double.NaN;}else return;}double now=Pose.Clock.Elapsed.TotalMilliseconds,off=now-f.Time;offset=double.IsNaN(offset)||off<offset?off:offset+0.002*(off-offset);queue.Add(f);if(queue.Count>30)queue.RemoveAt(0);lastArrival=DateTime.UtcNow;}}
 void Render(){var sw=Stopwatch.StartNew();while(!disposed){try{VideoFrame due=null;double now=Pose.Clock.Elapsed.TotalMilliseconds;double wait=10;lock(gate){if(wanted&&!double.IsNaN(offset)){while(queue.Count>0&&queue[0].Time+offset+DelayMs<=now){due=queue[0];queue.RemoveAt(0);if(queue.Count>0&&queue[0].Time+offset+DelayMs<=now){statLate++;continue;}break;}if(queue.Count>0)wait=Math.Max(1,Math.Min(10,queue[0].Time+offset+DelayMs-now));}}
   if(due!=null){Image img;using(var m=new MemoryStream(due.Jpeg))img=Image.FromStream(m);shownSeq=due.Seq;statShown++;if(due.Qpc!=0){double lat=(Stopwatch.GetTimestamp()-due.Qpc)*1000.0/Stopwatch.Frequency;if(lat>0&&lat<5000){statAge+=lat;statAgeN++;}}var v=view;if(v!=null&&v.IsHandleCreated)v.BeginInvoke((Action)(()=>{var old=v.Picture;v.Picture=img;v.Invalidate();if(old!=null)old.Dispose();}));else img.Dispose();}
   double secs=(DateTime.UtcNow-statSince).TotalSeconds;if(secs>=20){if(statShown>0)AppPaths.Log("Zuschauerbild: "+(statShown/secs).ToString("0.0")+" Bilder/s angezeigt, "+statLate+" übersprungen, Puffer "+DelayMs.ToString("0")+" ms"+(statAgeN>0?", Verzögerung Aufnahme bis Anzeige "+(statAge/statAgeN).ToString("0")+" ms (nur auf einem PC messbar)":""));statShown=0;statLate=0;statAge=0;statAgeN=0;statSince=DateTime.UtcNow;}
   Thread.Sleep((int)wait);}catch(Exception e){AppPaths.Log("Zuschauerbild: "+e.Message);Thread.Sleep(200);}}}
 void Place(){try{var v=view;if(v==null)return;IntPtr h=window();bool show=Showing&&h!=IntPtr.Zero&&!IsIconic(h)&&IsWindowVisible(h);if(!show){if(v.Visible)v.Hide();return;}
   if(owner!=h){SetWindowLong(v.Handle,-8,h);owner=h;}RECT r;GetClientRect(h,out r);var p=new POINT();ClientToScreen(h,ref p);int w=r.R-r.L,hh=r.B-r.T;
   if(!v.Visible)v.Show();if(v.Left!=p.X||v.Top!=p.Y||v.Width!=w||v.Height!=hh)SetWindowPos(v.Handle,IntPtr.Zero,p.X,p.Y,w,hh,0x10/*NOACTIVATE*/|0x4/*NOZORDER*/);}catch{}}
 public void Dispose(){disposed=true;try{var v=view;if(v!=null&&v.IsHandleCreated)v.BeginInvoke((Action)(()=>v.Close()));}catch{}}
}
}
