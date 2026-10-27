namespace HwMon.Core.Sensors;

/// <summary>
/// Agrega todos os provedores, mantém estatísticas de sessão e isola falhas.
/// </summary>
public sealed class SensorHub : IDisposable
{
    private sealed class Stat
    {
        public double Min = double.MaxValue;
        public double Max = double.MinValue;
        public double Sum;
        public long Count;
    }

    private readonly List<ISensorProvider> _providers;
    private readonly Dictionary<string, Stat> _stats = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _disposed;

    public SensorHub(IEnumerable<ISensorProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IReadOnlyList<ISensorProvider> Providers => _providers;

    public IReadOnlyList<string> StatusMessages =>
        _providers.Where(p => !string.IsNullOrWhiteSpace(p.StatusMessage))
                  .Select(p => $"{p.DisplayName}: {p.StatusMessage}")
                  .ToList();

    public void Initialize()
    {
        foreach (var provider in _providers)
        {
            try
            {
                provider.Initialize();
            }
            catch (Exception ex)
            {
                // Um provedor com problema não pode impedir a coleta dos demais.
                Console.Error.WriteLine($"[hwmon] {provider.DisplayName} falhou: {ex.Message}");
            }
        }
    }

    /// <summary>Coleta uma amostra de todos os provedores disponíveis.</summary>
    public IReadOnlyList<SensorSnapshot> Poll()
    {
        var readings = new List<SensorReading>();

        foreach (var provider in _providers)
        {
            if (!provider.IsAvailable)
            {
                continue;
            }

            try
            {
                readings.AddRange(provider.Read());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[hwmon] {provider.DisplayName} erro na leitura: {ex.Message}");
            }
        }

        return Accumulate(readings);
    }

    private IReadOnlyList<SensorSnapshot> Accumulate(List<SensorReading> readings)
    {
        var result = new List<SensorSnapshot>(readings.Count);

        lock (_gate)
        {
            foreach (var reading in readings)
            {
                if (!double.IsFinite(reading.Value))
                {
                    continue;
                }

                if (!_stats.TryGetValue(reading.Id, out var stat))
                {
                    stat = new Stat();
                    _stats[reading.Id] = stat;
                }

                stat.Min = Math.Min(stat.Min, reading.Value);
                stat.Max = Math.Max(stat.Max, reading.Value);
                stat.Sum += reading.Value;
                stat.Count++;

                result.Add(new SensorSnapshot(
                    reading.Id,
                    reading.Hardware,
                    reading.Name,
                    reading.Kind,
                    reading.Value,
                    reading.Unit,
                    stat.Min,
                    stat.Max,
                    stat.Sum / stat.Count));
            }
        }

        return result;
    }

    public void ResetStatistics()
    {
        lock (_gate)
        {
            _stats.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var provider in _providers)
        {
            try
            {
                provider.Dispose();
            }
            catch
            {
                // Descarte é best-effort.
            }
        }
    }
}
