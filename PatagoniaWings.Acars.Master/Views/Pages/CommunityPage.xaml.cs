using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class CommunityPage : UserControl
    {
        public CommunityPage() => InitializeComponent();

        private void TabOnline_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is CommunityViewModel vm)
            {
                vm.SelectedTab = 0;
                vm.LoadOnlinePilots();
            }
        }

        private void TabLeader_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is CommunityViewModel vm)
            {
                vm.SelectedTab = 1;
                vm.LoadLeaderboard();
            }
        }
    }
}
