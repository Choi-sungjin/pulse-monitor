using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;

namespace PulseWidget {
    public sealed class SensorService : IDisposable {
        readonly object sync = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile bool stopping;
        Thread worker;
        Settings settings;
        Snapshot latest;
        List<GpuInfo> devices = new List<GpuInfo>();
        List<ExtraReading> cpuSensors = new List<ExtraReading>();
        Nvml nvml;
        Pdh pdh;
        long lastIdle,lastKernel,lastUser;
        bool cpuBaseline;
        string cpuName="CPU";
        string provider="",providerState="센서 공급자 없음";
        DateTime nextProviderProbe=DateTime.MinValue;
        Dictionary<string,NetBaseline> net = new Dictionary<string,NetBaseline>();
        sealed class NetBaseline { public long Sent,Received,Ticks; }
        public SensorService(Settings s) { settings=s.Copy(); }
        public Snapshot Latest { get { lock(sync) return latest; } }
        public List<GpuInfo> Devices { get { lock(sync) return new List<GpuInfo>(devices); } }
        public List<ExtraReading> CpuSensors { get { lock(sync) return new List<ExtraReading>(cpuSensors); } }
        public void Change(Settings s) { lock(sync) settings=s.Copy(); wake.Set(); }
        public void Resume() { wake.Set(); }
        public void Start() { worker=new Thread(Run) { IsBackground=true,Name="Pulse sensors" }; worker.Start(); }
        void Run() {
            try {
                nvml=new Nvml(); pdh=new Pdh();
                var gpuList=Dxgi.Enumerate();
                // Match only unique names. Identical NVIDIA cards remain separate NVML UUID devices.
                foreach(var n in nvml.Devices) {
                    var matches=gpuList.Where(g=>g.Name==n.Name).ToList();
                    if(matches.Count==1 && nvml.Devices.Count(x=>x.Name==n.Name)==1) { n.Luid=matches[0].Luid; gpuList.Remove(matches[0]); }
                    gpuList.Add(n);
                }
                gpuList=gpuList.OrderByDescending(g=>g.NvHandle!=IntPtr.Zero).ToList();
                lock(sync) devices=gpuList;
                try { using(var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) if(key!=null) cpuName=Convert.ToString(key.GetValue("ProcessorNameString")).Trim(); } catch {}
                while(!stopping) {
                    Settings s; lock(sync) s=settings.Copy(); var start=Stopwatch.StartNew();
                    Snapshot shot;
                    try { shot=Sample(s); } catch(Exception ex) { shot=new Snapshot { ProviderStatus="수집 오류: "+ex.GetType().Name }; }
                    lock(sync) latest=shot;
                    int delay=Math.Max(50,s.Interval-(int)start.ElapsedMilliseconds);
                    wake.WaitOne(delay);
                }
            } catch(Exception ex) { lock(sync) latest=new Snapshot { ProviderStatus="초기화 오류: "+ex.GetType().Name }; }
            finally { if(pdh!=null)pdh.Dispose(); if(nvml!=null)nvml.Dispose(); wake.Dispose(); }
        }
        Snapshot Sample(Settings settings) {
            var s=new Snapshot { CpuName=cpuName };
            long idle,kernel,user;
            if(Native.GetSystemTimes(out idle,out kernel,out user)) {
                long all=kernel-lastKernel+user-lastUser, idleDelta=idle-lastIdle;
                if(cpuBaseline && all>0 && idleDelta>=0) s.Cpu=Reading.Ok(Math.Max(0,Math.Min(100,100.0*(all-idleDelta)/all)),"%","GetSystemTimes");
                lastIdle=idle;lastKernel=kernel;lastUser=user;cpuBaseline=true;
            } else s.Cpu=Reading.Missing("확인 불가","GetSystemTimes");
            var m=new Native.MemoryStatus();
            if(Native.GlobalMemoryStatusEx(m)) { s.RamUsed=Reading.Ok(m.TotalPhys-m.AvailPhys,"B","GlobalMemoryStatusEx");s.RamTotal=Reading.Ok(m.TotalPhys,"B","GlobalMemoryStatusEx"); }
            bool pdhOk=pdh.Collect();
            var gpu=devices.FirstOrDefault(x=>x.Id==settings.GpuId) ?? (String.IsNullOrEmpty(settings.GpuId)?devices.FirstOrDefault():null);
            if(gpu!=null) {
                s.GpuName=gpu.Name;s.GpuId=gpu.Id;
                if(gpu.NvHandle!=IntPtr.Zero) nvml.Read(gpu,s);
                else {
                    s.GpuTemp=Reading.Missing("미지원","Windows GPU counter has no temperature");
                    s.VramTotal=Reading.Ok(gpu.DedicatedBytes,"B","DXGI dedicated memory");
                }
                if(pdhOk && !String.IsNullOrEmpty(gpu.Luid)) {
                    if(!s.Gpu.Value.HasValue) {
                        var engines=pdh.Read("engine").Where(x=>x.Key.IndexOf(gpu.Luid,StringComparison.OrdinalIgnoreCase)>=0).ToList();
                        // A device's busiest engine; sum processes on each engine, not all engines.
                        if(engines.Count>0) {
                            double use=engines.GroupBy(x=>x.Key.Substring(x.Key.IndexOf("_phys_",StringComparison.Ordinal))).Select(g=>g.Sum(x=>x.Value)).Max();
                            s.Gpu=Reading.Ok(Math.Max(0,Math.Min(100,use)),"%","PDH busiest GPU engine / "+gpu.Luid);
                        } else s.Gpu=Reading.Missing("확인 불가","GPU counter has no current sample");
                    }
                    if(!s.VramUsed.Value.HasValue) {
                        var memory=pdh.Read("memory").Where(x=>x.Key.IndexOf(gpu.Luid,StringComparison.OrdinalIgnoreCase)>=0).ToList();
                        if(memory.Count>0) s.VramUsed=Reading.Ok(memory.Sum(x=>x.Value),"B","PDH dedicated usage");
                    }
                }
            } else { s.GpuName=String.IsNullOrEmpty(settings.GpuId)?"GPU 없음":"선택 GPU 연결 끊김";s.Gpu=Reading.Missing("확인 불가","GPU disconnected"); }
            if(pdhOk) {
                AddCounter(s,"cpuMHz","CPU 보고 클럭 (Windows)","MHz");
                AddCounter(s,"diskRead","전체 디스크 읽기","B/s");AddCounter(s,"diskWrite","전체 디스크 쓰기","B/s");
            }
            ReadNetwork(s,settings.NetworkId);
            ReadProvider(s,settings.CpuSensorId,gpu);
            s.Timestamp=DateTime.UtcNow;
            return s;
        }
        void AddCounter(Snapshot s,string id,string label,string unit) { var r=pdh.Read(id);if(r.Count>0) Nvml.Add(s,id,label,Reading.Ok(r.Values.First(),unit,"Windows PDH")); }
        public static NetworkInterface[] NetworkDevices() { return NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.NetworkInterfaceType!=NetworkInterfaceType.Loopback && n.NetworkInterfaceType!=NetworkInterfaceType.Tunnel).ToArray(); }
        void ReadNetwork(Snapshot s,string selection) {
            try {
                var candidates=NetworkDevices().Where(n=>n.OperationalStatus==OperationalStatus.Up).ToList();
                // Default to the fastest active interface with a gateway. Explicit selection is available.
                NetworkInterface nic=String.IsNullOrEmpty(selection)?candidates.Where(n=>n.GetIPProperties().GatewayAddresses.Any(g=>!g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any))).OrderByDescending(n=>n.Speed).FirstOrDefault():candidates.FirstOrDefault(n=>n.Id==selection);
                if(nic==null) {s.Up=Reading.Missing("연결 없음","NetworkInterface");s.Down=Reading.Missing("연결 없음","NetworkInterface");return;}
                s.NetworkName=nic.Name;
                var stats=nic.GetIPStatistics(); long ticks=Stopwatch.GetTimestamp(); NetBaseline prev;
                if(net.TryGetValue(nic.Id,out prev)) {
                    double seconds=(ticks-prev.Ticks)/(double)Stopwatch.Frequency;
                    if(seconds>0 && seconds<30 && stats.BytesSent>=prev.Sent && stats.BytesReceived>=prev.Received) {
                        s.Up=Reading.Ok((stats.BytesSent-prev.Sent)/seconds,"B/s",nic.Name);s.Down=Reading.Ok((stats.BytesReceived-prev.Received)/seconds,"B/s",nic.Name);
                    } else { s.Up=Reading.Missing("수집 중",nic.Name);s.Down=Reading.Missing("수집 중",nic.Name); }
                } else { s.Up=Reading.Missing("수집 중",nic.Name);s.Down=Reading.Missing("수집 중",nic.Name); }
                net.Clear(); net[nic.Id]=new NetBaseline {Sent=stats.BytesSent,Received=stats.BytesReceived,Ticks=ticks};
            } catch(NetworkInformationException) { s.Up=Reading.Missing("확인 불가","NetworkInterface");s.Down=Reading.Missing("확인 불가","NetworkInterface"); }
        }
        static ManagementObjectSearcher Search(string ns,string query) { return new ManagementObjectSearcher(new ManagementScope(ns,new ConnectionOptions {Timeout=TimeSpan.FromSeconds(1)}),new ObjectQuery(query),new EnumerationOptions {Timeout=TimeSpan.FromSeconds(1),ReturnImmediately=false}); }
        void ReadProvider(Snapshot s,string selected,GpuInfo gpu) {
            if(String.IsNullOrEmpty(provider) && DateTime.UtcNow>=nextProviderProbe) {
                nextProviderProbe=DateTime.UtcNow.AddSeconds(30);providerState="센서 공급자 없음";
                foreach(string ns in new[] {@"root\LibreHardwareMonitor",@"root\OpenHardwareMonitor"}) {
                    try { using(var q=Search(ns,"SELECT Identifier FROM Hardware")) using(var rows=q.Get()) { foreach(ManagementObject row in rows) row.Dispose(); provider=ns;break; } }
                    catch(UnauthorizedAccessException) {providerState="권한 부족";}
                    catch(ManagementException ex) {if(ex.ErrorCode==ManagementStatus.AccessDenied)providerState="권한 부족";}
                    catch {providerState="공급자 확인 불가";}
                }
            }
            s.ProviderStatus=providerState;
            if(String.IsNullOrEmpty(provider)) { s.CpuTemp=Reading.Missing(providerState=="권한 부족"?"권한 부족":"미지원",providerState);lock(sync)cpuSensors=new List<ExtraReading>();return; }
            try {
                var rowsList=new List<ExtraReading>();
                using(var q=Search(provider,"SELECT Identifier,Name,SensorType,Value,Parent FROM Sensor")) using(var rows=q.Get()) {
                    foreach(ManagementObject row in rows) using(row) {
                        if(row["Value"]==null)continue;
                        string id=Convert.ToString(row["Identifier"]), name=Convert.ToString(row["Name"]), type=Convert.ToString(row["SensorType"]),parent=Convert.ToString(row["Parent"]);
                        double value=Convert.ToDouble(row["Value"]);
                        string unit=type=="Temperature"?"°C":type=="Fan"?"RPM":type=="Power"?"W":type=="Clock"?"MHz":"";
                        if(unit=="")continue;
                        bool cpu=id.IndexOf("cpu",StringComparison.OrdinalIgnoreCase)>=0;
                        var read=Reading.Ok(value,unit,provider+" / "+id);
                        if(cpu && type=="Temperature")rowsList.Add(new ExtraReading {Id=id,Name=name,Reading=read});
                        // GPU sensors from a generic provider are intentionally not guessed across adapters.
                        if(id.IndexOf("gpu",StringComparison.OrdinalIgnoreCase)<0 && !(cpu && type=="Temperature")) Nvml.Add(s,id,name,read);
                    }
                }
                lock(sync)cpuSensors=rowsList;
                var chosen=String.IsNullOrEmpty(selected)?rowsList.FirstOrDefault(x=>x.Name.IndexOf("Package",StringComparison.OrdinalIgnoreCase)>=0):rowsList.FirstOrDefault(x=>x.Id==selected);
                if(chosen!=null) {s.CpuTemp=chosen.Reading;s.CpuTempLabel=chosen.Name;}
                else s.CpuTemp=Reading.Missing(String.IsNullOrEmpty(selected)?"미지원":"확인 불가",String.IsNullOrEmpty(selected)?"CPU package sensor not exposed":"Selected sensor absent");
                foreach(var r in rowsList) Nvml.Add(s,r.Id,r.Name,r.Reading);
                s.ProviderStatus="연결됨: "+provider;
            } catch(UnauthorizedAccessException) { s.CpuTemp=Reading.Missing("권한 부족",provider); provider="";providerState="권한 부족"; }
            catch { s.CpuTemp=Reading.Missing("확인 불가",provider);provider="";providerState="공급자 연결 끊김"; }
        }
        public void Dispose() { stopping=true; try {wake.Set();}catch(ObjectDisposedException){} if(worker!=null)worker.Join(1800); }
    }
}
