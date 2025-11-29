using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace CAT_wpf_app
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
        }

        private void LinkedIn_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://www.linkedin.com/in/mazeas/") { UseShellExecute = true });
        }

        private void GitHub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/mazeasdamien/CAT_project") { UseShellExecute = true });
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
