using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class PreFlightPage : UserControl
    {
        private bool _dispatchRequested;
        private PreFlightViewModel? _boundVm;

        public PreFlightPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await Dispatcher.BeginInvoke(new System.Action(async () => await EnsureLoadedAsync()), DispatcherPriority.Loaded);
        }

        private async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _dispatchRequested = false;
            await EnsureLoadedAsync();
        }

        private async Task EnsureLoadedAsync()
        {
            var vm = ResolveViewModel();
            if (vm == null)
            {
                return;
            }

            if (!ReferenceEquals(PageRoot.DataContext, vm))
            {
                PageRoot.DataContext = vm;
            }

            if (!ReferenceEquals(_boundVm, vm))
            {
                _boundVm = vm;
                _dispatchRequested = false;
            }

            if (_dispatchRequested)
            {
                return;
            }

            _dispatchRequested = true;
            await vm.LoadPreparedDispatchAsync();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var hostWindow = Window.GetWindow(this);
            if (hostWindow == null)
            {
                return;
            }

            var shellVm = hostWindow.DataContext as AcarsShellViewModel;
            if (shellVm != null && shellVm.GoPilotLoungeCommand != null && shellVm.GoPilotLoungeCommand.CanExecute(null))
            {
                shellVm.GoPilotLoungeCommand.Execute(null);
                return;
            }

            var mainVm = hostWindow.DataContext as MainViewModel;
            if (mainVm != null && mainVm.NavDashboardCommand != null && mainVm.NavDashboardCommand.CanExecute(null))
            {
                mainVm.NavDashboardCommand.Execute(null);
            }
        }

        private PreFlightViewModel? ResolveViewModel()
        {
            var vm = PageRoot != null ? PageRoot.DataContext as PreFlightViewModel : null;
            if (vm != null)
            {
                return vm;
            }

            vm = DataContext as PreFlightViewModel;
            if (vm != null)
            {
                return vm;
            }

            var mainVm = DataContext as MainViewModel;
            if (mainVm != null)
            {
                return mainVm.PreFlightVM;
            }

            var hostWindow = Window.GetWindow(this);
            var windowVm = hostWindow != null ? hostWindow.DataContext as MainViewModel : null;
            if (windowVm != null)
            {
                return windowVm.PreFlightVM;
            }

            var shellVm = hostWindow != null ? hostWindow.DataContext as AcarsShellViewModel : null;
            if (shellVm != null)
            {
                return shellVm.MainVM.PreFlightVM;
            }

            var parentElement = Parent as FrameworkElement;
            var parentVm = parentElement != null ? parentElement.DataContext as MainViewModel : null;
            if (parentVm != null)
            {
                return parentVm.PreFlightVM;
            }

            var parentShellVm = parentElement != null ? parentElement.DataContext as AcarsShellViewModel : null;
            if (parentShellVm != null)
            {
                return parentShellVm.MainVM.PreFlightVM;
            }

            return null;
        }
    }
}
