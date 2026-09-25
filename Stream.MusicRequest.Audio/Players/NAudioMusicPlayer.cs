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

    private readonly LocalAudioStreamServer
        _audioStreamServer;

    private WaveOut? _outputDevice;
    private MediaFoundationReader? _audioReader;
    private MediaFoundationResampler? _resampler;
    private TeeWaveProvider? _streamingProvider;
    private Track? _currentTrack;

    private CancellationTokenSource?
        _playbackMonitorCancellation;

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

        _audioStreamServer.SetFormat(
            OutputFormat);

        _audioStreamServer.Start();

        FileLogger.Info(
            $"OBS аудиопоток доступен по адресу: " +
            $"{ObsAudioUrl}");
    }

    public async Task PlayAsync(
        Track track)
    {
        if (string.IsNullOrWhiteSpace(
                track.SourceUrl))
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
            await Task.Run(
                () =>
                {
                    FileLogger.Info(
                        "Создание MediaFoundationReader.");

                    var reader =
                        new MediaFoundationReader(
                            track.SourceUrl);

                    FileLogger.Info(
                        $"MediaFoundationReader создан. " +
                        $"Duration: {reader.TotalTime}");

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

                    var output =
                        new WaveOut();

                    output.PlaybackStopped +=
                        Output_PlaybackStopped;

                    output.Init(
                        streamingProvider);

                    output.Volume =
                        _volume;

                    _audioReader =
                        reader;

                    _resampler =
                        resampler;

                    _streamingProvider =
                        streamingProvider;

                    _outputDevice =
                        output;

                    FileLogger.Info(
                        "WaveOut инициализирован. " +
                        "Запуск воспроизведения.");

                    output.Play();

                    FileLogger.Info(
                        "WaveOut.Play() выполнен.");

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

    public Task PauseAsync()
    {
        FileLogger.Info(
            "PauseAsync вызван.");

        _outputDevice?.Pause();

        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        FileLogger.Info(
            "ResumeAsync вызван.");

        _outputDevice?.Play();

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        FileLogger.Info(
            "StopAsync вызван.");

        StopPlayback();

        return Task.CompletedTask;
    }

    public Task SkipAsync()
    {
        FileLogger.Info(
            "SkipAsync вызван.");

        StopPlayback();

        return Task.CompletedTask;
    }

    public Task SeekAsync(
        TimeSpan position)
    {
        var reader =
            _audioReader;

        if (reader == null)
        {
            return Task.CompletedTask;
        }

        var duration =
            reader.TotalTime;

        if (duration <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        var clampedPosition =
            position < TimeSpan.Zero
                ? TimeSpan.Zero
                : position > duration
                    ? duration
                    : position;

        var wasPlaying =
            IsPlaying;

        FileLogger.Info(
            $"SeekAsync: перемещение на " +
            $"{clampedPosition} из {duration}.");

        try
        {
            _manualStop =
                true;

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

            reader.CurrentTime =
                clampedPosition;

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

            var output =
                new WaveOut();

            output.PlaybackStopped +=
                Output_PlaybackStopped;

            output.Init(
                streamingProvider);

            output.Volume =
                _volume;

            _resampler =
                resampler;

            _streamingProvider =
                streamingProvider;

            _outputDevice =
                output;

            _manualStop =
                false;

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

            _manualStop =
                true;

            throw;
        }

        return Task.CompletedTask;
    }

    public void SetVolume(
        float volume)
    {
        _volume =
            Math.Clamp(
                volume,
                0.0f,
                1.0f);

        if (_outputDevice != null)
        {
            _outputDevice.Volume =
                _volume;
        }

        FileLogger.Info(
            $"Громкость установлена: {_volume:0.00}");
    }

    public Track? GetCurrentTrack()
    {
        return _currentTrack;
    }

    public TimeSpan GetPosition()
    {
        return _audioReader?.CurrentTime
               ?? TimeSpan.Zero;
    }

    public TimeSpan GetDuration()
    {
        return _audioReader?.TotalTime
               ?? TimeSpan.Zero;
    }

    public async ValueTask DisposeAsync()
    {
        FileLogger.Info(
            "DisposeAsync NAudioMusicPlayer.");

        StopPlayback();

        await _audioStreamServer
            .DisposeAsync()
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        FileLogger.Info(
            "Dispose NAudioMusicPlayer.");

        StopPlayback();

        _audioStreamServer.Dispose();
    }

    private void StartPlaybackMonitor()
    {
        _playbackMonitorCancellation?.Cancel();
        _playbackMonitorCancellation?.Dispose();

        _playbackMonitorCancellation =
            new CancellationTokenSource();

        var cancellationToken =
            _playbackMonitorCancellation.Token;

        _ = Task.Run(
            async () =>
            {
                FileLogger.Info(
                    "Монитор окончания трека запущен.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(
                            500,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var reader =
                        _audioReader;

                    if (reader == null)
                    {
                        return;
                    }

                    TimeSpan position;
                    TimeSpan duration;

                    try
                    {
                        position =
                            reader.CurrentTime;

                        duration =
                            reader.TotalTime;
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

                    if (duration - position <=
                        TimeSpan.FromSeconds(1))
                    {
                        FileLogger.Info(
                            $"Трек находится менее чем в секунде " +
                            $"от окончания. " +
                            $"Position: {position}, " +
                            $"Duration: {duration}");
                    }
                }
            },
            cancellationToken);
    }

    private void Output_PlaybackStopped(
        object? sender,
        StoppedEventArgs e)
    {
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
        FileLogger.Info(
            "StopPlayback вызван.");

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

        FileLogger.Info(
            "StopPlayback завершён.");
    }

    private sealed class TeeWaveProvider :
        IWaveProvider
    {
        private readonly IWaveProvider _source;
        private readonly LocalAudioStreamServer _server;

        public TeeWaveProvider(
            IWaveProvider source,
            LocalAudioStreamServer server)
        {
            _source =
                source;

            _server =
                server;
        }

        public WaveFormat WaveFormat =>
            _source.WaveFormat;

        public int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            var bytesRead =
                _source.Read(
                    buffer.AsSpan(
                        offset,
                        count));

            if (bytesRead > 0)
            {
                _server.Publish(
                    buffer.AsSpan(
                        offset,
                        bytesRead));
            }

            return bytesRead;
        }

        public int Read(
            Span<byte> buffer)
        {
            var bytesRead =
                _source.Read(
                    buffer);

            if (bytesRead > 0)
            {
                _server.Publish(
                    buffer[..bytesRead]);
            }

            return bytesRead;
        }
    }
}