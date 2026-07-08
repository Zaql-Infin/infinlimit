using System.Collections.Generic;
using System.Windows;

namespace InfinLimit.Windows
{
    public partial class ScanResultWindow : Window
    {
        public string? Selected { get; private set; }

        public ScanResultWindow(List<string> ips)
        {
            InitializeComponent();
            foreach (var ip in ips)
                IpList.Items.Add(ip);

            if (ips.Count > 0)
                IpList.SelectedIndex = 0;
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            Selected = IpList.SelectedItem as string;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
