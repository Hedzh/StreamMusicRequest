using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading.Channels;
using NAudio.Wave;
using Stream.MusicRequest.Core.Diagnostics;

namespace Stream.MusicRequest.Audio.Streaming;

public sealed class LocalAudioStreamServer : IDisposable
{
    private const string Prefix =
        "http://127.0.0.1:8766/";

    private const int MaxBufferedChunks =
        64;

    private readonly HttpListener _listener =
        new();

    private readonly ConcurrentDictionary<
        int,
        ClientConnection> _clients =
        new();

    private readonly object _formatLock =
        new();

    private readonly TaskCompletionSource<WaveFormat>
        _formatReady =
        new(
            TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource? _serverCancellation;

    private Task? _acceptTask;

    private WaveFormat? _currentFormat;

    private int _nextClientId;

    private bool _started;

    public string BrowserSourceUrl =>
        Prefix;

    public string AudioStreamUrl =>
        Prefix + "audio";

    public LocalAudioStreamServer()
    {
        _listener.Prefixes.Add(
            Prefix);
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        try
        {
            _listener.Start();

            _serverCancellation =
                new CancellationTokenSource();

            _started =
                true;

            _acceptTask =
                AcceptLoopAsync(
                    _serverCancellation.Token);

            FileLogger.Info(
                $"Локальный аудиопоток запущен: " +
                $"{BrowserSourceUrl}");
        }
        catch
        {
            _serverCancellation?.Dispose();
            _serverCancellation = null;

            throw;
        }
    }

    public void SetFormat(
        WaveFormat format)
    {
        if (format == null)
        {
            throw new ArgumentNullException(
                nameof(format));
        }

        lock (_formatLock)
        {
            _currentFormat =
                format;

            _formatReady.TrySetResult(
                format);
        }

        FileLogger.Info(
            $"Формат локального аудиопотока: " +
            $"{format.SampleRate} Hz, " +
            $"{format.BitsPerSample} bit, " +
            $"{format.Channels} ch, " +
            $"{format.Encoding}");
    }

    public void Publish(
        ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length == 0 ||
            _clients.IsEmpty)
        {
            return;
        }

        var data =
            buffer.ToArray();

        foreach (var client in _clients.Values)
        {
            client.Publish(
                data);
        }
    }

    public async Task StopAsync()
    {
        if (!_started)
        {
            return;
        }

        _started =
            false;

        _serverCancellation?.Cancel();

        try
        {
            _listener.Stop();
        }
        catch
        {
            // Сервер уже мог быть остановлен.
        }

        foreach (var client in _clients.Values)
        {
            client.Complete();
        }

        _clients.Clear();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch
            {
                // Сервер завершается.
            }
        }

        _acceptTask =
            null;

        _serverCancellation?.Dispose();

        _serverCancellation =
            null;

        FileLogger.Info(
            "Локальный аудиопоток остановлен.");
    }

    public void Dispose()
    {
        try
        {
            StopAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // Завершение приложения не должно
            // зависеть от остановки HTTP-сервера.
        }

        _listener.Close();
    }

    private async Task AcceptLoopAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context =
                    await _listener
                        .GetContextAsync()
                        .WaitAsync(
                            cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = HandleRequestAsync(
                context,
                cancellationToken);
        }
    }

    private async Task HandleRequestAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        var path =
            context.Request.Url?.AbsolutePath;

        if (string.Equals(
                path,
                "/",
                StringComparison.OrdinalIgnoreCase))
        {
            await WriteBrowserSourcePageAsync(
                context,
                cancellationToken);

            return;
        }

        if (!string.Equals(
                path,
                "/audio",
                StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode =
                (int)HttpStatusCode.NotFound;

            context.Response.Close();

            return;
        }

        WaveFormat? format;

        lock (_formatLock)
        {
            format =
                _currentFormat;
        }

        if (format == null)
        {
            try
            {
                format =
                    await _formatReady.Task.WaitAsync(
                        cancellationToken);
            }
            catch
            {
                context.Response.StatusCode =
                    (int)HttpStatusCode.ServiceUnavailable;

                context.Response.Close();

                return;
            }
        }

        var client =
            new ClientConnection(
                Interlocked.Increment(
                    ref _nextClientId));

        if (!_clients.TryAdd(
                client.Id,
                client))
        {
            context.Response.StatusCode =
                (int)HttpStatusCode.ServiceUnavailable;

            context.Response.Close();

            return;
        }

        try
        {
            context.Response.StatusCode =
                (int)HttpStatusCode.OK;

            context.Response.ContentType =
                "audio/wav";

            context.Response.SendChunked =
                true;

            context.Response.KeepAlive =
                true;

            context.Response.Headers[
                "Cache-Control"] =
                "no-cache, no-store, must-revalidate";

            context.Response.Headers[
                "Pragma"] =
                "no-cache";

            context.Response.Headers[
                "Access-Control-Allow-Origin"] =
                "*";

            var header =
                CreateStreamingWaveHeader(
                    format);

            await context.Response.OutputStream.WriteAsync(
                header,
                cancellationToken);

            await context.Response.OutputStream.FlushAsync(
                cancellationToken);

            await foreach (
                var data in client.ReadAllAsync(
                    cancellationToken))
            {
                await context.Response.OutputStream.WriteAsync(
                    data,
                    cancellationToken);

                await context.Response.OutputStream.FlushAsync(
                    cancellationToken);
            }
        }
        catch (
            OperationCanceledException)
        {
            // Клиент или сервер отключён.
        }
        catch (HttpListenerException)
        {
            // Клиент закрыл соединение.
        }
        catch (IOException)
        {
            // Клиент закрыл соединение.
        }
        catch (Exception ex)
        {
            FileLogger.Error(
                "Ошибка клиента локального аудиопотока.",
                ex);
        }
        finally
        {
            _clients.TryRemove(
                client.Id,
                out _);

            client.Complete();

            try
            {
                context.Response.Close();
            }
            catch
            {
                // Ответ уже закрыт.
            }
        }
    }

    private static async Task WriteBrowserSourcePageAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        const string html =
            """
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <title>StreamMusicRequest Audio</title>
                <style>
                    html, body {
                        margin: 0;
                        width: 100%;
                        height: 100%;
                        overflow: hidden;
                        background: transparent;
                    }

                    audio {
                        display: none;
                    }
                </style>
            </head>
            <body>
                <audio id="audio" autoplay></audio>
                <script>
                    const audio = document.getElementById("audio");
                    audio.src = "/audio";
                    audio.autoplay = true;

                    const start = () => {
                        audio.play().catch(() => {
                            setTimeout(start, 1000);
                        });
                    };

                    audio.addEventListener("error", () => {
                        setTimeout(start, 1000);
                    });

                    start();
                </script>
            </body>
            </html>
            """;

        var data =
            Encoding.UTF8.GetBytes(
                html);

        context.Response.StatusCode =
            (int)HttpStatusCode.OK;

        context.Response.ContentType =
            "text/html; charset=utf-8";

        context.Response.ContentLength64 =
            data.Length;

        context.Response.Headers[
            "Cache-Control"] =
            "no-cache, no-store, must-revalidate";

        context.Response.Headers[
            "Access-Control-Allow-Origin"] =
            "*";

        await context.Response.OutputStream.WriteAsync(
            data,
            cancellationToken);

        context.Response.Close();
    }

    private static byte[] CreateStreamingWaveHeader(
        WaveFormat format)
    {
        var standardFormat =
            format.AsStandardWaveFormat();

        if (standardFormat.Encoding !=
            WaveFormatEncoding.Pcm)
        {
            throw new InvalidOperationException(
                "Локальный аудиопоток ожидает PCM.");
        }

        var header =
            new byte[44];

        using var stream =
            new MemoryStream(
                header,
                writable: true);

        using var writer =
            new BinaryWriter(
                stream);

        writer.Write(
            Encoding.ASCII.GetBytes(
                "RIFF"));

        writer.Write(
            uint.MaxValue);

        writer.Write(
            Encoding.ASCII.GetBytes(
                "WAVE"));

        writer.Write(
            Encoding.ASCII.GetBytes(
                "fmt "));

        writer.Write(
            16);

        writer.Write(
            (ushort)1);

        writer.Write(
            (ushort)standardFormat.Channels);

        writer.Write(
            standardFormat.SampleRate);

        writer.Write(
            standardFormat.AverageBytesPerSecond);

        writer.Write(
            (ushort)standardFormat.BlockAlign);

        writer.Write(
            (ushort)standardFormat.BitsPerSample);

        writer.Write(
            Encoding.ASCII.GetBytes(
                "data"));

        writer.Write(
            uint.MaxValue);

        writer.Flush();

        return header;
    }

    private sealed class ClientConnection
    {
        private readonly Channel<byte[]> _channel =
            Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(
                    MaxBufferedChunks)
                {
                    FullMode =
                        BoundedChannelFullMode.DropOldest,

                    SingleReader =
                        true,

                    SingleWriter =
                        false
                });

        public ClientConnection(
            int id)
        {
            Id =
                id;
        }

        public int Id { get; }

        public void Publish(
            byte[] data)
        {
            _channel.Writer.TryWrite(
                data);
        }

        public IAsyncEnumerable<byte[]> ReadAllAsync(
            CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(
                cancellationToken);
        }

        public void Complete()
        {
            _channel.Writer.TryComplete();
        }
    }
}