using System.Windows;
using System.Windows.Controls;
using PatagoniaWings.Acars.Master.Helpers;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views.Pages
{
    public partial class LoginPage : UserControl
    {
        private bool _loadedCredentials;
        private bool _settingPassword;

        public LoginPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_loadedCredentials)
            {
                return;
            }

            _loadedCredentials = true;

            if (DataContext is not LoginViewModel vm)
            {
                return;
            }

            var remembered = SavedLoginStore.Load();
            if (remembered != null)
            {
                vm.RememberMe = true;
                vm.Username = remembered.Email ?? string.Empty;

                EmailBox.Text = vm.Username;

                _settingPassword = true;
                vm.Password = remembered.Password ?? string.Empty;
                PwdBox.Password = vm.Password;
                _settingPassword = false;
                return;
            }

            var currentPilot = AcarsContext.Auth.CurrentPilot;
            if (currentPilot != null && !string.IsNullOrWhiteSpace(currentPilot.Email))
            {
                vm.Username = currentPilot.Email.Trim();
                EmailBox.Text = vm.Username;
            }
        }

        private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_settingPassword)
            {
                return;
            }

            if (DataContext is LoginViewModel vm)
            {
                vm.Password = PwdBox.Password;
            }
        }
    }
}
