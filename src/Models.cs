using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Web.Script.Serialization;

namespace PulseWidget {
    public sealed class Reading {
        public double? Value;
        public string Status = "미지원";
        public string Source = "";
        public string Unit = "";
        public static Reading Ok(double value, string unit, string source) {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return Missing("확인 불가", source);
            return new Reading { Value = value, Status = "정상", Unit = unit, Source = source };
        }
        public static Reading Missing(string status, string source) { return new Reading { Status = status, Source = source }; }
        public string Format(string format) { return Value.HasValue ? Value.Value.ToString(format) + Unit : Status; }
    }
    public sealed class GpuInfo {
        public string Id, Name, Luid;
        public ulong DedicatedBytes;
        public uint VendorId;
        [ScriptIgnore] public IntPtr NvHandle;
        public override string ToString() { return Name; }
    }
    public sealed class ExtraReading { public string Id, Name; public Reading Reading; }
    public sealed class Snapshot {
        public DateTime Timestamp = DateTime.UtcNow;
        public string CpuName = "CPU", GpuName = "GPU", GpuId = "", NetworkName = "활성 연결";
        public Reading Cpu = Reading.Missing("수집 중", "GetSystemTimes");
        public Reading CpuTemp = Reading.Missing("미지원", "CPU 온도 공급자 없음");
        public Reading RamUsed = Reading.Missing("확인 불가", "GlobalMemoryStatusEx"), RamTotal = new Reading();
        public Reading Gpu = new Reading(), GpuTemp = new Reading(), VramUsed = new Reading(), VramTotal = new Reading();
        public Reading Up = new Reading(), Down = new Reading();
        public string CpuTempLabel = "CPU 패키지";
        public string ProviderStatus = "센서 공급자 없음";
        public List<ExtraReading> Extras = new List<ExtraReading>();
    }
    public sealed class Settings {
        public int Version = 2;
        public bool TrayOnly = true;
        public int X = Int32.MinValue, Y = Int32.MinValue;
        public string Monitor = "";
        public bool Topmost = true, Locked, HideFullscreen = true, ClickThrough;
        public bool Expanded;
        public double Opacity = 0.94;
        public int TextSize = 10, Width = 820, Interval = 1000, TempWarning = 85;
        public string GpuId = "", NetworkId = "", CpuSensorId = "";
        public List<string> Items = new List<string> { "CPU", "GPU", "RAM", "VRAM", "NET" };
        public Settings Copy() { return Json.Read<Settings>(Json.Write(this)); }
        public void Normalize() {
            Interval = new int[] { 500, 1000, 2000, 5000 }.Contains(Interval) ? Interval : 1000;
            Width = Math.Max(500, Math.Min(1600, Width)); TextSize = Math.Max(9, Math.Min(16, TextSize));
            Opacity = Math.Max(.4, Math.Min(1, Opacity)); TempWarning = Math.Max(40, Math.Min(120, TempWarning));
            Items = (Items ?? new List<string>()).Where(x => new[] { "CPU", "GPU", "RAM", "VRAM", "NET" }.Contains(x)).Distinct().ToList();
            if (Items.Count == 0) Items.Add("CPU");
        }
    }
    public static class Json {
        public static string Write(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static T Read<T>(string value) { return new JavaScriptSerializer().Deserialize<T>(value); }
        public static void Save(string path, object value) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            File.WriteAllText(temp, Write(value), new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }
    public static class SettingsStore {
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseWidget");
        public static readonly string PathName = Path.Combine(DirectoryPath, "settings.json");
        public static Settings Load() {
            try { var s = Json.Read<Settings>(File.ReadAllText(PathName)); s.Normalize(); return s; }
            catch { return new Settings(); }
        }
        public static void Save(Settings settings) { Json.Save(PathName, settings); }
    }
    public static class Placement {
        public static Rectangle Clamp(Rectangle proposed, Rectangle work) {
            int w = Math.Min(proposed.Width, work.Width), h = Math.Min(proposed.Height, work.Height);
            return new Rectangle(Math.Max(work.Left, Math.Min(proposed.X, work.Right - w)), Math.Max(work.Top, Math.Min(proposed.Y, work.Bottom - h)), w, h);
        }
        public static Rectangle Snap(Rectangle r, Rectangle work, int threshold, int gap) {
            r = Clamp(r, work);
            if (Math.Abs(r.Left - work.Left) <= threshold) r.X = work.Left + gap;
            if (Math.Abs(r.Right - work.Right) <= threshold) r.X = work.Right - r.Width - gap;
            if (Math.Abs(r.Top - work.Top) <= threshold) r.Y = work.Top + gap;
            if (Math.Abs(r.Bottom - work.Bottom) <= threshold) r.Y = work.Bottom - r.Height - gap;
            return Clamp(r, work);
        }
    }
}
