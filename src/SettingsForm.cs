using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PulseWidget {
    public sealed class SettingsForm : Form {
        public event Action<Settings> Applied;
        readonly Settings draft;
        readonly NumericUpDown width=new NumericUpDown(),text=new NumericUpDown(),opacity=new NumericUpDown(),warning=new NumericUpDown();
        readonly ComboBox gpu=new ComboBox(),network=new ComboBox(),interval=new ComboBox(),cpu=new ComboBox();
        readonly CheckedListBox items=new CheckedListBox();
        sealed class Choice {public string Id,Name;public override string ToString(){return Name;}}
        public SettingsForm(Settings current,SensorService sensors) {
            SuspendLayout();
            Icon=Program.LoadAppIcon();
            Disposed+=delegate {if(Icon!=null)Icon.Dispose();};
            draft=current.Copy();Text="Pulse 설정";StartPosition=FormStartPosition.CenterScreen;Size=current.TrayOnly?new Size(560,565):new Size(640,790);MinimumSize=new Size(500,450);
            AutoScaleMode=AutoScaleMode.None;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(24,29,39);ForeColor=Color.FromArgb(233,238,248);
            var outer=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(20)};
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute,58));outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));outer.RowStyles.Add(new RowStyle(SizeType.Absolute,48));Controls.Add(outer);
            outer.Controls.Add(new Label {Text=current.TrayOnly?"작업표시줄 숫자 모니터\nC CPU · G GPU · R RAM — 모두 사용률(%)입니다.":"내 화면에 맞게\n크기와 센서, 표시 순서를 조절하세요.",Dock=DockStyle.Fill,AutoSize=false},0,0);
            var scroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true};outer.Controls.Add(scroll,0,1);
            var table=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(0,6,12,6)};table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(table);
            Number(width,500,1600,current.Width,20);Number(text,9,16,current.TextSize,1);Number(opacity,40,100,(decimal)(current.Opacity*100),5);Number(warning,40,120,current.TempWarning,1);
            if(!current.TrayOnly){Row(table,"위젯 너비",width,40);Row(table,"글자 크기",text,40);}
            Row(table,"상세 창 불투명도 (%)",opacity,40);
            if(!current.TrayOnly)Row(table,"온도 알림 기준 (°C)",warning,40);
            interval.DropDownStyle=ComboBoxStyle.DropDownList;foreach(int ms in new[]{500,1000,2000,5000})interval.Items.Add(new Choice {Id=ms.ToString(),Name=(ms/1000.0).ToString("0.#")+"초"});Select(interval,current.Interval.ToString());Row(table,"갱신 주기",interval,40);
            gpu.DropDownStyle=ComboBoxStyle.DropDownList;gpu.Items.Add(new Choice {Id="",Name="자동 선택"});foreach(var d in sensors.Devices)gpu.Items.Add(new Choice {Id=d.Id,Name=d.Name});Select(gpu,current.GpuId);Row(table,"GPU 장치",gpu,40);
            cpu.DropDownStyle=ComboBoxStyle.DropDownList;cpu.Items.Add(new Choice {Id="",Name="자동 (CPU 패키지)"});foreach(var r in sensors.CpuSensors)cpu.Items.Add(new Choice {Id=r.Id,Name=r.Name});Select(cpu,current.CpuSensorId);Row(table,"CPU 온도",cpu,40);
            network.DropDownStyle=ComboBoxStyle.DropDownList;network.Items.Add(new Choice {Id="",Name="자동 (활성 게이트웨이 연결)"});try{foreach(var n in SensorService.NetworkDevices())network.Items.Add(new Choice {Id=n.Id,Name=n.Name});}catch{}Select(network,current.NetworkId);Row(table,"네트워크 연결",network,40);
            items.CheckOnClick=true;items.BorderStyle=BorderStyle.None;items.BackColor=Color.FromArgb(34,41,54);items.ForeColor=ForeColor;
            foreach(string id in current.Items)items.Items.Add(id,true);foreach(string id in new[]{"CPU","GPU","RAM","VRAM","NET"}.Except(current.Items))items.Items.Add(id,false);
            items.IntegralHeight=false;
            if(!current.TrayOnly)Row(table,"표시 항목 / 순서",items,142);
            var order=new FlowLayoutPanel {Dock=DockStyle.Fill};var up=new Button {Text="위로",Width=80,Height=30,Margin=new Padding(0,0,8,0)};var down=new Button {Text="아래로",Width=80,Height=30,Margin=new Padding(0)};up.Click+=delegate{MoveItem(-1);};down.Click+=delegate{MoveItem(1);};order.Controls.Add(up);order.Controls.Add(down);if(!current.TrayOnly)Row(table,"",order,48);
            var hint=new Label {Text="CPU 온도·팬·저장장치 온도는 실행 중인 Libre Hardware Monitor / Open Hardware Monitor의 센서 공급자가 있을 때 읽습니다.\n\n이 위젯은 드라이버를 설치하지 않습니다. 미지원 센서는 0으로 표시하지 않습니다.\n\n단위: 메모리는 1024 기준 GB, 네트워크는 초당 바이트.\nCtrl + Alt + P: 표시 / 숨기기",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(159,175,198)};Row(table,"센서 안내",hint,160);
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var apply=new Button {Text="적용",Width=100,Height=34};var close=new Button {Text="닫기",Width=100,Height=34};buttons.Controls.Add(apply);buttons.Controls.Add(close);outer.Controls.Add(buttons,0,2);
            foreach(var button in new[]{apply,close,up,down}){button.BackColor=Color.FromArgb(55,71,91);button.ForeColor=ForeColor;button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;}
            apply.Click+=delegate{ApplySettings();};close.Click+=delegate{Close();};AcceptButton=apply;CancelButton=close;
            float scale=Native.GetDpiForWindow(Handle)/96f;
            Scale(new SizeF(scale,scale));
            ResumeLayout(true);
            Shown+=delegate {Bounds=Placement.Clamp(Bounds,Screen.FromHandle(Handle).WorkingArea);};
        }
        void Number(NumericUpDown n,decimal min,decimal max,decimal value,decimal increment) {n.Minimum=min;n.Maximum=max;n.Value=Math.Max(min,Math.Min(max,value));n.Increment=increment;}
        void Row(TableLayoutPanel table,string label,Control value,int height) {int row=table.RowCount++;table.RowStyles.Add(new RowStyle(SizeType.Absolute,height));table.Controls.Add(new Label {Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);value.Dock=DockStyle.Fill;value.Margin=new Padding(2,6,2,6);table.Controls.Add(value,1,row);}
        void Select(ComboBox box,string id) {for(int i=0;i<box.Items.Count;i++)if(((Choice)box.Items[i]).Id==id){box.SelectedIndex=i;return;}box.SelectedIndex=0;}
        void MoveItem(int delta) {int i=items.SelectedIndex,j=i+delta;if(i<0 || j<0 || j>=items.Items.Count)return;object value=items.Items[i];bool check=items.GetItemChecked(i);items.Items.RemoveAt(i);items.Items.Insert(j,value);items.SetItemChecked(j,check);items.SelectedIndex=j;}
        void ApplySettings() {
            if(items.CheckedItems.Count==0){MessageBox.Show(this,"표시할 항목을 하나 이상 선택해 주세요.","Pulse");return;}
            draft.Width=(int)width.Value;draft.TextSize=(int)text.Value;draft.Opacity=(double)opacity.Value/100;draft.TempWarning=(int)warning.Value;
            draft.GpuId=((Choice)gpu.SelectedItem).Id;draft.CpuSensorId=((Choice)cpu.SelectedItem).Id;draft.NetworkId=((Choice)network.SelectedItem).Id;draft.Interval=Int32.Parse(((Choice)interval.SelectedItem).Id);
            draft.Items=items.CheckedItems.Cast<string>().ToList();draft.Normalize();if(Applied!=null)Applied(draft.Copy());
        }
    }
}
