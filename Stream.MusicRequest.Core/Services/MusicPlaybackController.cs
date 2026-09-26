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

    private readonly SemaphoreSlim _operationGate =
        new(1, 1);

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
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
        if (album == null)
        {
            throw new ArgumentNullException(
                nameof(album));
        }

        FileLogger.Info(
            $"Установка нового основного источника: " +
            $"{album.Title}");

        if (_viewerModeActive)
        {
            _mainMusic.SetAlbum(
                album);

            _savedMainState =
                new MainMusicPlaybackState
                {
                    Album = album,
                    TrackIndex = 0,
                    Position = TimeSpan.Zero,
                    WasPlaying = true
                };

            FileLogger.Info(
                "Основной источник изменён во время " +
                "очереди зрителей. Новый источник " +
                "будет запущен после окончания очереди.");

            return;
        }

        if (_mainMusic.IsMainMusicActive)
        {
            await _mainMusic.StopAsync();
        }

        _savedMainState =
            null;

        _mainMusic.SetAlbum(
            album);
    
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<bool> StartMainMusicAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
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
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task PauseMainMusicAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
        await _mainMusic.PauseAsync();
    
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ResumeMainMusicAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
        await _mainMusic.ResumeAsync();
    
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task AddViewerRequestAsync(
        QueueItem item)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
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
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task PauseViewerMusicAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
        await _viewerMusic.PauseAsync();
    
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ResumeViewerMusicAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
        await _viewerMusic.ResumeAsync();
    
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task NextAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
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
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task PreviousAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
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
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ResumeMainAfterViewerQueueAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
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
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
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
        finally
        {
            _operationGate.Release();
        }
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