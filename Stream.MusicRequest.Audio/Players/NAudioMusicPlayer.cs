using NAudio.Wave;
using Stream.MusicRequest.Audio.Streaming;
using Stream.MusicRequest.Core.Diagnostics;
using Stream.MusicRequest.Core.Models;
using Stream.MusicRequest.Core.Player;

namespace Stream.MusicRequest.Audio.Players;

public class NAudioMusicPlayer :
    IMusicPlayer,
    IDisposable,
    IAsyncDisposable
{
    private static readonly WaveFormat OutputFormat =
        new(
            48000,
            16,
            2);

    private readonly LocalAudioStreamServer _audioStreamServer;

    private WaveOut? _outputDevice;
    private MediaFoundationReader? _audioReader;
    private MediaFoundationResampler? _resampler;
    private TeeWaveProvider? _streamingProvider;
    private Track? _currentTrack;

    private CancellationTokenSource? _playbackMonitorCancellation;

    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private bool _manualStop;
    private bool _playbackFinishedRaised;
    private float _volume = 1.0f;

    public event EventHandler? PlaybackFinished;

    public string ObsAudioUrl =>
        _audioStreamServer.BrowserSourceUrl;

    public bool IsPlaying =>
        _outputDevice?.PlaybackState ==
        PlaybackState.Playing;

    public NAudioMusicPlayer(
        string obsBindAddress = "127.0.0.1",
        int obsAudioPort = 8766)
    {
        _audioStreamServer =
            new LocalAudioStreamServer(
                obsBindAddress,
                obsAudioPort);

        _audioStreamServer.SetFormat(OutputFormat);
        _audioStreamServer.Start();

        FileLogger.Info(
            $"OBS аудиопоток доступен по адресу: " +
            $"{ObsAudioUrl}");
    }

    public async Task PlayAsync(Track track)
    {
        await PlayAsync(track, TimeSpan.Zero, false)
            .ConfigureAwait(false);
    }

    public async Task PlayAsync(
        Track track,
        TimeSpan startPosition)
    {
        await PlayAsync(track, startPosition, true)
            .ConfigureAwait(false);
    }

    private async Task PlayAsync(
        Track track,
        TimeSpan startPosition,
        bool useStartPosition)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (string.IsNullOrWhiteSpace(track.SourceUrl))
            {
                throw new ArgumentException(
                    "У трека не указана ссылка на аудиопоток.",
                    nameof(track));
            }

            FileLogger.Info(
                $"PlayAsync: подготовка трека " +
                $"{track.Artist} — {track.Title}");

            StopPlayback();

            _manualStop = false;
            _playbackFinishedRaised = false;
            _currentTrack = track;

            try
            {
                await Task.Run(() =>
                {
                    FileLogger.Info(
                        "Создание MediaFoundationReader.");

                    var reader =
                        new MediaFoundationReader(
                            track.SourceUrl);

                    FileLogger.Info(
                        $"MediaFoundationReader создан. " +
                        $"Duration: {reader.TotalTime}");

                    var effectivePosition = TimeSpan.Zero;

                    if (useStartPosition)
                    {
                        effectivePosition =
                            startPosition < TimeSpan.Zero
                                ? TimeSpan.Zero
                                : startPosition > reader.TotalTime
                                    ? reader.TotalTime
                                    : startPosition;

                        FileLogger.Info(
                            $"Начальная позиция восстановления: " +
                            $"{effectivePosition}.");

                        if (effectivePosition > TimeSpan.Zero)
                        {
                            reader.CurrentTime =
                                effectivePosition;

                            FileLogger.Info(
                                $"Позиция MediaFoundationReader " +
                                $"установлена до запуска WaveOut: " +
                                $"{reader.CurrentTime}");
                        }
                    }

                    var resampler =
                        new MediaFoundationResampler(
                            reader,
                            OutputFormat)
                        {
                            ResamplerQuality = 60
                        };

                    var streamingProvider =
                        new TeeWaveProvider(
                            resampler,
                            _audioStreamServer);

                    var output = new WaveOut();

                    output.PlaybackStopped +=
                        Output_PlaybackStopped;

                    output.Init(streamingProvider);
                    output.Volume = ToOutputVolume(_volume);

                    _audioReader = reader;
                    _resampler = resampler;
                    _streamingProvider = streamingProvider;
                    _outputDevice = output;

                    FileLogger.Info(
                        $"WaveOut инициализирован. " +
                        $"Фактическая позиция перед запуском: " +
                        $"{reader.CurrentTime}. " +
                        "Запуск воспроизведения.");

                    output.Play();

                    FileLogger.Info(
                        $"WaveOut.Play() выполнен. " +
                        $"Фактическая позиция после запуска: " +
                        $"{reader.CurrentTime}");

                    StartPlaybackMonitor();
                });
            }
            catch (Exception ex)
            {
                _manualStop = true;

                FileLogger.Error(
                    "Ошибка запуска воспроизведения.",
                    ex);

                StopPlayback();

                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task PauseAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            FileLogger.Info("PauseAsync вызван.");
            _outputDevice?.Pause();
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
            FileLogger.Info("ResumeAsync вызван.");
            _outputDevice?.Play();
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
            FileLogger.Info("StopAsync вызван.");
            StopPlayback();
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
            FileLogger.Info("SkipAsync вызван.");
            StopPlayback();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SeekAsync(TimeSpan position)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            var reader = _audioReader;

            if (reader == null)
            {
                throw new InvalidOperationException(
                    "Невозможно переместить позицию: " +
                    "аудиопоток не загружен.");
            }

            var duration = reader.TotalTime;

            if (duration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    "Невозможно переместить позицию: " +
                    "длительность трека неизвестна.");
            }

            var clampedPosition =
                position < TimeSpan.Zero
                    ? TimeSpan.Zero
                    : position > duration
                        ? duration
                        : position;

            var wasPlaying = IsPlaying;

            FileLogger.Info(
                $"SeekAsync: перемещение на " +
                $"{clampedPosition} из {duration}.");

            try
            {
                _manualStop = true;

                _playbackMonitorCancellation?.Cancel();
                _playbackMonitorCancellation?.Dispose();
                _playbackMonitorCancellation = null;

                if (_outputDevice != null)
                {
                    _outputDevice.PlaybackStopped -=
                        Output_PlaybackStopped;

                    _outputDevice.Stop();
                    _outputDevice.Dispose();
                    _outputDevice = null;
                }

                _resampler?.Dispose();
                _resampler = null;
                _streamingProvider = null;

                reader.CurrentTime = clampedPosition;

                var resampler =
                    new MediaFoundationResampler(
                        reader,
                        OutputFormat)
                    {
                        ResamplerQuality = 60
                    };

                var streamingProvider =
                    new TeeWaveProvider(
                        resampler,
                        _audioStreamServer);

                var output = new WaveOut();

                output.PlaybackStopped +=
                    Output_PlaybackStopped;

                output.Init(streamingProvider);
                output.Volume = _volume;

                _resampler = resampler;
                _streamingProvider = streamingProvider;
                _outputDevice = output;

                _manualStop = false;

                if (wasPlaying)
                {
                    output.Play();
                }

                StartPlaybackMonitor();
            }
            catch (Exception ex)
            {
                FileLogger.Error(
                    "Ошибка перемещения позиции " +
                    "воспроизведения.",
                    ex);

                _manualStop = true;
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0.0f, 1.0f);

        if (_outputDevice != null)
        {
            _outputDevice.Volume =
                ToOutputVolume(_volume);
        }

        FileLogger.Info(
            $"Громкость установлена: {_volume:0.00}, " +
            $"локальная громкость WaveOut: {ToOutputVolume(_volume):0.00}");
    }

    private static float ToOutputVolume(
        float userVolume)
    {
        var clampedVolume =
            Math.Clamp(userVolume, 0.0f, 1.0f);

        // The UI value is intentionally mapped to a softer
        // curve for local playback only. The OBS stream is
        // produced before WaveOut volume is applied, so OBS
        // remains at its original level.
        return MathF.Pow(clampedVolume, 2.0f);
    }

    public Track? GetCurrentTrack() => _currentTrack;

    public TimeSpan GetPosition() =>
        _audioReader?.CurrentTime ?? TimeSpan.Zero;

    public TimeSpan GetDuration() =>
        _audioReader?.TotalTime ?? TimeSpan.Zero;

    public async ValueTask DisposeAsync()
    {
        FileLogger.Info(
            "DisposeAsync NAudioMusicPlayer.");

        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            StopPlayback();

            await _audioStreamServer.DisposeAsync()
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        FileLogger.Info("Dispose NAudioMusicPlayer.");

        _operationGate.Wait();

        try
        {
            StopPlayback();
            _audioStreamServer.Dispose();
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private void StartPlaybackMonitor()
    {
        _playbackMonitorCancellation?.Cancel();
        _playbackMonitorCancellation?.Dispose();

        _playbackMonitorCancellation =
            new CancellationTokenSource();

        var cancellationToken =
            _playbackMonitorCancellation.Token;

        _ = Task.Run(async () =>
        {
            FileLogger.Info(
                "Монитор окончания трека запущен.");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var reader = _audioReader;

                if (reader == null)
                {
                    return;
                }

                TimeSpan position;
                TimeSpan duration;

                try
                {
                    position = reader.CurrentTime;
                    duration = reader.TotalTime;
                }
                catch (Exception ex)
                {
                    FileLogger.Error(
                        "Не удалось получить позицию " +
                        "или длительность трека.",
                        ex);

                    return;
                }

                if (duration <= TimeSpan.Zero)
                {
                    continue;
                }

                if (position >= duration)
                {
                    FileLogger.Info(
                        $"Монитор обнаружил окончание трека. " +
                        $"Position: {position}, " +
                        $"Duration: {duration}");

                    RaisePlaybackFinished();
                    return;
                }

                if (duration - position <= TimeSpan.FromSeconds(1))
                {
                    FileLogger.Info(
                        $"Трек находится менее чем в секунде " +
                        $"от окончания. Position: {position}, " +
                        $"Duration: {duration}");
                }
            }
        }, cancellationToken);
    }

    private void Output_PlaybackStopped(
        object? sender,
        StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, _outputDevice))
        {
            FileLogger.Info(
                "PlaybackStopped получен от устаревшего " +
                "WaveOut. Событие проигнорировано.");

            return;
        }

        FileLogger.Info(
            $"WaveOut.PlaybackStopped получен. " +
            $"ManualStop: {_manualStop}");

        if (_manualStop)
        {
            FileLogger.Info(
                "PlaybackStopped вызван после ручной " +
                "остановки. PlaybackFinished не вызывается.");

            return;
        }

        RaisePlaybackFinished();
    }

    private void RaisePlaybackFinished()
    {
        if (_manualStop)
        {
            return;
        }

        if (_playbackFinishedRaised)
        {
            FileLogger.Info(
                "PlaybackFinished уже был вызван. " +
                "Повторный вызов пропущен.");

            return;
        }

        _playbackFinishedRaised = true;

        FileLogger.Info(
            "PlaybackFinished вызывается.");

        _currentTrack = null;

        PlaybackFinished?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void StopPlayback()
    {
        FileLogger.Info("StopPlayback вызван.");

        _manualStop = true;

        _playbackMonitorCancellation?.Cancel();
        _playbackMonitorCancellation?.Dispose();
        _playbackMonitorCancellation = null;

        if (_outputDevice != null)
        {
            FileLogger.Info(
                "Остановка и освобождение WaveOut.");

            _outputDevice.PlaybackStopped -=
                Output_PlaybackStopped;

            _outputDevice.Stop();
            _outputDevice.Dispose();
            _outputDevice = null;
        }

        _streamingProvider = null;

        if (_resampler != null)
        {
            FileLogger.Info(
                "Освобождение MediaFoundationResampler.");

            _resampler.Dispose();
            _resampler = null;
        }

        if (_audioReader != null)
        {
            FileLogger.Info(
                "Освобождение MediaFoundationReader.");

            _audioReader.Dispose();
            _audioReader = null;
        }

        _currentTrack = null;
        _playbackFinishedRaised = false;

        FileLogger.Info("StopPlayback завершён.");
    }

    private sealed class TeeWaveProvider : IWaveProvider
    {
        private readonly IWaveProvider _source;
        private readonly LocalAudioStreamServer _server;

        public TeeWaveProvider(
            IWaveProvider source,
            LocalAudioStreamServer server)
        {
            _source = source;
            _server = server;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            var bytesRead =
                _source.Read(
                    buffer.AsSpan(offset, count));

            if (bytesRead > 0)
            {
                _server.Publish(
                    buffer.AsSpan(offset, bytesRead));
            }

            return bytesRead;
        }

        public int Read(Span<byte> buffer)
        {
            var bytesRead = _source.Read(buffer);

            if (bytesRead > 0)
            {
                _server.Publish(buffer[..bytesRead]);
            }

            return bytesRead;
        }
    }
}