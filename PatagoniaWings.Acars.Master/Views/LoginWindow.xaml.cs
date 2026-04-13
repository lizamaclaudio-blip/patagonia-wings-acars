using System;
using System.Windows;
using PatagoniaWings.Acars.Master.Helpers;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _vm;
        private bool _updateCheckStarted;

        public LoginWindow()
        {
            InitializeComponent();
            _vm = (LoginViewModel)DataContext;
            _vm.OnLoginSuccess = OpenMain;

            // Verificar actualizaciones al iniciar (antes de login)
            Loaded += OnLoaded;

            if (AcarsContext.Auth.CurrentPilot != null)
            {
                _vm.Username = string.IsNullOrWhiteSpace(AcarsContext.Auth.CurrentPilot.CallSign)
                    ? AcarsContext.Auth.CurrentPilot.Email
                    : AcarsContext.Auth.CurrentPilot.CallSign;
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_updateCheckStarted)
            {
                return;
            }

            _updateCheckStarted = true;
            await CheckForUpdatesAsync();
        }

        private async System.Threading.Tasks.Task CheckForUpdatesAsync()
        {
            try
            {
                await UpdateService.NotifyIfUpdateAvailableAsync(this);
            }
            catch
            {
            }
        }

        private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _vm.Password = PwdBox.Password;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }

        private void OpenMain()
        {
            try
            {
                var main = new MainWindow();
                main.Show();
                Close();
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException != null
                    ? $"{ex.GetType().Name}: {ex.Message}\n\nInnerException:\n{ex.InnerException.GetType().Name}: {ex.InnerException.Message}"
                    : $"{ex.GetType().Name}: {ex.Message}";

                MessageBox.Show(
                    $"No pude abrir la ventana principal.\n\n{detail}",
                    "Patagonia Wings ACARS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}