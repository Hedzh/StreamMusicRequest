using System.Diagnostics;
using System.Windows;

namespace Stream.MusicRequest.App;

public partial class DeviceAuthorizationWindow : Window
{
    public DeviceAuthorizationWindow(
        string title,
        string verificationUrl,
        string userCode)
    {
        InitializeComponent();

        Title = title;
        TitleTextBlock.Text = title;

        VerificationUrlTextBox.Text =
            verificationUrl;

        UserCodeTextBox.Text =
            userCode;
    }

    private void OpenBrowserButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        VerificationUrlTextBox.Text,

                    UseShellExecute = true
                });

            StatusTextBlock.Text =
                "Страница открыта в браузере.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text =
                "Не удалось открыть браузер.";

            MessageBox.Show(
                $"Не удалось открыть страницу авторизации.\n\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CopyUrlButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Clipboard.SetText(
            VerificationUrlTextBox.Text);

        StatusTextBlock.Text =
            "Ссылка скопирована.";
    }

    private void CopyCodeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Clipboard.SetText(
            UserCodeTextBox.Text);

        StatusTextBlock.Text =
            "Код скопирован.";
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}