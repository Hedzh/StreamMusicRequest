using System.Net.Http;
using Stream.MusicRequest.Infrastructure.Storage;
using Stream.MusicRequest.Twitch.Configuration;
using Stream.MusicRequest.Twitch.Services;
using Stream.MusicRequest.Twitch.Storage;

Console.WriteLine(
    "=== Twitch Custom Reward Test ===");

var settings =
    new TwitchSettings();

var storageDirectory =
    Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "StreamMusicRequest",
        "Auth");

var protectedStorage =
    new DpapiProtectedStorage(
        storageDirectory);

var authStorage =
    new TwitchAuthStorage(
        protectedStorage);

using var httpClient =
    new HttpClient();

var authService =
    new TwitchAuthService(
        httpClient,
        settings,
        authStorage);

var apiClient =
    new TwitchApiClient(
        httpClient,
        settings.ClientId);

try
{
    Console.WriteLine();
    Console.WriteLine(
        "Проверяем сохранённую авторизацию Twitch...");

    var token =
        await authService.TryRestoreAsync();

    if (token == null ||
        string.IsNullOrWhiteSpace(
            token.AccessToken))
    {
        Console.WriteLine(
            "Сохранённой авторизации нет.");

        Console.WriteLine();
        Console.WriteLine(
            "Запускаем Twitch Device Flow...");

        token =
            await authService.AuthorizeAsync(
                (verificationUri, userCode) =>
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        $"Откройте: {verificationUri}");

                    Console.WriteLine(
                        $"Код авторизации: {userCode}");

                    Console.WriteLine();
                    Console.WriteLine(
                        "После подтверждения в Twitch " +
                        "программа продолжит работу автоматически.");
                });
    }
    else
    {
        Console.WriteLine(
            "Сохранённая авторизация найдена.");
    }

    Console.WriteLine();
    Console.WriteLine(
        "Получаем текущего Twitch-пользователя...");

    var user =
        await apiClient.GetCurrentUserAsync(
            token.AccessToken);

    Console.WriteLine();
    Console.WriteLine(
        "Twitch-пользователь успешно получен:");

    Console.WriteLine(
        $"Имя: {user.DisplayName}");

    Console.WriteLine(
        $"Login: {user.Login}");

    Console.WriteLine(
        $"Broadcaster ID: {user.Id}");

    Console.WriteLine();


    Console.WriteLine();
    Console.WriteLine(
        "Проверяем фактические данные OAuth-токена Twitch...");

    using var validateRequest =
        new HttpRequestMessage(
            HttpMethod.Get,
            "https://id.twitch.tv/oauth2/validate");

    validateRequest.Headers.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "OAuth",
            token.AccessToken);

    using var validateResponse =
        await httpClient.SendAsync(
            validateRequest);

    var validateContent =
        await validateResponse.Content.ReadAsStringAsync();

    if (!validateResponse.IsSuccessStatusCode)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"OAuth-токен не прошёл проверку. " +
            $"HTTP {(int)validateResponse.StatusCode}: " +
            validateContent);

        return;
    }

    using var validateJson =
        System.Text.Json.JsonDocument.Parse(
            validateContent);

    var validateRoot =
        validateJson.RootElement;

    var validateClientId =
        validateRoot.TryGetProperty(
            "client_id",
            out var clientIdProperty)
            ? clientIdProperty.GetString()
            : null;

    var validateUserId =
        validateRoot.TryGetProperty(
            "user_id",
            out var userIdProperty)
            ? userIdProperty.GetString()
            : null;

    var validateLogin =
        validateRoot.TryGetProperty(
            "login",
            out var loginProperty)
            ? loginProperty.GetString()
            : null;

    var expiresIn =
        validateRoot.TryGetProperty(
            "expires_in",
            out var expiresInProperty)
            ? expiresInProperty.GetInt64()
            : 0;

    Console.WriteLine();
    Console.WriteLine(
        "OAuth-токен действителен.");

    Console.WriteLine(
        $"Client ID токена: {validateClientId ?? "—"}");

    Console.WriteLine(
        $"Client ID приложения: {settings.ClientId}");

    Console.WriteLine(
        $"User ID токена: {validateUserId ?? "—"}");

    Console.WriteLine(
        $"Login токена: {validateLogin ?? "—"}");

    Console.WriteLine(
        $"Срок действия: {expiresIn} сек.");

    Console.WriteLine();
    Console.WriteLine(
        "Фактические scopes токена:");

    var hasReadRedemptionsScope = false;
    var hasManageRedemptionsScope = false;

    if (validateRoot.TryGetProperty(
            "scopes",
            out var scopesProperty) &&
        scopesProperty.ValueKind ==
            System.Text.Json.JsonValueKind.Array)
    {
        foreach (var scope in scopesProperty.EnumerateArray())
        {
            var scopeName =
                scope.GetString();

            if (string.IsNullOrWhiteSpace(
                    scopeName))
            {
                continue;
            }

            Console.WriteLine(
                $"  - {scopeName}");

            if (scopeName ==
                "channel:read:redemptions")
            {
                hasReadRedemptionsScope = true;
            }

            if (scopeName ==
                "channel:manage:redemptions")
            {
                hasManageRedemptionsScope = true;
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        $"channel:read:redemptions: " +
        $"{(hasReadRedemptionsScope ? "ЕСТЬ" : "НЕТ")}");

    Console.WriteLine(
        $"channel:manage:redemptions: " +
        $"{(hasManageRedemptionsScope ? "ЕСТЬ" : "НЕТ")}");

    Console.WriteLine();

    if (!string.Equals(
            validateClientId,
            settings.ClientId,
            StringComparison.Ordinal))
    {
        Console.WriteLine(
            "ВНИМАНИЕ: Client ID токена не совпадает " +
            "с Client ID приложения.");
    }

    if (!string.Equals(
            validateUserId,
            user.Id,
            StringComparison.Ordinal))
    {
        Console.WriteLine(
            "ВНИМАНИЕ: User ID токена не совпадает " +
            "с Broadcaster ID.");
    }

    if (!hasReadRedemptionsScope &&
        !hasManageRedemptionsScope)
    {
        Console.WriteLine(
            "ВНИМАНИЕ: у токена нет ни одного scope, " +
            "необходимого для работы с Custom Rewards.");
    }

    Console.WriteLine();
    Console.WriteLine(
        "=== Twitch OAuth Refresh Test ===");

    Console.WriteLine();
    Console.WriteLine(
        "Принудительно обновляем Access Token через Refresh Token...");

    var refreshedToken =
        await authService.RefreshAccessTokenAsync();

    if (refreshedToken == null ||
        string.IsNullOrWhiteSpace(
            refreshedToken.AccessToken))
    {
        Console.WriteLine(
            "Refresh Token недействителен или отсутствует.");

        return;
    }

    Console.WriteLine(
        "Access Token успешно обновлён.");

    var refreshedUser =
        await apiClient.GetCurrentUserAsync(
            refreshedToken.AccessToken);

    Console.WriteLine(
        $"Пользователь после refresh: {refreshedUser.DisplayName} " +
        $"({refreshedUser.Login})");

    Console.WriteLine(
        $"Broadcaster ID: {refreshedUser.Id}");

    Console.WriteLine();
    Console.WriteLine(
        "Проверяем обновлённый OAuth-токен...");

    using var refreshedValidateRequest =
        new HttpRequestMessage(
            HttpMethod.Get,
            "https://id.twitch.tv/oauth2/validate");

    refreshedValidateRequest.Headers.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "OAuth",
            refreshedToken.AccessToken);

    using var refreshedValidateResponse =
        await httpClient.SendAsync(
            refreshedValidateRequest);

    var refreshedValidateContent =
        await refreshedValidateResponse.Content.ReadAsStringAsync();

    if (!refreshedValidateResponse.IsSuccessStatusCode)
    {
        Console.WriteLine(
            $"Новый Access Token не прошёл проверку. " +
            $"HTTP {(int)refreshedValidateResponse.StatusCode}: " +
            refreshedValidateContent);

        return;
    }

    using var refreshedJson =
        System.Text.Json.JsonDocument.Parse(
            refreshedValidateContent);

    var refreshedRoot =
        refreshedJson.RootElement;

    var refreshedClientId =
        refreshedRoot.TryGetProperty(
            "client_id",
            out var refreshedClientIdProperty)
            ? refreshedClientIdProperty.GetString()
            : null;

    var refreshedUserId =
        refreshedRoot.TryGetProperty(
            "user_id",
            out var refreshedUserIdProperty)
            ? refreshedUserIdProperty.GetString()
            : null;

    var refreshedExpiresIn =
        refreshedRoot.TryGetProperty(
            "expires_in",
            out var refreshedExpiresProperty)
            ? refreshedExpiresProperty.GetInt64()
            : 0;

    Console.WriteLine(
        "Новый Access Token действителен.");

    Console.WriteLine(
        $"Client ID: {refreshedClientId ?? "—"}");

    Console.WriteLine(
        $"User ID: {refreshedUserId ?? "—"}");

    Console.WriteLine(
        $"Срок действия: {refreshedExpiresIn} сек.");

    Console.WriteLine(
        $"Scopes: {string.Join(", ",
            refreshedRoot.TryGetProperty(
                "scopes",
                out var refreshedScopes) &&
            refreshedScopes.ValueKind ==
                System.Text.Json.JsonValueKind.Array
                ? refreshedScopes.EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                : Array.Empty<string>())}");

    if (!string.Equals(
            refreshedClientId,
            settings.ClientId,
            StringComparison.Ordinal) ||
        !string.Equals(
            refreshedUserId,
            refreshedUser.Id,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "После refresh Twitch вернул токен с неожиданным Client ID или User ID.");
    }

    Console.WriteLine();
    Console.WriteLine(
        "=== Twitch OAuth Refresh Test завершён успешно ===");

    Console.WriteLine();
    Console.WriteLine(
        "=== Диагностика Get Custom Rewards ===");

    var rewardsUrl =
        "https://api.twitch.tv/helix/channel_points/custom_rewards" +
        $"?broadcaster_id={Uri.EscapeDataString(user.Id)}";

    Console.WriteLine();
    Console.WriteLine(
        "1. Запрос всех Custom Rewards:");

    using var rewardsRequest =
        new HttpRequestMessage(
            HttpMethod.Get,
            rewardsUrl);

    rewardsRequest.Headers.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            token.AccessToken);

    rewardsRequest.Headers.Add(
        "Client-Id",
        settings.ClientId);

    using var rewardsResponse =
        await httpClient.SendAsync(
            rewardsRequest);

    var rewardsContent =
        await rewardsResponse.Content.ReadAsStringAsync();

    Console.WriteLine(
        $"HTTP статус: {(int)rewardsResponse.StatusCode} " +
        $"({rewardsResponse.StatusCode})");

    Console.WriteLine(
        "Raw JSON:");

    Console.WriteLine(
        rewardsContent);

    Console.WriteLine();
    Console.WriteLine(
        "2. Запрос Custom Rewards с only_manageable_rewards=true:");

    var manageableRewardsUrl =
        rewardsUrl +
        "&only_manageable_rewards=true";

    using var manageableRewardsRequest =
        new HttpRequestMessage(
            HttpMethod.Get,
            manageableRewardsUrl);

    manageableRewardsRequest.Headers.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            token.AccessToken);

    manageableRewardsRequest.Headers.Add(
        "Client-Id",
        settings.ClientId);

    using var manageableRewardsResponse =
        await httpClient.SendAsync(
            manageableRewardsRequest);

    var manageableRewardsContent =
        await manageableRewardsResponse.Content.ReadAsStringAsync();

    Console.WriteLine(
        $"HTTP статус: {(int)manageableRewardsResponse.StatusCode} " +
        $"({manageableRewardsResponse.StatusCode})");

    Console.WriteLine(
        "Raw JSON:");

    Console.WriteLine(
        manageableRewardsContent);

    Console.WriteLine();
    Console.WriteLine(
        "3. Проверяем GetCustomRewardsAsync через TwitchApiClient:");

    var rewards =
        await apiClient.GetCustomRewardsAsync(
            token.AccessToken,
            user.Id);

    Console.WriteLine(
        $"Результат TwitchApiClient: {rewards.Count} наград.");

    if (rewards.Count > 0)
    {
        Console.WriteLine();

        for (var i = 0; i < rewards.Count; i++)
        {
            var reward = rewards[i];

            Console.WriteLine(
                $"--- Награда #{i + 1} ---");

            Console.WriteLine(
                $"Reward ID: {reward.Id}");

            Console.WriteLine(
                $"Название: {reward.Title}");

            Console.WriteLine(
                $"Стоимость: {reward.Cost} Channel Points");

            Console.WriteLine(
                $"Описание / Prompt: " +
                $"{(string.IsNullOrWhiteSpace(reward.Prompt) ? "—" : reward.Prompt)}");

            Console.WriteLine(
                $"Требуется ввод пользователя: " +
                $"{(reward.IsUserInputRequired ? "Да" : "Нет")}");

            Console.WriteLine(
                $"Награда включена: " +
                $"{(reward.IsEnabled ? "Да" : "Нет")}");

            Console.WriteLine();
        }
    }

    Console.WriteLine(
        "=== Диагностика Get Custom Rewards завершена ===");

    Console.WriteLine();
    Console.WriteLine(
        "Важно: тест НЕ изменяет и НЕ удаляет Twitch-награду " +
        "и НЕ отзывает сохранённую авторизацию.");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine(
        "=== ОШИБКА ===");

    Console.WriteLine(
        ex.Message);

    Console.WriteLine();
    Console.WriteLine(
        "Подробности:");

    Console.WriteLine(
        ex);
    
    Environment.ExitCode = 1;
}
