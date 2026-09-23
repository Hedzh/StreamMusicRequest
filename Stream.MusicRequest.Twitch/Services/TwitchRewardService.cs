using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.Twitch.Services;

public class TwitchRewardService
{
    public const string OrderMusicFeatureKey =
        "order_music";

    public const string SkipTrackFeatureKey =
        "skip_track";

    private readonly TwitchApiClient _apiClient;

    public TwitchRewardService(
        TwitchApiClient apiClient)
    {
        _apiClient =
            apiClient
            ?? throw new ArgumentNullException(
                nameof(apiClient));
    }

    public async Task<TwitchRewardBinding> ValidateRewardAsync(
        string accessToken,
        string broadcasterId,
        TwitchRewardBinding binding,
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

        if (binding == null)
        {
            throw new ArgumentNullException(
                nameof(binding));
        }

        ValidateBinding(
            binding);

        if (string.IsNullOrWhiteSpace(
                binding.RewardId))
        {
            binding.IsEnabled =
                false;

            return binding;
        }

        var reward =
            await _apiClient.GetCustomRewardAsync(
                accessToken,
                broadcasterId,
                binding.RewardId,
                cancellationToken);

        if (reward == null)
        {
            binding.IsEnabled =
                false;

            throw new InvalidOperationException(
                "Twitch-награда с Reward ID " +
                $"\"{binding.RewardId}\" не найдена.");
        }

        binding.IsEnabled =
            reward.IsEnabled;

        return binding;
    }

    public static TwitchRewardBinding CreateDefaultOrderMusicBinding()
    {
        return new TwitchRewardBinding
        {
            FeatureKey =
                OrderMusicFeatureKey,

            Action =
                TwitchRewardAction.OrderMusic,

            CreateAutomatically =
                false,

            Title =
                "Заказ музыки",

            Prompt =
                string.Empty,

            Cost =
                0,

            IsUserInputRequired =
                true,

            IsEnabled =
                false
        };
    }

    public static TwitchRewardBinding CreateDefaultSkipTrackBinding()
    {
        return new TwitchRewardBinding
        {
            FeatureKey =
                SkipTrackFeatureKey,

            Action =
                TwitchRewardAction.SkipTrack,

            CreateAutomatically =
                false,

            Title =
                "Пропустить трек",

            Prompt =
                string.Empty,

            Cost =
                0,

            IsUserInputRequired =
                false,

            IsEnabled =
                false
        };
    }

    public static TwitchRewardAction ResolveAction(
        TwitchRewardBinding binding)
    {
        if (binding == null)
        {
            throw new ArgumentNullException(
                nameof(binding));
        }

        if (binding.Action !=
            TwitchRewardAction.Unknown)
        {
            return binding.Action;
        }

        return binding.FeatureKey
            .Trim()
            .ToLowerInvariant() switch
        {
            OrderMusicFeatureKey =>
                TwitchRewardAction.OrderMusic,

            SkipTrackFeatureKey =>
                TwitchRewardAction.SkipTrack,

            _ =>
                TwitchRewardAction.Unknown
        };
    }

    private static void ValidateBinding(
        TwitchRewardBinding binding)
    {
        var action =
            ResolveAction(binding);

        if (action ==
            TwitchRewardAction.Unknown)
        {
            throw new ArgumentException(
                "Не удалось определить действие Twitch-награды.",
                nameof(binding));
        }

        if (string.IsNullOrWhiteSpace(
                binding.FeatureKey))
        {
            throw new ArgumentException(
                "FeatureKey Twitch-награды не указан.",
                nameof(binding));
        }

        if (binding.CreateAutomatically)
        {
            throw new InvalidOperationException(
                "Автоматическое создание Twitch-наград " +
                "отключено для текущей версии приложения.");
        }

        if (string.IsNullOrWhiteSpace(
                binding.RewardId))
        {
            return;
        }

        if (action ==
                TwitchRewardAction.OrderMusic &&
            !binding.IsUserInputRequired)
        {
            throw new ArgumentException(
                "Для награды «Заказ музыки» необходимо " +
                "включить ввод текста пользователем.",
                nameof(binding));
        }

        if (action ==
                TwitchRewardAction.SkipTrack &&
            binding.IsUserInputRequired)
        {
            throw new ArgumentException(
                "Для награды «Пропустить трек» ввод " +
                "текста пользователем не требуется.",
                nameof(binding));
        }
    }
}