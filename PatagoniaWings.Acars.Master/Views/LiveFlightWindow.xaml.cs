#nullable enable
using System.Windows;
using PatagoniaWings.Acars.Master.Helpers;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views
{
    public partial class LiveFlightWindow : Window
    {
        public LiveFlightWindow()
        {
            InitializeComponent();

            // Bind to the same InFlightViewModel that the main window uses
            // by finding the existing MainWindow owner's DataContext
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Owner is MainWindow mw && mw.DataContext is MainViewModel vm)
            {
                DataContext = vm.InFlightVM;
            }
            else
            {
                // Fallback: use Runtime directly for display-only (no active flight)
                DataContext = AcarsContext.Runtime;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}
