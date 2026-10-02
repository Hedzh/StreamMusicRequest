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
    private QueueItem? _priorityResumeViewerItem;
    private MainMusicPlaybackState? _priorityResumeMainState;

    private bool _viewerModeActive;
    private bool _pendingNormalDonation;

    private readonly SemaphoreSlim _operationGate =
        new(1, 1);

    public MusicPlaybackController(
        MainMusicService mainMusic,
        MusicPlaybackService viewerMusic,
        MusicQueueService queue)
    {
        _mainMusic = mainMusic;
        _viewerMusic = viewerMusic;
        _queue = queue;

        _viewerMusic.QueuePlaybackFinished +=
            ViewerMusic_QueuePlaybackFinished;

        _mainMusic.PlaybackFinished +=
            MainMusic_PlaybackFinished;

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

    public async Task SetMainMusicAsync(MusicAlbum album)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (album == null)
                throw new ArgumentNullException(nameof(album));

            FileLogger.Info(
                $"Установка нового основного источника: {album.Title}");

            if (_viewerModeActive)
            {
                _mainMusic.SetAlbum(album);

                _savedMainState = new MainMusicPlaybackState
                {
                    Album = album,
                    TrackIndex = 0,
                    Position = TimeSpan.Zero,
                    WasPlaying = true
                };

                FileLogger.Info(
                    "Основной источник изменён во время очереди зрителей. " +
                    "Новый источник будет запущен после окончания очереди.");

                return;
            }

            if (_mainMusic.IsMainMusicActive)
                await _mainMusic.StopAsync();

            _savedMainState = null;
            _pendingNormalDonation = false;
            _mainMusic.AutoAdvanceEnabled = true;
            _mainMusic.SetAlbum(album);
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
                return false;

            _mainMusic.AutoAdvanceEnabled = true;
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

    public async Task AddViewerRequestAsync(QueueItem item)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            ValidateQueueItem(item);

            if (!_viewerModeActive)
            {
                _savedMainState =
                    _mainMusic.CaptureState();

                if (_mainMusic.IsMainMusicActive)
                    await _mainMusic.StopAsync();

                _mainMusic.AutoAdvanceEnabled = true;
                _viewerModeActive = true;
            }

            _viewerMusic.AutoAdvanceEnabled = true;

            if (!_viewerMusic.IsPlaying)
                await _viewerMusic.StartNextAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task AddDonationRequestAsync(QueueItem item)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            ValidateQueueItem(item);

            item.QueuePriority = 10;
            _queue.PromoteByPriority(item.Id);

            if (_viewerModeActive)
            {
                _viewerMusic.AutoAdvanceEnabled = true;

                if (!_viewerMusic.IsPlaying)
                    await _viewerMusic.StartNextAsync();

                FileLogger.Info(
                    $"Donation NORMAL: элемент #{item.Id} поставлен следующим в очереди зрителей.");

                return;
            }

            if (_mainMusic.IsMainMusicActive)
            {
                _pendingNormalDonation = true;
                _mainMusic.AutoAdvanceEnabled = false;

                FileLogger.Info(
                    $"Donation NORMAL: #{item.Id} будет воспроизведён после текущего основного трека.");

                return;
            }

            _viewerModeActive = true;
            _viewerMusic.AutoAdvanceEnabled = true;

            await _viewerMusic.StartNextAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task PlaySuperPriorityDonationAsync(QueueItem item)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            ValidateQueueItem(item);

            if (_priorityResumeMainState != null ||
                _priorityResumeViewerItem != null)
            {
                item.QueuePriority = 100;
                _queue.PromoteByPriority(item.Id);

                FileLogger.Info(
                    $"Donation SUPER-PRIORITY: #{item.Id} добавлен в очередь " +
                    "SUPER-PRIORITY и будет воспроизведён после текущего.");

                return;
            }

            if (_viewerModeActive)
            {
                var currentViewerItem =
                    _viewerMusic.CurrentItem;

                if (currentViewerItem != null)
                {
                    currentViewerItem.ResumePosition =
                        _viewerMusic.GetPosition();

                    currentViewerItem.ResumePlayback =
                        _viewerMusic.IsPlaying;

                    _priorityResumeViewerItem =
                        currentViewerItem;
                }

                await _viewerMusic.StopAsync();

                if (_priorityResumeViewerItem != null)
                {
                    _queue.EnqueueFront(
                        _priorityResumeViewerItem);
                }

                _queue.PromoteToFront(item.Id);

                _viewerMusic.AutoAdvanceEnabled = false;

                if (!_viewerMusic.IsPlaying)
                    await _viewerMusic.StartNextAsync();

                FileLogger.Info(
                    $"Donation SUPER-PRIORITY: #{item.Id} запущен поверх очереди зрителей.");

                return;
            }

            if (_mainMusic.IsMainMusicActive)
            {
                _priorityResumeMainState =
                    _mainMusic.CaptureState();

                await _mainMusic.StopAsync();

                _mainMusic.AutoAdvanceEnabled = true;
            }

            _viewerModeActive = true;

            item.QueuePriority = 100;
            _queue.PromoteByPriority(item.Id);

            _viewerMusic.AutoAdvanceEnabled = false;

            await _viewerMusic.StartNextAsync();

            FileLogger.Info(
                $"Donation SUPER-PRIORITY: #{item.Id} запущен немедленно.");
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
                await _viewerMusic.SkipAsync();
                return;
            }

            if (!HasMainMusic)
                return;

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
            if (_viewerModeActive || !HasMainMusic)
                return;

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
            if (!_viewerModeActive ||
                _queue.Count > 0)
                return;

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
            await _viewerMusic.StopAsync();
            await _mainMusic.StopAsync();

            _viewerModeActive = false;
            _pendingNormalDonation = false;
            _savedMainState = null;
            _priorityResumeMainState = null;
            _priorityResumeViewerItem = null;

            _viewerMusic.AutoAdvanceEnabled = true;
            _mainMusic.AutoAdvanceEnabled = true;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async void MainMusic_PlaybackFinished(
        object? sender,
        EventArgs e)
    {
        try
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);

            try
            {
                if (!_pendingNormalDonation ||
                    _viewerModeActive ||
                    _queue.Count == 0)
                    return;

                _pendingNormalDonation = false;
                _mainMusic.AutoAdvanceEnabled = true;
                _viewerModeActive = true;
                _viewerMusic.AutoAdvanceEnabled = true;

                FileLogger.Info(
                    "Основной трек завершён. Запускаем ожидающий NORMAL donation order.");

                await _viewerMusic.StartNextAsync();
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error(
                "Ошибка запуска NORMAL donation после основного трека.",
                ex);
        }
    }

    private async void ViewerMusic_QueuePlaybackFinished(
        object? sender,
        EventArgs e)
    {
        try
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);

            try
            {
                if (_queue.Peek()?.QueuePriority >= 100)
                {
                    _viewerModeActive = true;
                    _viewerMusic.AutoAdvanceEnabled = false;

                    var startedNextPriority =
                        await _viewerMusic.StartNextAsync();

                    FileLogger.Info(
                        $"SUPER-PRIORITY завершён. Следующий SUPER-PRIORITY " +
                        $"заказ запущен: {startedNextPriority}.");

                    return;
                }

                if (_priorityResumeMainState != null)
                {
                    var state = _priorityResumeMainState;
                    _priorityResumeMainState = null;

                    _viewerModeActive = false;
                    _viewerMusic.AutoAdvanceEnabled = true;
                    _mainMusic.AutoAdvanceEnabled = true;

                    await _mainMusic.RestoreStateAsync(state);

                    FileLogger.Info(
                        "SUPER-PRIORITY завершён. Основная музыка восстановлена.");
                    return;
                }

                if (_priorityResumeViewerItem != null)
                {
                    var item = _priorityResumeViewerItem;
                    _priorityResumeViewerItem = null;

                    _queue.EnqueueFront(item);

                    _viewerMusic.AutoAdvanceEnabled = true;
                    _viewerModeActive = true;

                    await _viewerMusic.StartNextAsync();

                    FileLogger.Info(
                        "SUPER-PRIORITY завершён. Прерванный зрительский трек восстановлен.");
                    return;
                }

                if (!_viewerModeActive ||
                    _queue.Count > 0)
                    return;

                await RestoreMainMusicAsync();
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error(
                "Ошибка обработки окончания очереди зрителей.",
                ex);
        }
    }

    private async Task RestoreMainMusicAsync()
    {
        if (!_viewerModeActive)
            return;

        _viewerModeActive = false;

        var state = _savedMainState;
        _savedMainState = null;

        if (state == null)
            return;

        _mainMusic.AutoAdvanceEnabled = true;

        var restored =
            await _mainMusic.RestoreStateAsync(state);

        FileLogger.Info(
            $"Результат восстановления основной музыки: {restored}");
    }

    private static void ValidateQueueItem(QueueItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (item.Track == null)
            throw new InvalidOperationException(
                "Музыкальная заявка не содержит трек.");
    }
}
