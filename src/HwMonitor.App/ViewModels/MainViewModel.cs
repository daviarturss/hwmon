using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using HwMon.Core.Alerts;
using HwMon.Core.Logging;
using HwMon.Core.Providers;
using HwMon.Core.Sensors;

namespace HwMon.App.ViewModels;

public sealed class SensorRow : INotifyPropertyChanged
{
    private SensorSnapshot _snapshot;
    private bool _isAlert;

    public SensorRow(SensorSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public string Id => _snapshot.Id;

    public string Hardware => _snapshot.Hardware;

    public string Name => _snapshot.Name;

    public SensorKind Kind => _snapshot.Kind;

    public double Value => _snapshot.Value;

    public double Min => _snapshot.Min;

    public double Max => _snapshot.Max;

    public double Average => _snapshot.Average;

    public bool IsAlert
    {
        get => _isAlert;
        set
        {
            if (_isAlert == value)
            {
                return;
            }

            _isAlert = value;
            Raise(nameof(IsAlert));
        }
    }

    public string DisplayValue => _snapshot.DisplayValue;

    public string KindLabel => Kind switch
    {
        SensorKind.Temperature => "Temperatura",
        SensorKind.Voltage => "Tensão",
        SensorKind.Fan => "Ventoinha",
        SensorKind.Load => "Carga",
        SensorKind.Clock => "Clock",
        SensorKind.Power => "Potência",
        SensorKind.Current => "Corrente",
        SensorKind.Throughput => "Rede",
        SensorKind.Data => "Dados",
        SensorKind.Level => "Nível",
        SensorKind.Factor => "Fator",
        _ => "Outro",
    };

    public string MinText => Format(_snapshot.Min);

    public string MaxText => Format(_snapshot.Max);

    public string AverageText => Format(_snapshot.Average);

    public void Update(SensorSnapshot snapshot)
    {
        _snapshot = snapshot;
        Raise(nameof(Value));
        Raise(nameof(Min));
        Raise(nameof(Max));
        Raise(nameof(Average));
        Raise(nameof(DisplayValue));
        Raise(nameof(MinText));
        Raise(nameof(MaxText));
        Raise(nameof(AverageText));
    }

    private static string Format(double value) => value.ToString("0.##");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SensorHub _hub;
    private readonly DispatcherTimer _timer;
    private readonly AlertEvaluator _alerts;
    private readonly Dictionary<string, SensorRow> _rows = new(StringComparer.Ordinal);
    private CsvLogger? _logger;
    private bool _isPaused;
    private bool _polling;
    private int _intervalMs = 1000;
    private string _status = "iniciando…";
    private string _summary = string.Empty;
    private string _alertText = string.Empty;

    public MainViewModel()
    {
        _hub = new SensorHub(
        [
            new LibreHardwareMonitorProvider(),
            new SystemPerformanceProvider(),
        ]);

        _hub.Initialize();

        Sensors = new ObservableCollection<SensorRow>();
        SensorsView = CollectionViewSource.GetDefaultView(Sensors);
        SensorsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorRow.Hardware)));
        SensorsView.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Hardware), ListSortDirection.Ascending));
        SensorsView.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Kind), ListSortDirection.Ascending));
        SensorsView.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Name), ListSortDirection.Ascending));

        _alerts = new AlertEvaluator(AlertEvaluator.DefaultRules());

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(_intervalMs),
        };
        _timer.Tick += (_, _) => PollAsync();
        _timer.Start();

        var providerStatus = _hub.StatusMessages.Count == 0
            ? string.Join(" · ", _hub.Providers.Select(p => $"{p.DisplayName}: {(p.IsAvailable ? "ok" : "off")}"))
            : string.Join(" | ", _hub.StatusMessages);

        Status = providerStatus;
        PollAsync();
    }

    public ObservableCollection<SensorRow> Sensors { get; }

    public ICollectionView SensorsView { get; }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused == value)
            {
                return;
            }

            _isPaused = value;
            Raise(nameof(IsPaused));
            Raise(nameof(PauseLabel));
        }
    }

    public string PauseLabel => IsPaused ? "Retomar" : "Pausar";

    public int IntervalMs
    {
        get => _intervalMs;
        set
        {
            var clamped = Math.Clamp(value, 250, 10_000);
            if (_intervalMs == clamped)
            {
                return;
            }

            _intervalMs = clamped;
            _timer.Interval = TimeSpan.FromMilliseconds(clamped);
            Raise(nameof(IntervalMs));
        }
    }

    public bool IsLogging => _logger is not null;

    public string LogLabel => IsLogging ? "Parar log" : "Gravar CSV";

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Raise(nameof(Status));
        }
    }

    public string Summary
    {
        get => _summary;
        private set
        {
            _summary = value;
            Raise(nameof(Summary));
        }
    }

    public string AlertText
    {
        get => _alertText;
        private set
        {
            _alertText = value;
            Raise(nameof(AlertText));
            Raise(nameof(HasAlert));
        }
    }

    public bool HasAlert => !string.IsNullOrEmpty(_alertText);

    public string Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            Raise(nameof(Filter));
            SensorsView.Filter = item => string.IsNullOrWhiteSpace(_filter) ||
                                        (item is SensorRow row &&
                                         (row.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                                          row.Hardware.Contains(_filter, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private string _filter = string.Empty;

    public void TogglePause() => IsPaused = !IsPaused;

    public void ResetStatistics()
    {
        _hub.ResetStatistics();
        _rows.Clear();
        Sensors.Clear();
        AlertText = string.Empty;
        PollAsync();
    }

    public void ToggleLogging()
    {
        if (_logger is not null)
        {
            _logger.Dispose();
            _logger = null;
            Status = "log encerrado";
        }
        else
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "HwMon",
                $"hwmon-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            _logger = new CsvLogger(path);
            Status = $"gravando em {path}";
        }

        Raise(nameof(IsLogging));
        Raise(nameof(LogLabel));
    }

    private async void PollAsync()
    {
        if (IsPaused || _polling)
        {
            return;
        }

        _polling = true;

        try
        {
            var timestamp = DateTimeOffset.Now;

            // A leitura de hardware bloqueia (SMBus, NVAPI, IOCTL); fora da thread da UI.
            var snapshot = await Task.Run(_hub.Poll);

            foreach (var reading in snapshot)
            {
                if (_rows.TryGetValue(reading.Id, out var row))
                {
                    row.Update(reading);
                }
                else
                {
                    var created = new SensorRow(reading);
                    _rows[reading.Id] = created;
                    Sensors.Add(created);
                }
            }

            _logger?.Write(timestamp, snapshot);

            var events = _alerts.Evaluate(snapshot, timestamp);

            // Marca as linhas que estão violando alguma regra agora.
            var triggered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sensor in snapshot)
            {
                foreach (var rule in _alerts.Rules)
                {
                    if (rule.Matches(sensor) && rule.IsTriggered(sensor.Value))
                    {
                        triggered.Add(sensor.Id);
                    }
                }
            }

            foreach (var row in _rows.Values)
            {
                row.IsAlert = triggered.Contains(row.Id);
            }

            var liveHardware = snapshot.Select(s => s.Hardware).Distinct(StringComparer.Ordinal).Count();
            Summary = $"{snapshot.Count} sensores · {liveHardware} hardwares · atualizado {timestamp:HH:mm:ss}";

            if (events.Count > 0)
            {
                AlertText = string.Join("  ·  ", events.Select(e => $"{e.SensorName}: {e.Message}"));
            }
            else if (_alerts.ActiveKeys.Count == 0 && AlertText.Length > 0)
            {
                AlertText = string.Empty;
            }
        }
        catch (Exception ex)
        {
            Status = $"erro na leitura: {ex.Message}";
        }
        finally
        {
            _polling = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public void Dispose()
    {
        _timer.Stop();
        _logger?.Dispose();
        _hub.Dispose();
    }
}

