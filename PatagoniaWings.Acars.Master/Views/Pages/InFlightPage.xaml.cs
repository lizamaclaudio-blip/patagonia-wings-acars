using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.Views;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class InFlightPage : UserControl
    {
        public InFlightPage() => InitializeComponent();

        private MainWindow? GetMainWindow() =>
            Window.GetWindow(this) as MainWindow;

        private void BtnConnectMsfs_Click(object sender, RoutedEventArgs e)
            => GetMainWindow()?.ConnectMsfs();

        private void BtnConnectXPlane_Click(object sender, RoutedEventArgs e)
            => GetMainWindow()?.ConnectXPlane();
    }
}
