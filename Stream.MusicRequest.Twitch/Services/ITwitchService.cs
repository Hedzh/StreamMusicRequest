using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.Twitch.Services;

public interface ITwitchService
{
    event EventHandler<TwitchRewardActionReceivedEventArgs>?
        RewardActionReceived;

    Task ConnectAsync(
        CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}