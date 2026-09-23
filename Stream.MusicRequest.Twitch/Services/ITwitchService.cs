using MusicRequestModel =
    Stream.MusicRequest.Core.Requests.MusicRequest;

namespace Stream.MusicRequest.Twitch.Services;

public interface ITwitchService
{
    event EventHandler<MusicRequestModel>? MusicRequestReceived;

    Task ConnectAsync(
        CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}