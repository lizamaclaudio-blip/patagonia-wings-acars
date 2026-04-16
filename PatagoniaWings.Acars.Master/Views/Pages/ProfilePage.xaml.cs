using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class ProfilePage : UserControl
    {
        private bool _initialProfileRequested;
        private ProfileViewModel _boundVm;

        public ProfilePage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            EnsureProfileLoaded();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _initialProfileRequested = false;
            EnsureProfileLoaded();
        }

        private void EnsureProfileLoaded()
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
                _initialProfileRequested = false;
            }

            if (_initialProfileRequested)
            {
                return;
            }

            _initialProfileRequested = true;
            vm.LoadAsync();
        }

        private ProfileViewModel ResolveViewModel()
        {
            if (PageRoot != null && PageRoot.DataContext is ProfileViewModel pageVm)
            {
                return pageVm;
            }

            if (DataContext is ProfileViewModel vm)
            {
                return vm;
            }

            if (DataContext is MainViewModel main)
            {
                return main.ProfileVM;
            }

            var hostWindow = Window.GetWindow(this);
            if (hostWindow != null && hostWindow.DataContext is MainViewModel windowMain)
            {
                return windowMain.ProfileVM;
            }

            var parentElement = Parent as FrameworkElement;
            if (parentElement != null && parentElement.DataContext is MainViewModel parentMain)
            {
                return parentMain.ProfileVM;
            }

            return null;
        }
    }
}
