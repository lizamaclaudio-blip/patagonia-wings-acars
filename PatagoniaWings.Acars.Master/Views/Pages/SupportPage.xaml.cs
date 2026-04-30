using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.ViewModels;
using PatagoniaWings.Acars.Master.Views;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class SupportPage : UserControl
    {
        public SupportPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is SupportViewModel)
            {
                return;
            }

            var window = Window.GetWindow(this) as MainWindow;
            var shell = window?.DataContext as AcarsShellViewModel;
            if (shell?.SupportVM != null)
            {
                DataContext = shell.SupportVM;
            }
        }
    }
}
