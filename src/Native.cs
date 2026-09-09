using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using Microsoft.Win32;

namespace PulseWidget {
    public static class Native {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public class MemoryStatus {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)); public uint Load;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus value);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr LoadLibraryEx(string name, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr module);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder value, int size);
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    }

    public sealed class Nvml : IDisposable {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CountCall(out uint n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int HandleCall(uint index, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TextCall(IntPtr device, StringBuilder text, uint len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int UintCall(IntPtr device, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TypeUintCall(IntPtr device, uint type, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int UtilCall(IntPtr device, out Util value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int MemoryCall(IntPtr device, out Memory value);
        [StructLayout(LayoutKind.Sequential)] struct Util { public uint Gpu, Memory; }
        [StructLayout(LayoutKind.Sequential)] struct Memory { public ulong Total, Free, Used; }
        IntPtr module;
        readonly Dictionary<string, Delegate> calls = new Dictionary<string, Delegate>();
        public string Status = "미지원";
        public string LibraryPath = "";
        public List<GpuInfo> Devices = new List<GpuInfo>();
        T Get<T>(string name) where T:class {
            if (!calls.ContainsKey(name)) { var p = Native.GetProcAddress(module, name); if (p == IntPtr.Zero) throw new EntryPointNotFoundException(name); calls[name] = Marshal.GetDelegateForFunctionPointer(p, typeof(T)); }
            return calls[name] as T;
        }
        public Nvml() {
            try {
                var candidates = new List<string> { Path.Combine(Environment.SystemDirectory, "nvml.dll"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll") };
                using (var video = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video")) {
                    if (video != null) foreach (var sub in video.GetSubKeyNames()) using (var k = video.OpenSubKey(sub + @"\0000")) {
                        if (k == null) continue;
                        var names = k.GetValue("OpenGLDriverName") as string[];
                        if (names != null) foreach (var name in names) {
                            string p = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(name), "nvml.dll"));
                            if (p.StartsWith(Path.Combine(Environment.SystemDirectory, "DriverStore") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) candidates.Add(p);
                        }
                    }
                }
                foreach (string path in candidates) if (File.Exists(path)) { module = Native.LoadLibraryEx(path, IntPtr.Zero, 0x1100); if (module != IntPtr.Zero) { LibraryPath = path; break; } }
                if (module == IntPtr.Zero) return;
                int rc = Get<InitCall>("nvmlInit_v2")(); if (rc != 0) { Status = Error(rc); return; }
                uint count; rc = Get<CountCall>("nvmlDeviceGetCount_v2")(out count); if (rc != 0) { Status = Error(rc); return; }
                for (uint i=0; i<count; i++) {
                    IntPtr h; if (Get<HandleCall>("nvmlDeviceGetHandleByIndex_v2")(i, out h) != 0) continue;
                    var name = new StringBuilder(128); var uuid = new StringBuilder(128);
                    Get<TextCall>("nvmlDeviceGetName")(h, name, 128); Get<TextCall>("nvmlDeviceGetUUID")(h, uuid, 128);
                    Memory m; Get<MemoryCall>("nvmlDeviceGetMemoryInfo")(h,out m);
                    Devices.Add(new GpuInfo { Id = uuid.Length > 0 ? uuid.ToString() : "nvml:"+i, Name = name.ToString(), DedicatedBytes=m.Total, VendorId=0x10DE, NvHandle=h });
                }
                Status = "정상";
            } catch (UnauthorizedAccessException) { Status="권한 부족"; } catch { Status="확인 불가"; }
        }
        public static string Error(int rc) { return rc == 3 ? "미지원" : rc == 4 ? "권한 부족" : "확인 불가"; }
        public Reading Scalar(IntPtr h, string name, string unit, double divisor, uint? type) {
            try { uint v; int rc = type.HasValue ? Get<TypeUintCall>(name)(h,type.Value,out v) : Get<UintCall>(name)(h,out v); return rc == 0 ? Reading.Ok(v/divisor,unit,"NVML / "+name) : Reading.Missing(Error(rc),"NVML / "+rc); }
            catch { return Reading.Missing("확인 불가","NVML / "+name); }
        }
        public void Read(GpuInfo gpu, Snapshot s) {
            IntPtr h=gpu.NvHandle;
            try { Util u; int rc=Get<UtilCall>("nvmlDeviceGetUtilizationRates")(h,out u); s.Gpu=rc==0?Reading.Ok(u.Gpu,"%","NVML GPU active time"):Reading.Missing(Error(rc),"NVML / "+rc); }
            catch { s.Gpu=Reading.Missing("확인 불가","NVML"); }
            s.GpuTemp=Scalar(h,"nvmlDeviceGetTemperature","°C",1,0);
            try { Memory m; int rc=Get<MemoryCall>("nvmlDeviceGetMemoryInfo")(h,out m); s.VramUsed=rc==0?Reading.Ok(m.Used,"B","NVML dedicated memory"):Reading.Missing(Error(rc),"NVML / "+rc); s.VramTotal=rc==0?Reading.Ok(m.Total,"B","NVML usable dedicated memory"):Reading.Missing(Error(rc),"NVML"); } catch { s.VramUsed=Reading.Missing("확인 불가","NVML"); }
            Add(s,"gpu-clock","GPU 코어 클럭",Scalar(h,"nvmlDeviceGetClockInfo","MHz",1,0));
            Add(s,"gpu-power","GPU 전력",Scalar(h,"nvmlDeviceGetPowerUsage","W",1000,null));
            Add(s,"gpu-fan","GPU 팬 속도 (최대 대비)",Scalar(h,"nvmlDeviceGetFanSpeed","%",1,null));
        }
        public static void Add(Snapshot s,string id,string name,Reading r) { if (r.Value.HasValue) s.Extras.Add(new ExtraReading {Id=id,Name=name,Reading=r}); }
        public void Dispose() { if(module!=IntPtr.Zero) { try { Get<InitCall>("nvmlShutdown")(); } catch {} Native.FreeLibrary(module); module=IntPtr.Zero; } }
    }

    public static class Dxgi {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Desc {
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description;
            public uint VendorId,DeviceId,SubSysId,Revision;
            public UIntPtr DedicatedVideoMemory,DedicatedSystemMemory,SharedSystemMemory;
            public uint LuidLow; public int LuidHigh; public uint Flags;
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumCall(IntPtr self,uint index,out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DescCall(IntPtr self,out Desc desc);
        [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid guid,out IntPtr factory);
        static T Method<T>(IntPtr obj,int slot) where T:class { return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size),typeof(T)) as T; }
        public static List<GpuInfo> Enumerate() {
            var result=new List<GpuInfo>(); IntPtr factory=IntPtr.Zero;
            try {
                Guid iid=new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); if(CreateDXGIFactory1(ref iid,out factory)!=0) return result;
                for(uint i=0;i<32;i++) { IntPtr adapter; if(Method<EnumCall>(factory,12)(factory,i,out adapter)!=0) break;
                    try { Desc d; if(Method<DescCall>(adapter,10)(adapter,out d)!=0 || (d.Flags&2)!=0) continue;
                        string luid=String.Format("luid_0x{0:X8}_0x{1:X8}",unchecked((uint)d.LuidHigh),d.LuidLow);
                        result.Add(new GpuInfo { Id=luid, Luid=luid,Name=d.Description,VendorId=d.VendorId,DedicatedBytes=d.DedicatedVideoMemory.ToUInt64() });
                    } finally { Marshal.Release(adapter); }
                }
            } catch {} finally { if(factory!=IntPtr.Zero) Marshal.Release(factory); }
            return result;
        }
    }

    public sealed class Pdh : IDisposable {
        [StructLayout(LayoutKind.Sequential)] struct Value { public uint Status; public double Number; }
        [StructLayout(LayoutKind.Sequential)] struct Item { public IntPtr Name; public Value Value; }
        [DllImport("pdh.dll",CharSet=CharSet.Unicode)] static extern uint PdhOpenQuery(string source,IntPtr user,out IntPtr query);
        [DllImport("pdh.dll",CharSet=CharSet.Unicode)] static extern uint PdhAddEnglishCounter(IntPtr query,string path,IntPtr user,out IntPtr counter);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll",CharSet=CharSet.Unicode)] static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint size,out uint count,IntPtr buffer);
        IntPtr query; Dictionary<string,IntPtr> counters=new Dictionary<string,IntPtr>();
        public Pdh() {
            if(PdhOpenQuery(null,IntPtr.Zero,out query)!=0) return;
            Add("engine",@"\GPU Engine(*)\Utilization Percentage"); Add("memory",@"\GPU Adapter Memory(*)\Dedicated Usage");
            Add("cpuMHz",@"\Processor Information(_Total)\Processor Frequency");
            Add("diskRead",@"\PhysicalDisk(_Total)\Disk Read Bytes/sec"); Add("diskWrite",@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
            PdhCollectQueryData(query);
        }
        void Add(string id,string path) { IntPtr h; if(PdhAddEnglishCounter(query,path,IntPtr.Zero,out h)==0) counters[id]=h; }
        public bool Collect() { return query!=IntPtr.Zero && PdhCollectQueryData(query)==0; }
        public Dictionary<string,double> Read(string id) {
            var r=new Dictionary<string,double>(); IntPtr c; if(!counters.TryGetValue(id,out c)) return r;
            uint bytes=0,count; uint rc=PdhGetFormattedCounterArray(c,0x200,ref bytes,out count,IntPtr.Zero);
            if(bytes==0 || bytes>16*1024*1024) return r;
            IntPtr p=Marshal.AllocHGlobal((int)bytes);
            try { if(PdhGetFormattedCounterArray(c,0x200,ref bytes,out count,p)!=0) return r;
                int size=Marshal.SizeOf(typeof(Item)); for(int i=0;i<count;i++) { var item=(Item)Marshal.PtrToStructure(IntPtr.Add(p,i*size),typeof(Item)); if(item.Value.Status<=1 && !Double.IsNaN(item.Value.Number)) r[Marshal.PtrToStringUni(item.Name)]=item.Value.Number; }
            } finally { Marshal.FreeHGlobal(p); } return r;
        }
        public void Dispose() { if(query!=IntPtr.Zero) PdhCloseQuery(query); query=IntPtr.Zero; }
    }
}
