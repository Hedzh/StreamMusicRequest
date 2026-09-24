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
    Console.WriteLine(
        "Введите Reward ID для проверки.");

    Console.WriteLine(
        "Например: 123456789");

    Console.WriteLine();
    Console.Write(
        "Reward ID: ");

    var rewardId =
        Console.ReadLine()?.Trim();

    if (string.IsNullOrWhiteSpace(
            rewardId))
    {
        Console.WriteLine();
        Console.WriteLine(
            "Reward ID не указан.");

        return;
    }

    Console.WriteLine();
    Console.WriteLine(
        "Запрашиваем награду через Twitch API...");

    var reward =
        await apiClient.GetCustomRewardAsync(
            token.AccessToken,
            user.Id,
            rewardId);

    Console.WriteLine();

    if (reward == null)
    {
        Console.WriteLine(
            "Награда не найдена.");

        Console.WriteLine(
            "Twitch API вернул 404 для указанного Reward ID.");

        return;
    }

    Console.WriteLine(
        "✓ Награда найдена!");

    Console.WriteLine();
    Console.WriteLine(
        "=== Метаданные Twitch Custom Reward ===");

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

    Console.WriteLine(
        $"Broadcaster ID: {reward.BroadcasterId}");

    Console.WriteLine(
        $"Broadcaster Login: {reward.BroadcasterLogin}");

    Console.WriteLine(
        $"Broadcaster Name: {reward.BroadcasterName}");

    Console.WriteLine();
    Console.WriteLine(
        "=== Проверка завершена успешно ===");

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
