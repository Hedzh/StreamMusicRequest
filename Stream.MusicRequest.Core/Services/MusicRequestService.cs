using Stream.MusicRequest.Core.Queue;
using Stream.MusicRequest.Core.Results;
using Stream.MusicRequest.Core.Sources;

namespace Stream.MusicRequest.Core.Services;

public class MusicRequestService
{
    private readonly MusicQueueService _queue;
    private readonly IMusicSourceResolver _sourceResolver;

    public MusicRequestService(
        MusicQueueService queue,
        IMusicSourceResolver sourceResolver)
    {
        _queue = queue;
        _sourceResolver = sourceResolver;
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

        var source =
            _sourceResolver.Resolve(
                request.SourceUrl);

        if (source == null)
        {
            return MusicRequestResult.Failed(
                "Источник музыки не поддерживается.");
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

        var track =
            await source.ResolveTrackAsync(
                request.SourceUrl);

        if (track == null)
        {
            return MusicRequestResult.Failed(
                "Не удалось найти музыкальный трек.");
        }

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