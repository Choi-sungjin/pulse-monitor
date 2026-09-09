using System;
using System.Drawing;
using System.Windows.Forms;
// A separate, real foreground window for topmost/fullscreen integration checks.
// Does not simulate sensor data or automate another application.
class WindowHost : Form {
    bool full;
    WindowHost() {
        Text="Pulse QA Window";BackColor=Color.FromArgb(30,49,72);ForeColor=Color.White;
        StartPosition=FormStartPosition.CenterScreen;Size=new Size(1200,800);KeyPreview=true;
        Controls.Add(new Label {Text="위젯 표시 검증용 창\nF11: 전체 화면 전환\nAlt+F4: 종료",Font=new Font("Segoe UI",24),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter});
        KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.F11){full=!full;WindowState=FormWindowState.Normal;FormBorderStyle=full?FormBorderStyle.None:FormBorderStyle.Sizable;if(full)Bounds=Screen.FromControl(this).Bounds;else{Size=new Size(1200,800);CenterToScreen();}}};
    }
    [STAThread]static void Main(){Application.EnableVisualStyles();Application.Run(new WindowHost());}
}
