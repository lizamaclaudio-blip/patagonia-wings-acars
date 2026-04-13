using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.Views;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class InFlightPage : UserControl
    {
        public InFlightPage()
        {
            InitializeComponent();
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            // Establecer DataContext manualmente desde la ventana principal
            MainWindow mainWindow = GetMainWindow();
            if (mainWindow != null && mainWindow.DataContext is ViewModels.MainViewModel vm)
            {
                DataContext = vm.InFlightVM;
            }
        }

        private MainWindow? GetMainWindow()
        {
            return Window.GetWindow(this) as MainWindow;
        }

        private void BtnConnectMsfs_Click(object sender, RoutedEventArgs e)
        {
            MainWindow mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                return;
            }

            mainWindow.ConnectSim(false);
        }

        private void BtnRefreshFlight_Click(object sender, RoutedEventArgs e)
        {
            MainWindow mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                return;
            }

            mainWindow.ConnectSim(true);
        }
    }
}
