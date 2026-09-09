using System;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PulseWidget {
    // Native notification-area icons have stable GUIDs so Explorer can remember placement.
    // No taskbar injection, Explorer patching, drivers or always-visible overlay window.
    public sealed class TrayMetrics : IDisposable {
        public const int CallbackMessage=0x803B;
        public static readonly int TaskbarCreated=(int)RegisterWindowMessage("TaskbarCreated");
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct NotifyData {
            public uint Size;public IntPtr Window;public uint Id,Flags,Callback;public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Tip;
            public uint State,StateMask;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string InfoTitle;
            public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
        }
        [StructLayout(LayoutKind.Sequential)] struct Identifier { public uint Size;public IntPtr Window;public uint Id;public Guid Guid; }
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,EntryPoint="Shell_NotifyIconW")] static extern bool Notify(uint message,ref NotifyData data);
        [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref Identifier id,out Native.Rect rect);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        readonly IntPtr owner;
        readonly Action click,context;
        readonly Guid[] guids={new Guid("F2D6DF2F-04DF-4E52-824F-1BBF7C303601"),new Guid("F2D6DF2F-04DF-4E52-824F-1BBF7C303602"),new Guid("F2D6DF2F-04DF-4E52-824F-1BBF7C303603")};
        readonly string[] labels={"C","G","R"};
        readonly Color[] colors={Color.FromArgb(100,236,209),Color.FromArgb(121,191,255),Color.FromArgb(210,172,255)};
        readonly string[] last={"","",""};
        readonly IntPtr[] icons=new IntPtr[3];
        readonly bool[] added=new bool[3];
        double?[] values=new double?[3];
        string[] tips={"CPU · 수집 중","GPU · 수집 중","RAM · 수집 중"};
        int size;
        public TrayMetrics(IntPtr window,Action onClick,Action onContext) {owner=window;click=onClick;context=onContext;size=Math.Max(16,(int)Math.Round(16*GetDpiForSystem()/96.0));Refresh();}
        NotifyData Data(int index) {return new NotifyData {Size=(uint)Marshal.SizeOf(typeof(NotifyData)),Window=owner,Id=(uint)(index+1),Guid=guids[index],Flags=0x27,Callback=CallbackMessage,Icon=icons[index],Tip=tips[index],Info="",InfoTitle=""};}
        public static string Number(double? value) {return value.HasValue?Math.Max(0,Math.Min(100,Math.Round(value.Value))).ToString("0"):"--";}
        public static Bitmap Render(int pixels,string label,double? value,Color color) {
            var bitmap=new Bitmap(pixels,pixels,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(Color.Transparent);g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
                float ratio=pixels/16f;string number=Number(value);
                using(var brush=new SolidBrush(color))
                using(var labelFont=new Font("Consolas",5.7f*ratio,FontStyle.Bold,GraphicsUnit.Pixel))
                using(var valueFont=new Font("Consolas",(number.Length==3?9.4f:11.8f)*ratio,FontStyle.Bold,GraphicsUnit.Pixel))
                using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap}) {
                    g.DrawString(label,labelFont,brush,new RectangleF(0,-.7f*ratio,pixels,6.5f*ratio),format);
                    g.DrawString(number,valueFont,brush,new RectangleF(-ratio,5*ratio,pixels+2*ratio,11*ratio),format);
                }
            }return bitmap;
        }
        public void Update(Snapshot s,bool stale) {
            values[0]=stale?null:s.Cpu.Value;values[1]=stale?null:s.Gpu.Value;
            values[2]=stale || !(s.RamTotal.Value>0)?null:s.RamUsed.Value/s.RamTotal.Value*100;
            tips[0]="CPU "+(stale?"확인 불가":s.Cpu.Format("0.0"))+" · 온도 "+(stale?"확인 불가":s.CpuTemp.Format("0"))+"\n클릭: 상세 · 우클릭: 설정";
            tips[1]="GPU "+(stale?"확인 불가":s.Gpu.Format("0.0"))+" · "+(stale?"확인 불가":s.GpuTemp.Format("0"))+"\n"+s.GpuName;
            tips[2]="RAM "+Number(values[2])+"% · "+Widget.Bytes(s.RamUsed.Value,false)+" / "+Widget.Bytes(s.RamTotal.Value,false)+"\n↓ "+Widget.Bytes(s.Down.Value,true)+" · ↑ "+Widget.Bytes(s.Up.Value,true);
            if(stale)tips[2]="RAM 확인 불가 · 업데이트 지연";
            for(int i=0;i<3;i++)if(tips[i].Length>127)tips[i]=tips[i].Substring(0,127);
            Refresh();
        }
        void Refresh() {
            for(int i=0;i<3;i++) {
                string key=size+":"+Number(values[i]);
                if(last[i]!=key || icons[i]==IntPtr.Zero) {
                    IntPtr old=icons[i];using(var bitmap=Render(size,labels[i],values[i],colors[i]))icons[i]=bitmap.GetHicon();last[i]=key;
                    var data=Data(i);if(!added[i]){added[i]=Notify(0,ref data);data.Version=4;if(added[i])Notify(4,ref data);}else Notify(1,ref data);
                    if(old!=IntPtr.Zero)DestroyIcon(old);
                } else {var data=Data(i);if(!added[i]){added[i]=Notify(0,ref data);data.Version=4;if(added[i])Notify(4,ref data);}else Notify(1,ref data);}
            }
        }
        public bool ProcessMessage(ref Message message) {
            if(message.Msg==TaskbarCreated) {for(int i=0;i<3;i++)added[i]=false;Refresh();return false;}
            if(message.Msg!=CallbackMessage)return false;
            int code=(int)(message.LParam.ToInt64()&0xffff);
            if(code==0x400 || code==0x401)click();else if(code==0x7b)context();
            return true;
        }
        public object[] Positions() {
            var result=new object[3];
            for(int i=0;i<3;i++){var id=new Identifier {Size=(uint)Marshal.SizeOf(typeof(Identifier)),Window=owner,Id=(uint)(i+1),Guid=guids[i]};Native.Rect r;int rc=Shell_NotifyIconGetRect(ref id,out r);result[i]=new {Label=labels[i],Registered=added[i],Result=rc,X=r.Left,Y=r.Top,Width=r.Right-r.Left,Height=r.Bottom-r.Top,Value=Number(values[i])};}return result;
        }
        public void ExportPreview(string path) {
            using(var bitmap=new Bitmap(size*5,size))using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(Color.FromArgb(25,25,25));for(int i=0;i<3;i++)using(var icon=Render(size,labels[i],values[i],colors[i]))g.DrawImageUnscaled(icon,i*size*2,0);
                bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        public void Dispose() {for(int i=0;i<3;i++){var data=Data(i);Notify(2,ref data);if(icons[i]!=IntPtr.Zero)DestroyIcon(icons[i]);icons[i]=IntPtr.Zero;}}
    }
}
