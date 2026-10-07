using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Stream.MusicRequest.Core.Diagnostics;
using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.App;

public sealed class ObsWidgetServer : IAsyncDisposable
{
    private const int MaxRequestSize = 8192;

    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<int, byte> _clients = new();
    private readonly object _stateLock = new();

    private CancellationTokenSource? _cancellation;
    private Task? _acceptTask;
    private int _nextClientId;
    private bool _started;

    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _requestedBy = string.Empty;
    private string _coverUrl = string.Empty;
    private double _scrollSpeed = 60.0;
    private string _displayMode = "CoverLeft";
    private bool _visible;

    private readonly HttpClient _coverHttpClient = new();
    private readonly SemaphoreSlim _coverCacheLock = new(1, 1);
    private byte[]? _cachedCoverBytes;
    private string? _cachedCoverUrl;
    private string _cachedCoverContentType = "image/jpeg";

    public ObsWidgetServer(
        string bindAddress,
        int port,
        double scrollSpeed,
        string displayMode)
    {
        if (!IPAddress.TryParse(
                bindAddress,
                out var address))
        {
            throw new ArgumentException(
                "HTTP-адрес OBS должен быть корректным IPv4-адресом.",
                nameof(bindAddress));
        }

        if (address.AddressFamily !=
            AddressFamily.InterNetwork)
        {
            throw new ArgumentException(
                "Для OBS используется IPv4-адрес.",
                nameof(bindAddress));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port));
        }

        _listener =
            new TcpListener(
                address,
                port);

        _scrollSpeed =
            Math.Clamp(
                scrollSpeed,
                10.0,
                300.0);

        _displayMode =
            NormalizeDisplayMode(displayMode);
    }

    public string Url
    {
        get
        {
            var endpoint =
                _listener.LocalEndpoint as IPEndPoint;

            if (endpoint == null)
            {
                return string.Empty;
            }

            return $"http://{endpoint.Address}:{endpoint.Port}/widget";
        }
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _listener.Start();

        _cancellation =
            new CancellationTokenSource();

        _started =
            true;

        _acceptTask =
            AcceptLoopAsync(
                _cancellation.Token);

        FileLogger.Info(
            $"OBS виджет запущен: {Url}");
    }

    public void SetCurrentTrack(
        Track? track)
    {
        lock (_stateLock)
        {
            if (track == null)
            {
                _title = string.Empty;
                _artist = string.Empty;
                _requestedBy = string.Empty;
                _coverUrl = string.Empty;
                _visible = false;
                return;
            }

            _visible = true;

            _title =
                track.Title?.Trim()
                ?? string.Empty;

            _artist =
                track.Artist?.Trim()
                ?? string.Empty;

            _requestedBy =
                track.RequestedBy?.Trim()
                ?? string.Empty;

            _coverUrl =
                track.CoverUrl?.Trim()
                ?? string.Empty;
        }
    }

    public void SetScrollSpeed(
        double scrollSpeed)
    {
        lock (_stateLock)
        {
            _scrollSpeed =
                Math.Clamp(
                    scrollSpeed,
                    10.0,
                    300.0);
        }
    }

    public void SetDisplayMode(
        string displayMode)
    {
        lock (_stateLock)
        {
            _displayMode =
                NormalizeDisplayMode(displayMode);
        }
    }

    private static string NormalizeDisplayMode(
        string? displayMode)
    {
        return displayMode switch
        {
            "Text" => "Text",
            "CoverBottom" => "CoverBottom",
            _ => "CoverLeft"
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started)
        {
            return;
        }

        _started =
            false;

        _cancellation?.Cancel();

        try
        {
            _listener.Stop();
        }
        catch
        {
            // Сервер уже мог быть остановлен.
        }

        _clients.Clear();

        _coverHttpClient.Dispose();
        _coverCacheLock.Dispose();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask
                    .WaitAsync(
                        TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }
            catch
            {
                // Завершение приложения не должно зависеть от сервера виджета.
            }
        }

        _acceptTask = null;

        _cancellation?.Dispose();
        _cancellation = null;

        FileLogger.Info(
            "OBS виджет остановлен.");
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

            var id =
                Interlocked.Increment(
                    ref _nextClientId);

            _clients.TryAdd(
                id,
                0);

            _ = HandleClientAsync(
                id,
                client,
                cancellationToken);
        }
    }

    private async Task HandleClientAsync(
        int id,
        TcpClient client,
        CancellationToken cancellationToken)
    {
        using (client)

            try
            {
                client.NoDelay = true;

                await using var stream =
                    client.GetStream();

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
                        "/widget",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        request.Path,
                        "/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await WriteHtmlAsync(
                        stream,
                        cancellationToken);

                    return;
                }

                if (string.Equals(
                        request.Path,
                        "/state",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await WriteStateAsync(
                        stream,
                        cancellationToken);

                    return;
                }

                if (string.Equals(
                        request.Path,
                        "/cover",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await WriteCoverAsync(
                        stream,
                        cancellationToken);

                    return;
                }

                await WriteResponseAsync(
                    stream,
                    "404 Not Found",
                    "Not Found",
                    "text/plain; charset=utf-8",
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (Exception ex)
            {
                FileLogger.Error(
                    "Ошибка клиента OBS виджета.",
                    ex);
            }
            finally
            {
                _clients.TryRemove(
                    id,
                    out _);
            }
    }

    private async Task WriteHtmlAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        const string html =
            """
            <!doctype html>
            <html lang="ru">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <style>
            html,body {
                margin:0;
                padding:0;
                width:100%;
                height:100%;
                overflow:hidden;
                background:transparent;
                color:#fff;
                font-family:Arial,Helvetica,sans-serif;
            }

            #viewport {
                width:100%;
                height:100%;
                overflow:hidden;
                display:flex;
                align-items:center;
                gap:18px;
            }

            #coverFrame {
                flex:0 0 96px;
                width:96px;
                height:96px;
                border-radius:10px;
                overflow:hidden;
                background:rgba(20,24,30,.85);
                box-shadow:0 2px 8px rgba(0,0,0,.45);
                display:none;
            }

            #cover {
                width:100%;
                height:100%;
                object-fit:cover;
                display:block;
            }

            #textViewport {
                flex:1 1 auto;
                min-width:0;
                overflow:hidden;
            }

            #marquee {
                display:inline-flex;
                width:max-content;
                white-space:nowrap;
                will-change:transform;
            }

            .item {
                display:inline-block;
                padding-right:80px;
                font-size:32px;
                font-weight:600;
                line-height:1.2;
                text-shadow:0 2px 4px rgba(0,0,0,.75);
            }

            @keyframes marquee {
                from {
                    transform:translateX(0);
                }

                to {
                    transform:translateX(-50%);
                }
            }

            #viewport.mode-text #coverFrame {
                display:none !important;
            }

            #viewport.mode-cover-left {
                flex-direction:row;
            }

            #viewport.mode-cover-bottom {
                flex-direction:column;
                justify-content:center;
                align-items:center;
                gap:10px;
            }

            #viewport.mode-cover-bottom #coverFrame {
                flex:0 0 min(700px, calc(100vw - 20px), calc(100vh - 60px));
                width:min(700px, calc(100vw - 20px), calc(100vh - 60px));
                height:min(700px, calc(100vw - 20px), calc(100vh - 60px));
            }

            #viewport.mode-cover-bottom #textViewport {
                width:100%;
                flex:0 0 auto;
                min-height:38px;
            }

            .idle {
                animation:none !important;
            }
            </style>
            </head>
            <body>
            <div id="viewport" class="mode-cover-left">
                <div id="coverFrame">
                    <img id="cover" alt="" />
                </div>
                <div id="textViewport">
                    <div id="marquee" class="idle">
                        <span id="item1" class="item"></span>
                        <span id="item2" class="item"></span>
                    </div>
                </div>
            </div>

            <script>
            (() => {
                const viewport = document.getElementById("viewport");
                const coverFrame = document.getElementById("coverFrame");
                const cover = document.getElementById("cover");
                const marquee = document.getElementById("marquee");
                const item1 = document.getElementById("item1");
                const item2 = document.getElementById("item2");

                let lastKey = "";

                const setState = state => {
                    if (state.visible === false) {
                        clearState();
                        return;
                    }

                    const artist = (state.artist || "").trim();
                    const title = (state.title || "").trim();
                    const requestedBy = (state.requestedBy || "").trim();
                    const coverUrl = (state.coverUrl || "").trim();
                    const displayMode = state.displayMode || "CoverLeft";

                    viewport.className =
                        displayMode === "Text"
                            ? "mode-text"
                            : displayMode === "CoverBottom"
                                ? "mode-cover-bottom"
                                : "mode-cover-left";

                    let text = "";

                    if (artist || title) {
                        text =
                            (artist && title)
                                ? artist + " — " + title
                                : (title || artist);

                        if (requestedBy)
                            text += "  •  Заказал: " + requestedBy;
                    }

                    const key =
                        text +
                        "|" +
                        coverUrl +
                        "|" +
                        displayMode +
                        "|" +
                        String(state.scrollSpeed || 60);

                    if (key === lastKey)
                        return;

                    lastKey = key;

                    item1.textContent = text;
                    item2.textContent = text;

                    if (coverUrl && displayMode !== "Text") {
                        cover.src = "/cover";
                        coverFrame.style.display = "block";
                    } else {
                        cover.removeAttribute("src");
                        coverFrame.style.display = "none";
                    }

                    marquee.classList.remove("idle");
                    marquee.style.animation = "none";
                    void marquee.offsetWidth;

                    if (!(artist || title)) {
                        marquee.classList.add("idle");
                        marquee.style.transform = "translateX(0)";
                        return;
                    }

                    const speed =
                        Math.max(
                            10,
                            Math.min(
                                300,
                                Number(state.scrollSpeed) || 60));

                    const distance =
                        Math.max(
                            320,
                            item1.getBoundingClientRect().width);

                    const duration =
                        Math.max(
                            5,
                            distance / speed);

                    marquee.style.animation =
                        "marquee " + duration + "s linear infinite";
                };

                const clearState = () => {
                    lastKey = "";

                    item1.textContent = "";
                    item2.textContent = "";

                    cover.removeAttribute("src");
                    coverFrame.style.display = "none";

                    marquee.classList.add("idle");
                    marquee.style.animation = "none";
                    marquee.style.transform = "translateX(0)";
                };

                cover.addEventListener("error", () => {
                    coverFrame.style.display = "none";
                });

                const load = async () => {
                    try {
                        const response =
                            await fetch(
                                "/state",
                                {cache:"no-store"});

                        if (!response.ok)
                            throw new Error();

                        setState(
                            await response.json());
                    } catch {
                        clearState();
                    }
                };

                load();
                setInterval(load, 500);
            })();
            </script>
            </body>
            </html>
            """;

        await WriteResponseAsync(
            stream,
            "200 OK",
            html,
            "text/html; charset=utf-8",
            cancellationToken);
    }

    private async Task WriteCoverAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        string coverUrl;

        lock (_stateLock)
        {
            coverUrl = _coverUrl;
        }

        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            await WriteResponseAsync(
                stream,
                "404 Not Found",
                "Not Found",
                "text/plain; charset=utf-8",
                cancellationToken);

            return;
        }

        try
        {
            await _coverCacheLock.WaitAsync(
                cancellationToken);

            try
            {
                if (!string.Equals(
                        _cachedCoverUrl,
                        coverUrl,
                        StringComparison.Ordinal) ||
                    _cachedCoverBytes == null)
                {
                    using var request =
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            coverUrl);

                    using var response =
                        await _coverHttpClient.SendAsync(
                            request,
                            HttpCompletionOption.ResponseHeadersRead,
                            cancellationToken);

                    response.EnsureSuccessStatusCode();

                    var contentType =
                        response.Content.Headers.ContentType?.MediaType;

                    var bytes =
                        await response.Content.ReadAsByteArrayAsync(
                            cancellationToken);

                    if (bytes.Length == 0)
                        throw new InvalidOperationException(
                            "Сервер обложки вернул пустой файл.");

                    _cachedCoverBytes = bytes;
                    _cachedCoverUrl = coverUrl;
                    _cachedCoverContentType =
                        string.IsNullOrWhiteSpace(contentType)
                            ? "image/jpeg"
                            : contentType;
                }

                await WriteResponseBytesAsync(
                    stream,
                    "200 OK",
                    _cachedCoverBytes,
                    _cachedCoverContentType,
                    cancellationToken);
            }
            finally
            {
                _coverCacheLock.Release();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Info(
                $"OBS: не удалось получить обложку через локальный прокси. URL: {coverUrl}. Ошибка: {ex.Message}");

            await WriteResponseAsync(
                stream,
                "404 Not Found",
                "Not Found",
                "text/plain; charset=utf-8",
                cancellationToken);
        }
    }

    private async Task WriteStateAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        string title;
        string artist;
        string requestedBy;
        string coverUrl;
        double scrollSpeed;
        string displayMode;
        bool visible;

        lock (_stateLock)
        {
            title = _title;
            artist = _artist;
            requestedBy = _requestedBy;
            coverUrl = _coverUrl;
            scrollSpeed = _scrollSpeed;
            displayMode = _displayMode;
            visible = _visible;
        }

        var state =
            JsonSerializer.Serialize(
                new
                {
                    title,
                    artist,
                    requestedBy,
                    coverUrl,
                    scrollSpeed,
                    displayMode,
                    visible
                });

        await WriteResponseAsync(
            stream,
            "200 OK",
            state,
            "application/json; charset=utf-8",
            cancellationToken);
    }

    private static async Task<HttpRequest?> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer =
            new byte[MaxRequestSize];

        var bytesRead = 0;

        while (bytesRead < buffer.Length)
        {
            var count =
                await stream.ReadAsync(
                    buffer.AsMemory(
                        bytesRead,
                        buffer.Length - bytesRead),
                    cancellationToken);

            if (count == 0)
                return null;

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
            return null;

        var firstLine =
            requestText[..firstLineEnd];

        var parts =
            firstLine.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            return null;

        return new HttpRequest(
            parts[0],
            parts[1]);
    }

    private static bool ContainsHeaderTerminator(
        byte[] buffer,
        int length)
    {
        for (var i = 3; i < length; i++)
        {
            if (buffer[i - 3] == '\r' &&
                buffer[i - 2] == '\n' &&
                buffer[i - 1] == '\r' &&
                buffer[i] == '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteResponseBytesAsync(
        NetworkStream stream,
        string status,
        byte[] data,
        string contentType,
        CancellationToken cancellationToken)
    {
        var header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {data.Length}\r\n" +
            "Cache-Control: public, max-age=60\r\n" +
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

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        string status,
        string body,
        string contentType,
        CancellationToken cancellationToken)
    {
        var data =
            Encoding.UTF8.GetBytes(body);

        var header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {data.Length}\r\n" +
            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
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

    private sealed record HttpRequest(
        string Method,
        string Path);
}
