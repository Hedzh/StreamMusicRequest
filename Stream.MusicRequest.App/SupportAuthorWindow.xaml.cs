using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace Stream.MusicRequest.App;

public partial class SupportAuthorWindow : Window
{
    private const string SupportUrl = "https://dalink.to/hedzh";

    public SupportAuthorWindow()
    {
        InitializeComponent();
    }

    private void SupportLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenSupportLink();
        e.Handled = true;
    }

    private void OpenSupportLinkButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSupportLink();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void OpenSupportLink()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = SupportUrl,
            UseShellExecute = true
        });
    }
}
