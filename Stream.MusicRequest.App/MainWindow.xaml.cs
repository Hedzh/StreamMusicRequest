using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Stream.MusicRequest.Audio.Players;
using Stream.MusicRequest.Core.Events;
using Stream.MusicRequest.Core.Services;
using Stream.MusicRequest.Core.Sources;
using Stream.MusicRequest.Infrastructure.Storage;
using Stream.MusicRequest.Twitch.Configuration;
using Stream.MusicRequest.Twitch.Models;
using Stream.MusicRequest.Twitch.Services;
using Stream.MusicRequest.Twitch.Storage;
using Stream.MusicRequest.Yandex.Services;
using Stream.MusicRequest.Yandex.Sources;
using Stream.MusicRequest.Yandex.Storage;
using YandexMusic;

namespace Stream.MusicRequest.App;

public partial class MainWindow : Window
{
    private readonly AppServices _appServices;
    private readonly MusicQueueService _queue;
    private readonly MusicRequestService _requestService;
    private readonly MusicPlaybackService _playbackService;
    private readonly MainMusicService _mainMusicService;
    private readonly MusicPlaybackController _playbackController;

    private readonly YandexMusicClient _yandexClient;
    private readonly YandexAuthService _yandexAuthService;

    private readonly TwitchAuthService _twitchAuthService;
    private readonly TwitchApiClient _twitchApiClient;

    private readonly YandexMusicSource _yandexMusicSource;
    private readonly NAudioMusicPlayer _musicPlayer;

    private DeviceAuthorizationWindow? _twitchAuthorizationWindow;

    private readonly DispatcherTimer _playbackUiTimer;

    private bool _isSeeking;

    public MainWindow()
    {
        InitializeComponent();

        _queue =
            new MusicQueueService();

        _queue.QueueChanged +=
            Queue_QueueChanged;

        _yandexClient =
            new YandexMusicClient();

        var storageDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "StreamMusicRequest",
                "Auth");

        var protectedStorage =
            new DpapiProtectedStorage(
                storageDirectory);

        var authStorage =
            new YandexAuthStorage(
                protectedStorage);

        _yandexAuthService =
            new YandexAuthService(
                _yandexClient,
                authStorage);

        _yandexAuthService.AuthenticationStateChanged +=
            YandexAuthService_AuthenticationStateChanged;

        _yandexMusicSource =
            new YandexMusicSource(
                _yandexClient);

        var sourceResolver =
            new MusicSourceResolver(
                new IMusicSource[]
                {
                    _yandexMusicSource
                });

        _requestService =
            new MusicRequestService(
                _queue,
                sourceResolver);

        _musicPlayer =
            new NAudioMusicPlayer();

        _mainMusicService =
            new MainMusicService(
                _musicPlayer);

        _playbackService =
            new MusicPlaybackService(
                _queue,
                _musicPlayer);

        _playbackController =
            new MusicPlaybackController(
                _mainMusicService,
                _playbackService,
                _queue);

        var twitchSettings =
            new TwitchSettings();

        var twitchAuthStorage =
            new TwitchAuthStorage(
                protectedStorage);

        var twitchHttpClient =
            new HttpClient();

        _twitchAuthService =
            new TwitchAuthService(
                twitchHttpClient,
                twitchSettings,
                twitchAuthStorage);

        _twitchApiClient =
            new TwitchApiClient(
                twitchHttpClient,
                twitchSettings.ClientId);

        _twitchAuthService.AuthenticationStateChanged +=
            TwitchAuthService_AuthenticationStateChanged;

        var twitchRewardSettingsDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "StreamMusicRequest",
                "Settings");

        var twitchRewardSettingsStorage =
            new TwitchRewardSettingsStorage(
                twitchRewardSettingsDirectory);

        _appServices =
            new AppServices(
                _yandexAuthService,
                _twitchAuthService,
                _twitchApiClient,
                twitchRewardSettingsStorage);

        _appServices.TwitchService.RewardActionReceived +=
            TwitchService_RewardActionReceived;

        _playbackUiTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(250)
            };

        _playbackUiTimer.Tick +=
            PlaybackUiTimer_Tick;

        UpdateQueueStatus();
        RefreshQueueUi();
        UpdatePlaybackModeUi();
        UpdateMainMusicUi();

        Loaded +=
            MainWindow_Loaded;

        Closed +=
            MainWindow_Closed;

        UpdateConnectionStatus(
            yandexAuthenticated: false,
            twitchAuthenticated: false);

        Loaded +=
            MainWindow_ControlsLoaded;
    }

    private void MainWindow_ControlsLoaded(
        object sender,
        RoutedEventArgs e)
    {
        ConnectPlaybackControls();

        _playbackUiTimer.Start();

        Loaded -=
            MainWindow_ControlsLoaded;
    }

    private void ConnectPlaybackControls()
    {
        var buttons =
            FindVisualChildren<Button>(
                this);

        foreach (var button in buttons)
        {
            if (button.ToolTip is not string toolTip)
            {
                continue;
            }

            switch (toolTip)
            {
                case "Воспроизвести / пауза":
                    button.Click +=
                        PlaybackToggleButton_Click;
                    break;

                case "Предыдущий трек":
                case "Предыдущий":
                    button.Click +=
                        PreviousTrackButton_Click;
                    break;

                case "Следующий трек":
                case "Следующий":
                    button.Click +=
                        NextTrackButton_Click;
                    break;
            }
        }

        SetMainMusicButton.Click +=
            SetMainMusicButton_Click;

        PlaybackSlider.PreviewMouseLeftButtonDown +=
            PlaybackSlider_PreviewMouseLeftButtonDown;

        PlaybackSlider.PreviewMouseLeftButtonUp +=
            PlaybackSlider_PreviewMouseLeftButtonUp;

        PlaybackSlider.ValueChanged +=
            PlaybackSlider_ValueChanged;

        VolumeSlider.ValueChanged +=
            VolumeSlider_ValueChanged;

        _musicPlayer.SetVolume(
            (float)(VolumeSlider.Value / 100.0));
    }

    private static IEnumerable<T> FindVisualChildren<T>(
        DependencyObject dependencyObject)
        where T : DependencyObject
    {
        if (dependencyObject == null)
        {
            yield break;
        }

        for (var i = 0;
             i < VisualTreeHelper.GetChildrenCount(
                     dependencyObject);
             i++)
        {
            var child =
                VisualTreeHelper.GetChild(
                    dependencyObject,
                    i);

            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in
                     FindVisualChildren<T>(
                         child))
            {
                yield return descendant;
            }
        }
    }

    private async void PlaybackToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (_playbackController.IsViewerModeActive)
            {
                if (_musicPlayer.IsPlaying)
                {
                    await _playbackController
                        .PauseViewerMusicAsync();

                    SetPlaybackButtonText(
                        "▶");
                }
                else
                {
                    await _playbackController
                        .ResumeViewerMusicAsync();

                    SetPlaybackButtonText(
                        "Ⅱ");
                }

                return;
            }

            if (!_playbackController.HasMainMusic)
            {
                return;
            }

            if (_musicPlayer.IsPlaying)
            {
                await _playbackController
                    .PauseMainMusicAsync();

                SetPlaybackButtonText(
                    "▶");
            }
            else
            {
                await _playbackController
                    .ResumeMainMusicAsync();

                SetPlaybackButtonText(
                    "Ⅱ");
            }

            UpdatePlaybackModeUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось изменить состояние воспроизведения.\n\n{ex.Message}",
                "Ошибка плеера",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void PreviousTrackButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _playbackController
                .PreviousAsync();

            UpdatePlaybackUi();
            UpdatePlaybackModeUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось переключить на предыдущий трек.\n\n{ex.Message}",
                "Ошибка плеера",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void NextTrackButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _playbackController
                .NextAsync();

            UpdatePlaybackUi();
            UpdatePlaybackModeUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось переключить трек.\n\n{ex.Message}",
                "Ошибка плеера",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SetPlaybackButtonText(
        string text)
    {
        var buttons =
            FindVisualChildren<Button>(
                this);

        foreach (var button in buttons)
        {
            if (button.ToolTip is string toolTip &&
                toolTip ==
                "Воспроизвести / пауза")
            {
                button.Content =
                    text;

                return;
            }
        }
    }

    private void PlaybackUiTimer_Tick(
        object? sender,
        EventArgs e)
    {
        UpdatePlaybackUi();
    }

    private void UpdatePlaybackUi()
    {
        var track =
            _musicPlayer.GetCurrentTrack();

        UpdatePlaybackModeUi();
        UpdateMainMusicUi();

        if (track == null)
        {
            return;
        }

        if (!string.Equals(
                CurrentArtistText.Text,
                track.Artist,
                StringComparison.Ordinal))
        {
            CurrentArtistText.Text =
                track.Artist;
        }

        if (!string.Equals(
                CurrentTitleText.Text,
                track.Title,
                StringComparison.Ordinal))
        {
            CurrentTitleText.Text =
                track.Title;
        }

        CurrentAlbumText.Text =
            string.IsNullOrWhiteSpace(
                track.Album)
                ? "Без альбома"
                : track.Album;

        var duration =
            _musicPlayer.GetDuration();

        var position =
            _musicPlayer.GetPosition();

        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        if (!_isSeeking)
        {
            PlaybackSlider.Minimum =
                0;

            PlaybackSlider.Maximum =
                duration.TotalSeconds;

            PlaybackSlider.Value =
                Math.Clamp(
                    position.TotalSeconds,
                    0,
                    duration.TotalSeconds);
        }

        CurrentPositionText.Text =
            FormatTime(position);

        DurationText.Text =
            FormatTime(duration);

        SetPlaybackButtonText(
            _musicPlayer.IsPlaying
                ? "Ⅱ"
                : "▶");
    }

    private void UpdatePlaybackModeUi()
    {
        if (_playbackController.IsViewerModeActive)
        {
            PlaybackModeText.Text =
                "Музыка зрителей";

            return;
        }

        if (_playbackController.HasMainMusic)
        {
            PlaybackModeText.Text =
                "Основная музыка";

            return;
        }

        PlaybackModeText.Text =
            "Воспроизведение не запущено";
    }

    private void UpdateMainMusicUi()
    {
        var album =
            _playbackController.CurrentMainMusic;

        if (album == null)
        {
            MainAlbumTitleText.Text =
                "Не выбран";

            MainAlbumInfoText.Text =
                "Добавьте альбом или плейлист Yandex Music";

            return;
        }

        MainAlbumTitleText.Text =
            album.Title;

        var isPlaylist =
            album.SourceUrl?.Contains(
                "/playlists/",
                StringComparison.OrdinalIgnoreCase)
            == true;

        var sourceType =
            isPlaylist
                ? "Плейлист"
                : "Альбом";

        var trackCount =
            album.Tracks.Count;

        var trackWord =
            GetRussianTrackWord(
                trackCount);

        MainAlbumInfoText.Text =
            $"{sourceType} • {trackCount} {trackWord}";
    }

    private static string GetRussianTrackWord(
        int count)
    {
        var value =
            Math.Abs(count) % 100;

        if (value >= 11 &&
            value <= 19)
        {
            return "треков";
        }

        var lastDigit =
            value % 10;

        return lastDigit switch
        {
            1 => "трек",
            2 or 3 or 4 => "трека",
            _ => "треков"
        };
    }

    private void PlaybackSlider_PreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        _isSeeking = true;
    }

    private async void PlaybackSlider_PreviewMouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        _isSeeking = false;

        try
        {
            var position =
                TimeSpan.FromSeconds(
                    PlaybackSlider.Value);

            if (_playbackController.IsViewerModeActive)
            {
                await _playbackService.SeekAsync(
                    position);
            }
            else
            {
                await _mainMusicService
                    .SeekCurrentAsync(
                        position);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось перемотать трек.\n\n{ex.Message}",
                "Ошибка перемотки",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PlaybackSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isSeeking)
        {
            return;
        }

        var previewPosition =
            TimeSpan.FromSeconds(
                Math.Max(
                    0,
                    PlaybackSlider.Value));

        CurrentPositionText.Text =
            FormatTime(previewPosition);
    }

    private void VolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        var volume =
            Math.Clamp(
                VolumeSlider.Value / 100.0,
                0.0,
                1.0);

        _musicPlayer.SetVolume(
            (float)volume);
    }

    private static string FormatTime(
        TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return time.ToString(
                @"h\:mm\:ss");
        }

        return time.ToString(
            @"mm\:ss");
    }

    private void OpenSettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var settingsWindow =
            new SettingsWindow(
                _appServices)
            {
                Owner = this
            };

        settingsWindow.ShowDialog();
    }

    private async void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        await _appServices
            .LoadTwitchRewardSettingsAsync();

        await RestoreYandexAuthenticationAsync();

        await RestoreTwitchAuthenticationAsync();

        UpdateMainMusicUi();
    }

    private async Task RestoreYandexAuthenticationAsync()
    {
        try
        {
            var restored =
                await _yandexAuthService.TryRestoreAsync();

            if (restored)
            {
                YandexStatusText.Text =
                    "Подключено";

                SetYandexStatusVisual(
                    isConnected: true);
            }
            else
            {
                YandexStatusText.Text =
                    "Не подключено";

                SetYandexStatusVisual(
                    isConnected: false);
            }
        }
        catch (Exception ex)
        {
            YandexStatusText.Text =
                "Ошибка подключения";

            SetYandexStatusVisual(
                isConnected: false);

            MessageBox.Show(
                $"Не удалось восстановить авторизацию Yandex Music.\n\n{ex.Message}",
                "Ошибка восстановления",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task RestoreTwitchAuthenticationAsync()
    {
        try
        {
            var twitchToken =
                await _twitchAuthService.TryRestoreAsync();

            if (twitchToken == null)
            {
                await _appServices.TwitchService
                    .DisconnectAsync();

                SetTwitchDisconnectedUi();
                return;
            }

            TwitchStatusText.Text =
                "Проверка подключения...";

            SetTwitchStatusVisual(
                isConnected: false);

            var user =
                await _twitchApiClient.GetCurrentUserAsync(
                    twitchToken.AccessToken);

            TwitchStatusText.Text =
                $"Подключено — {user.DisplayName}";

            SetTwitchStatusVisual(
                isConnected: true);

            await ConnectTwitchServiceAsync();
        }
        catch (Exception ex)
        {
            TwitchStatusText.Text =
                "Ошибка подключения";

            SetTwitchStatusVisual(
                isConnected: false);

            MessageBox.Show(
                $"Не удалось проверить авторизацию Twitch через API.\n\n{ex.Message}",
                "Ошибка Twitch API",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void TwitchAuthService_AuthenticationStateChanged(
        object? sender,
        bool isAuthenticated)
    {
        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(
                () =>
                    TwitchAuthService_AuthenticationStateChanged(
                        sender,
                        isAuthenticated));

            return;
        }

        if (!isAuthenticated)
        {
            await DisconnectTwitchServiceAsync();

            SetTwitchDisconnectedUi();

            return;
        }

        try
        {
            TwitchStatusText.Text =
                "Проверка подключения...";

            SetTwitchStatusVisual(
                isConnected: false);

            var token =
                await _twitchAuthService.TryRestoreAsync();

            if (token == null ||
                string.IsNullOrWhiteSpace(
                    token.AccessToken))
            {
                await DisconnectTwitchServiceAsync();

                SetTwitchDisconnectedUi();

                return;
            }

            var user =
                await _twitchApiClient.GetCurrentUserAsync(
                    token.AccessToken);

            TwitchStatusText.Text =
                $"Подключено — {user.DisplayName}";

            SetTwitchStatusVisual(
                isConnected: true);

            await ConnectTwitchServiceAsync();
        }
        catch
        {
            await DisconnectTwitchServiceAsync();

            SetTwitchDisconnectedUi();
        }
    }

    private async Task ConnectTwitchServiceAsync()
    {
        try
        {
            await _appServices.TwitchService
                .ConnectAsync();
        }
        catch
        {
            SetTwitchDisconnectedUi();
        }
    }

    private async Task DisconnectTwitchServiceAsync()
    {
        try
        {
            await _appServices.TwitchService
                .DisconnectAsync();
        }
        catch
        {
            // Twitch already disconnected.
        }
    }

    private async void TwitchService_RewardActionReceived(
        object? sender,
        TwitchRewardActionReceivedEventArgs e)
    {
        try
        {
            switch (e.Action)
            {
                case TwitchRewardAction.OrderMusic:
                    await HandleOrderMusicRewardAsync(
                        e.Redemption);

                    break;

                case TwitchRewardAction.SkipTrack:
                    await HandleSkipTrackRewardAsync();

                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось обработать Twitch-награду.\n\n{ex.Message}",
                "Ошибка Twitch-награды",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task HandleOrderMusicRewardAsync(
        TwitchRewardRedemption redemption)
    {
        var sourceUrl =
            redemption.UserInput.Trim();

        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return;
        }

        var request =
            new Stream.MusicRequest.Core.Requests.MusicRequest
            {
                Id =
                    redemption.Id,

                ViewerName =
                    redemption.UserName,

                SourceUrl =
                    sourceUrl,

                CreatedAt =
                    redemption.RedeemedAt
            };

        var result =
            await _requestService.AddRequestAsync(
                request);

        if (!result.Success)
        {
            return;
        }

        var item =
            result.Item;

        if (item == null ||
            item.Track == null)
        {
            return;
        }

        await _playbackController
            .AddViewerRequestAsync(
                item);

        await Dispatcher.InvokeAsync(
            () =>
            {
                UpdatePlaybackUi();
                UpdatePlaybackModeUi();
                RefreshQueueUi();
            });
    }

    private async Task HandleSkipTrackRewardAsync()
    {
        await _playbackController
            .NextAsync();

        await Dispatcher.InvokeAsync(
            () =>
            {
                UpdatePlaybackUi();
                UpdatePlaybackModeUi();
                RefreshQueueUi();
            });
    }

    private void SetTwitchDisconnectedUi()
    {
        TwitchStatusText.Text =
            "Не подключено";

        SetTwitchStatusVisual(
            isConnected: false);
    }

    private void YandexAuthService_AuthenticationStateChanged(
        object? sender,
        bool isAuthenticated)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                        YandexAuthService_AuthenticationStateChanged(
                            sender,
                            isAuthenticated)));

            return;
        }

        YandexStatusText.Text =
            isAuthenticated
                ? "Подключено"
                : "Не подключено";

        SetYandexStatusVisual(
            isAuthenticated);
    }

    private void UpdateConnectionStatus(
        bool yandexAuthenticated,
        bool twitchAuthenticated)
    {
        YandexStatusText.Text =
            yandexAuthenticated
                ? "Подключено"
                : "Не подключено";

        TwitchStatusText.Text =
            twitchAuthenticated
                ? "Подключено"
                : "Не подключено";

        SetYandexStatusVisual(
            yandexAuthenticated);

        SetTwitchStatusVisual(
            twitchAuthenticated);
    }

    private void SetYandexStatusVisual(
        bool isConnected)
    {
        YandexStatusText.Foreground =
            isConnected
                ? FindResource("SuccessBrush")
                    as Brush
                : FindResource("TextMuted")
                    as Brush;
    }

    private void SetTwitchStatusVisual(
        bool isConnected)
    {
        TwitchStatusText.Foreground =
            isConnected
                ? FindResource("SuccessBrush")
                    as Brush
                : FindResource("TextMuted")
                    as Brush;
    }

    private async void AddRequestButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var url =
            TrackUrlTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(
                "Введите ссылку на музыкальный трек.",
                "Музыкальный запрос",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            var request =
                new Stream.MusicRequest.Core.Requests.MusicRequest
                {
                    ViewerName =
                        "TestViewer",

                    SourceUrl =
                        url
                };

            var result =
                await _requestService.AddRequestAsync(
                    request);

            if (!result.Success)
            {
                MessageBox.Show(
                    result.Error ??
                    "Не удалось добавить музыкальный запрос.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var item =
                result.Item;

            if (item == null ||
                item.Track == null)
            {
                MessageBox.Show(
                    "Запрос считается успешным, но данные трека отсутствуют.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            await _playbackController
                .AddViewerRequestAsync(
                    item);

            UpdatePlaybackUi();
            UpdatePlaybackModeUi();
            RefreshQueueUi();

            TrackUrlTextBox.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось обработать музыкальный запрос.\n\n{ex.Message}",
                "Ошибка музыкального запроса",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void SetMainMusicButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var url =
            MainMusicUrlTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(
                "Введите ссылку на альбом или плейлист Yandex Music.",
                "Основная музыка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (_playbackController.IsViewerModeActive)
        {
            MessageBox.Show(
                "Сейчас воспроизводится очередь зрителей.\n\n" +
                "Сначала дождитесь её окончания, " +
                "после чего можно будет изменить основной источник.",
                "Основная музыка",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            Stream.MusicRequest.Core.Models.MusicAlbum?
                album = null;

            if (_yandexMusicSource.CanHandleAlbumUrl(url))
            {
                album =
                    await _yandexMusicSource
                        .ResolveAlbumAsync(url);
            }
            else if (_yandexMusicSource.CanHandlePlaylistUrl(url))
            {
                album =
                    await _yandexMusicSource
                        .ResolvePlaylistAsync(url);
            }
            else
            {
                MessageBox.Show(
                    "Для основной музыки необходимо указать " +
                    "ссылку именно на альбом или плейлист Yandex Music.\n\n" +
                    "Ссылки на отдельные треки здесь не принимаются.",
                    "Основная музыка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (album == null)
            {
                MessageBox.Show(
                    "Не удалось загрузить альбом или плейлист.",
                    "Основная музыка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (album.Tracks.Count == 0)
            {
                MessageBox.Show(
                    "Источник загружен, но в нём нет доступных треков.",
                    "Основная музыка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            await _playbackController
                .SetMainMusicAsync(
                    album);

            UpdateMainMusicUi();

            var started =
                await _playbackController
                    .StartMainMusicAsync();

            if (!started)
            {
                MessageBox.Show(
                    "Основной источник загружен, " +
                    "но воспроизведение не удалось запустить.",
                    "Основная музыка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                UpdatePlaybackModeUi();
                UpdateMainMusicUi();

                return;
            }

            UpdatePlaybackUi();
            UpdatePlaybackModeUi();
            UpdateMainMusicUi();

            MainMusicUrlTextBox.Clear();

            MessageBox.Show(
                $"Основная музыка загружена.\n\n" +
                $"Название: {album.Title}\n" +
                $"Треков: {album.Tracks.Count}\n\n" +
                "Воспроизведение первого трека запущено.",
                "Основная музыка",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось загрузить основную музыку.\n\n{ex.Message}",
                "Ошибка основной музыки",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DequeueButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string itemId)
        {
            return;
        }

        _queue.Remove(
            itemId);
    }

    private void Queue_QueueChanged(
        object? sender,
        QueueChangedEvent e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                        Queue_QueueChanged(
                            sender,
                            e)));

            return;
        }

        QueueStatusText.Text =
            $"В очереди: {e.QueueLength}";

        RefreshQueueUi();
    }

    private void UpdateQueueStatus()
    {
        QueueStatusText.Text =
            $"В очереди: {_queue.Count}";
    }

    private void RefreshQueueUi()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(
                    RefreshQueueUi));

            return;
        }

        var items =
            _queue.GetSnapshot();

        QueueItemsPanel.Children.Clear();

        EmptyQueueState.Visibility =
            items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        var queuePosition =
            1;

        foreach (var item in items)
        {
            if (item.Track == null)
            {
                continue;
            }

            var border =
                new Border
                {
                    Background =
                        (Brush)FindResource(
                            "CardBackground"),

                    BorderBrush =
                        (Brush)FindResource(
                            "BorderBrushDark"),

                    BorderThickness =
                        new Thickness(1),

                    CornerRadius =
                        new CornerRadius(8),

                    Padding =
                        new Thickness(12),

                    Margin =
                        new Thickness(0, 0, 0, 8)
                };

            var grid =
                new Grid();

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(34)
                });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        GridLength.Auto
                });

            var numberText =
                new TextBlock
                {
                    Text =
                        $"{queuePosition}",

                    FontSize =
                        13,

                    FontWeight =
                        FontWeights.Bold,

                    Foreground =
                        (Brush)FindResource(
                            "AccentBrush"),

                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            Grid.SetColumn(
                numberText,
                0);

            grid.Children.Add(
                numberText);

            var infoPanel =
                new StackPanel();

            var trackText =
                new TextBlock
                {
                    Text =
                        $"{item.Track.Artist} — {item.Track.Title}",

                    FontSize =
                        13,

                    FontWeight =
                        FontWeights.SemiBold,

                    Foreground =
                        (Brush)FindResource(
                            "TextPrimary"),

                    TextWrapping =
                        TextWrapping.Wrap
                };

            infoPanel.Children.Add(
                trackText);

            var viewerText =
                new TextBlock
                {
                    Text =
                        $"Зритель: {item.Request.ViewerName}",

                    FontSize =
                        11,

                    Foreground =
                        (Brush)FindResource(
                            "TextMuted"),

                    Margin =
                        new Thickness(
                            0,
                            4,
                            0,
                            0)
                };

            infoPanel.Children.Add(
                viewerText);

            Grid.SetColumn(
                infoPanel,
                1);

            grid.Children.Add(
                infoPanel);

            var removeButton =
                new Button
                {
                    Content =
                        "×",

                    Style =
                        (Style)FindResource(
                            "QueueActionButton"),

                    ToolTip =
                        "Удалить",

                    Tag =
                        item.Id
                };

            removeButton.Click +=
                DequeueButton_Click;

            Grid.SetColumn(
                removeButton,
                2);

            grid.Children.Add(
                removeButton);

            border.Child =
                grid;

            QueueItemsPanel.Children.Add(
                border);

            queuePosition++;
        }

        QueueStatusText.Text =
            $"В очереди: {items.Count}";
    }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _playbackUiTimer.Stop();

        _yandexAuthService.AuthenticationStateChanged -=
            YandexAuthService_AuthenticationStateChanged;

        _twitchAuthService.AuthenticationStateChanged -=
            TwitchAuthService_AuthenticationStateChanged;

        _appServices.TwitchService.RewardActionReceived -=
            TwitchService_RewardActionReceived;

        try
        {
            _appServices.TwitchService
                .DisconnectAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // Twitch service is being shut down.
        }

        CloseTwitchAuthorizationWindow();

        _musicPlayer.Dispose();

        _yandexClient.DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    private void CloseTwitchAuthorizationWindow()
    {
        var window =
            _twitchAuthorizationWindow;

        _twitchAuthorizationWindow = null;

        if (window != null)
        {
            window.Closed -=
                TwitchAuthorizationWindow_Closed;

            if (window.IsVisible)
            {
                window.Close();
            }
        }
    }

    private void TwitchAuthorizationWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (ReferenceEquals(
                sender,
                _twitchAuthorizationWindow))
        {
            _twitchAuthorizationWindow = null;
        }
    }
}