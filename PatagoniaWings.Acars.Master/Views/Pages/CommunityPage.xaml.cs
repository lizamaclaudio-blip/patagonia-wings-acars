using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class CommunityPage : UserControl
    {
        public CommunityPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
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

            if (!ReferenceEquals(PageRoot.DataContext, vm))
            {
                PageRoot.DataContext = vm;
            }
        }

        private CommunityViewModel? ResolveViewModel()
        {
            if (PageRoot != null && PageRoot.DataContext is CommunityViewModel pageVm)
            {
                return pageVm;
            }

            if (DataContext is CommunityViewModel vm)
            {
                return vm;
            }

            if (DataContext is MainViewModel main)
            {
                return main.CommunityVM;
            }

            var hostWindow = Window.GetWindow(this);
            if (hostWindow != null && hostWindow.DataContext is MainViewModel windowMain)
            {
                return windowMain.CommunityVM;
            }

            var parentElement = Parent as FrameworkElement;
            if (parentElement != null && parentElement.DataContext is MainViewModel parentMain)
            {
                return parentMain.CommunityVM;
            }

            return null;
        }

        private void TabOnline_Click(object sender, RoutedEventArgs e)
        {
            var vm = ResolveViewModel();
            if (vm == null)
            {
                return;
            }

            vm.SelectedTab = 0;
            vm.LoadOnlinePilots();
        }

        private void TabLeader_Click(object sender, RoutedEventArgs e)
        {
            var vm = ResolveViewModel();
            if (vm == null)
            {
                return;
            }

            vm.SelectedTab = 1;
            vm.LoadLeaderboard();
        }
    }
}
