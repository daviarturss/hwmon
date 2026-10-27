using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using HwMon.App.ViewModels;

namespace HwMon.App;

public partial class MainWindow : Window
{
    /// <summary>Soma das larguras fixas (Tipo, Valor, Mín, Máx, Média) mais a barra de rolagem.</summary>
    private const double FixedColumnsWidth = 520;

    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        SizeChanged += (_, _) => AdjustColumns();
        Loaded += (_, _) => AdjustColumns();
    }

    private void AdjustColumns()
    {
        if (NameColumn is null || SensorList is null)
        {
            return;
        }

        var available = SensorList.ActualWidth - FixedColumnsWidth;
        if (available > 220)
        {
            NameColumn.Width = available;
        }
    }

    /// <summary>Pede ao DWM a barra de título escura para o app não destoar do resto da janela.</summary>
    private void ApplyDarkTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;

        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+); 19 nas builds anteriores.
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void OnTogglePause(object sender, RoutedEventArgs e) => _viewModel.TogglePause();

    private void OnToggleLog(object sender, RoutedEventArgs e) => _viewModel.ToggleLogging();

    private void OnReset(object sender, RoutedEventArgs e) => _viewModel.ResetStatistics();

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IntervalBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            int.TryParse(tag, out var milliseconds))
        {
            _viewModel.IntervalMs = milliseconds;
        }
    }
}
