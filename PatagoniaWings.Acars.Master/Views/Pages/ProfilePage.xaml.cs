using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class ProfilePage : UserControl
    {
        public ProfilePage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProfileViewModel vm)
            {
                vm.LoadAsync();
            }
        }
    }
}
