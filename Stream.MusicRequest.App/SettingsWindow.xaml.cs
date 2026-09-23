using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Stream.MusicRequest.Yandex.Services;

namespace Stream.MusicRequest.App;

public partial class SettingsWindow : Window
{
    private readonly AppServices _appServices;

    private readonly StackPanel[] _pages;

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
                                OpenYandexAuthorization(
                                    verificationUrl,
                                    userCode);
                            });
                    });

            UpdateYandexUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось авторизоваться в Yandex Music.\n\n{ex.Message}",
                "Yandex Music",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            UpdateYandexUi();
        }
    }

    private void OpenYandexAuthorization(
        string verificationUrl,
        string userCode)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(
                    verificationUrl))
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = verificationUrl,
                        UseShellExecute = true
                    });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось автоматически открыть страницу авторизации.\n\n" +
                $"Открой ссылку вручную:\n\n{verificationUrl}\n\n" +
                $"Ошибка: {ex.Message}",
                "Yandex Music",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        MessageBox.Show(
            this,
            $"Открыл страницу авторизации Yandex Music в браузере.\n\n" +
            $"Введите код:\n\n" +
            $"{userCode}\n\n" +
            $"После подтверждения авторизации дождитесь завершения процесса.",
            "Yandex Music — авторизация",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
}