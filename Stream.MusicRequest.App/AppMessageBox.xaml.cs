using System.Windows;
using System.Windows.Media;

namespace Stream.MusicRequest.App;

public partial class AppMessageBox : Window
{
    private AppMessageBox(string message, string title, MessageBoxImage image, Window? owner)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

        if (owner is not null)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        ApplyImageStyle(image);
    }

    public static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton button,
        MessageBoxImage image)
    {
        return Show(null, message, title, button, image);
    }

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title,
        MessageBoxButton button,
        MessageBoxImage image)
    {
        var dialog = new AppMessageBox(message, title, image, owner);
        dialog.ShowDialog();
        return MessageBoxResult.OK;
    }

    private void ApplyImageStyle(MessageBoxImage image)
    {
        switch (image)
        {
            case MessageBoxImage.Error:
                IconText.Text = "×";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 103, 103));
                break;

            case MessageBoxImage.Warning:
                IconText.Text = "!";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 171, 64));
                break;

            case MessageBoxImage.Information:
                IconText.Text = "i";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(105, 190, 255));
                break;

            default:
                IconText.Text = "•";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(210, 216, 224));
                break;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
