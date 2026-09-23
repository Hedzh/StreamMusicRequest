using System.Windows;
using System.Windows.Controls;

namespace Stream.MusicRequest.App;

public partial class SettingsWindow : Window
{
    private readonly AppServices _appServices;

    private readonly StackPanel[] _pages;

    private YandexAuthorizationWindow? _yandexAuthorizationWindow;

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

        _isInitialized = true;

        ShowPage(
            NavigationList.SelectedIndex >= 0
                ? NavigationList.SelectedIndex
                : 0);

        UpdateYandexUi();

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
                as System.Windows.Media.Brush;

            YandexStatusText.Text =
                "Авторизован";

            YandexStatusText.Foreground =
                FindResource("SuccessBrush")
                as System.Windows.Media.Brush;

            YandexAuthorizeButton.IsEnabled =
                false;

            YandexLogoutButton.IsEnabled =
                true;
        }
        else
        {
            YandexStatusIndicator.Fill =
                FindResource("TextMuted")
                as System.Windows.Media.Brush;

            YandexStatusText.Text =
                "Не авторизован";

            YandexStatusText.Foreground =
                FindResource("TextMuted")
                as System.Windows.Media.Brush;

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
                as System.Windows.Media.Brush;

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

    private void SettingsWindow_Closed(
        object? sender,
        EventArgs e)
    {
        CloseYandexAuthorizationWindow();

        Closed -=
            SettingsWindow_Closed;
    }
}