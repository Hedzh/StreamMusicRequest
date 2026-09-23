using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.Twitch.Services;

public class TwitchApiClient
{
    private const string ApiBaseUrl =
        "https://api.twitch.tv/helix/";

    private readonly HttpClient _httpClient;
    private readonly string _clientId;

    public TwitchApiClient(
        HttpClient httpClient,
        string clientId)
    {
        _httpClient = httpClient;
        _clientId = clientId;
    }

    public async Task<TwitchUser> GetCurrentUserAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                accessToken))
        {
            throw new ArgumentException(
                "Access Token Twitch не указан.",
                nameof(accessToken));
        }

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                $"{ApiBaseUrl}users");

        AddAuthenticationHeaders(
            request,
            accessToken);

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
                $"Twitch API не смог получить данные пользователя. " +
                $"HTTP {(int)response.StatusCode}: " +
                responseContent);
        }

        var result =
            JsonSerializer.Deserialize<TwitchUsersResponse>(
                responseContent);

        if (result == null ||
            result.Data.Length == 0)
        {
            throw new InvalidOperationException(
                "Twitch API не вернул данные авторизованного пользователя.");
        }

        return result.Data[0];
    }

    public async Task<TwitchCustomReward> CreateCustomRewardAsync(
        string accessToken,
        string broadcasterId,
        TwitchCreateCustomRewardRequest rewardRequest,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                accessToken))
        {
            throw new ArgumentException(
                "Access Token Twitch не указан.",
                nameof(accessToken));
        }

        if (string.IsNullOrWhiteSpace(
                broadcasterId))
        {
            throw new ArgumentException(
                "Broadcaster ID Twitch не указан.",
                nameof(broadcasterId));
        }

        if (rewardRequest == null)
        {
            throw new ArgumentNullException(
                nameof(rewardRequest));
        }

        if (string.IsNullOrWhiteSpace(
                rewardRequest.Title))
        {
            throw new ArgumentException(
                "Название Twitch-награды не указано.",
                nameof(rewardRequest));
        }

        if (rewardRequest.Title.Length > 45)
        {
            throw new ArgumentException(
                "Название Twitch-награды не может быть длиннее 45 символов.",
                nameof(rewardRequest));
        }

        if (rewardRequest.Cost < 1)
        {
            throw new ArgumentException(
                "Стоимость Twitch-награды должна быть не меньше 1 Channel Point.",
                nameof(rewardRequest));
        }

        if (rewardRequest.Prompt.Length > 200)
        {
            throw new ArgumentException(
                "Описание Twitch-награды не может быть длиннее 200 символов.",
                nameof(rewardRequest));
        }

        var requestUrl =
            $"{ApiBaseUrl}" +
            $"channel_points/custom_rewards" +
            $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}";

        var json =
            JsonSerializer.Serialize(
                rewardRequest);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                requestUrl);

        AddAuthenticationHeaders(
            request,
            accessToken);

        request.Content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

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
                $"Twitch API не смог создать Custom Reward. " +
                $"HTTP {(int)response.StatusCode}: " +
                responseContent);
        }

        var result =
            JsonSerializer.Deserialize<TwitchCustomRewardResponse>(
                responseContent);

        if (result == null ||
            result.Data.Length == 0)
        {
            throw new InvalidOperationException(
                "Twitch API не вернул созданную Custom Reward.");
        }

        return result.Data[0];
    }

    public async Task<TwitchCustomReward?> GetCustomRewardAsync(
        string accessToken,
        string broadcasterId,
        string rewardId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                accessToken))
        {
            throw new ArgumentException(
                "Access Token Twitch не указан.",
                nameof(accessToken));
        }

        if (string.IsNullOrWhiteSpace(
                broadcasterId))
        {
            throw new ArgumentException(
                "Broadcaster ID Twitch не указан.",
                nameof(broadcasterId));
        }

        if (string.IsNullOrWhiteSpace(
                rewardId))
        {
            throw new ArgumentException(
                "Reward ID Twitch не указан.",
                nameof(rewardId));
        }

        var requestUrl =
            $"{ApiBaseUrl}" +
            $"channel_points/custom_rewards" +
            $"?broadcaster_id={Uri.EscapeDataString(broadcasterId)}" +
            $"&id={Uri.EscapeDataString(rewardId)}";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                requestUrl);

        AddAuthenticationHeaders(
            request,
            accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        var responseContent =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (response.StatusCode ==
            HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Twitch API не смог проверить Custom Reward. " +
                $"HTTP {(int)response.StatusCode}: " +
                responseContent);
        }

        var result =
            JsonSerializer.Deserialize<TwitchCustomRewardResponse>(
                responseContent);

        if (result == null ||
            result.Data.Length == 0)
        {
            return null;
        }

        return result.Data[0];
    }

    public async Task CreateEventSubSubscriptionAsync(
        string accessToken,
        string sessionId,
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                accessToken))
        {
            throw new ArgumentException(
                "Access Token Twitch не указан.",
                nameof(accessToken));
        }

        if (string.IsNullOrWhiteSpace(
                sessionId))
        {
            throw new ArgumentException(
                "Session ID EventSub не указан.",
                nameof(sessionId));
        }

        if (string.IsNullOrWhiteSpace(
                broadcasterId))
        {
            throw new ArgumentException(
                "Broadcaster ID Twitch не указан.",
                nameof(broadcasterId));
        }

        var requestBody =
            new
            {
                type =
                    "channel.channel_points_custom_reward_redemption.add",

                version =
                    "1",

                condition =
                    new
                    {
                        broadcaster_user_id =
                            broadcasterId
                    },

                transport =
                    new
                    {
                        method =
                            "websocket",

                        session_id =
                            sessionId
                    }
            };

        var json =
            JsonSerializer.Serialize(
                requestBody);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                $"{ApiBaseUrl}eventsub/subscriptions");

        AddAuthenticationHeaders(
            request,
            accessToken);

        request.Content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

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
                $"Twitch API не смог создать EventSub-подписку " +
                "на погашение Channel Point-наград. " +
                $"HTTP {(int)response.StatusCode}: " +
                responseContent);
        }
    }

    private void AddAuthenticationHeaders(
        HttpRequestMessage request,
        string accessToken)
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Headers.Add(
            "Client-Id",
            _clientId);
    }
}