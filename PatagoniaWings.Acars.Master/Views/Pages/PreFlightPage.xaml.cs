using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class PreFlightPage : UserControl
    {
        public PreFlightPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is PreFlightViewModel vm)
            {
                await vm.LoadPreparedDispatchAsync();
            }
        }
    }
}
