using System.Diagnostics;
using System.Windows;

namespace Stream.MusicRequest.App;

public partial class YandexAuthorizationWindow : Window
{
    private readonly string _verificationUrl;

    public YandexAuthorizationWindow(
        string verificationUrl,
        string userCode)
    {
        InitializeComponent();

        _verificationUrl =
            verificationUrl;

        VerificationUrlText.Text =
            verificationUrl;

        UserCodeText.Text =
            userCode;
    }

    private void OpenVerificationUrlButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _verificationUrl))
        {
            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = _verificationUrl,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось открыть страницу авторизации.\n\n{ex.Message}",
                "Yandex Music",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}