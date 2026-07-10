using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace InfinLimit
{
    public partial class AuthDialog : Window
    {
        public AuthDialog(string title, string message, string hwid)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            HwidBox.Text = hwid;
        }

        private void OkClick(object sender, RoutedEventArgs e) => Close();

        private void CopyBorder_Click(object sender, MouseButtonEventArgs e)
        {
            Clipboard.SetText(HwidBox.Text);
            CopyLabel.Text = "Copied!";
            CopyLabel.Foreground = (SolidColorBrush)FindResource("AccentColor");
        }

        private void CopyBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            if (CopyLabel.Text != "Copied!")
                CopyLabel.Foreground = (SolidColorBrush)FindResource("TextPrimary");
        }

        private void CopyBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            if (CopyLabel.Text != "Copied!")
                CopyLabel.Foreground = (SolidColorBrush)FindResource("TextSecondary");
        }
    }
}
