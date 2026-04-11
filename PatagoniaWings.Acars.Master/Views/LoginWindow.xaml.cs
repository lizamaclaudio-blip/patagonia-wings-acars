using System.Windows;
using PatagoniaWings.Acars.Master.Helpers;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _vm;

        public LoginWindow()
        {
            InitializeComponent();
            _vm = (LoginViewModel)DataContext;
            _vm.OnLoginSuccess = OpenMain;

            // Si hay sesión guardada, abrir directo
            if (AcarsContext.Auth.IsLoggedIn)
                OpenMain();
        }

        private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _vm.Password = PwdBox.Password;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // Arrastre de ventana sin borde
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }

        private void OpenMain()
        {
            var main = new MainWindow();
            main.Show();
            Close();
        }
    }
}
