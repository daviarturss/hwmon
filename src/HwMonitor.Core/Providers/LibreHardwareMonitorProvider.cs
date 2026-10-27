using HwMon.Core.Sensors;
using LibreHardwareMonitor.Hardware;

namespace HwMon.Core.Providers;

/// <summary>
/// Provedor principal: cobre CPU, GPU, placa-mãe (SMBus/EC), memória, discos e rede
/// usando LibreHardwareMonitorLib (MPL-2.0).
///
/// Aviso: para ler MSR/SMBus a biblioteca carrega um driver de kernel (WinRing0)
/// extraído para a pasta temporária. Isso exige privilégios de administrador.
/// Sem ele, apenas os sensores que dependem de APIs de usuário ficam disponíveis.
/// </summary>
public sealed class LibreHardwareMonitorProvider : ISensorProvider
{
    private Computer? _computer;

    public string Id => "lhm";

    public string DisplayName => "LibreHardwareMonitor";

    public bool IsAvailable { get; private set; }

    public string? StatusMessage { get; private set; }

    public void Initialize()
    {
        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsControllerEnabled = true,
            };

            computer.Open();
            _computer = computer;
            IsAvailable = true;

            if (!IsElevated())
            {
                StatusMessage = "sem privilégios de administrador: sensores de MSR/SMBus podem faltar";
            }
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            StatusMessage = $"não foi possível abrir o hardware: {ex.Message}";
        }
    }

    public IReadOnlyList<SensorReading> Read()
    {
        if (_computer is null)
        {
            return Array.Empty<SensorReading>();
        }

        var readings = new List<SensorReading>(256);

        foreach (var hardware in _computer.Hardware)
        {
            Visit(hardware, parent: null, readings);
        }

        return readings;
    }

    private static void Visit(IHardware hardware, string? parent, List<SensorReading> readings)
    {
        hardware.Update();

        var label = parent is null ? hardware.Name : $"{parent} / {hardware.Name}";

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not { } value || !float.IsFinite(value))
            {
                continue;
            }

            var kind = MapKind(sensor.SensorType);

            // Temperatura 0 °C significa "sem acesso ao sensor" (driver ausente ou sem admin),
            // não uma leitura real. Reportar 0,0 °C seria enganoso.
            if (kind == SensorKind.Temperature && value <= 0)
            {
                continue;
            }

            readings.Add(new SensorReading(
                // sensor.Identifier não é único (ex.: "GPU Bus" e "GPU Memory" compartilham
                // /gpu-nvidia/0/load/3), então o nome entra na chave.
                Id: $"lhm{sensor.Identifier}/{sensor.Name}",
                Hardware: label,
                Name: sensor.Name,
                Kind: kind,
                Value: value,
                Unit: MapUnit(sensor.SensorType)));
        }

        foreach (var sub in hardware.SubHardware)
        {
            Visit(sub, label, readings);
        }
    }

    private static SensorKind MapKind(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Current => SensorKind.Current,
        SensorType.Power => SensorKind.Power,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Frequency => SensorKind.Clock,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Load => SensorKind.Load,
        SensorType.Control => SensorKind.Level,
        SensorType.Level => SensorKind.Level,
        SensorType.Throughput => SensorKind.Throughput,
        SensorType.Data => SensorKind.Data,
        SensorType.SmallData => SensorKind.Data,
        SensorType.Factor => SensorKind.Factor,
        _ => SensorKind.Other,
    };

    private static string MapUnit(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Power => "W",
        SensorType.Clock => "MHz",
        SensorType.Frequency => "MHz",
        SensorType.Fan => "RPM",
        SensorType.Load => "%",
        SensorType.Control => "%",
        SensorType.Level => "%",
        SensorType.Throughput => "B/s",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Energy => "mWh",
        SensorType.TimeSpan => "s",
        _ => string.Empty,
    };

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public void Dispose()
    {
        try
        {
            _computer?.Close();
        }
        catch
        {
            // Descarte é best-effort.
        }

        _computer = null;
        IsAvailable = false;
    }
}

