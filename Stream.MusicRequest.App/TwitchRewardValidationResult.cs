using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.App;

public sealed class TwitchRewardValidationResult
{
    public TwitchCustomReward Reward { get; }

    public bool IsValid { get; }

    public string? WarningMessage { get; }

    public TwitchRewardValidationResult(
        TwitchCustomReward reward,
        bool isValid,
        string? warningMessage = null)
    {
        Reward = reward
            ?? throw new ArgumentNullException(nameof(reward));

        IsValid = isValid;
        WarningMessage = warningMessage;
    }
}
