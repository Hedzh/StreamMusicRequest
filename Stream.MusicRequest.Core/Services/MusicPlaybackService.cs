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

            return await StartNextAsync();
        }

        if (item.Source is not IPlayableMusicSource playableSource)
        {
            FileLogger.Info(
                "Источник не поддерживает воспроизведение. " +
                "Переход к следующему.");

            return await StartNextAsync();
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

            return await StartNextAsync();
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

    public async Task ResumeAsync()
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

    public async Task StopAsync()
    {
        FileLogger.Info(
            "MusicPlaybackService.StopAsync вызван.");

        await _player.StopAsync();

        _isPlaying =
            false;

        FileLogger.Info(
            "Воспроизведение остановлено.");
    }

    public async Task SkipAsync()
    {
        FileLogger.Info(
            "MusicPlaybackService.SkipAsync вызван.");

        await _player.SkipAsync();

        _isPlaying =
            false;

        FileLogger.Info(
            "Текущий трек пропущен. " +
            "Попытка запуска следующего.");

        await StartNextAsync();
    }

    public async Task SeekAsync(
        TimeSpan position)
    {
        if (!_isPlaying)
        {
            return;
        }

        await _player.SeekAsync(
            position);
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
                await StartNextAsync();

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

    private void RaiseQueuePlaybackFinished()
    {
        FileLogger.Info(
            "Очередь зрительских заявок полностью закончилась.");

        QueuePlaybackFinished?.Invoke(
            this,
            EventArgs.Empty);
    }
}