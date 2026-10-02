using System.Net.Http;

namespace Stream.MusicRequest.Infrastructure.Services;

public sealed class InternetConnectionService : IDisposable
{
    private const string ConnectivityCheckUrl =
        "https://www.msftconnecttest.com/connecttest.txt";

    private readonly HttpClient _httpClient;
    private bool? _isConnected;

    public InternetConnectionService()
    {
        _httpClient =
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StreamMusicRequest/1.2");
    }

    public bool? IsConnected =>
        _isConnected;

    public event EventHandler<bool?>? ConnectionStateChanged;

    public async Task<bool> CheckConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        bool isConnected;

        try
        {
            using var response =
                await _httpClient.GetAsync(
                    ConnectivityCheckUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            isConnected =
                response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            isConnected = false;
        }
        catch (HttpRequestException)
        {
            isConnected = false;
        }
        catch (TaskCanceledException)
        {
            isConnected = false;
        }

        SetConnectionState(isConnected);

        return isConnected;
    }

    private void SetConnectionState(bool isConnected)
    {
        if (_isConnected == isConnected)
        {
            return;
        }

        _isConnected = isConnected;

        ConnectionStateChanged?.Invoke(
            this,
            _isConnected);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
