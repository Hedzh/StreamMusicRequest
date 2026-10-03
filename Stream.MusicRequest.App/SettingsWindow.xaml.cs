using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Stream.MusicRequest.DonationAlerts.Models;
using Stream.MusicRequest.Infrastructure.Storage;
using Stream.MusicRequest.Twitch.Models;
using Stream.MusicRequest.Yandex.Sources;

namespace Stream.MusicRequest.App;

public partial class SettingsWindow : Window
{
    private readonly AppServices _appServices;
    private readonly YandexMusicSource _yandexMusicSource;
    private readonly Action<float>? _setPlaybackVolume;

    private readonly StackPanel[] _pages;

    private YandexAuthorizationWindow? _yandexAuthorizationWindow;

    private DeviceAuthorizationWindow? _twitchAuthorizationWindow;

    private bool _isInitialized;

    private readonly ObsSettingsStorage _obsSettingsStorage;

    private readonly PlaybackSettingsStorage _playbackSettingsStorage;

    private ObsSettings _obsSettings = new();

    private PlaybackSettings _playbackSettings = new();

    public SettingsWindow(
        AppServices appServices,
        YandexMusicSource yandexMusicSource,
        Action<float>? setPlaybackVolume = null)
    {
        InitializeComponent();

        _appServices =
            appServices
            ?? throw new ArgumentNullException(
                nameof(appServices));

        _yandexMusicSource =
            yandexMusicSource
            ?? throw new ArgumentNullException(
                nameof(yandexMusicSource));

        _setPlaybackVolume =
            setPlaybackVolume;

        var settingsDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "StreamMusicRequest",
                "Settings");

        _obsSettingsStorage =
            new ObsSettingsStorage(
                settingsDirectory);

        _playbackSettingsStorage =
            new PlaybackSettingsStorage(
                settingsDirectory);

        _pages =
        [
            GeneralPage,
            MusicSourcesPage,
            TwitchPage,
            TwitchRewardsPage,
            DonationAlertsPage,
            PlaybackPage,
            ObsPage,
            ChangelogPage
        ];

        _appServices
            .TwitchAuthService
            .AuthenticationStateChanged +=
                TwitchAuthService_AuthenticationStateChanged;

        _appServices
            .TwitchService
            .ConnectionStateChanged +=
                TwitchService_ConnectionStateChanged;

        _appServices
            .DonationAlertsMusicService
            .ConnectionStateChanged +=
                DonationAlertsMusicService_ConnectionStateChanged;

        _appServices
            .YandexAuthService
            .AuthenticationStateChanged +=
                YandexAuthService_AuthenticationStateChanged;

        _isInitialized = true;

        ShowPage(
            NavigationList.SelectedIndex >= 0
                ? NavigationList.SelectedIndex
                : 0);

        UpdateYandexUi();
        LoadObsSettingsUi();
        LoadPlaybackSettingsUi();

        _ = UpdateTwitchUiAsync();
        _ = LoadTwitchRewardSettingsUiAsync();
        LoadDonationAlertsSettingsUi();

        Closed +=
            SettingsWindow_Closed;
    }

    private void NavigationList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
            return;

        var selectedIndex =
            NavigationList.SelectedIndex;

        if (selectedIndex < 0 ||
            selectedIndex >= _pages.Length)
        {
            return;
        }

        ShowPage(selectedIndex);
    }

    private void ShowPage(
        int pageIndex)
    {
        if (pageIndex < 0 ||
            pageIndex >= _pages.Length)
        {
            return;
        }

        for (var i = 0;
             i < _pages.Length;
             i++)
        {
            _pages[i].Visibility =
                i == pageIndex
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    // ============================================================
    // YANDEX
    // ============================================================

    private void YandexAuthService_AuthenticationStateChanged(
        object? sender,
        bool isAuthenticated)
    {
        Dispatcher.Invoke(
            UpdateYandexUi);
    }

    private void UpdateYandexUi()
    {
        var isAuthenticated =
            _appServices
                .YandexAuthService
                .IsAuthenticated;

        if (isAuthenticated)
        {
            YandexStatusIndicator.Fill =
                FindResource("SuccessBrush")
                as Brush;

            YandexStatusText.Text =
                "Авторизован";

            YandexStatusText.Foreground =
                FindResource("SuccessBrush")
                as Brush;

            YandexAuthorizeButton.IsEnabled =
                false;

            YandexLogoutButton.IsEnabled =
                true;
        }
        else
        {
            YandexStatusIndicator.Fill =
                FindResource("TextMuted")
                as Brush;

            YandexStatusText.Text =
                "Не авторизован";

            YandexStatusText.Foreground =
                FindResource("TextMuted")
                as Brush;

            YandexAuthorizeButton.IsEnabled =
                true;

            YandexLogoutButton.IsEnabled =
                false;
        }
    }

    private async void YandexAuthorizeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            YandexAuthorizeButton.IsEnabled =
                false;

            YandexLogoutButton.IsEnabled =
                false;

            YandexStatusText.Text =
                "Ожидание авторизации...";

            YandexStatusText.Foreground =
                FindResource("TextMuted")
                as Brush;

            await _appServices
                .YandexAuthService
                .AuthorizeAsync(
                    (verificationUrl, userCode) =>
                    {
                        Dispatcher.Invoke(
                            () =>
                            {
                                ShowYandexAuthorizationWindow(
                                    verificationUrl,
                                    userCode);
                            });
                    });

            CloseYandexAuthorizationWindow();

            UpdateYandexUi();
        }
        catch (Exception ex)
        {
            CloseYandexAuthorizationWindow();

            AppMessageBox.Show(
                this,
                $"Не удалось авторизоваться в Yandex Music.\n\n{ex.Message}",
                "Yandex Music",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            UpdateYandexUi();
        }
    }

    private void ShowYandexAuthorizationWindow(
        string verificationUrl,
        string userCode)
    {
        CloseYandexAuthorizationWindow();

        _yandexAuthorizationWindow =
            new YandexAuthorizationWindow(
                verificationUrl,
                userCode)
            {
                Owner = this
            };

        _yandexAuthorizationWindow.Closed +=
            YandexAuthorizationWindow_Closed;

        _yandexAuthorizationWindow.Show();

        _yandexAuthorizationWindow.Activate();
    }

    private void YandexAuthorizationWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (ReferenceEquals(
                sender,
                _yandexAuthorizationWindow))
        {
            _yandexAuthorizationWindow = null;
        }
    }

    private void CloseYandexAuthorizationWindow()
    {
        var window =
            _yandexAuthorizationWindow;

        _yandexAuthorizationWindow =
            null;

        if (window == null)
        {
            return;
        }

        window.Closed -=
            YandexAuthorizationWindow_Closed;

        if (window.IsVisible)
        {
            window.Close();
        }
    }

    private async void YandexLogoutButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _appServices
                .YandexAuthService
                .SignOutAsync();

            UpdateYandexUi();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(
                this,
                $"Не удалось выйти из аккаунта Yandex Music.\n\n{ex.Message}",
                "Yandex Music",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ============================================================
    // TWITCH
    // ============================================================

    private void DonationAlertsMusicService_ConnectionStateChanged(
        object? sender,
        bool isConnected)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () => DonationAlertsMusicService_ConnectionStateChanged(
                        sender,
                        isConnected)));
            return;
        }

        UpdateDonationAlertsConnectionUi();
    }

    private void TwitchService_ConnectionStateChanged(
        object? sender,
        bool isConnected)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () => TwitchService_ConnectionStateChanged(
                        sender,
                        isConnected)));

            return;
        }

        if (isConnected)
        {
            TwitchStatusIndicator.Fill =
                FindResource("SuccessBrush")
                as Brush;

            TwitchStatusText.Text =
                "Подключен";

            TwitchStatusText.Foreground =
                FindResource("SuccessBrush")
                as Brush;
        }
        else
        {
            TwitchStatusIndicator.Fill =
                FindResource("TextMuted")
                as Brush;

            TwitchStatusText.Text =
                "Соединение потеряно";

            TwitchStatusText.Foreground =
                FindResource("TextMuted")
                as Brush;
        }
    }

    private void TwitchAuthService_AuthenticationStateChanged(
        object? sender,
        bool isAuthenticated)
    {
        Dispatcher.Invoke(
            async () =>
            {
                await UpdateTwitchUiAsync();
            });
    }

    private async Task UpdateTwitchUiAsync()
    {
        var token =
            await _appServices
                .TwitchAuthService
                .TryRestoreAsync();

        if (token == null ||
            string.IsNullOrWhiteSpace(
                token.AccessToken))
        {
            SetTwitchDisconnectedUi();
            return;
        }

        try
        {
            TwitchStatusIndicator.Fill =
                FindResource("SuccessBrush")
                as Brush;

            TwitchStatusText.Text =
                "Подключен";

            TwitchStatusText.Foreground =
                FindResource("SuccessBrush")
                as Brush;

            TwitchChannelText.Text =
                "Получение информации о канале...";

            TwitchAuthorizeButton.IsEnabled =
                false;

            TwitchLogoutButton.IsEnabled =
                true;

            var user =
                await _appServices
                    .TwitchApiClient
                    .GetCurrentUserAsync(
                        token.AccessToken);

            TwitchChannelText.Text =
                string.IsNullOrWhiteSpace(
                    user.DisplayName)
                    ? $"Канал: {user.Login}"
                    : $"Канал: {user.DisplayName}";
        }
        catch
        {
            SetTwitchDisconnectedUi();
        }
    }

    private void SetTwitchDisconnectedUi()
    {
        TwitchStatusIndicator.Fill =
            FindResource("TextMuted")
            as Brush;

        TwitchStatusText.Text =
            "Не авторизован";

        TwitchStatusText.Foreground =
            FindResource("TextMuted")
            as Brush;

        TwitchChannelText.Text =
            "Канал: —";

        TwitchAuthorizeButton.IsEnabled =
            true;

        TwitchLogoutButton.IsEnabled =
            false;
    }

    private async void TwitchAuthorizeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            TwitchAuthorizeButton.IsEnabled =
                false;

            TwitchLogoutButton.IsEnabled =
                false;

            TwitchStatusIndicator.Fill =
                FindResource("TextMuted")
                as Brush;

            TwitchStatusText.Text =
                "Ожидание авторизации...";

            TwitchStatusText.Foreground =
                FindResource("TextMuted")
                as Brush;

            TwitchChannelText.Text =
                "Следуйте инструкции авторизации.";

            await _appServices
                .TwitchAuthService
                .AuthorizeAsync(
                    (verificationUrl, userCode) =>
                    {
                        Dispatcher.Invoke(
                            () =>
                            {
                                ShowTwitchAuthorizationWindow(
                                    verificationUrl,
                                    userCode);
                            });
                    });

            CloseTwitchAuthorizationWindow();

            await UpdateTwitchUiAsync();
        }
        catch (Exception ex)
        {
            CloseTwitchAuthorizationWindow();

            AppMessageBox.Show(
                this,
                $"Не удалось авторизоваться в Twitch.\n\n{ex.Message}",
                "Twitch",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            await UpdateTwitchUiAsync();
        }
    }

    private void ShowTwitchAuthorizationWindow(
        string verificationUrl,
        string userCode)
    {
        CloseTwitchAuthorizationWindow();

        _twitchAuthorizationWindow =
            new DeviceAuthorizationWindow(
                "Авторизация Twitch",
                verificationUrl,
                userCode)
            {
                Owner = this
            };

        _twitchAuthorizationWindow.Closed +=
            TwitchAuthorizationWindow_Closed;

        _twitchAuthorizationWindow.Show();

        _twitchAuthorizationWindow.Activate();
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

    private void CloseTwitchAuthorizationWindow()
    {
        var window =
            _twitchAuthorizationWindow;

        _twitchAuthorizationWindow =
            null;

        if (window == null)
        {
            return;
        }

        window.Closed -=
            TwitchAuthorizationWindow_Closed;

        if (window.IsVisible)
        {
            window.Close();
        }
    }

    private async void TwitchLogoutButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            TwitchLogoutButton.IsEnabled =
                false;

            await _appServices
                .TwitchAuthService
                .SignOutAsync();

            SetTwitchDisconnectedUi();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(
                this,
                $"Не удалось выйти из Twitch.\n\n{ex.Message}",
                "Twitch",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            await UpdateTwitchUiAsync();
        }
    }

    // ============================================================
    // PLAYBACK
    // ============================================================

    private void LoadPlaybackSettingsUi()
    {
        _playbackSettings =
            _playbackSettingsStorage.Load();

        PlaybackSettingsVolumeSlider.Value =
            _playbackSettings.Volume * 100.0;

        PlaybackSettingsVolumeText.Text =
            $"{PlaybackSettingsVolumeSlider.Value:0}%";

        DefaultPlaylistUrlTextBox.Text =
            _playbackSettings.DefaultPlaylistUrl;

        LoadDefaultPlaylistOnStartupCheckBox.IsChecked =
            _playbackSettings.LoadDefaultPlaylistOnStartup;

        DefaultPlaylistStatusText.Text =
            string.IsNullOrWhiteSpace(
                _playbackSettings.DefaultPlaylistUrl)
                ? string.Empty
                : "Сохранённая ссылка загружена из настроек.";
    }

    private void PlaybackSettingsVolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized)
        {
            return;
        }

        var volume =
            Math.Clamp(
                PlaybackSettingsVolumeSlider.Value / 100.0,
                0.0,
                1.0);

        PlaybackSettingsVolumeText.Text =
            $"{PlaybackSettingsVolumeSlider.Value:0}%";

        _setPlaybackVolume?.Invoke(
            (float)volume);
    }

    private void DefaultPlaylistClearButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DefaultPlaylistUrlTextBox.Clear();
        LoadDefaultPlaylistOnStartupCheckBox.IsChecked = false;
        DefaultPlaylistStatusText.Text = string.Empty;
    }

    private void DefaultPlaylistCheckButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var url =
            DefaultPlaylistUrlTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            DefaultPlaylistStatusText.Text =
                "Ссылка не указана.";
            return;
        }

        if (!_appServices.YandexAuthService.IsAuthenticated)
        {
            DefaultPlaylistStatusText.Text =
                "Сначала подключите Yandex Music.";
            return;
        }

        try
        {
            DefaultPlaylistStatusText.Text =
                "Проверка ссылки...";

            if (!_yandexMusicSource.CanHandlePlaylistUrl(url))
            {
                DefaultPlaylistStatusText.Text =
                    "Это не ссылка на плейлист Yandex Music.";
                return;
            }

            DefaultPlaylistStatusText.Text =
                "Ссылка распознана как плейлист Yandex Music.";
        }
        catch (Exception ex)
        {
            DefaultPlaylistStatusText.Text =
                $"Ошибка проверки: {ex.Message}";
        }
    }

    private async void PlaybackSettingsSaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var url =
            DefaultPlaylistUrlTextBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(url) &&
            !_appServices.YandexAuthService.IsAuthenticated)
        {
            AppMessageBox.Show(
                this,
                "Для сохранения плейлиста по умолчанию сначала подключите Yandex Music.",
                "Воспроизведение",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!_yandexMusicSource.CanHandlePlaylistUrl(url))
            {
                AppMessageBox.Show(
                    this,
                    "Указана некорректная ссылка на плейлист Yandex Music.",
                    "Воспроизведение",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _playbackSettings =
            new PlaybackSettings
            {
                Volume =
                    (float)Math.Clamp(
                        PlaybackSettingsVolumeSlider.Value / 100.0,
                        0.0,
                        1.0),

                DefaultPlaylistUrl = url,

                LoadDefaultPlaylistOnStartup =
                    LoadDefaultPlaylistOnStartupCheckBox.IsChecked == true
                    && !string.IsNullOrWhiteSpace(url)
            };

        await _playbackSettingsStorage
            .SaveAsync(_playbackSettings);

        DefaultPlaylistStatusText.Text =
            "Настройки сохранены.";

        AppMessageBox.Show(
            this,
            "Настройки воспроизведения сохранены.",
            "Воспроизведение",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenLogsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var logsDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "StreamMusicRequest",
                    "Logs");

            Directory.CreateDirectory(
                logsDirectory);

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = logsDirectory,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(
                this,
                $"Не удалось открыть папку логов.\n\n{ex.Message}",
                "Логи",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ============================================================
    // OBS
    // ============================================================

    private void LoadObsSettingsUi()
    {
        _obsSettings =
            _obsSettingsStorage.Load();

        ObsBindAddressTextBox.Text =
            _obsSettings.BindAddress;

        ObsWidgetPortTextBox.Text =
            _obsSettings.WidgetPort.ToString();

        ObsAudioPortTextBox.Text =
            _obsSettings.AudioPort.ToString();

        ObsScrollSpeedTextBox.Text =
            _obsSettings.ScrollSpeed.ToString("0.##");

        UpdateObsUrlsUi();
    }

    private void UpdateObsUrlsUi()
    {
        ObsWidgetUrlTextBox.Text =
            $"http://{_obsSettings.BindAddress}:{_obsSettings.WidgetPort}/widget";

        ObsAudioUrlTextBox.Text =
            $"http://{_obsSettings.BindAddress}:{_obsSettings.AudioPort}/";
    }

    private void ObsResetButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _obsSettings =
            new ObsSettings();

        LoadObsSettingsUi();
    }

    private async void ObsSaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var bindAddress =
            ObsBindAddressTextBox.Text.Trim();

        if (!System.Net.IPAddress.TryParse(
                bindAddress,
                out var address) ||
            address.AddressFamily !=
            System.Net.Sockets.AddressFamily.InterNetwork)
        {
            AppMessageBox.Show(
                this,
                "Адрес привязки должен быть корректным IPv4-адресом, например 127.0.0.1 или 0.0.0.0.",
                "OBS",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(
                ObsWidgetPortTextBox.Text.Trim(),
                out var widgetPort) ||
            widgetPort is < 1 or > 65535)
        {
            AppMessageBox.Show(
                this,
                "Порт виджета должен быть целым числом от 1 до 65535.",
                "OBS",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(
                ObsAudioPortTextBox.Text.Trim(),
                out var audioPort) ||
            audioPort is < 1 or > 65535)
        {
            AppMessageBox.Show(
                this,
                "Порт аудио должен быть целым числом от 1 до 65535.",
                "OBS",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (widgetPort == audioPort)
        {
            AppMessageBox.Show(
                this,
                "Порт виджета и порт аудио должны отличаться.",
                "OBS",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(
                ObsScrollSpeedTextBox.Text.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var scrollSpeed))
        {
            if (!double.TryParse(
                    ObsScrollSpeedTextBox.Text.Trim(),
                    out scrollSpeed))
            {
                AppMessageBox.Show(
                    this,
                    "Скорость текста должна быть числом.",
                    "OBS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        scrollSpeed =
            Math.Clamp(
                scrollSpeed,
                10.0,
                300.0);

        _obsSettings =
            new ObsSettings
            {
                BindAddress = bindAddress,
                WidgetPort = widgetPort,
                AudioPort = audioPort,
                ScrollSpeed = scrollSpeed
            };

        await _obsSettingsStorage
            .SaveAsync(_obsSettings);

        LoadObsSettingsUi();

        AppMessageBox.Show(
            this,
            "Настройки OBS сохранены. Адрес и порты будут применены после перезапуска приложения.",
            "OBS",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ObsOpenWidgetButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = ObsWidgetUrlTextBox.Text,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(
                this,
                $"Не удалось открыть виджет.\\n\\n{ex.Message}",
                "OBS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ============================================================
    // TWITCH REWARDS
    // ============================================================

    private async Task LoadTwitchRewardSettingsUiAsync()
    {
        try
        {
            await _appServices.LoadTwitchRewardSettingsAsync();

            var orderMusicBinding =
                _appServices.TwitchRewardBindings
                    .FirstOrDefault(
                        binding =>
                            binding.FeatureKey ==
                            Stream.MusicRequest.Twitch.Services.TwitchRewardService.OrderMusicFeatureKey);

            var skipTrackBinding =
                _appServices.TwitchRewardBindings
                    .FirstOrDefault(
                        binding =>
                            binding.FeatureKey ==
                            Stream.MusicRequest.Twitch.Services.TwitchRewardService.SkipTrackFeatureKey);

            OrderMusicRewardIdTextBox.Text =
                orderMusicBinding?.RewardId ?? string.Empty;

            SkipTrackRewardIdTextBox.Text =
                skipTrackBinding?.RewardId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(
                    OrderMusicRewardIdTextBox.Text))
            {
                ResetOrderMusicRewardDetails();
            }
            else
            {
                await CheckTwitchRewardAsync(
                    OrderMusicRewardIdTextBox.Text,
                    true);
            }

            if (string.IsNullOrWhiteSpace(
                    SkipTrackRewardIdTextBox.Text))
            {
                ResetSkipTrackRewardDetails();
            }
            else
            {
                await CheckTwitchRewardAsync(
                    SkipTrackRewardIdTextBox.Text,
                    false);
            }
        }
        catch
        {
            // Reward settings are optional and should not prevent the settings window from opening.
        }
    }

    private async void OrderMusicRewardCheckButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CheckTwitchRewardAsync(
            OrderMusicRewardIdTextBox.Text,
            true);
    }

    private async void SkipTrackRewardCheckButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CheckTwitchRewardAsync(
            SkipTrackRewardIdTextBox.Text,
            false);
    }

    private async Task CheckTwitchRewardAsync(
        string rewardId,
        bool isOrderMusic)
    {
        rewardId = rewardId.Trim();

        if (string.IsNullOrWhiteSpace(rewardId))
        {
            SetRewardCheckError(
                isOrderMusic,
                "Укажите Reward ID.");
            return;
        }

        if (!Guid.TryParse(
                rewardId,
                out _))
        {
            SetRewardCheckError(
                isOrderMusic,
                "Некорректный Reward ID. Используйте ID в формате UUID.");
            return;
        }

        var checkButton =
            isOrderMusic
                ? FindName("OrderMusicRewardCheckButton") as Button
                : FindName("SkipTrackRewardCheckButton") as Button;

        try
        {
            if (checkButton != null)
            {
                checkButton.IsEnabled = false;
            }

            using var timeoutCts =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));

            var token =
                await _appServices
                    .TwitchAuthService
                    .TryRestoreAsync();

            if (token == null ||
                string.IsNullOrWhiteSpace(token.AccessToken))
            {
                SetRewardCheckError(
                    isOrderMusic,
                    "Twitch не авторизован.");
                return;
            }

            var user =
                await _appServices
                    .TwitchApiClient
                    .GetCurrentUserAsync(
                        token.AccessToken,
                        timeoutCts.Token);

            var reward =
                await _appServices
                    .TwitchApiClient
                    .GetCustomRewardAsync(
                        token.AccessToken,
                        user.Id,
                        rewardId,
                        timeoutCts.Token);

            if (reward == null)
            {
                SetRewardCheckError(
                    isOrderMusic,
                    "Награда с таким Reward ID не найдена.");
                return;
            }

            if (isOrderMusic)
            {
                OrderMusicRewardStatusText.Text =
                    "Найдена";

                OrderMusicRewardStatusText.Foreground =
                    FindResource("SuccessBrush") as Brush;

                OrderMusicRewardNameText.Text =
                    reward.Title;

                OrderMusicRewardCostText.Text =
                    $"{reward.Cost} Channel Points";

                OrderMusicRewardInputText.Text =
                    reward.IsUserInputRequired ? "Да" : "Нет";

                OrderMusicRewardEnabledText.Text =
                    reward.IsEnabled ? "Да" : "Нет";
            }
            else
            {
                SkipTrackRewardStatusText.Text =
                    "Найдена";

                SkipTrackRewardStatusText.Foreground =
                    FindResource("SuccessBrush") as Brush;

                SkipTrackRewardNameText.Text =
                    reward.Title;

                SkipTrackRewardCostText.Text =
                    $"{reward.Cost} Channel Points";

                SkipTrackRewardInputText.Text =
                    reward.IsUserInputRequired ? "Да" : "Нет";

                SkipTrackRewardEnabledText.Text =
                    reward.IsEnabled ? "Да" : "Нет";
            }
        }
        catch (OperationCanceledException)
        {
            SetRewardCheckError(
                isOrderMusic,
                "Проверка не завершилась за 10 секунд. Проверьте подключение к Twitch.");
        }
        catch (Exception ex)
        {
            SetRewardCheckError(
                isOrderMusic,
                $"Ошибка проверки: {ex.Message}");
        }
        finally
        {
            if (checkButton != null)
            {
                checkButton.IsEnabled = true;
            }
        }
    }

    private async void OrderMusicRewardSaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _appServices
                .SaveTwitchRewardSettingAsync(
                    Stream.MusicRequest.Twitch.Services.TwitchRewardService.OrderMusicFeatureKey,
                    OrderMusicRewardIdTextBox.Text);

            OrderMusicRewardStatusText.Text =
                "Сохранено";

            OrderMusicRewardStatusText.Foreground =
                FindResource("SuccessBrush") as Brush;
        }
        catch (Exception ex)
        {
            SetRewardCheckError(
                true,
                $"Ошибка сохранения: {ex.Message}");
        }
    }

    private async void SkipTrackRewardSaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _appServices
                .SaveTwitchRewardSettingAsync(
                    Stream.MusicRequest.Twitch.Services.TwitchRewardService.SkipTrackFeatureKey,
                    SkipTrackRewardIdTextBox.Text);

            SkipTrackRewardStatusText.Text =
                "Сохранено";

            SkipTrackRewardStatusText.Foreground =
                FindResource("SuccessBrush") as Brush;
        }
        catch (Exception ex)
        {
            SetRewardCheckError(
                false,
                $"Ошибка сохранения: {ex.Message}");
        }
    }

    private async void OrderMusicRewardClearButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            OrderMusicRewardIdTextBox.Clear();

            await _appServices
                .SaveTwitchRewardSettingAsync(
                    Stream.MusicRequest.Twitch.Services.TwitchRewardService.OrderMusicFeatureKey,
                    string.Empty);

            ResetOrderMusicRewardDetails();
        }
        catch (Exception ex)
        {
            SetRewardCheckError(
                true,
                $"Ошибка очистки: {ex.Message}");
        }
    }

    private async void SkipTrackRewardClearButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            SkipTrackRewardIdTextBox.Clear();

            await _appServices
                .SaveTwitchRewardSettingAsync(
                    Stream.MusicRequest.Twitch.Services.TwitchRewardService.SkipTrackFeatureKey,
                    string.Empty);

            ResetSkipTrackRewardDetails();
        }
        catch (Exception ex)
        {
            SetRewardCheckError(
                false,
                $"Ошибка очистки: {ex.Message}");
        }
    }

    private void ResetOrderMusicRewardDetails()
    {
        OrderMusicRewardStatusText.Text = "Не проверена";
        OrderMusicRewardStatusText.Foreground =
            FindResource("TextSecondary") as Brush;
        OrderMusicRewardNameText.Text = "—";
        OrderMusicRewardCostText.Text = "—";
        OrderMusicRewardInputText.Text = "—";
        OrderMusicRewardEnabledText.Text = "—";
    }

    private void ResetSkipTrackRewardDetails()
    {
        SkipTrackRewardStatusText.Text = "Не проверена";
        SkipTrackRewardStatusText.Foreground =
            FindResource("TextSecondary") as Brush;
        SkipTrackRewardNameText.Text = "—";
        SkipTrackRewardCostText.Text = "—";
        SkipTrackRewardInputText.Text = "—";
        SkipTrackRewardEnabledText.Text = "—";
    }

    private void SetRewardCheckError(
        bool isOrderMusic,
        string message)
    {
        var statusText =
            isOrderMusic
                ? OrderMusicRewardStatusText
                : SkipTrackRewardStatusText;

        statusText.Text =
            message;

        statusText.Foreground =
            FindResource("WarningBrush") as Brush;
    }

    // ============================================================
    // DONATION ALERTS
    // ============================================================

    private void LoadDonationAlertsSettingsUi()
    {
        var settings =
            _appServices.DonationAlertsMusicService.Settings;

        DonationAlertsMusicOrdersCheckBox.IsChecked =
            settings.MusicOrdersEnabled;

        SetDonationThresholdUi(
            settings.DonationThresholds,
            "RUB",
            DonationAlertsRubNormalTextBox,
            DonationAlertsRubSuperTextBox);

        SetDonationThresholdUi(
            settings.DonationThresholds,
            "EUR",
            DonationAlertsEurNormalTextBox,
            DonationAlertsEurSuperTextBox);

        SetDonationThresholdUi(
            settings.DonationThresholds,
            "USD",
            DonationAlertsUsdNormalTextBox,
            DonationAlertsUsdSuperTextBox);

        UpdateDonationAlertsConnectionUi();
    }

    private static void SetDonationThresholdUi(
        IReadOnlyDictionary<string, DonationThresholdSettings> thresholds,
        string currency,
        TextBox normalTextBox,
        TextBox superTextBox)
    {
        if (thresholds.TryGetValue(currency, out var threshold))
        {
            normalTextBox.Text =
                threshold.Normal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

            superTextBox.Text =
                threshold.SuperPriority.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            normalTextBox.Clear();
            superTextBox.Clear();
        }
    }

    private void UpdateDonationAlertsConnectionUi()
    {
        var service =
            _appServices.DonationAlertsMusicService;

        if (service.IsConnected &&
            service.CurrentUser != null)
        {
            DonationAlertsStatusIndicator.Fill =
                FindResource("SuccessBrush") as Brush;

            DonationAlertsStatusText.Text =
                "Подключен";

            DonationAlertsStatusText.Foreground =
                FindResource("SuccessBrush") as Brush;

            DonationAlertsUserText.Text =
                $"Аккаунт: {service.CurrentUser.Name} ({service.CurrentUser.Code})";

            DonationAlertsConnectButton.IsEnabled =
                false;

            DonationAlertsLogoutButton.IsEnabled =
                true;
        }
        else if (service.CurrentUser != null)
        {
            DonationAlertsStatusIndicator.Fill =
                FindResource("AccentBrush") as Brush;

            DonationAlertsStatusText.Text =
                "Авторизован";

            DonationAlertsStatusText.Foreground =
                FindResource("AccentBrush") as Brush;

            DonationAlertsUserText.Text =
                $"Аккаунт: {service.CurrentUser.Name} ({service.CurrentUser.Code})";

            DonationAlertsConnectButton.IsEnabled =
                true;

            DonationAlertsLogoutButton.IsEnabled =
                true;
        }
        else
        {
            DonationAlertsStatusIndicator.Fill =
                FindResource("TextMuted") as Brush;

            DonationAlertsStatusText.Text =
                "Не подключен";

            DonationAlertsStatusText.Foreground =
                FindResource("TextMuted") as Brush;

            DonationAlertsUserText.Text =
                "Авторизация Donation Alerts не выполнена.";

            DonationAlertsConnectButton.IsEnabled =
                true;

            DonationAlertsLogoutButton.IsEnabled =
                false;
        }
    }

    private void DonationAlertsSettingsChanged(
        object sender,
        RoutedEventArgs e)
    {
        // Значение сохраняется общей кнопкой.
    }

    private async void DonationAlertsConnectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            DonationAlertsConnectButton.IsEnabled = false;
            DonationAlertsStatusText.Text = "Ожидание авторизации...";

            await _appServices
                .DonationAlertsMusicService
                .AuthorizeAndConnectAsync();

            UpdateDonationAlertsConnectionUi();
        }
        catch (Exception ex)
        {
            UpdateDonationAlertsConnectionUi();

            AppMessageBox.Show(
                this,
                $"Не удалось подключить Donation Alerts.\n\n{ex.Message}",
                "Donation Alerts",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void DonationAlertsLogoutButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _appServices
                .DonationAlertsMusicService
                .LogoutAsync();

            UpdateDonationAlertsConnectionUi();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(
                this,
                $"Не удалось отключить Donation Alerts.\n\n{ex.Message}",
                "Donation Alerts",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void DonationAlertsSaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var settings =
                _appServices
                    .DonationAlertsMusicService
                    .Settings;

            settings.MusicOrdersEnabled =
                DonationAlertsMusicOrdersCheckBox.IsChecked == true;

            settings.DonationThresholds =
                new Dictionary<string, DonationThresholdSettings>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["RUB"] = ParseDonationThreshold(
                        "RUB",
                        DonationAlertsRubNormalTextBox.Text,
                        DonationAlertsRubSuperTextBox.Text),

                    ["EUR"] = ParseDonationThreshold(
                        "EUR",
                        DonationAlertsEurNormalTextBox.Text,
                        DonationAlertsEurSuperTextBox.Text),

                    ["USD"] = ParseDonationThreshold(
                        "USD",
                        DonationAlertsUsdNormalTextBox.Text,
                        DonationAlertsUsdSuperTextBox.Text)
                };

            await _appServices
                .DonationAlertsMusicService
                .SaveSettingsAsync(settings);

            AppMessageBox.Show(
                this,
                "Настройки Donation Alerts сохранены.",
                "Donation Alerts",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(
                this,
                ex.Message,
                "Donation Alerts",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static DonationThresholdSettings ParseDonationThreshold(
        string currency,
        string normalText,
        string superText)
    {
        if (!TryParseDonationAmount(normalText, out var normal) ||
            !TryParseDonationAmount(superText, out var superPriority))
        {
            throw new InvalidOperationException(
                $"Для {currency} укажите оба порога.");
        }

        if (normal <= 0 ||
            superPriority <= normal)
        {
            throw new InvalidOperationException(
                $"Для {currency} SUPER-PRIORITY должен быть больше обычного порога.");
        }

        return new DonationThresholdSettings
        {
            Normal = normal,
            SuperPriority = superPriority
        };
    }

    private static bool TryParseDonationAmount(
        string text,
        out decimal value)
    {
        return decimal.TryParse(
                   text.Trim(),
                   System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out value)
               ||
               decimal.TryParse(
                   text.Trim(),
                   System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.CurrentCulture,
                   out value);
    }

    // ============================================================
    // WINDOW
    // ============================================================

    private void SettingsWindow_Closed(
        object? sender,
        EventArgs e)
    {
        CloseYandexAuthorizationWindow();
        CloseTwitchAuthorizationWindow();

        _appServices
            .YandexAuthService
            .AuthenticationStateChanged -=
                YandexAuthService_AuthenticationStateChanged;

        _appServices
            .TwitchAuthService
            .AuthenticationStateChanged -=
                TwitchAuthService_AuthenticationStateChanged;

        _appServices
            .TwitchService
            .ConnectionStateChanged -=
                TwitchService_ConnectionStateChanged;

        _appServices
            .DonationAlertsMusicService
            .ConnectionStateChanged -=
                DonationAlertsMusicService_ConnectionStateChanged;

        Closed -=
            SettingsWindow_Closed;
    }
}