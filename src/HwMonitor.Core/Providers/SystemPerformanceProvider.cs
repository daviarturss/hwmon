using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using HwMon.Core.Sensors;

namespace HwMon.Core.Providers;

/// <summary>
/// Provedor sem privilégios especiais: usa apenas APIs de usuário do Windows
/// (GetSystemTimes, GlobalMemoryStatusEx, DriveInfo, NetworkInterface).
/// Serve como linha de base quando o provedor de hardware não está disponível.
/// </summary>
public sealed class SystemPerformanceProvider : ISensorProvider
{
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private long _prevTimestamp;

    private Dictionary<string, (long Received, long Sent)> _prevNetwork = new(StringComparer.Ordinal);

    public string Id => "system";

    public string DisplayName => "Windows (usuário)";

    public bool IsAvailable { get; private set; }

    public string? StatusMessage { get; private set; }

    public void Initialize()
    {
        _prevTimestamp = Environment.TickCount64;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _prevIdle = ToUInt64(idle);
            _prevKernel = ToUInt64(kernel);
            _prevUser = ToUInt64(user);
        }

        IsAvailable = true;
    }

    public IReadOnlyList<SensorReading> Read()
    {
        var readings = new List<SensorReading>(32);

        ReadCpu(readings);
        ReadMemory(readings);
        ReadUptime(readings);
        ReadDisks(readings);
        ReadNetwork(readings);

        return readings;
    }

    private void ReadCpu(List<SensorReading> readings)
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return;
        }

        var idleNow = ToUInt64(idle);
        var kernelNow = ToUInt64(kernel);
        var userNow = ToUInt64(user);

        var idleDelta = idleNow - _prevIdle;
        var totalDelta = (kernelNow - _prevKernel) + (userNow - _prevUser);

        _prevIdle = idleNow;
        _prevKernel = kernelNow;
        _prevUser = userNow;

        if (totalDelta == 0)
        {
            return;
        }

        var load = 100d * (totalDelta - idleDelta) / totalDelta;
        readings.Add(new SensorReading("system/cpu/load", "Processador", "Uso total da CPU", SensorKind.Load, Math.Clamp(load, 0, 100), "%"));
    }

    private static void ReadMemory(List<SensorReading> readings)
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return;
        }

        const double gb = 1024d * 1024 * 1024;
        var totalGb = status.ullTotalPhys / gb;
        var availGb = status.ullAvailPhys / gb;
        var usedGb = totalGb - availGb;

        readings.Add(new SensorReading("system/mem/load", "Memória", "Uso de memória", SensorKind.Load, status.dwMemoryLoad, "%"));
        readings.Add(new SensorReading("system/mem/used", "Memória", "Memória usada", SensorKind.Data, usedGb, "GB"));
        readings.Add(new SensorReading("system/mem/available", "Memória", "Memória disponível", SensorKind.Data, availGb, "GB"));
        readings.Add(new SensorReading("system/mem/total", "Memória", "Memória total", SensorKind.Data, totalGb, "GB"));
    }

    private static void ReadUptime(List<SensorReading> readings)
    {
        var hours = Environment.TickCount64 / 3_600_000d;
        readings.Add(new SensorReading("system/uptime", "Sistema", "Tempo ligado", SensorKind.Data, hours, "h"));
    }

    private static void ReadDisks(List<SensorReading> readings)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }

                const double gb = 1024d * 1024 * 1024;
                var totalGb = drive.TotalSize / gb;
                var freeGb = drive.AvailableFreeSpace / gb;
                var usedPercent = totalGb <= 0 ? 0 : 100d * (totalGb - freeGb) / totalGb;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : $"{drive.Name} {drive.VolumeLabel}";

                readings.Add(new SensorReading($"system/disk/{drive.Name}/free", $"Disco {label}", "Espaço livre", SensorKind.Data, freeGb, "GB"));
                readings.Add(new SensorReading($"system/disk/{drive.Name}/used", $"Disco {label}", "Uso do disco", SensorKind.Load, usedPercent, "%"));
            }
            catch (IOException)
            {
                // Unidade removida durante a leitura.
            }
            catch (UnauthorizedAccessException)
            {
                // Sem permissão para o volume.
            }
        }
    }

    private void ReadNetwork(List<SensorReading> readings)
    {
        var now = Environment.TickCount64;
        var elapsed = Math.Max(1, now - _prevTimestamp) / 1000d;
        var next = new Dictionary<string, (long, long)>(StringComparer.Ordinal);

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            try
            {
                var stats = nic.GetIPStatistics();
                next[nic.Id] = (stats.BytesReceived, stats.BytesSent);

                if (!_prevNetwork.TryGetValue(nic.Id, out var previous))
                {
                    continue;
                }

                var rx = Math.Max(0, stats.BytesReceived - previous.Received) / elapsed;
                var tx = Math.Max(0, stats.BytesSent - previous.Sent) / elapsed;

                readings.Add(new SensorReading($"system/net/{nic.Id}/rx", $"Rede {nic.Name}", "Recebido", SensorKind.Throughput, rx, "B/s"));
                readings.Add(new SensorReading($"system/net/{nic.Id}/tx", $"Rede {nic.Name}", "Enviado", SensorKind.Throughput, tx, "B/s"));
            }
            catch (NetworkInformationException)
            {
                // Interface desmontada durante a leitura.
            }
        }

        _prevNetwork = next;
        _prevTimestamp = now;
    }

    private static ulong ToUInt64(FileTime time) => ((ulong)time.High << 32) | time.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public void Dispose()
    {
        IsAvailable = false;
    }
}
