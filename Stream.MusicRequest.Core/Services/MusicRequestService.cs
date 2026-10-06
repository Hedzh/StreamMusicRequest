using Stream.MusicRequest.Core.Queue;
using Stream.MusicRequest.Core.Results;
using Stream.MusicRequest.Core.Sources;

namespace Stream.MusicRequest.Core.Services;

public class MusicRequestService
{
    private readonly MusicQueueService _queue;
    private readonly IMusicSourceResolver _sourceResolver;
    private readonly IBanService _banService;

    public MusicRequestService(
        MusicQueueService queue,
        IMusicSourceResolver sourceResolver,
        IBanService banService)
    {
        _queue = queue;
        _sourceResolver = sourceResolver;
        _banService = banService
            ?? throw new ArgumentNullException(nameof(banService));
    }

    public async Task<MusicRequestResult> AddRequestAsync(
        Requests.MusicRequest request)
    {
        if (string.IsNullOrWhiteSpace(
                request.SourceUrl))
        {
            return MusicRequestResult.Failed(
                "Ссылка на музыкальный трек не указана.");
        }

        if (_banService.IsUserBanned(request.ViewerName))
        {
            return MusicRequestResult.Failed(
                "Пользователь заблокирован и не может заказывать музыку.");
        }

        var source =
            _sourceResolver.Resolve(
                request.SourceUrl);

        if (source == null)
        {
            return MusicRequestResult.Failed(
                "Ссылка не распознана. Поддерживаются ссылки на треки Yandex Music.");
        }

        /*
         * Зрительские заявки могут содержать
         * только отдельные треки.
         *
         * Альбомы и плейлисты должны добавляться
         * только как основная музыка стримера.
         */

        if (source is IAlbumMusicSource albumSource &&
            albumSource.CanHandleAlbumUrl(
                request.SourceUrl))
        {
            return MusicRequestResult.Failed(
                "Зрители не могут заказывать альбомы.");
        }

        if (source is IPlaylistMusicSource playlistSource &&
            playlistSource.CanHandlePlaylistUrl(
                request.SourceUrl))
        {
            return MusicRequestResult.Failed(
                "Зрители не могут заказывать плейлисты.");
        }

        if (source is IPlayableMusicSource playableSource &&
            !playableSource.CanHandleTrackUrl(
                request.SourceUrl))
        {
            return MusicRequestResult.Failed(
                "Ссылка не является ссылкой на отдельный трек Yandex Music.");
        }

        var track =
            await source.ResolveTrackAsync(
                request.SourceUrl);

        if (track == null)
        {
            return MusicRequestResult.Failed(
                "Трек не найден в Yandex Music или недоступен.");
        }

        if (_banService.IsTrackBanned(
                source.Name,
                track.Id))
        {
            return MusicRequestResult.Failed(
                "Этот трек заблокирован и не может быть добавлен в очередь.");
        }

        track.RequestedBy =
            request.ViewerName;

        track.RequestSource =
            "Twitch";

        track.RequestMode =
            "Обычный";

        var item =
            new QueueItem
            {
                Request = request,
                Track = track,
                Source = source
            };

        _queue.Enqueue(item);

        return MusicRequestResult.Succeeded(
            item);
    }
}