using System.Text;

namespace Stream.MusicRequest.DonationAlerts.Test.Logging;

public sealed class DonationAlertsTestLogger : IDisposable
{
    private readonly string _directory;
    private readonly object _sync = new();

    private StreamWriter? _writer;
    private DateOnly _writerDate;

    public DonationAlertsTestLogger()
    {
        _directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "StreamMusicRequest",
                "DonationAlertsTest",
                "Logs");

        Directory.CreateDirectory(_directory);
    }

    public void Log(string message)
    {
        lock (_sync)
        {
            EnsureWriter();

            _writer!.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}");

            _writer.Flush();
        }
    }

    public void LogRaw(string title, string json)
    {
        lock (_sync)
        {
            EnsureWriter();

            _writer!.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] {title}");

            _writer.WriteLine(json);
            _writer.Flush();
        }
    }

    private void EnsureWriter()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        if (_writer != null &&
            _writerDate == today)
        {
            return;
        }

        _writer?.Dispose();

        var path =
            Path.Combine(
                _directory,
                $"donationalerts-{today:yyyy-MM-dd}.log");

        _writer =
            new StreamWriter(
                new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite),
                new UTF8Encoding(false))
            {
                AutoFlush = false
            };

        _writerDate = today;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
