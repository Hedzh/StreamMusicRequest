using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Stream.MusicRequest.App;

public partial class AppMessageBox : Window
{
    private readonly MessageBoxButton _buttonType;

    private AppMessageBox(
        string message,
        string title,
        MessageBoxButton button,
        MessageBoxImage image,
        Window? owner)
    {
        InitializeComponent();

        _buttonType = button;

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

        if (owner is not null)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        ConfigureButtons(button);
        ApplyImageStyle(image);
    }

    public static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton button,
        MessageBoxImage image)
    {
        var owner =
            Application.Current?
                .Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive);

        if (owner is null)
        {
            owner =
                Application.Current?
                    .Windows
                    .OfType<Window>()
                    .FirstOrDefault(window => window.IsVisible);
        }

        return Show(owner, message, title, button, image);
    }

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title,
        MessageBoxButton button,
        MessageBoxImage image)
    {
        var dialog =
            new AppMessageBox(
                message,
                title,
                button,
                image,
                owner);

        dialog.ShowDialog();

        return dialog.GetResult();
    }

    private void ConfigureButtons(MessageBoxButton button)
    {
        OkButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        YesButton.Visibility = Visibility.Collapsed;
        NoButton.Visibility = Visibility.Collapsed;

        switch (button)
        {
            case MessageBoxButton.OK:
                OkButton.Visibility = Visibility.Visible;
                OkButton.IsDefault = true;
                break;

            case MessageBoxButton.OKCancel:
                OkButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                OkButton.IsDefault = true;
                CancelButton.IsCancel = true;
                break;

            case MessageBoxButton.YesNo:
                YesButton.Visibility = Visibility.Visible;
                NoButton.Visibility = Visibility.Visible;
                YesButton.IsDefault = true;
                break;

            case MessageBoxButton.YesNoCancel:
                YesButton.Visibility = Visibility.Visible;
                NoButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                YesButton.IsDefault = true;
                CancelButton.IsCancel = true;
                break;

            default:
                OkButton.Visibility = Visibility.Visible;
                OkButton.IsDefault = true;
                break;
        }
    }

    private MessageBoxResult GetResult()
    {
        return DialogResult switch
        {
            true when YesButton.Visibility == Visibility.Visible
                && YesButton.IsDefault => MessageBoxResult.Yes,

            false when CancelButton.Visibility == Visibility.Visible
                && CancelButton.IsCancel => MessageBoxResult.Cancel,

            false when NoButton.Visibility == Visibility.Visible
                && YesButton.Visibility == Visibility.Visible => MessageBoxResult.No,

            true when OkButton.Visibility == Visibility.Visible => MessageBoxResult.OK,

            _ => GetFallbackResult()
        };
    }

    private MessageBoxResult GetFallbackResult()
    {
        if (YesButton.Visibility == Visibility.Visible)
        {
            return MessageBoxResult.Yes;
        }

        if (CancelButton.Visibility == Visibility.Visible)
        {
            return MessageBoxResult.Cancel;
        }

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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
