using Stream.MusicRequest.Core.Diagnostics;
using Stream.MusicRequest.Core.Models;
using Stream.MusicRequest.Core.Queue;

namespace Stream.MusicRequest.Core.Services;

public class MusicPlaybackController
{
    private readonly MainMusicService _mainMusic;
    private readonly MusicPlaybackService _viewerMusic;
    private readonly MusicQueueService _queue;

    private MainMusicPlaybackState? _savedMainState;

    private bool _viewerModeActive;

    public MusicPlaybackController(
        MainMusicService mainMusic,
        MusicPlaybackService viewerMusic,
        MusicQueueService queue)
    {
        _mainMusic =
            mainMusic;

        _viewerMusic =
            viewerMusic;

        _queue =
            queue;

        _viewerMusic.QueuePlaybackFinished +=
            ViewerMusic_QueuePlaybackFinished;

        FileLogger.Info(
            "MusicPlaybackController создан.");
    }

    public bool IsViewerModeActive =>
        _viewerModeActive;

    public bool HasMainMusic =>
        _mainMusic.CurrentAlbum != null;

    public MusicAlbum? CurrentMainMusic =>
        _mainMusic.CurrentAlbum;

    public Track? CurrentMainTrack =>
        _mainMusic.CurrentTrack;

    public async Task SetMainMusicAsync(
        MusicAlbum album)
    {
        if (album == null)
        {
            throw new ArgumentNullException(
                nameof(album));
        }

        FileLogger.Info(
            $"Установка нового основного источника: " +
            $"{album.Title}");

        if (_mainMusic.IsMainMusicActive)
        {
            await _mainMusic.StopAsync();
        }

        _savedMainState =
            null;

        _mainMusic.SetAlbum(
            album);
    }

    public async Task<bool> StartMainMusicAsync()
    {
        if (_viewerModeActive)
        {
            FileLogger.Info(
                "StartMainMusicAsync: " +
                "активен режим зрительской очереди.");

            return false;
        }

        return await _mainMusic.PlayAsync();
    }

    public async Task PauseMainMusicAsync()
    {
        await _mainMusic.PauseAsync();
    }

    public async Task ResumeMainMusicAsync()
    {
        await _mainMusic.ResumeAsync();
    }

    public async Task AddViewerRequestAsync(
        QueueItem item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(
                nameof(item));
        }

        if (item.Track == null)
        {
            throw new InvalidOperationException(
                "Заявка зрителя не содержит трек.");
        }

        if (!_viewerModeActive)
        {
            FileLogger.Info(
                "Первая заявка зрителя. " +
                "Переключение в режим очереди.");

            _savedMainState =
                _mainMusic.CaptureState();

            if (_mainMusic.IsMainMusicActive)
            {
                await _mainMusic.StopAsync();
            }

            _viewerModeActive =
                true;
        }

        /*
         * MusicRequestService уже добавил
         * заявку в очередь.
         *
         * Здесь только запускаем очередь,
         * если сейчас ничего не играет.
         */

        if (!_viewerMusic.IsPlaying)
        {
            await _viewerMusic.StartNextAsync();
        }
    }

    public async Task PauseViewerMusicAsync()
    {
        await _viewerMusic.PauseAsync();
    }

    public async Task ResumeViewerMusicAsync()
    {
        await _viewerMusic.ResumeAsync();
    }

    public async Task NextAsync()
    {
        if (_viewerModeActive)
        {
            FileLogger.Info(
                "NextAsync: активна очередь зрителей. " +
                "Пропускаем текущий трек.");

            await _viewerMusic.SkipAsync();

            return;
        }

        if (!HasMainMusic)
        {
            FileLogger.Info(
                "NextAsync: основной источник " +
                "не установлен.");

            return;
        }

        await _mainMusic.NextAsync();
    }

    public async Task PreviousAsync()
    {
        if (_viewerModeActive)
        {
            FileLogger.Info(
                "PreviousAsync: активна очередь зрителей. " +
                "Предыдущий трек для очереди " +
                "не поддерживается.");

            return;
        }

        if (!HasMainMusic)
        {
            FileLogger.Info(
                "PreviousAsync: основной источник " +
                "не установлен.");

            return;
        }

        await _mainMusic.PreviousAsync();
    }

    public async Task ResumeMainAfterViewerQueueAsync()
    {
        if (!_viewerModeActive)
        {
            return;
        }

        if (_queue.Count > 0)
        {
            return;
        }

        await RestoreMainMusicAsync();
    }

    public async Task StopAsync()
    {
        FileLogger.Info(
            "MusicPlaybackController.StopAsync.");

        await _viewerMusic.StopAsync();

        await _mainMusic.StopAsync();

        _viewerModeActive =
            false;

        _savedMainState =
            null;
    }

    private async void ViewerMusic_QueuePlaybackFinished(
        object? sender,
        EventArgs e)
    {
        if (!_viewerModeActive)
        {
            return;
        }

        try
        {
            await ResumeMainAfterViewerQueueAsync();
        }
        catch (Exception ex)
        {
            FileLogger.Error(
                "Ошибка восстановления основной музыки " +
                "после окончания очереди.",
                ex);
        }
    }

    private async Task RestoreMainMusicAsync()
    {
        if (!_viewerModeActive)
        {
            return;
        }

        FileLogger.Info(
            "Очередь зрителей закончилась. " +
            "Возвращаем основную музыку.");

        _viewerModeActive =
            false;

        var state =
            _savedMainState;

        _savedMainState =
            null;

        if (state == null)
        {
            FileLogger.Info(
                "Сохранённого состояния основной музыки нет.");

            return;
        }

        var restored =
            await _mainMusic.RestoreStateAsync(
                state);

        FileLogger.Info(
            $"Результат восстановления основной музыки: " +
            $"{restored}");
    }
}