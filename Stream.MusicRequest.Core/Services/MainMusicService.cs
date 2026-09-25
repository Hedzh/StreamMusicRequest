using Stream.MusicRequest.Core.Diagnostics;
using Stream.MusicRequest.Core.Models;
using Stream.MusicRequest.Core.Player;
using Stream.MusicRequest.Core.Sources;

namespace Stream.MusicRequest.Core.Services;

public class MainMusicService
{
    private readonly IMusicPlayer _player;

    private MusicAlbum? _currentAlbum;

    private int _currentTrackIndex = -1;

    private bool _isMainMusicActive;

    private bool _handlingPlaybackFinished;

    private bool _shuffleEnabled;

    private readonly Random _shuffleRandom =
        new();

    private readonly List<int> _shuffleOrder =
        new();

    private int _shufflePosition;

    private readonly List<int> _shuffleHistory =
        new();

    public MainMusicService(
        IMusicPlayer player)
    {
        _player = player;

        _player.PlaybackFinished +=
            Player_PlaybackFinished;

        FileLogger.Info(
            "MainMusicService создан.");
    }

    public MusicAlbum? CurrentAlbum =>
        _currentAlbum;

    public Track? CurrentTrack =>
        _currentAlbum == null ||
        _currentTrackIndex < 0 ||
        _currentTrackIndex >=
            _currentAlbum.Tracks.Count
            ? null
            : _currentAlbum.Tracks[
                _currentTrackIndex];

    public int CurrentTrackIndex =>
        _currentTrackIndex;

    public bool IsMainMusicActive =>
        _isMainMusicActive;

    public bool IsShuffleEnabled =>
        _shuffleEnabled;

    public void SetShuffleEnabled(
        bool enabled)
    {
        if (_shuffleEnabled == enabled)
        {
            return;
        }

        _shuffleEnabled = enabled;
        _shuffleOrder.Clear();
        _shuffleHistory.Clear();
        _shufflePosition = 0;

        if (_shuffleEnabled &&
            _currentAlbum != null &&
            _currentAlbum.Tracks.Count > 1)
        {
            BuildShuffleOrder(
                _currentTrackIndex);
        }

        FileLogger.Info(
            $"Случайный порядок основной музыки: " +
            $"{(_shuffleEnabled ? "включён" : "выключен")}");
    }

    public void SetAlbum(
        MusicAlbum album)
    {
        if (album == null)
        {
            throw new ArgumentNullException(
                nameof(album));
        }

        if (album.Tracks.Count == 0)
        {
            throw new InvalidOperationException(
                "Нельзя установить пустой альбом.");
        }

        _currentAlbum =
            album;

        _currentTrackIndex =
            0;

        _isMainMusicActive =
            false;

        _shuffleOrder.Clear();
        _shuffleHistory.Clear();
        _shufflePosition = 0;

        if (_shuffleEnabled &&
            album.Tracks.Count > 1)
        {
            BuildShuffleOrder(0);
        }

        FileLogger.Info(
            $"Основной источник установлен: " +
            $"{album.Title}. " +
            $"Треков: {album.Tracks.Count}");
    }

    public async Task<bool> PlayAsync()
    {
        if (_currentAlbum == null)
        {
            FileLogger.Info(
                "PlayAsync основной музыки: " +
                "основной источник не установлен.");

            return false;
        }

        if (_currentAlbum.Tracks.Count == 0)
        {
            FileLogger.Info(
                "PlayAsync основной музыки: " +
                "основной источник пуст.");

            return false;
        }

        if (_currentTrackIndex < 0 ||
            _currentTrackIndex >=
            _currentAlbum.Tracks.Count)
        {
            _currentTrackIndex =
                0;
        }

        var track =
            _currentAlbum.Tracks[
                _currentTrackIndex];

        FileLogger.Info(
            $"Подготовка основной музыки: " +
            $"{track.Artist} — {track.Title}");

        var playbackUrl =
            await GetPlaybackUrlAsync(
                track);

        if (string.IsNullOrWhiteSpace(
                playbackUrl))
        {
            FileLogger.Info(
                "Не удалось получить прямую " +
                "ссылку на основной трек.");

            return false;
        }

        track.SourceUrl =
            playbackUrl;

        FileLogger.Info(
            "Прямая ссылка на основной трек получена.");

        await _player.PlayAsync(
            track);

        _isMainMusicActive =
            true;

        FileLogger.Info(
            $"Основная музыка запущена: " +
            $"{track.Artist} — {track.Title}");

        return true;
    }

    public async Task PauseAsync()
    {
        if (!_isMainMusicActive)
        {
            return;
        }

        FileLogger.Info(
            "Основная музыка поставлена на паузу.");

        await _player.PauseAsync();
    }

    public async Task ResumeAsync()
    {
        if (!_isMainMusicActive)
        {
            return;
        }

        FileLogger.Info(
            "Основная музыка возобновлена.");

        await _player.ResumeAsync();
    }

    public async Task StopAsync()
    {
        FileLogger.Info(
            "Основная музыка остановлена.");

        await _player.StopAsync();

        _isMainMusicActive =
            false;
    }

    public async Task NextAsync()
    {
        if (_currentAlbum == null ||
            _currentAlbum.Tracks.Count == 0)
        {
            return;
        }

        var previousTrackIndex =
            _currentTrackIndex;

        if (_shuffleEnabled &&
            _currentAlbum.Tracks.Count > 1)
        {
            var nextTrackIndex =
                GetNextShuffleTrackIndex();

            if (nextTrackIndex !=
                previousTrackIndex)
            {
                _shuffleHistory.Add(
                    previousTrackIndex);
            }

            _currentTrackIndex =
                nextTrackIndex;
        }
        else
        {
            _currentTrackIndex++;

            if (_currentTrackIndex >=
                _currentAlbum.Tracks.Count)
            {
                _currentTrackIndex =
                    0;
            }
        }

        FileLogger.Info(
            $"Переход на следующий трек " +
            $"основной музыки. " +
            $"Индекс: {_currentTrackIndex}");

        await PlayAsync();
    }

    public async Task PreviousAsync()
    {
        if (_currentAlbum == null ||
            _currentAlbum.Tracks.Count == 0)
        {
            return;
        }

        if (_shuffleEnabled &&
            _shuffleHistory.Count > 0)
        {
            _currentTrackIndex =
                _shuffleHistory[^1];

            _shuffleHistory.RemoveAt(
                _shuffleHistory.Count - 1);
        }
        else
        {
            _currentTrackIndex--;

            if (_currentTrackIndex < 0)
            {
                _currentTrackIndex =
                    _currentAlbum.Tracks.Count - 1;
            }
        }

        FileLogger.Info(
            $"Переход на предыдущий трек " +
            $"основной музыки. " +
            $"Индекс: {_currentTrackIndex}");

        await PlayAsync();
    }

    public async Task SeekCurrentAsync(
        TimeSpan position)
    {
        if (!_isMainMusicActive)
        {
            FileLogger.Info(
                "SeekCurrentAsync: " +
                "основная музыка сейчас не активна.");

            return;
        }

        var duration =
            _player.GetDuration();

        if (duration <= TimeSpan.Zero)
        {
            FileLogger.Info(
                "SeekCurrentAsync: " +
                "длительность текущего трека неизвестна.");

            return;
        }

        var clampedPosition =
            position < TimeSpan.Zero
                ? TimeSpan.Zero
                : position > duration
                    ? duration
                    : position;

        FileLogger.Info(
            $"Перемотка основной музыки: " +
            $"{clampedPosition} из {duration}");

        await _player.SeekAsync(
            clampedPosition);
    }

    public MainMusicPlaybackState CaptureState()
    {
        var state =
            new MainMusicPlaybackState
            {
                Album =
                    _currentAlbum,

                TrackIndex =
                    _currentTrackIndex,

                Position =
                    _player.GetPosition(),

                WasPlaying =
                    _isMainMusicActive &&
                    _player.IsPlaying
            };

        FileLogger.Info(
            $"Состояние основной музыки сохранено. " +
            $"TrackIndex: {state.TrackIndex}, " +
            $"Position: {state.Position}, " +
            $"WasPlaying: {state.WasPlaying}");

        return state;
    }

    public async Task<bool> RestoreStateAsync(
        MainMusicPlaybackState? state)
    {
        if (state == null)
        {
            return false;
        }

        if (state.Album == null)
        {
            return false;
        }

        if (state.Album.Tracks.Count == 0)
        {
            return false;
        }

        _currentAlbum =
            state.Album;

        _currentTrackIndex =
            Math.Clamp(
                state.TrackIndex,
                0,
                state.Album.Tracks.Count - 1);

        _isMainMusicActive =
            false;

        _shuffleOrder.Clear();
        _shuffleHistory.Clear();
        _shufflePosition = 0;

        if (_shuffleEnabled &&
            state.Album.Tracks.Count > 1)
        {
            BuildShuffleOrder(
                _currentTrackIndex);
        }

        var track =
            _currentAlbum.Tracks[
                _currentTrackIndex];

        FileLogger.Info(
            $"Восстановление основной музыки: " +
            $"{track.Artist} — {track.Title}. " +
            $"Позиция: {state.Position}");

        var playbackUrl =
            await GetPlaybackUrlAsync(
                track);

        if (string.IsNullOrWhiteSpace(
                playbackUrl))
        {
            FileLogger.Info(
                "Не удалось получить прямую ссылку " +
                "при восстановлении основной музыки.");

            return false;
        }

        track.SourceUrl =
            playbackUrl;

        await _player.PlayAsync(
            track);

        if (state.Position > TimeSpan.Zero)
        {
            await _player.SeekAsync(
                state.Position);
        }

        _isMainMusicActive =
            true;

        if (!state.WasPlaying)
        {
            await _player.PauseAsync();
        }

        FileLogger.Info(
            "Основная музыка успешно восстановлена.");

        return true;
    }

    public void ClearAlbum()
    {
        FileLogger.Info(
            "Основной источник очищен.");

        _currentAlbum =
            null;

        _currentTrackIndex =
            -1;

        _isMainMusicActive =
            false;

        _shuffleOrder.Clear();
        _shuffleHistory.Clear();
        _shufflePosition = 0;
    }

    private int GetNextShuffleTrackIndex()
    {
        if (_currentAlbum == null ||
            _currentAlbum.Tracks.Count <= 1)
        {
            return _currentTrackIndex;
        }

        if (_shufflePosition >=
            _shuffleOrder.Count)
        {
            BuildShuffleOrder(
                _currentTrackIndex);
        }

        if (_shuffleOrder.Count == 0)
        {
            return _currentTrackIndex;
        }

        return _shuffleOrder[
            _shufflePosition++];
    }

    private void BuildShuffleOrder(
        int excludedTrackIndex)
    {
        _shuffleOrder.Clear();
        _shufflePosition = 0;

        if (_currentAlbum == null)
        {
            return;
        }

        for (var index = 0;
             index < _currentAlbum.Tracks.Count;
             index++)
        {
            if (index != excludedTrackIndex)
            {
                _shuffleOrder.Add(index);
            }
        }

        for (var index = _shuffleOrder.Count - 1;
             index > 0;
             index--)
        {
            var swapIndex =
                _shuffleRandom.Next(index + 1);

            (_shuffleOrder[index],
             _shuffleOrder[swapIndex]) =
                (_shuffleOrder[swapIndex],
                 _shuffleOrder[index]);
        }
    }

    private async Task<string?> GetPlaybackUrlAsync(
        Track track)
    {
        if (_currentAlbum?.Source
            is not IPlayableMusicSource playableSource)
        {
            FileLogger.Info(
                "Основной источник не поддерживает " +
                "получение прямой ссылки.");

            return null;
        }

        FileLogger.Info(
            $"Получение прямой ссылки через источник: " +
            $"{playableSource.Name}");

        return await playableSource.GetPlaybackUrlAsync(
            track);
    }

    private async void Player_PlaybackFinished(
        object? sender,
        EventArgs e)
    {
        if (!_isMainMusicActive)
        {
            return;
        }

        if (_handlingPlaybackFinished)
        {
            return;
        }

        _handlingPlaybackFinished =
            true;

        try
        {
            FileLogger.Info(
                "Основной трек завершён. " +
                "Запуск следующего трека.");

            _isMainMusicActive =
                false;

            await NextAsync();
        }
        catch (Exception ex)
        {
            FileLogger.Error(
                "Ошибка автоматического перехода " +
                "основной музыки.",
                ex);
        }
        finally
        {
            _handlingPlaybackFinished =
                false;
        }
    }
}