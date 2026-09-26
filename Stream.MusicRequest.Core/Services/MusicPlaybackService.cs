using Stream.MusicRequest.Core.Diagnostics;
using Stream.MusicRequest.Core.Models;
using Stream.MusicRequest.Core.Player;
using Stream.MusicRequest.Core.Queue;
using Stream.MusicRequest.Core.Sources;

namespace Stream.MusicRequest.Core.Services;

public class MusicPlaybackService
{
    private readonly MusicQueueService _queue;
    private readonly IMusicPlayer _player;

    private bool _isPlaying;

    private readonly SemaphoreSlim _operationGate =
        new(1, 1);

    public event EventHandler? QueuePlaybackFinished;

    public MusicPlaybackService(
        MusicQueueService queue,
        IMusicPlayer player)
    {
        _queue = queue;
        _player = player;

        _player.PlaybackFinished +=
            Player_PlaybackFinished;

        FileLogger.Info(
            "MusicPlaybackService создан.");
    }

    public async Task<bool> StartNextAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            return await StartNextCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<bool> StartNextCoreAsync()
    {
        FileLogger.Info(
            $"StartNextAsync вызван. " +
            $"IsPlaying: {_isPlaying}, " +
            $"QueueCount: {_queue.Count}");

        if (_isPlaying)
        {
            FileLogger.Info(
                "StartNextAsync остановлен: " +
                "воспроизведение уже идёт.");

            return false;
        }

        var item =
            _queue.Dequeue();

        if (item == null)
        {
            FileLogger.Info(
                "StartNextAsync: очередь пуста.");

            RaiseQueuePlaybackFinished();

            return false;
        }

        FileLogger.Info(
            $"Из очереди взят элемент. " +
            $"Track: {item.Track?.Artist} — " +
            $"{item.Track?.Title}");

        if (item.Track == null)
        {
            FileLogger.Info(
                "У элемента нет Track. " +
                "Переход к следующему.");

            return await StartNextCoreAsync();
        }

        if (item.Source is not IPlayableMusicSource playableSource)
        {
            FileLogger.Info(
                "Источник не поддерживает воспроизведение. " +
                "Переход к следующему.");

            return await StartNextCoreAsync();
        }

        FileLogger.Info(
            $"Получение прямой ссылки. " +
            $"Источник: {playableSource.Name}");

        var playbackUrl =
            await playableSource.GetPlaybackUrlAsync(
                item.Track);

        if (string.IsNullOrWhiteSpace(
                playbackUrl))
        {
            FileLogger.Info(
                "Прямая ссылка не получена. " +
                "Переход к следующему.");

            return await StartNextCoreAsync();
        }

        FileLogger.Info(
            "Прямая ссылка успешно получена.");

        item.Track.SourceUrl =
            playbackUrl;

        _isPlaying =
            true;

        try
        {
            FileLogger.Info(
                $"Запуск плеера: " +
                $"{item.Track.Artist} — " +
                $"{item.Track.Title}");

            await _player.PlayAsync(
                item.Track);

            FileLogger.Info(
                "PlayAsync плеера завершён успешно.");

            return true;
        }
        catch (Exception ex)
        {
            _isPlaying =
                false;

            FileLogger.Error(
                "Ошибка запуска плеера.",
                ex);

            throw;
        }
    
    }

    public async Task PauseAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
        if (!_isPlaying)
        {
            FileLogger.Info(
                "PauseAsync: воспроизведение уже остановлено.");

            return;
        }

        FileLogger.Info(
            "MusicPlaybackService.PauseAsync вызван.");

        await _player.PauseAsync();

        FileLogger.Info(
            "Воспроизведение поставлено на паузу.");
    
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ResumeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
        if (!_isPlaying)
        {
            FileLogger.Info(
                "ResumeAsync: активного трека нет.");

            return;
        }

        FileLogger.Info(
            "MusicPlaybackService.ResumeAsync вызван.");

        await _player.ResumeAsync();

        FileLogger.Info(
            "Воспроизведение продолжено.");
    
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
            "MusicPlaybackService.StopAsync вызван.");

        await _player.StopAsync();

        _isPlaying =
            false;

        FileLogger.Info(
            "Воспроизведение остановлено.");
    
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SkipAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
        FileLogger.Info(
            "MusicPlaybackService.SkipAsync вызван.");

        await _player.SkipAsync();

        _isPlaying =
            false;

        FileLogger.Info(
            "Текущий трек пропущен. " +
            "Попытка запуска следующего.");

        await StartNextCoreAsync();
    
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SeekAsync(
        TimeSpan position)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
        if (!_isPlaying)
        {
            return;
        }

        await _player.SeekAsync(
            position);
    
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public bool IsPlaying =>
        _isPlaying &&
        _player.IsPlaying;

    public bool HasCurrentTrack =>
        _player.GetCurrentTrack() != null;

    public Track? GetCurrentTrack()
    {
        return _player.GetCurrentTrack();
    }

    public TimeSpan GetPosition()
    {
        return _player.GetPosition();
    }

    public TimeSpan GetDuration()
    {
        return _player.GetDuration();
    }

    private async void Player_PlaybackFinished(
        object? sender,
        EventArgs e)
    {
        if (!_isPlaying)
        {
            FileLogger.Info(
                "PlaybackFinished относится не к активному " +
                "треку очереди зрителей. Событие проигнорировано.");

            return;
        }

        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (!_isPlaying)
            {
                return;
            }

            FileLogger.Info(
                "Получено событие PlaybackFinished " +
                "от музыкального плеера.");

            _isPlaying =
                false;

        FileLogger.Info(
            $"Перед запуском следующего: " +
            $"QueueCount: {_queue.Count}");

            try
            {
                var started =
                    await StartNextCoreAsync();

                FileLogger.Info(
                    $"Попытка автоматического запуска " +
                    "следующего трека завершена. " +
                    $"Started: {started}");
            }
            catch (Exception ex)
            {
                FileLogger.Error(
                    "Ошибка автоматического запуска " +
                    "следующего трека.",
                    ex);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void RaiseQueuePlaybackFinished()
    {
        FileLogger.Info(
            "Очередь зрительских заявок полностью закончилась.");

        QueuePlaybackFinished?.Invoke(
            this,
            EventArgs.Empty);
    }
}