using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.ViewModels;
using PatagoniaWings.Acars.Master.Views;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class InFlightPage : UserControl
    {
        public InFlightPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            BindViewModel();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BindViewModel();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            BindViewModel();
        }

        private void BindViewModel()
        {
            var vm = ResolveViewModel();
            if (vm == null)
            {
                return;
            }

            if (PageRoot != null && !ReferenceEquals(PageRoot.DataContext, vm))
            {
                PageRoot.DataContext = vm;
            }
        }

        private InFlightViewModel? ResolveViewModel()
        {
            if (PageRoot != null && PageRoot.DataContext is InFlightViewModel pageVm)
            {
                return pageVm;
            }

            if (DataContext is InFlightViewModel vm)
            {
                return vm;
            }

            if (DataContext is MainViewModel main)
            {
                return main.InFlightVM;
            }

            var hostWindow = Window.GetWindow(this);
            if (hostWindow != null)
            {
                if (hostWindow.DataContext is MainViewModel windowMain)
                {
                    return windowMain.InFlightVM;
                }

                if (hostWindow is MainWindow mw && mw.DataContext is MainViewModel mainWindowVm)
                {
                    return mainWindowVm.InFlightVM;
                }
            }

            var parentElement = Parent as FrameworkElement;
            if (parentElement != null && parentElement.DataContext is MainViewModel parentMain)
            {
                return parentMain.InFlightVM;
            }

            return null;
        }

        private MainWindow? GetMainWindow()
        {
            return Window.GetWindow(this) as MainWindow;
        }

        private void BtnConnectMsfs_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                return;
            }

            mainWindow.ConnectSim(false);
        }

        private void BtnRefreshFlight_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                return;
            }

            mainWindow.ConnectSim(true);
        }
    }
}
