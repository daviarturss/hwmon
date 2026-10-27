namespace HwMon.Core.Sensors;

/// <summary>Uma leitura pontual de um sensor, sem estado acumulado.</summary>
public sealed record SensorReading(
    string Id,
    string Hardware,
    string Name,
    SensorKind Kind,
    double Value,
    string Unit)
{
    public string Category => Kind.ToString();
}

/// <summary>Leitura com estatísticas acumuladas desde o início da sessão.</summary>
public sealed record SensorSnapshot(
    string Id,
    string Hardware,
    string Name,
    SensorKind Kind,
    double Value,
    string Unit,
    double Min,
    double Max,
    double Average)
{
    public string Category => Kind.ToString();

    public string DisplayValue => Kind switch
    {
        SensorKind.Temperature => $"{Value:0.0} °C",
        SensorKind.Voltage => $"{Value:0.000} V",
        SensorKind.Fan => $"{Value:0} RPM",
        SensorKind.Load => $"{Value:0.0} %",
        SensorKind.Clock => $"{Value:0} MHz",
        SensorKind.Power => $"{Value:0.0} W",
        SensorKind.Current => $"{Value:0.00} A",
        SensorKind.Throughput => FormatRate(Value),
        SensorKind.Data => $"{Value:0.0} {Unit}",
        SensorKind.Level => $"{Value:0.0} %",
        _ => $"{Value:0.###} {Unit}",
    };

    private static string FormatRate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024d * 1024 * 1024 => $"{bytesPerSecond / (1024d * 1024 * 1024):0.00} GB/s",
        >= 1024d * 1024 => $"{bytesPerSecond / (1024d * 1024):0.0} MB/s",
        >= 1024d => $"{bytesPerSecond / 1024d:0.0} KB/s",
        _ => $"{bytesPerSecond:0} B/s",
    };
}
