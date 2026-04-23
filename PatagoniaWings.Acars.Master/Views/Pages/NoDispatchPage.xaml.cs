using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class NoDispatchPage : UserControl
    {
        public NoDispatchPage()
        {
            InitializeComponent();
        }

        private void ReserveButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteShellCommand(vm => vm.OpenPatagoniaPortalCommand);
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteShellCommand(vm => vm.RetryDispatchResolutionCommand);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteShellCommand(vm => vm.GoPilotLoungeCommand);
        }

        private void ExecuteShellCommand(System.Func<AcarsShellViewModel, ICommand> selector)
        {
            var shellVm = ResolveShellVm();
            if (shellVm == null)
            {
                return;
            }

            var command = selector(shellVm);
            if (command != null && command.CanExecute(null))
            {
                command.Execute(null);
            }
        }

        private AcarsShellViewModel? ResolveShellVm()
        {
            return DataContext as AcarsShellViewModel
                   ?? Window.GetWindow(this)?.DataContext as AcarsShellViewModel;
        }
    }
}
