using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Stream.MusicRequest.Twitch.Models;

namespace Stream.MusicRequest.App;

public partial class SettingsWindow : Window
{
    private readonly AppServices _appServices;

    private readonly StackPanel[] _pages;

    private YandexAuthorizationWindow? _yandexAuthorizationWindow;

    private DeviceAuthorizationWindow? _twitchAuthorizationWindow;

    private bool _isInitialized;

    public SettingsWindow(
        AppServices appServices)
    {
        InitializeComponent();

        _appServices =
            appServices
            ?? throw new ArgumentNullException(
                nameof(appServices));

        _pages =
        [
            GeneralPage,
            MusicSourcesPage,
            TwitchPage,
            TwitchRewardsPage,
            DonationAlertsPage,
            PlaybackPage,
            ObsPage
        ];

        _appServices
            .TwitchAuthService
            .AuthenticationStateChanged +=
                TwitchAuthService_AuthenticationStateChanged;

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

        _ = UpdateTwitchUiAsync();

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

            MessageBox.Show(
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
            MessageBox.Show(
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

            MessageBox.Show(
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
            MessageBox.Show(
                this,
                $"Не удалось выйти из Twitch.\n\n{ex.Message}",
                "Twitch",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            await UpdateTwitchUiAsync();
        }
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

        Closed -=
            SettingsWindow_Closed;
    }
}