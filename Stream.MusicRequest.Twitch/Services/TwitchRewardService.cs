using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.Twitch.Services;

public class TwitchRewardService
{
    private readonly TwitchApiClient _apiClient;

    public TwitchRewardService(
        TwitchApiClient apiClient)
    {
        _apiClient = apiClient;
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

    private async Task<TwitchRewardBinding> ValidateExistingRewardAsync(
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
                    "Описание Twitch-награды не может быть " +
                    "длиннее 200 символов.",
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