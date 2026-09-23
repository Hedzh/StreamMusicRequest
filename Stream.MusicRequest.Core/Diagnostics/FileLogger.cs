using System.Text;

namespace Stream.MusicRequest.Core.Diagnostics;

public static class FileLogger
{
    private static readonly object SyncRoot = new();

    private static readonly string LogDirectory =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "StreamMusicRequest",
            "Logs");

    private static readonly string LogFilePath =
        Path.Combine(
            LogDirectory,
            "app.log");

    public static void Info(
        string message)
    {
        Write(
            "INFO",
            message);
    }

    public static void Error(
        string message,
        Exception? exception = null)
    {
        if (exception != null)
        {
            message +=
                Environment.NewLine +
                exception;
        }

        Write(
            "ERROR",
            message);
    }

    private static void Write(
        string level,
        string message)
    {
        try
        {
            Directory.CreateDirectory(
                LogDirectory);

            var timestamp =
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff");

            var text =
                $"[{timestamp}] [{level}] {message}" +
                Environment.NewLine;

            lock (SyncRoot)
            {
                File.AppendAllText(
                    LogFilePath,
                    text,
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Ошибка логирования не должна
            // ломать работу приложения.
        }
    }
}