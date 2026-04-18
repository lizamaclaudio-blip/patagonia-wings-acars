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

            var parentElement = Parent as FrameworkElement;
            var parentVm = parentElement != null ? parentElement.DataContext as MainViewModel : null;
            if (parentVm != null)
            {
                return parentVm.PreFlightVM;
            }

            return null;
        }
    }
}
