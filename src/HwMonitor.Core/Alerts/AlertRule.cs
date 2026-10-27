using HwMon.Core.Sensors;

namespace HwMon.Core.Alerts;

public enum Comparison
{
    Above,
    Below,
}

/// <summary>Regra de alerta avaliada a cada amostra.</summary>
public sealed record AlertRule(
    string SensorPattern,
    Comparison Comparison,
    double Threshold,
    string? Message = null,
    SensorKind? Kind = null)
{
    public bool Matches(SensorSnapshot sensor)
    {
        if (Kind is { } kind && sensor.Kind != kind)
        {
            return false;
        }

        if (string.IsNullOrEmpty(SensorPattern))
        {
            return Kind is not null;
        }

        return sensor.Id.Contains(SensorPattern, StringComparison.OrdinalIgnoreCase) ||
               sensor.Name.Contains(SensorPattern, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsTriggered(double value) =>
        Comparison == Comparison.Above ? value > Threshold : value < Threshold;

    public string Describe() => Comparison == Comparison.Above
        ? $"{SensorPattern} > {Threshold:0.##}"
        : $"{SensorPattern} < {Threshold:0.##}";
}

public sealed record AlertEvent(
    string SensorId,
    string SensorName,
    string Rule,
    string Message,
    double Value,
    DateTimeOffset Timestamp);

/// <summary>
/// Avalia regras e emite apenas transições (disparou / normalizou) para não inundar a UI.
/// </summary>
public sealed class AlertEvaluator
{
    private readonly List<AlertRule> _rules;
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);

    public AlertEvaluator(IEnumerable<AlertRule> rules)
    {
        _rules = rules.ToList();
    }

    public IReadOnlyList<AlertRule> Rules => _rules;

    public IReadOnlyCollection<string> ActiveKeys => _active;

    public static IReadOnlyList<AlertRule> DefaultRules() =>
    [
        // Alertas padrão propositalmente conservadores: só temperatura realmente alta.
        // Para vigiar ventoinhas ou carga, acrescente regras como:
        //   new AlertRule(string.Empty, Comparison.Below, 300, "Ventoinha parada", SensorKind.Fan)
        //   new AlertRule(string.Empty, Comparison.Above, 95, "Carga acima de 95%", SensorKind.Load)
        new AlertRule(string.Empty, Comparison.Above, 90, "Temperatura acima de 90 °C", SensorKind.Temperature),
    ];

    /// <summary>
    /// Retorna alertas que mudaram de estado nesta amostra.
    /// </summary>
    public IReadOnlyList<AlertEvent> Evaluate(IEnumerable<SensorSnapshot> snapshots, DateTimeOffset timestamp)
    {
        var events = new List<AlertEvent>();
        var stillActive = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sensor in snapshots)
        {
            for (var index = 0; index < _rules.Count; index++)
            {
                var rule = _rules[index];

                if (!rule.Matches(sensor))
                {
                    continue;
                }

                var key = $"{index}|{sensor.Id}";

                if (rule.IsTriggered(sensor.Value))
                {
                    stillActive.Add(key);

                    if (_active.Add(key))
                    {
                        events.Add(new AlertEvent(
                            sensor.Id,
                            $"{sensor.Hardware} / {sensor.Name}",
                            rule.Describe(),
                            rule.Message ?? rule.Describe(),
                            sensor.Value,
                            timestamp));
                    }
                }
            }
        }

        // Regras que deixaram de disparar liberam a chave para um futuro alerta.
        _active.IntersectWith(stillActive);

        return events;
    }
}
