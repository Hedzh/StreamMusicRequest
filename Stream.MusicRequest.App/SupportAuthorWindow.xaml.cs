using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Stream.MusicRequest.App;

public partial class SupportAuthorWindow : Window
{
    private const string SupportUrl = "https://dalink.to/hedzh";

    public SupportAuthorWindow()
    {
        InitializeComponent();
        BuildQrCode();
    }

    private void BuildQrCode()
    {
        const string qr = "1111111010110010101111111|1000001001110110101000001|1011101010101100001011101|1011101001010111001011101|1011101000011010001011101|1000001010110000001000001|1111111010101010101111111|0000000000001000100000000|1010001101011001000100101|0011110001110001101101011|1011111010000101111111101|1111010010111011110101000|1101001011011000101100001|0010110011101001001100011|1110111111101101000001101|0011010011011001000111000|1100011001100000111110010|0000000010000110100010001|1111111011010010101010001|1000001000000001100010011|1011101000111001111110001|1011101001001100110010110|1011101010101110010111011|1000001001111000101110000|1111111011101110101001001";
        var rows = qr.Split('|');
        const double moduleSize = 10.4;

        QrCodeCanvas.Children.Clear();

        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                if (rows[y][x] != '1')
                    continue;

                var module = new Rectangle
                {
                    Width = moduleSize,
                    Height = moduleSize,
                    Fill = Brushes.Black
                };

                Canvas.SetLeft(module, x * moduleSize);
                Canvas.SetTop(module, y * moduleSize);
                QrCodeCanvas.Children.Add(module);
            }
        }
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
