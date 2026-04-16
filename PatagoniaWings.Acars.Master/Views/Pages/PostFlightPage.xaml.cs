using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class PostFlightPage : UserControl
    {
        public PostFlightPage()
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

        private PostFlightViewModel ResolveViewModel()
        {
            if (PageRoot != null && PageRoot.DataContext is PostFlightViewModel pageVm)
            {
                return pageVm;
            }

            if (DataContext is PostFlightViewModel vm)
            {
                return vm;
            }

            if (DataContext is MainViewModel main)
            {
                return main.PostFlightVM;
            }

            var hostWindow = Window.GetWindow(this);
            if (hostWindow != null && hostWindow.DataContext is MainViewModel windowMain)
            {
                return windowMain.PostFlightVM;
            }

            var parentElement = Parent as FrameworkElement;
            if (parentElement != null && parentElement.DataContext is MainViewModel parentMain)
            {
                return parentMain.PostFlightVM;
            }

            return null;
        }
    }
}
