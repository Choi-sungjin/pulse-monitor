using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
[assembly:System.Reflection.AssemblyTitle("Pulse System Widget")]
[assembly:System.Reflection.AssemblyVersion("1.1.1.0")]

namespace PulseWidget {
    static class Program {
        internal static Icon LoadAppIcon() {
            using(var stream=typeof(Program).Assembly.GetManifestResourceStream("PulseWidget.AppIcon"))
            using(var icon=new Icon(stream,32,32))return (Icon)icon.Clone();
        }
        [STAThread] static int Main(string[] args) {
            if(args.Length>=2 && args[0]=="--self-test")return SelfTest(args[1]);
            if(args.Length>=2 && args[0]=="--probe") {
                using(var sensors=new SensorService(SettingsStore.Load())) {sensors.Start();var shots=new List<Snapshot>();for(int i=0;i<8;i++){Thread.Sleep(1100);if(sensors.Latest!=null)shots.Add(sensors.Latest);}Json.Save(args[1],new {Devices=sensors.Devices,Samples=shots});return shots.Count>0 && shots[shots.Count-1].Cpu.Value.HasValue?0:1;}
            }
            bool first;
            using(var mutex=new Mutex(true,@"Local\PulseWidget-"+Environment.UserName,out first)) {
                if(!first)return 0;
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e) {try {Json.Save(Path.Combine(SettingsStore.DirectoryPath,"last-error.json"),new {When=DateTime.UtcNow,Error=e.Exception.ToString()});}catch{}MessageBox.Show("위젯 오류가 발생했습니다. 로컬 오류 기록을 확인해 주세요.","Pulse");Application.Exit();};
                string diag=args.Length>=2 && args[0]=="--diagnostics"?Path.GetFullPath(args[1]):null;
                Application.Run(new Widget(SettingsStore.Load(),diag));mutex.ReleaseMutex();
            }
            return 0;
        }
        static int SelfTest(string path) {
            var checks=new List<string>();
            try {
                Action<bool,string> require=(ok,name)=>{if(!ok)throw new Exception(name);checks.Add(name);};
                Rectangle left=new Rectangle(-1920,0,1920,1040);
                require(Placement.Clamp(new Rectangle(-2500,1200,820,104),left)==new Rectangle(-1920,936,820,104),"negative-coordinate monitor clamping");
                require(Placement.Snap(new Rectangle(500,925,820,104),new Rectangle(0,0,1920,1040),22,8).Bottom==1032,"taskbar snap gap");
                require(Placement.Clamp(new Rectangle(0,0,2000,900),new Rectangle(0,0,800,600)).Size==new Size(800,600),"oversized window recovery");
                require(!Reading.Missing("미지원","test").Value.HasValue,"unsupported is null not zero");
                require(!Reading.Ok(Double.NaN,"","test").Value.HasValue,"invalid numeric sensor rejected");
                require(Nvml.Error(4)=="권한 부족" && Nvml.Error(3)=="미지원" && Nvml.Error(999)=="확인 불가","NVML error classification");
                var s=new Settings {Width=5,Items=new List<string>{"NET","bad","NET"},Interval=33};s.Normalize();require(s.Width==500 && s.Items.Count==1 && s.Items[0]=="NET" && s.Interval==1000,"corrupt settings normalization");
                var clone=Json.Read<Settings>(Json.Write(s));require(clone.Items[0]=="NET" && clone.Width==500,"settings round trip");
                Json.Save(path,new {Passed=true,Checks=checks});return 0;
            }catch(Exception ex){Json.Save(path,new {Passed=false,Checks=checks,Failure=ex.Message});return 1;}
        }
    }
}
