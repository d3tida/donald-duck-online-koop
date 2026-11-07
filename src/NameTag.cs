using System;using System.Drawing;using System.Runtime.InteropServices;using System.Windows.Forms;
namespace DonaldOnline {
public sealed partial class GameBridge {
 // The game camera is the actor of family "Camera" (StdCam). Its global matrix: rows = right, -forward, up.
 uint cameraSO;string cameraLevel;
 public float[] CameraMatrix(){try{/* the engine's standard camera character g_hStdCamCharacter (0x5AE4A0, right after main/next main actor as in ACP_Ray2); a level can have several Camera actors, the first one found was not always the one rendering (6.11.) */{uint std=U(0x5AE4A0);if(std>65536&&U(std)==2&&std!=cameraSO){cameraSO=std;cameraLevel=level;}}if(cameraSO==0||cameraLevel!=level||U(cameraSO)!=2){cameraSO=0;uint at=U(world+8),n=U(world+16);for(uint i=0;i<Math.Min(n,2000)&&at>65536;i++){if(U(at)==2&&Name(0,U(U(U(at+4)+4)))=="Camera"){cameraSO=at;break;}at=U(at+20);}cameraLevel=level;}
  if(cameraSO==0)return null;byte[] g=R(U(cameraSO+36),88);var m=new float[12];for(int i=0;i<12;i++)m[i]=BitConverter.ToSingle(g,4+i*4);return m;}catch{return null;}}
}

// Transparent, click-through window over the game's client area that writes the partner's name above his Donald.
public sealed class NameOverlay:Form {
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out RECT r);[DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr h,ref POINT p);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);[DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]static extern IntPtr SetWindowLong(IntPtr h,int i,IntPtr v);
 [StructLayout(LayoutKind.Sequential)]struct RECT{public int L,T,R,B;}[StructLayout(LayoutKind.Sequential)]struct POINT{public int X,Y;}
 // Vertical field of view of the game camera (radians); the name sits HeadHeight units above the Donald's feet.
 // Checked 30.10. in the lab: projected feet within ~5 px of both Donalds (FOV 0.9 fits); Donald is ~2.1 units tall,
 // so 2.3 puts the name just above the hat (2.0 overlapped it).
 public static float FieldOfView=0.9f,HeadHeight=2.3f,RightSign=-1f;DateTime tuned;
 // Calibration values can be overridden in settings.txt (TagFov, TagHead, TagRight) for testing.
 static float Setting(string key,float fallback){float v;return float.TryParse(AppPaths.Get(key),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v)?v:fallback;}
 string label;PointF? at;IntPtr owner;readonly Font font=new Font("Segoe UI",11f,FontStyle.Bold);
 public NameOverlay(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;BackColor=Color.Magenta;TransparencyKey=Color.Magenta;DoubleBuffered=true;}
 protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x80000|0x20|0x80|0x08000000;return cp;}}
 protected override bool ShowWithoutActivation{get{return true;}}
 // Called by the launcher every frame: game window, partner name, partner feet position and camera (from GameBridge).
 public void Update(IntPtr game,string name,float[] feet,float[] cam){
  if(game==IntPtr.Zero||IsIconic(game)||!IsWindowVisible(game)||string.IsNullOrEmpty(name)||feet==null||cam==null){if(Visible)Hide();return;}
  if(owner!=game){owner=game;SetWindowLong(Handle,-8,game);}
  RECT r;GetClientRect(game,out r);var p=new POINT();ClientToScreen(game,ref p);int w=r.R-r.L,h=r.B-r.T;if(w<50||h<50){if(Visible)Hide();return;}
  if(Left!=p.X||Top!=p.Y||Width!=w||Height!=h)SetBounds(p.X,p.Y,w,h);
  if(DateTime.UtcNow-tuned>TimeSpan.FromSeconds(2)){tuned=DateTime.UtcNow;FieldOfView=Setting("TagFov",0.9f);HeadHeight=Setting("TagHead",2.3f);RightSign=Setting("TagRight",-1f);}
  float dx=feet[0]-cam[0],dy=feet[1]-cam[1],dz=feet[2]+HeadHeight-cam[2];
  float right=dx*cam[3]+dy*cam[4]+dz*cam[5],depth=-(dx*cam[6]+dy*cam[7]+dz*cam[8]),up=dx*cam[9]+dy*cam[10]+dz*cam[11];
  PointF? now=null;if(depth>0.5f){float f=(h/2f)/(float)Math.Tan(FieldOfView/2);now=new PointF(w/2f+RightSign*right/depth*f,h/2f-up/depth*f);}
  if(label!=name||!Equals(at,now)){label=name;at=now;Invalidate();}if(!Visible)Show();}
 protected override void OnPaint(PaintEventArgs e){if(at==null||label==null)return;var g=e.Graphics;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
  SizeF size=g.MeasureString(label,font);float x=at.Value.X-size.Width/2,y=at.Value.Y-size.Height;
  using(var outline=new SolidBrush(Color.FromArgb(20,30,60)))for(int ox=-1;ox<=1;ox++)for(int oy=-1;oy<=1;oy++)if(ox!=0||oy!=0)g.DrawString(label,font,outline,x+ox*2,y+oy*2);
  using(var fill=new SolidBrush(Color.FromArgb(255,214,90)))g.DrawString(label,font,fill,x,y);}
}
}
