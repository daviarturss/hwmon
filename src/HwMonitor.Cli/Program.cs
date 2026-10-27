using System.Globalization;
using System.Text;
using HwMon.Core.Alerts;
using HwMon.Core.Logging;
using HwMon.Core.Providers;
using HwMon.Core.Sensors;

Console.OutputEncoding = Encoding.UTF8;

var options = CliOptions.Parse(args);

if (options.ShowHelp)
{
    CliOptions.PrintHelp();
    return 0;
}

using var hub = new SensorHub(
[
    new LibreHardwareMonitorProvider(),
    new SystemPerformanceProvider(),
]);

hub.Initialize();

foreach (var message in hub.StatusMessages)
{
    Console.Error.WriteLine($"[hwmon] {message}");
}

switch (options.Command)
{
    case "providers":
        PrintProviders(hub);
        return 0;

    case "list":
        PrintSnapshot(hub.Poll());
        return 0;

    case "watch":
        return Watch(hub, options);

    case "export":
        return Export(hub, options);

    default:
        Console.Error.WriteLine($"Comando desconhecido: {options.Command}");
        CliOptions.PrintHelp();
        return 2;
}

static void PrintProviders(SensorHub hub)
{
    foreach (var provider in hub.Providers)
    {
        var state = provider.IsAvailable ? "OK" : "INDISPONÍVEL";
        Console.WriteLine($"{provider.DisplayName,-28} {state,-14} {provider.StatusMessage}");
    }
}

static int Watch(SensorHub hub, CliOptions options)
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancellation.Cancel();
    };

    var alerts = new AlertEvaluator(AlertEvaluator.DefaultRules());
    var deadline = options.Duration is { } duration ? DateTimeOffset.UtcNow.AddSeconds(duration) : (DateTimeOffset?)null;

    while (!cancellation.IsCancellationRequested)
    {
        var timestamp = DateTimeOffset.Now;
        var snapshot = hub.Poll();

        Console.Clear();
        Console.WriteLine($"hwmon — {timestamp:HH:mm:ss}   (Ctrl+C para sair)");
        PrintSnapshot(snapshot, compact: true);

        foreach (var alert in alerts.Evaluate(snapshot, timestamp))
        {
            Console.WriteLine($"! ALERTA {alert.Timestamp:HH:mm:ss}  {alert.SensorName}: {alert.Message} ({alert.Value:0.##})");
        }

        if (deadline is { } end && DateTimeOffset.UtcNow >= end)
        {
            break;
        }

        if (cancellation.Token.WaitHandle.WaitOne(options.Interval))
        {
            break;
        }
    }

    return 0;
}

static int Export(SensorHub hub, CliOptions options)
{
    var path = options.CsvPath ?? $"hwmon-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
    using var logger = new CsvLogger(path);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancellation.Cancel();
    };

    var deadline = options.Duration is { } duration ? DateTimeOffset.UtcNow.AddSeconds(duration) : (DateTimeOffset?)null;
    var samples = 0;

    Console.Error.WriteLine($"[hwmon] gravando em {Path.GetFullPath(path)}");

    while (!cancellation.IsCancellationRequested)
    {
        var snapshot = hub.Poll();
        logger.Write(DateTimeOffset.Now, snapshot);
        samples++;

        if (deadline is { } end && DateTimeOffset.UtcNow >= end)
        {
            break;
        }

        if (cancellation.Token.WaitHandle.WaitOne(options.Interval))
        {
            break;
        }
    }

    Console.Error.WriteLine($"[hwmon] {samples} amostras gravadas");
    return 0;
}

static void PrintSnapshot(IReadOnlyList<SensorSnapshot> snapshot, bool compact = false)
{
    if (snapshot.Count == 0)
    {
        Console.WriteLine("Nenhum sensor disponível. Rode como administrador para MSR/SMBus.");
        return;
    }

    string? currentHardware = null;

    foreach (var sensor in snapshot.OrderBy(s => s.Hardware, StringComparer.OrdinalIgnoreCase)
                                  .ThenBy(s => s.Kind)
                                  .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
    {
        if (!string.Equals(currentHardware, sensor.Hardware, StringComparison.Ordinal))
        {
            currentHardware = sensor.Hardware;
            Console.WriteLine();
            Console.WriteLine($"[{currentHardware}]");
        }

        var name = sensor.Name.Length > 34 ? sensor.Name[..33] + "⬦" : sensor.Name;

        if (compact)
        {
            Console.WriteLine($"  {name,-35} {sensor.DisplayValue,14}   min {Format(sensor.Min),10}   max {Format(sensor.Max),10}");
        }
        else
        {
            Console.WriteLine($"  {name,-35} {sensor.DisplayValue,14}   min {Format(sensor.Min),10}   max {Format(sensor.Max),10}   avg {Format(sensor.Average),10}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"{snapshot.Count} sensores");
}

static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

internal sealed record CliOptions(
    string Command,
    int Interval,
    double? Duration,
    string? CsvPath,
    bool ShowHelp)
{
    public static CliOptions Parse(string[] args)
    {
        var command = "list";
        var interval = 1000;
        double? duration = null;
        string? csv = null;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-h":
                case "--help":
                case "help":
                    help = true;
                    break;
                case "--interval" when i + 1 < args.Length:
                    interval = Math.Max(100, int.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
                case "--duration" when i + 1 < args.Length:
                    duration = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--csv" when i + 1 < args.Length:
                    csv = args[++i];
                    break;
                default:
                    if (!arg.StartsWith('-'))
                    {
                        command = arg;
                    }

                    break;
            }
        }

        return new CliOptions(command, interval, duration, csv, help);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            hwmon — monitor de sensores (clone funcional inspirado no HWiNFO64)

            Uso:
              hwmon <comando> [opções]

            Comandos:
              list        Uma amostra de todos os sensores (padrão)
              watch       Atualiza a tela continuamente
              export      Grava amostras em CSV
              providers   Mostra o estado de cada provedor

            Opções:
              --interval <ms>    Intervalo entre amostras (padrão 1000)
              --duration <s>     Encerra após N segundos
              --csv <arquivo>    Caminho do CSV (export)
              -h, --help         Esta ajuda

            Sensores de MSR/SMBus exigem executar como administrador.
            """);
    }
}

