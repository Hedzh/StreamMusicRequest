using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using NAudio.Wave;
using Stream.MusicRequest.Core.Diagnostics;

namespace Stream.MusicRequest.Audio.Streaming;

public sealed class LocalAudioStreamServer : IDisposable
{
    private readonly IPAddress _address;

    private readonly int _port;

    private const int MaxBufferedChunks =
        64;

    private readonly TcpListener _listener;

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

    private long _publishedBytes;
    private long _publishedChunks;
    private long _droppedChunks;
    private long _deliveredBytes;
    private long _deliveredChunks;

    public string BrowserSourceUrl =>
        $"http://{_address}:{_port}/";

    public LocalAudioStreamServer(
        string bindAddress = "127.0.0.1",
        int port = 8766)
    {
        if (!IPAddress.TryParse(
                bindAddress,
                out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException(
                "HTTP-адрес OBS должен быть корректным IPv4-адресом.",
                nameof(bindAddress));
        }

        _address = address;

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port));
        }

        _port = port;

        _listener =
            new TcpListener(
                _address,
                _port);
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

        Interlocked.Add(ref _publishedBytes, data.Length);
        Interlocked.Increment(ref _publishedChunks);

        foreach (var client in _clients.Values)
        {
            if (!client.Publish(data))
            {
                Interlocked.Increment(ref _droppedChunks);
            }
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
                await _acceptTask.WaitAsync(
                    TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                FileLogger.Info(
                    "Ожидание остановки локального " +
                    "аудиосервера превысило 2 секунды. " +
                    "Продолжаем завершение приложения.");
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

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync()
                .ConfigureAwait(false);
        }
        catch
        {
            // Завершение приложения не должно
            // зависеть от остановки HTTP-сервера.
        }
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
    }

    private async Task AcceptLoopAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client =
                    await _listener.AcceptTcpClientAsync(
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = HandleClientAsync(
                client,
                cancellationToken);
        }
    }

    private async Task HandleClientAsync(
        TcpClient tcpClient,
        CancellationToken cancellationToken)
    {
        using (tcpClient)

        try
        {
            tcpClient.NoDelay =
                true;

            await using var stream =
                tcpClient.GetStream();

            var request =
                await ReadRequestAsync(
                    stream,
                    cancellationToken);

            if (request == null)
            {
                return;
            }

            if (string.Equals(
                    request.Path,
                    "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                await WriteBrowserSourcePageAsync(
                    stream,
                    cancellationToken);

                return;
            }

            if (string.Equals(
                    request.Path,
                    "/debug",
                    StringComparison.OrdinalIgnoreCase))
            {
                await WriteDebugResponseAsync(
                    stream,
                    cancellationToken);

                return;
            }

            if (!string.Equals(
                    request.Path,
                    "/pcm",
                    StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextResponseAsync(
                    stream,
                    "404 Not Found",
                    "Not Found",
                    cancellationToken);

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
                    await WriteTextResponseAsync(
                        stream,
                        "503 Service Unavailable",
                        "Audio format is not ready.",
                        cancellationToken);

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
                await WriteTextResponseAsync(
                    stream,
                    "503 Service Unavailable",
                    "Audio stream is unavailable.",
                    cancellationToken);

                return;
            }

            try
            {
                if (string.Equals(
                        request.Path,
                        "/pcm",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await WritePcmHeadersAsync(
                        stream,
                        cancellationToken);
                }
                else
                {
                    await WriteAudioHeadersAsync(
                        stream,
                        format,
                        cancellationToken);
                }

                await foreach (
                    var data in client.ReadAllAsync(
                        cancellationToken))
                {
                    await WriteChunkAsync(
                        stream,
                        data,
                        cancellationToken);

                    Interlocked.Add(ref _deliveredBytes, data.Length);
                    Interlocked.Increment(ref _deliveredChunks);

                    await stream.FlushAsync(
                        cancellationToken);
                }
            }
            finally
            {
                _clients.TryRemove(
                    client.Id,
                    out _);

                client.Complete();
            }
        }
        catch (OperationCanceledException)
        {
            // Клиент или сервер отключён.
        }
        catch (IOException)
        {
            // Клиент закрыл соединение.
        }
        catch (SocketException)
        {
            // Клиент закрыл соединение.
        }
        catch (Exception ex)
        {
            FileLogger.Error(
                "Ошибка клиента локального аудиопотока.",
                ex);
        }
    }

    private static async Task<HttpRequest?> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer =
            new byte[8192];

        var bytesRead =
            0;

        while (bytesRead <
               buffer.Length)
        {
            var count =
                await stream.ReadAsync(
                    buffer.AsMemory(
                        bytesRead,
                        buffer.Length - bytesRead),
                    cancellationToken);

            if (count == 0)
            {
                return null;
            }

            bytesRead += count;

            if (ContainsHeaderTerminator(
                    buffer,
                    bytesRead))
            {
                break;
            }
        }

        var requestText =
            Encoding.ASCII.GetString(
                buffer,
                0,
                bytesRead);

        var firstLineEnd =
            requestText.IndexOf(
                "\r\n",
                StringComparison.Ordinal);

        if (firstLineEnd < 0)
        {
            return null;
        }

        var firstLine =
            requestText[..firstLineEnd];

        var parts =
            firstLine.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return null;
        }

        return new HttpRequest(
            parts[0],
            parts[1]);
    }

    private static bool ContainsHeaderTerminator(
        byte[] buffer,
        int length)
    {
        for (var index = 3;
             index < length;
             index++)
        {
            if (buffer[index - 3] == '\r' &&
                buffer[index - 2] == '\n' &&
                buffer[index - 1] == '\r' &&
                buffer[index] == '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteBrowserSourcePageAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        const string html =
            """
            <!doctype html>
            <html>
            <body>
            <script>
            (() => {
                const bytesPerFrame = 4;
                const context = new AudioContext({latencyHint:"interactive", sampleRate:48000});
                const processor = context.createScriptProcessor(2048, 0, 2);
                const queue = [];
                let pending = new Uint8Array(0);

                processor.onaudioprocess = e => {
                    const left = e.outputBuffer.getChannelData(0);
                    const right = e.outputBuffer.getChannelData(1);

                    for (let i = 0; i < left.length; i++) {
                        if (!queue.length) {
                            left[i] = 0;
                            right[i] = 0;
                            continue;
                        }

                        const frame = queue[0];
                        const p = frame.position++;
                        left[i] = frame.data[p * 2];
                        right[i] = frame.data[p * 2 + 1];

                        if (frame.position >= frame.data.length / 2)
                            queue.shift();
                    }
                };

                processor.connect(context.destination);

                const append = bytes => {
                    const usable = bytes.length - (bytes.length % bytesPerFrame);
                    if (!usable) return;

                    const view = new DataView(bytes.buffer, bytes.byteOffset, usable);
                    const data = new Float32Array(usable / 2);

                    for (let i = 0; i < data.length; i++)
                        data[i] = view.getInt16(i * 2, true) / 32768;

                    queue.push({data:data, position:0});

                    while (queue.length > 1) {
                        let frames = 0;
                        for (const item of queue)
                            frames += item.data.length / 2 - item.position;

                        if (frames <= 12000) break;
                        queue.shift();
                    }
                };

                const connect = async () => {
                    try {
                        const response = await fetch("/pcm", {cache:"no-store"});
                        if (!response.ok || !response.body) throw new Error();

                        await context.resume();
                        const reader = response.body.getReader();

                        while (true) {
                            const result = await reader.read();
                            if (result.done) break;

                            const combined = new Uint8Array(
                                pending.length + result.value.length);

                            combined.set(pending);
                            combined.set(result.value, pending.length);

                            const usable =
                                combined.length -
                                (combined.length % bytesPerFrame);

                            append(combined.subarray(0, usable));
                            pending = combined.subarray(usable);
                        }
                    } catch {
                        setTimeout(connect, 250);
                    }
                };

                connect();
            })();
            </script>
            </body>
            </html>
            """;

        var data =
            Encoding.UTF8.GetBytes(
                html);

        var header =
            $"HTTP/1.1 200 OK\r\n" +
            $"Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {data.Length}\r\n" +
            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(header),
            cancellationToken);

        await stream.WriteAsync(
            data,
            cancellationToken);

        await stream.FlushAsync(
            cancellationToken);
    }


    private async Task WriteDebugResponseAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var activeClients = _clients.Count;
        var publishedBytes = Interlocked.Read(ref _publishedBytes);
        var publishedChunks = Interlocked.Read(ref _publishedChunks);
        var droppedChunks = Interlocked.Read(ref _droppedChunks);
        var deliveredBytes = Interlocked.Read(ref _deliveredBytes);
        var deliveredChunks = Interlocked.Read(ref _deliveredChunks);

        var json =
            $"{{\"activeClients\":{activeClients}," +
            $" \"publishedBytes\":{publishedBytes}," +
            $" \"publishedChunks\":{publishedChunks}," +
            $" \"droppedChunks\":{droppedChunks}," +
            $" \"deliveredBytes\":{deliveredBytes}," +
            $" \"deliveredChunks\":{deliveredChunks}}}";

        var data = Encoding.UTF8.GetBytes(json);
        var header =
            $"HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {data.Length}\r\n" +
            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
            "Connection: close\r\n\r\n";

        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(header),
            cancellationToken);

        await stream.WriteAsync(
            data,
            cancellationToken);

        await stream.FlushAsync(
            cancellationToken);
    }

    private static async Task WriteTextResponseAsync(
        NetworkStream stream,
        string status,
        string body,
        CancellationToken cancellationToken)
    {
        var data =
            Encoding.UTF8.GetBytes(
                body);

        var header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {data.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        var headerData =
            Encoding.ASCII.GetBytes(
                header);

        await stream.WriteAsync(
            headerData,
            cancellationToken);

        await stream.WriteAsync(
            data,
            cancellationToken);

        await stream.FlushAsync(
            cancellationToken);
    }

    private static async Task WritePcmHeadersAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        const string header =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/octet-stream\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
            "Connection: keep-alive\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "\r\n";

        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(header),
            cancellationToken);

        await stream.FlushAsync(
            cancellationToken);
    }

    private static async Task WriteAudioHeadersAsync(
        NetworkStream stream,
        WaveFormat format,
        CancellationToken cancellationToken)
    {
        var header =
            CreateStreamingWaveHeader(
                format);

        var headerText =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: audio/wav\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
            "Connection: keep-alive\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "\r\n";

        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(
                headerText),
            cancellationToken);

        await WriteChunkAsync(
            stream,
            header,
            cancellationToken);

        await stream.FlushAsync(
            cancellationToken);
    }

    private static async Task WriteChunkAsync(
        NetworkStream stream,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var size =
            Encoding.ASCII.GetBytes(
                $"{data.Length:X}\r\n");

        await stream.WriteAsync(
            size,
            cancellationToken);

        await stream.WriteAsync(
            data,
            cancellationToken);

        await stream.WriteAsync(
            "\r\n"u8.ToArray(),
            cancellationToken);
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

    private sealed record HttpRequest(
        string Method,
        string Path);

    private sealed class ClientConnection
    {
        private readonly Channel<byte[]> _channel =
            Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(
                    MaxBufferedChunks)
                {
                    FullMode =
                        BoundedChannelFullMode.DropWrite,

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

        public bool Publish(
            byte[] data)
        {
            return _channel.Writer.TryWrite(
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