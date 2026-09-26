using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stream.MusicRequest.Twitch.Configuration;
using Stream.MusicRequest.Twitch.Models;
using Stream.MusicRequest.Twitch.Storage;

namespace Stream.MusicRequest.Twitch.Services;

public class TwitchAuthService
{
    private const string DeviceAuthorizationUrl =
        "https://id.twitch.tv/oauth2/device";

    private const string TokenUrl =
        "https://id.twitch.tv/oauth2/token";

    private const string RevokeTokenUrl =
        "https://id.twitch.tv/oauth2/revoke";

    private const string ValidateTokenUrl =
        "https://id.twitch.tv/oauth2/validate";

    private const string DeviceGrantType =
        "urn:ietf:params:oauth:grant-type:device_code";

    private static readonly TimeSpan TokenValidationTimeout =
        TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly TwitchSettings _settings;
    private readonly TwitchAuthStorage _storage;

    private readonly SemaphoreSlim _tokenOperationLock =
        new(1, 1);

    public event EventHandler<bool>? AuthenticationStateChanged;

    public TwitchAuthService(
        HttpClient httpClient,
        TwitchSettings settings,
        TwitchAuthStorage storage)
    {
        _httpClient = httpClient;
        _settings = settings;
        _storage = storage;
    }

    public async Task<TwitchDeviceCodeResponse>
        RequestDeviceCodeAsync(
            CancellationToken cancellationToken = default)
    {
        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                DeviceAuthorizationUrl);

        var formData =
            new Dictionary<string, string>
            {
                ["client_id"] = _settings.ClientId,
                ["scopes"] = string.Join(
                    " ",
                    _settings.Scopes)
            };

        request.Content =
            new FormUrlEncodedContent(
                formData);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        var responseContent =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Twitch не смог выдать код авторизации. " +
                $"HTTP {(int)response.StatusCode}: " +
                responseContent);
        }

        var result =
            JsonSerializer.Deserialize<TwitchDeviceCodeResponse>(
                responseContent);

        if (result == null)
        {
            throw new InvalidOperationException(
                "Twitch вернул пустой или некорректный ответ.");
        }

        return result;
    }

    public async Task<TwitchTokenResponse>
        AuthorizeAsync(
            Action<string, string>? onDeviceCode = null,
            CancellationToken cancellationToken = default)
    {
        var deviceCode =
            await RequestDeviceCodeAsync(
                cancellationToken);

        onDeviceCode?.Invoke(
            deviceCode.VerificationUri,
            deviceCode.UserCode);

        var interval =
            Math.Max(
                deviceCode.Interval,
                1);

        var deadline =
            DateTime.UtcNow.AddSeconds(
                deviceCode.ExpiresIn);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(interval),
                cancellationToken);

            var result =
                await TryRequestTokenAsync(
                    deviceCode.DeviceCode,
                    cancellationToken);

            if (result.Token != null)
            {
                await SaveTokenAsync(
                    result.Token);

                AuthenticationStateChanged?.Invoke(
                    this,
                    true);

                return result.Token;
            }

            if (result.Error == "authorization_pending")
            {
                continue;
            }

            if (result.Error == "slow_down")
            {
                interval += 5;
                continue;
            }

            if (result.Error == "expired_token")
            {
                throw new InvalidOperationException(
                    "Код авторизации Twitch истёк.");
            }

            if (result.Error == "access_denied")
            {
                throw new InvalidOperationException(
                    "Авторизация Twitch была отклонена пользователем.");
            }

            throw new InvalidOperationException(
                $"Twitch вернул ошибку авторизации: " +
                $"{result.Error ?? "неизвестная ошибка"}.");
        }

        throw new InvalidOperationException(
            "Время ожидания авторизации Twitch истекло.");
    }

    public async Task<TwitchTokenResponse?> TryRestoreAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot =
            await _storage.LoadAsync();

        if (snapshot == null ||
            string.IsNullOrWhiteSpace(snapshot.AccessToken))
        {
            return null;
        }

        await _tokenOperationLock.WaitAsync(
            cancellationToken);

        try
        {
            var token =
                new TwitchTokenResponse
                {
                    AccessToken = snapshot.AccessToken,
                    RefreshToken = snapshot.RefreshToken,
                    ExpiresIn = snapshot.ExpiresIn,
                    Scope = snapshot.Scope,
                    TokenType = snapshot.TokenType
                };

            try
            {
                await ValidateTokenAsync(
                    token.AccessToken,
                    cancellationToken);

                return token;
            }
            catch (TwitchTokenInvalidException)
            {
                if (string.IsNullOrWhiteSpace(
                        token.RefreshToken))
                {
                    await _storage.DeleteAsync();
                    AuthenticationStateChanged?.Invoke(
                        this,
                        false);
                    return null;
                }
            }

            var refreshedToken =
                await RefreshTokenAsync(
                    token.RefreshToken,
                    cancellationToken);

            await SaveTokenAsync(
                refreshedToken);

            AuthenticationStateChanged?.Invoke(
                this,
                true);

            return refreshedToken;
        }
        catch (TwitchRefreshTokenInvalidException)
        {
            await _storage.DeleteAsync();

            AuthenticationStateChanged?.Invoke(
                this,
                false);

            return null;
        }
        finally
        {
            _tokenOperationLock.Release();
        }
    }

    public async Task<string?> GetValidAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        var token =
            await TryRestoreAsync(
                cancellationToken);

        if (token == null ||
            string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return null;
        }

        return token.AccessToken;
    }

    public async Task<TwitchTokenResponse?> RefreshAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot =
            await _storage.LoadAsync();

        if (snapshot == null ||
            string.IsNullOrWhiteSpace(snapshot.RefreshToken))
        {
            return null;
        }

        await _tokenOperationLock.WaitAsync(
            cancellationToken);

        try
        {
            var refreshedToken =
                await RefreshTokenAsync(
                    snapshot.RefreshToken,
                    cancellationToken);

            await SaveTokenAsync(
                refreshedToken);

            AuthenticationStateChanged?.Invoke(
                this,
                true);

            return refreshedToken;
        }
        catch (TwitchRefreshTokenInvalidException)
        {
            await _storage.DeleteAsync();

            AuthenticationStateChanged?.Invoke(
                this,
                false);

            return null;
        }
        finally
        {
            _tokenOperationLock.Release();
        }
    }

    private async Task ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                ValidateTokenUrl);

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCancellation.CancelAfter(
            TokenValidationTimeout);

        using var response =
            await _httpClient.SendAsync(
                request,
                timeoutCancellation.Token);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if ((int)response.StatusCode == 401)
        {
            throw new TwitchTokenInvalidException();
        }

        var responseContent =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        throw new InvalidOperationException(
            $"Twitch не смог проверить OAuth-токен. " +
            $"HTTP {(int)response.StatusCode}: " +
            responseContent);
    }

    private async Task<TwitchTokenResponse> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new TwitchRefreshTokenInvalidException();
        }

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                TokenUrl);

        request.Content =
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = _settings.ClientId,
                    ["refresh_token"] = refreshToken,
                    ["grant_type"] = "refresh_token"
                });

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        var responseContent =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var token =
                JsonSerializer.Deserialize<TwitchTokenResponse>(
                    responseContent);

            if (token == null ||
                string.IsNullOrWhiteSpace(
                    token.AccessToken))
            {
                throw new InvalidOperationException(
                    "Twitch вернул пустой или некорректный ответ при обновлении токена.");
            }

            return token;
        }

        if ((int)response.StatusCode == 400 ||
            (int)response.StatusCode == 401)
        {
            throw new TwitchRefreshTokenInvalidException();
        }

        throw new InvalidOperationException(
            $"Twitch не смог обновить OAuth-токен. " +
            $"HTTP {(int)response.StatusCode}: " +
            responseContent);
    }

    public async Task SignOutAsync()
    {
        var snapshot =
            await _storage.LoadAsync();

        Exception? revokeException = null;

        if (snapshot != null &&
            !string.IsNullOrWhiteSpace(
                snapshot.AccessToken))
        {
            try
            {
                await RevokeTokenAsync(
                    snapshot.AccessToken);
            }
            catch (Exception ex)
            {
                revokeException = ex;
            }
        }

        // Локальная авторизация должна быть удалена
        // независимо от результата отзыва токена Twitch.
        await _storage.DeleteAsync();

        AuthenticationStateChanged?.Invoke(
            this,
            false);

        // Ошибка отзыва не должна оставлять пользователя
        // авторизованным локально.
        if (revokeException != null)
        {
            return;
        }
    }

    private async Task SaveTokenAsync(
        TwitchTokenResponse token)
    {
        var snapshot =
            new TwitchAuthSnapshot
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                ExpiresIn = token.ExpiresIn,
                Scope = token.Scope,
                TokenType = token.TokenType
            };

        await _storage.SaveAsync(
            snapshot);
    }

    private async Task RevokeTokenAsync(
        string accessToken)
    {
        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                RevokeTokenUrl);

        request.Content =
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = _settings.ClientId,
                    ["token"] = accessToken
                });

        using var response =
            await _httpClient.SendAsync(
                request);

        if (!response.IsSuccessStatusCode)
        {
            var responseContent =
                await response.Content.ReadAsStringAsync();

            throw new InvalidOperationException(
                $"Twitch не смог отозвать токен. " +
                $"HTTP {(int)response.StatusCode}: " +
                responseContent);
        }
    }

    private async Task<TwitchTokenAttemptResult>
        TryRequestTokenAsync(
            string deviceCode,
            CancellationToken cancellationToken)
    {
        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                TokenUrl);

        var formData =
            new Dictionary<string, string>
            {
                ["client_id"] = _settings.ClientId,
                ["scopes"] = string.Join(
                    " ",
                    _settings.Scopes),
                ["device_code"] = deviceCode,
                ["grant_type"] = DeviceGrantType
            };

        request.Content =
            new FormUrlEncodedContent(
                formData);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        var responseContent =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var token =
                JsonSerializer.Deserialize<TwitchTokenResponse>(
                    responseContent);

            if (token == null)
            {
                throw new InvalidOperationException(
                    "Twitch вернул пустой или некорректный ответ токена.");
            }

            return new TwitchTokenAttemptResult(
                token,
                null);
        }

        var errorResponse =
            JsonSerializer.Deserialize<TwitchOAuthErrorResponse>(
                responseContent);

        return new TwitchTokenAttemptResult(
            null,
            errorResponse?.Message ??
            errorResponse?.Error);
    }

    private sealed class TwitchTokenInvalidException : Exception
    {
    }

    private sealed class TwitchRefreshTokenInvalidException : Exception
    {
    }

    private class TwitchTokenAttemptResult
    {
        public TwitchTokenResponse? Token { get; }

        public string? Error { get; }

        public TwitchTokenAttemptResult(
            TwitchTokenResponse? token,
            string? error)
        {
            Token = token;
            Error = error;
        }
    }

    private class TwitchOAuthErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }
    }
}