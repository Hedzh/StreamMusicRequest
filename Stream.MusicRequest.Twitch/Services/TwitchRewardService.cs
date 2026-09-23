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

    public async Task<TwitchRewardBinding> EnsureRewardAsync(
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

        if (binding.CreateAutomatically)
        {
            return await CreateRewardAsync(
                accessToken,
                broadcasterId,
                binding,
                cancellationToken);
        }

        return await ValidateExistingRewardAsync(
            accessToken,
            broadcasterId,
            binding,
            cancellationToken);
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
                true,

            Title =
                "Заказ музыки",

            Prompt =
                "Вставьте ссылку на трек Yandex Music.",

            Cost =
                100,

            IsUserInputRequired =
                true,

            IsEnabled =
                true
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
                true,

            Title =
                "Пропустить трек",

            Prompt =
                string.Empty,

            Cost =
                50,

            IsUserInputRequired =
                false,

            IsEnabled =
                true
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

    private async Task<TwitchRewardBinding> CreateRewardAsync(
        string accessToken,
        string broadcasterId,
        TwitchRewardBinding binding,
        CancellationToken cancellationToken)
    {
        var rewardRequest =
            new TwitchCreateCustomRewardRequest
            {
                Title =
                    binding.Title,

                Cost =
                    binding.Cost,

                Prompt =
                    binding.Prompt,

                IsUserInputRequired =
                    binding.IsUserInputRequired
            };

        var reward =
            await _apiClient.CreateCustomRewardAsync(
                accessToken,
                broadcasterId,
                rewardRequest,
                cancellationToken);

        binding.RewardId =
            reward.Id;

        binding.IsEnabled =
            reward.IsEnabled;

        return binding;
    }

    private async Task<TwitchRewardBinding>
        ValidateExistingRewardAsync(
            string accessToken,
            string broadcasterId,
            TwitchRewardBinding binding,
            CancellationToken cancellationToken)
    {
        var reward =
            await _apiClient.GetCustomRewardAsync(
                accessToken,
                broadcasterId,
                binding.RewardId,
                cancellationToken);

        if (reward == null)
        {
            throw new InvalidOperationException(
                $"Twitch-награда с Reward ID " +
                $"\"{binding.RewardId}\" не найдена.");
        }

        binding.IsEnabled =
            reward.IsEnabled;

        return binding;
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
            if (string.IsNullOrWhiteSpace(
                    binding.Title))
            {
                throw new ArgumentException(
                    "Название Twitch-награды не указано.",
                    nameof(binding));
            }

            if (binding.Title.Length > 45)
            {
                throw new ArgumentException(
                    "Название Twitch-награды не может " +
                    "быть длиннее 45 символов.",
                    nameof(binding));
            }

            if (binding.Cost < 1)
            {
                throw new ArgumentException(
                    "Стоимость Twitch-награды должна быть " +
                    "не меньше 1 Channel Point.",
                    nameof(binding));
            }

            if (binding.Prompt.Length > 200)
            {
                throw new ArgumentException(
                    "Описание Twitch-награды не может " +
                    "быть длиннее 200 символов.",
                    nameof(binding));
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

            return;
        }

        if (string.IsNullOrWhiteSpace(
                binding.RewardId))
        {
            throw new ArgumentException(
                "Для существующей Twitch-награды " +
                "необходимо указать Reward ID.",
                nameof(binding));
        }
    }
}