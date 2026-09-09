using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PulseWidget {
    public sealed class Widget : Form {
        Settings settings;
        readonly SensorService sensors;
        readonly Timer timer=new Timer();
        TrayMetrics tray;
        readonly ContextMenuStrip menu=new ContextMenuStrip();
        readonly ToolTip tooltip=new ToolTip();
        readonly Button expand=new Button(), gear=new Button();
        readonly string diagnosticPath;
        Snapshot sample;
        DateTime lastData=DateTime.MinValue,lastDiagnostic=DateTime.MinValue;
        DateTime popupOpened=DateTime.MinValue;
        bool userHidden,fullscreenHidden,dragging,moved,closing,ready,diagnosticCaptured;
        Point dragMouse,dragOrigin;
        float dpi=1;
        int detailScroll;
        Rectangle workArea;
        SettingsForm settingsForm;
        bool hotkey;
        public Widget(Settings s,string diagnostics) {
            settings=s;settings.TrayOnly=true;userHidden=true;diagnosticPath=diagnostics;
            Text="Pulse · 시스템 모니터";AccessibleName="Pulse 시스템 모니터";
            Icon=Program.LoadAppIcon();
            Disposed+=delegate {if(Icon!=null)Icon.Dispose();};
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;KeyPreview=true;
            BackColor=Color.FromArgb(21,25,34);ForeColor=Color.FromArgb(235,239,247);
            AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);
            TopMost=s.Topmost;Opacity=s.Opacity;
            ConfigureButton(expand,"⌄","세부 정보 펼치기 / 접기",delegate {ToggleExpand();});
            ConfigureButton(gear,"···","위젯 설정 열기",OpenSettings);
            ContextMenuStrip=menu;menu.Opening+=delegate {BuildMenu();};
            timer.Interval=250;timer.Tick+=Tick;
            sensors=new SensorService(s);sensors.Start();
            SystemEvents.DisplaySettingsChanged+=DisplayChanged;SystemEvents.PowerModeChanged+=PowerChanged;
        }
        protected override void SetVisibleCore(bool value) {
            if(!ready) {
                if(!IsHandleCreated)CreateHandle();
                dpi=Native.GetDpiForWindow(Handle)/96f;LayoutWidget(false);RestoreLocation();ApplyClickThrough();
                tray=new TrayMetrics(Handle,TogglePopup,delegate{BuildMenu();menu.Show(Cursor.Position);});
                hotkey=Native.RegisterHotKey(Handle,1,0x4003,0x50);ready=true;timer.Start();
            }
            base.SetVisibleCore(value && !userHidden);
        }
        void TogglePopup() {if(Visible){userHidden=true;Hide();}else{userHidden=false;fullscreenHidden=false;RestoreWidget();}}
        protected override void OnDeactivate(EventArgs e) {base.OnDeactivate(e);if(!closing && ready)BeginInvoke((Action)delegate{if(!ContainsFocus && !menu.Visible && (settingsForm==null || settingsForm.IsDisposed)){userHidden=true;Hide();}});}
        protected override void OnKeyDown(KeyEventArgs e) {base.OnKeyDown(e);if(e.KeyCode==Keys.Escape){userHidden=true;Hide();e.Handled=true;}}
        protected override bool ShowWithoutActivation {get{return true;}}
        void ConfigureButton(Button b,string text,string help,Action action) {
            b.Text=text;b.AccessibleName=help;b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.BackColor=BackColor;b.ForeColor=Color.FromArgb(163,179,203);b.TabStop=true;
            b.Click+=delegate {action();};Controls.Add(b);tooltip.SetToolTip(b,help);
        }
        int Px(double value) {return (int)Math.Round(value*dpi);}
        Rectangle Work(Screen screen) {
            // WorkingArea honors the taskbar. Keep a small gap even with auto-hide.
            return screen.WorkingArea;
        }
        void LayoutWidget(bool keepBottom) {
            int bottom=Bottom;
            int minimum=(int)(settings.Items.Count*(142+Math.Max(0,settings.TextSize-10)*9)+36);
            int width=Px(Math.Max(settings.Width,minimum));
            int height=Px(104+Math.Max(0,settings.TextSize-10)*4+(settings.Expanded?272:0));
            if(settings.TrayOnly){width=Px(360);height=Px(settings.Expanded?440:304);}
            Size=new Size(width,height);
            if(keepBottom)Top=bottom-Height;
            expand.Bounds=new Rectangle(Width-Px(64),Px(6),Px(28),Px(24));
            gear.Bounds=new Rectangle(Width-Px(34),Px(6),Px(28),Px(24));
            expand.Text=settings.Expanded?"⌃":"⌄";
            using(var path=Rounded(new RectangleF(0,0,Width-1,Height-1),Px(16))) {var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}
            Invalidate();
        }
        static GraphicsPath Rounded(RectangleF r,float radius) {
            var p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
        }
        void RestoreLocation() {
            var screen=Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==settings.Monitor)??Screen.PrimaryScreen;
            workArea=Work(screen);
            if(settings.X==Int32.MinValue || !Screen.AllScreens.Any(x=>x.DeviceName==settings.Monitor)) {
                Bounds=Placement.Clamp(new Rectangle(workArea.Left+(workArea.Width-Width)/2,workArea.Bottom-Height-Px(8),Width,Height),workArea);
            } else Bounds=Placement.Clamp(new Rectangle(settings.X,settings.Y,Width,Height),workArea);
            SavePosition();
        }
        void SavePosition() {
            if(WindowState!=FormWindowState.Normal)return;
            settings.X=Left;settings.Y=Top;settings.Monitor=Screen.FromRectangle(Bounds).DeviceName;Save();
        }
        void Save() {try {SettingsStore.Save(settings);}catch(Exception ex) {tooltip.SetToolTip(gear,"설정 저장 실패: "+ex.Message);}}
        void ApplyClickThrough() {
            // WS_EX_TRANSPARENT click-through requires a layered window, even at the 100% setting.
            // WinForms removes WS_EX_LAYERED at opacity 1; retain it without installing hooks.
            bool through=!settings.TrayOnly && settings.ClickThrough;
            Opacity=through?Math.Min(settings.Opacity,0.99):settings.Opacity;
            long style=Native.GetWindowLongPtr(Handle,-20).ToInt64();style=through?style|0x20:style&~0x20;Native.SetWindowLongPtr(Handle,-20,new IntPtr(style));
        }
        void ToggleExpand() {settings.Expanded=!settings.Expanded;detailScroll=0;LayoutWidget(true);Bounds=Placement.Clamp(Bounds,Work(Screen.FromRectangle(Bounds)));SavePosition();}
        void Tick(object sender,EventArgs args) {
            var now=sensors.Latest;
            if(now!=null && now.Timestamp!=lastData) {sample=now;lastData=now.Timestamp;UpdateTooltip();tray.Update(sample,IsStale());Invalidate();}
            else if(sample!=null && IsStale()){tray.Update(sample,true);Invalidate();}
            if(settings.TrayOnly && Visible && DateTime.UtcNow-popupOpened>TimeSpan.FromMilliseconds(600) && !menu.Visible && (settingsForm==null || settingsForm.IsDisposed) && Native.GetForegroundWindow()!=Handle){userHidden=true;Hide();}
            bool full=settings.HideFullscreen && ForegroundFullscreen();
            if(full!=fullscreenHidden) {fullscreenHidden=full;if(full)Hide();else if(!userHidden)RestoreWidget();}
            if(!String.IsNullOrEmpty(diagnosticPath) && DateTime.UtcNow-lastDiagnostic>TimeSpan.FromSeconds(2)) {
                lastDiagnostic=DateTime.UtcNow;
                try { using(var p=Process.GetCurrentProcess()) Json.Save(diagnosticPath,new { Timestamp=DateTime.UtcNow,Sample=sample,Settings=settings,Visible=Visible,ShowInTaskbar=ShowInTaskbar,TrayIcons=tray.Positions(),TopMost=TopMost,ContainsFocus=ContainsFocus,Foreground=Native.GetForegroundWindow().ToInt64(),Handle=Handle.ToInt64(),Bounds=new {X=Left,Y=Top,Width=Width,Height=Height},Dpi=dpi,WorkArea=new {X=workArea.X,Y=workArea.Y,Width=workArea.Width,Height=workArea.Height},Devices=sensors.Devices,WorkingSet=p.WorkingSet64,PrivateBytes=p.PrivateMemorySize64,Handles=p.HandleCount,CpuSeconds=p.TotalProcessorTime.TotalSeconds,Hotkey=hotkey,FullscreenHidden=fullscreenHidden,Stale=IsStale()});
                    if(Visible && sample!=null){using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,ClientRectangle);bitmap.Save(Path.Combine(Path.GetDirectoryName(diagnosticPath),"tray-popup-preview.png"),System.Drawing.Imaging.ImageFormat.Png);}}
                    if(!diagnosticCaptured && sample!=null && sample.Cpu.Value.HasValue) {tray.ExportPreview(Path.Combine(Path.GetDirectoryName(diagnosticPath),"tray-preview.png"));diagnosticCaptured=true;}
                }catch{}
            }
        }
        bool ForegroundFullscreen() {
            if(settingsForm!=null && !settingsForm.IsDisposed)return false;
            IntPtr h=Native.GetForegroundWindow();if(h==IntPtr.Zero || h==Handle || Native.IsIconic(h))return false;
            var name=new StringBuilder(128);Native.GetClassName(h,name,128);if(new[]{"Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd"}.Contains(name.ToString()))return false;
            Native.Rect r;if(!Native.GetWindowRect(h,out r))return false;
            var target=Screen.FromRectangle(Bounds);var b=target.Bounds;
            return r.Left<=b.Left && r.Top<=b.Top && r.Right>=b.Right && r.Bottom>=b.Bottom;
        }
        void RestoreWidget() {userHidden=false;popupOpened=DateTime.UtcNow;WindowState=FormWindowState.Normal;LayoutWidget(false);var screen=Screen.FromPoint(Cursor.Position);workArea=Work(screen);Bounds=Placement.Clamp(new Rectangle(workArea.Right-Width-Px(8),workArea.Bottom-Height-Px(8),Width,Height),workArea);Show();TopMost=true;ApplyClickThrough();Activate();}
        bool IsStale() {return sample==null || DateTime.UtcNow-sample.Timestamp>TimeSpan.FromMilliseconds(Math.Max(4000,settings.Interval*3));}
        void UpdateTooltip() {
            if(sample==null)return;
            string info=sample.CpuName+"\n"+sample.GpuName+"\n"+sample.CpuTempLabel+": "+sample.CpuTemp.Format("0")+"\n"+sample.CpuTemp.Source+"\n"+sample.ProviderStatus+"\n네트워크: "+sample.NetworkName+"\n빈 영역 드래그 · 클릭하면 상세 보기 · 우클릭 메뉴";
            AccessibleDescription=info;
        }
        public static string Bytes(double? value,bool rate) {
            if(!value.HasValue)return "확인 불가";double v=value.Value;
            string[] units={"B","KB","MB","GB","TB"};int i=0;while(v>=1024 && i<4){v/=1024;i++;}
            return v.ToString(i==0?"0":"0.0")+" "+units[i]+(rate?"/s":"");
        }
        string MemoryText(Reading used,Reading total) {
            if(!used.Value.HasValue)return used.Status;
            if(!total.Value.HasValue)return Bytes(used.Value,false)+" / ?";
            return (used.Value.Value/1073741824).ToString("0.0")+" / "+(total.Value.Value/1073741824).ToString("0.0")+" GB";
        }
        void TextDraw(Graphics g,string text,float size,Color color,RectangleF rect,bool mono) {
            // Graphics.DpiX already reflects monitor DPI. Use explicit pixels to avoid double scaling points.
            using(var font=new Font(mono?"Consolas":"Segoe UI",size*(96f/72f)*dpi,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(color))
            using(var format=new StringFormat {Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap,LineAlignment=StringAlignment.Center}) g.DrawString(text,font,brush,rect,format);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            if(settings.TrayOnly){PaintPopup(g);return;}
            Color muted=Color.FromArgb(143,157,182),accent=Color.FromArgb(97,216,199),white=ForeColor;
            using(var p=Rounded(new RectangleF(0.5f,0.5f,Width-1.5f,Height-1.5f),Px(16)))using(var pen=new Pen(Color.FromArgb(57,66,83),1))g.DrawPath(pen,p);
            bool stale=IsStale();
            using(var brush=new SolidBrush(stale?Color.FromArgb(246,179,91):accent))g.FillEllipse(brush,Px(15),Px(15),Px(6),Px(6));
            string title="PULSE  /  "+(stale?(sample==null?"센서 연결 중":"업데이트 지연"):(settings.Locked?"위치 잠금":"시스템 모니터"));
            TextDraw(g,title,8.5f,muted,new RectangleF(Px(27),Px(6),Width-Px(100),Px(24)),false);
            int cardTop=Px(33),cardHeight=Px(64+(settings.TextSize-10)*4), n=settings.Items.Count;
            float cardWidth=(Width-Px(28))/(float)n;
            for(int i=0;i<n;i++) {
                string id=settings.Items[i];float x=Px(14)+i*cardWidth;
                if(i>0)using(var pen=new Pen(Color.FromArgb(43,51,66)))g.DrawLine(pen,x-Px(6),cardTop+Px(5),x-Px(6),cardTop+cardHeight-Px(10));
                string value="수집 중",sub="",label=id;double? percent=null;bool warn=false;
                if(sample!=null) {
                    switch(id) {
                        case "CPU": value=sample.Cpu.Format("0.0");sub="온도 "+sample.CpuTemp.Format("0");percent=sample.Cpu.Value;warn=sample.CpuTemp.Value>=settings.TempWarning;break;
                        case "GPU": value=sample.Gpu.Format("0.0");sub="온도 "+sample.GpuTemp.Format("0");percent=sample.Gpu.Value;warn=sample.GpuTemp.Value>=settings.TempWarning;break;
                        case "RAM": value=MemoryText(sample.RamUsed,sample.RamTotal);percent=sample.RamTotal.Value>0?sample.RamUsed.Value/sample.RamTotal.Value*100:null;sub=percent.HasValue?percent.Value.ToString("0.0")+"% 사용 중":"확인 불가";break;
                        case "VRAM": value=MemoryText(sample.VramUsed,sample.VramTotal);percent=sample.VramTotal.Value>0?sample.VramUsed.Value/sample.VramTotal.Value*100:null;sub="전용 GPU 메모리";break;
                        case "NET": value="↓ "+(sample.Down.Value.HasValue?Bytes(sample.Down.Value,true):sample.Down.Status);sub="↑ "+(sample.Up.Value.HasValue?Bytes(sample.Up.Value,true):sample.Up.Status);break;
                    }
                }
                if(stale && sample!=null) {value="확인 불가";sub="업데이트 지연";percent=null;warn=false;}
                Color c=warn?Color.FromArgb(255,177,107):accent;
                TextDraw(g,label,8,muted,new RectangleF(x,cardTop,cardWidth-Px(10),Px(16)),true);
                TextDraw(g,value,settings.TextSize,white,new RectangleF(x,cardTop+Px(17),cardWidth-Px(10),Px(23+(settings.TextSize-10)*2)),true);
                TextDraw(g,sub,8.5f,warn?c:muted,new RectangleF(x,cardTop+Px(40+(settings.TextSize-10)*2),cardWidth-Px(10),Px(17)),false);
                float barY=cardTop+cardHeight-Px(3),barWidth=cardWidth-Px(16);
                if(id!="NET") {using(var brush=new SolidBrush(Color.FromArgb(45,54,69)))g.FillRectangle(brush,x,barY,barWidth,Px(2));if(percent.HasValue)using(var brush=new SolidBrush(c))g.FillRectangle(brush,x,barY,(float)(barWidth*Math.Max(0,Math.Min(100,percent.Value))/100),Px(2));}
            }
            if(settings.Expanded)PaintDetails(g,muted,accent,stale);
        }
        void PaintPopup(Graphics g) {
            Color muted=Color.FromArgb(151,167,188);
            using(var path=Rounded(new RectangleF(.5f,.5f,Width-1.5f,Height-1.5f),Px(14)))using(var pen=new Pen(Color.FromArgb(61,74,93)))g.DrawPath(pen,path);
            TextDraw(g,"PULSE · 시스템 상태",11,ForeColor,new RectangleF(Px(16),Px(8),Width-Px(90),Px(25)),false);
            var rows=new List<KeyValuePair<string,string>>();
            if(sample!=null){
                rows.Add(new KeyValuePair<string,string>("CPU",sample.Cpu.Format("0.0")));
                rows.Add(new KeyValuePair<string,string>("GPU",sample.Gpu.Format("0.0")));
                rows.Add(new KeyValuePair<string,string>("RAM",MemoryText(sample.RamUsed,sample.RamTotal)));
                rows.Add(new KeyValuePair<string,string>("전용 GPU 메모리",MemoryText(sample.VramUsed,sample.VramTotal)));
                rows.Add(new KeyValuePair<string,string>(sample.CpuTempLabel,sample.CpuTemp.Format("0")));
                rows.Add(new KeyValuePair<string,string>("GPU 온도",sample.GpuTemp.Format("0")));
                rows.Add(new KeyValuePair<string,string>("다운로드 ↓",Bytes(sample.Down.Value,true)));
                rows.Add(new KeyValuePair<string,string>("업로드 ↑",Bytes(sample.Up.Value,true)));
                if(settings.Expanded)foreach(var item in sample.Extras)rows.Add(new KeyValuePair<string,string>(item.Name,item.Reading.Unit=="B/s"?Bytes(item.Reading.Value,true):item.Reading.Format("0.0")));
            }
            int count=settings.Expanded?13:8;detailScroll=Math.Max(0,Math.Min(detailScroll,Math.Max(0,rows.Count-count)));
            for(int i=0;i<count && i+detailScroll<rows.Count;i++){
                var row=rows[i+detailScroll];TextDraw(g,row.Key,10,muted,new RectangleF(Px(16),Px(42+i*28),Px(155),Px(25)),false);
                TextDraw(g,IsStale()?"확인 불가":row.Value,10,ForeColor,new RectangleF(Px(173),Px(42+i*28),Width-Px(187),Px(25)),false);
            }
            TextDraw(g,sample==null?"센서 연결 중":"C CPU · G GPU · R RAM  |  바깥 클릭으로 닫기",8,muted,new RectangleF(Px(16),Height-Px(30),Width-Px(32),Px(23)),false);
        }
        void PaintDetails(Graphics g,Color muted,Color accent,bool stale) {
            int top=Px(109+(settings.TextSize-10)*4);
            using(var pen=new Pen(Color.FromArgb(43,51,66)))g.DrawLine(pen,Px(16),top,Width-Px(16),top);
            string device=sample==null?"장치 확인 중":sample.GpuName;
            TextDraw(g,device,10,ForeColor,new RectangleF(Px(18),top+Px(7),Width-Px(36),Px(27)),false);
            var rows=new List<KeyValuePair<string,string>>();
            if(sample!=null) {
                rows.Add(new KeyValuePair<string,string>(sample.CpuTempLabel,stale?"확인 불가":sample.CpuTemp.Format("0")));
                rows.Add(new KeyValuePair<string,string>("GPU 온도",stale?"확인 불가":sample.GpuTemp.Format("0")));
                foreach(var r in sample.Extras)rows.Add(new KeyValuePair<string,string>(r.Name,stale?"확인 불가":r.Reading.Unit=="B/s"?Bytes(r.Reading.Value,true):r.Reading.Format("0.0")));
            }
            int count=12;detailScroll=Math.Max(0,Math.Min(detailScroll,Math.Max(0,rows.Count-count)));
            for(int j=0;j<count && j+detailScroll<rows.Count;j++) {
                var row=rows[j+detailScroll];int col=j%2,line=j/2;float x=Px(18)+col*(Width-Px(36))/2f;float w=(Width-Px(48))/2f;
                TextDraw(g,row.Key,9,muted,new RectangleF(x,top+Px(40+line*28),w*.65f,Px(25)),false);
                TextDraw(g,row.Value,9,ForeColor,new RectangleF(x+w*.65f,top+Px(40+line*28),w*.35f,Px(25)),false);
            }
            string status=sample==null?"센서 확인 중":sample.ProviderStatus;
            TextDraw(g,status+" · 네트워크: "+(sample==null?"—":sample.NetworkName),8,muted,new RectangleF(Px(18),Height-Px(51),Width-Px(36),Px(22)),false);
            TextDraw(g,"미지원 센서는 공급자에서 제공하지 않는 항목입니다.  "+(rows.Count>count?"스크롤로 더 보기":"온도 경고 기준: "+settings.TempWarning+"°C"),8,muted,new RectangleF(Px(18),Height-Px(28),Width-Px(36),Px(20)),false);
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;dragging=true;moved=false;dragMouse=PointToScreen(e.Location);dragOrigin=Location;Capture=true;
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);if(!dragging || settings.Locked)return;
            var point=PointToScreen(e.Location);int dx=point.X-dragMouse.X,dy=point.Y-dragMouse.Y;
            if(Math.Abs(dx)>SystemInformation.DragSize.Width/2 || Math.Abs(dy)>SystemInformation.DragSize.Height/2)moved=true;
            if(moved)Location=new Point(dragOrigin.X+dx,dragOrigin.Y+dy);
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);if(!dragging)return;dragging=false;Capture=false;
            if(moved) {workArea=Work(Screen.FromRectangle(Bounds));Bounds=Placement.Snap(Bounds,workArea,Px(22),Px(8));SavePosition();}
            else if(e.Button==MouseButtons.Left && !settings.TrayOnly)ToggleExpand();
        }
        protected override void OnMouseWheel(MouseEventArgs e) {base.OnMouseWheel(e);if(settings.Expanded){detailScroll+=e.Delta<0?2:-2;Invalidate();}}
        protected override void WndProc(ref Message m) {
            if(tray!=null && tray.ProcessMessage(ref m))return;
            if(m.Msg==0x312 && m.WParam.ToInt32()==1) {userHidden=!userHidden;if(userHidden)Hide();else RestoreWidget();return;}
            base.WndProc(ref m);
            if(m.Msg==0x2E0 && ready && !closing) BeginInvoke((Action)delegate {dpi=Native.GetDpiForWindow(Handle)/96f;LayoutWidget(false);Bounds=Placement.Clamp(Bounds,Work(Screen.FromRectangle(Bounds)));SavePosition();});
        }
        void DisplayChanged(object sender,EventArgs e) {if(!closing && IsHandleCreated)BeginInvoke((Action)delegate {workArea=Work(Screen.FromRectangle(Bounds));Bounds=Placement.Clamp(Bounds,workArea);SavePosition();});}
        void PowerChanged(object sender,PowerModeChangedEventArgs e) {if(e.Mode==PowerModes.Resume)sensors.Resume();}
        ToolStripMenuItem ActionItem(string text,Action action) {var item=new ToolStripMenuItem(text);item.Click+=delegate{action();};return item;}
        ToolStripMenuItem CheckItem(string text,bool check,Action action) {var item=ActionItem(text,action);item.Checked=check;return item;}
        void Apply() {settings.Normalize();TopMost=settings.Topmost;Opacity=settings.Opacity;ApplyClickThrough();sensors.Change(settings);Save();Invalidate();}
        void BuildMenu() {
            // Dispose old menu items; repeated opening must not accumulate native/GDI handles.
            while(menu.Items.Count>0){var old=menu.Items[0];menu.Items.RemoveAt(0);old.Dispose();}
            menu.Items.Add(ActionItem(userHidden?"상세 정보 보기(&H)":"상세 정보 닫기(&H)",TogglePopup));
            if(!settings.TrayOnly){
            menu.Items.Add(CheckItem("항상 위에 표시(&T)",settings.Topmost,delegate {settings.Topmost=!settings.Topmost;Apply();}));
            menu.Items.Add(CheckItem("위치 잠금(&L)",settings.Locked,delegate {settings.Locked=!settings.Locked;Apply();}));
            menu.Items.Add(CheckItem("클릭 통과 · 트레이에서 해제(&C)",settings.ClickThrough,delegate {settings.ClickThrough=!settings.ClickThrough;Apply();}));
            menu.Items.Add(CheckItem("전체 화면 앱에서 숨기기(&F)",settings.HideFullscreen,delegate {settings.HideFullscreen=!settings.HideFullscreen;Apply();}));
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(ActionItem("센서 및 갱신 설정(&S)…",OpenSettings));
            var gpu=new ToolStripMenuItem("GPU 선택");
            foreach(var d in sensors.Devices) {var chosen=d;gpu.DropDownItems.Add(CheckItem(d.Name,(sample!=null && sample.GpuId==d.Id),delegate {settings.GpuId=chosen.Id;Apply();}));}
            menu.Items.Add(gpu);
            var cpu=new ToolStripMenuItem("CPU 온도 센서");
            cpu.DropDownItems.Add(CheckItem("자동 (CPU 패키지)",String.IsNullOrEmpty(settings.CpuSensorId),delegate {settings.CpuSensorId="";Apply();}));
            foreach(var r in sensors.CpuSensors) {var chosen=r;cpu.DropDownItems.Add(CheckItem(r.Name,settings.CpuSensorId==r.Id,delegate {settings.CpuSensorId=chosen.Id;Apply();}));}
            menu.Items.Add(cpu);
            var interval=new ToolStripMenuItem("갱신 주기");foreach(int ms in new[]{500,1000,2000,5000}){int selected=ms;interval.DropDownItems.Add(CheckItem((ms/1000.0).ToString("0.#")+"초",settings.Interval==ms,delegate{settings.Interval=selected;Apply();}));}menu.Items.Add(interval);
            menu.Items.Add(CheckItem("로그인 시 자동 실행",StartupEnabled(),ToggleStartup));
            menu.Items.Add(new ToolStripSeparator());menu.Items.Add(ActionItem("종료(&X)",Close));
        }
        bool StartupEnabled() {try {using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return k!=null && k.GetValue("PulseWidget")!=null;}catch{return false;}}
        void ToggleStartup() {
            try {bool on=StartupEnabled();using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(on)k.DeleteValue("PulseWidget",false);else k.SetValue("PulseWidget","\""+Application.ExecutablePath+"\"");}}
            catch(Exception ex){MessageBox.Show(this,"자동 실행 설정을 저장하지 못했습니다.\n"+ex.Message,"Pulse");}
        }
        void OpenSettings() {
            if(settingsForm!=null && !settingsForm.IsDisposed){settingsForm.Activate();return;}
            settingsForm=new SettingsForm(settings,sensors);
            settingsForm.TopMost=settings.Topmost;
            settingsForm.Applied+=delegate(Settings changed) {settings=changed;Apply();LayoutWidget(true);Bounds=Placement.Clamp(Bounds,Work(Screen.FromRectangle(Bounds)));SavePosition();};
            settingsForm.Show(this);
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            closing=true;SavePosition();timer.Stop();timer.Dispose();if(tray!=null)tray.Dispose();tooltip.Dispose();menu.Dispose();
            Native.UnregisterHotKey(Handle,1);SystemEvents.DisplaySettingsChanged-=DisplayChanged;SystemEvents.PowerModeChanged-=PowerChanged;sensors.Dispose();
            base.OnFormClosing(e);
        }
    }
}
