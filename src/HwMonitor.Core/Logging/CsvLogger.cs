using System.Globalization;
using System.Text;
using HwMon.Core.Sensors;

namespace HwMon.Core.Logging;

/// <summary>
/// Gravação incremental de amostras em CSV (uma linha por sensor por amostra).
/// </summary>
public sealed class CsvLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public CsvLogger(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var exists = File.Exists(path) && new FileInfo(path).Length > 0;
        _writer = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = true };

        if (!exists)
        {
            _writer.WriteLine("timestamp,sensor_id,hardware,sensor,kind,value,unit,min,max,avg");
        }
    }

    public void Write(DateTimeOffset timestamp, IEnumerable<SensorSnapshot> snapshots)
    {
        if (_disposed)
        {
            return;
        }

        var stamp = timestamp.ToString("O", CultureInfo.InvariantCulture);

        foreach (var sensor in snapshots)
        {
            _writer.Write(stamp);
            _writer.Write(',');
            _writer.Write(Escape(sensor.Id));
            _writer.Write(',');
            _writer.Write(Escape(sensor.Hardware));
            _writer.Write(',');
            _writer.Write(Escape(sensor.Name));
            _writer.Write(',');
            _writer.Write(sensor.Kind);
            _writer.Write(',');
            _writer.Write(sensor.Value.ToString("0.####", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.Write(Escape(sensor.Unit));
            _writer.Write(',');
            _writer.Write(sensor.Min.ToString("0.####", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.Write(sensor.Max.ToString("0.####", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.WriteLine(sensor.Average.ToString("0.####", CultureInfo.InvariantCulture));
        }
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        return value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.Dispose();
    }
}
