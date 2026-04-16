using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PatagoniaWings.Acars.Master.Helpers;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views
{
    public partial class LoginWindow : Window
    {
        private LoginViewModel _vm;
        private bool _updateCheckStarted;
        private bool _settingSavedPassword;

        public LoginWindow()
        {
            InitializeComponent();

            _vm = DataContext as LoginViewModel ?? new LoginViewModel();
            if (!ReferenceEquals(DataContext, _vm))
            {
                DataContext = _vm;
            }

            _vm.OnLoginSuccess = HandleLoginSuccess;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadRememberedCredentials();

            if (!_updateCheckStarted)
            {
                _updateCheckStarted = true;
                await CheckForUpdatesAsync();
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                await UpdateService.NotifyIfUpdateAvailableAsync(this);
            }
            catch
            {
            }
        }

        private void LoadRememberedCredentials()
        {
            try
            {
                var remembered = LoginRememberStore.Load();
                if (remembered != null)
                {
                    if (ChkRemember != null)
                    {
                        ChkRemember.IsChecked = true;
                    }

                    if (EmailBox != null)
                    {
                        EmailBox.Text = remembered.Email ?? string.Empty;
                    }

                    _settingSavedPassword = true;
                    if (PwdBox != null)
                    {
                        PwdBox.Password = remembered.Password ?? string.Empty;
                    }
                    _settingSavedPassword = false;

                    if (_vm != null)
                    {
                        _vm.Username = EmailBox != null ? (EmailBox.Text ?? string.Empty).Trim() : string.Empty;
                        _vm.Password = PwdBox != null ? (PwdBox.Password ?? string.Empty) : string.Empty;
                    }
                    return;
                }
            }
            catch
            {
            }

            var currentPilot = AcarsContext.Auth.CurrentPilot;
            var rememberedEmail = currentPilot != null ? (currentPilot.Email ?? string.Empty).Trim() : string.Empty;
            if (!string.IsNullOrWhiteSpace(rememberedEmail) && rememberedEmail.Contains("@"))
            {
                if (EmailBox != null)
                {
                    EmailBox.Text = rememberedEmail;
                }

                if (_vm != null)
                {
                    _vm.Username = rememberedEmail;
                }
            }
        }

        private void EmailBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_vm == null || EmailBox == null)
            {
                return;
            }

            _vm.Username = (EmailBox.Text ?? string.Empty).Trim();

            if (TxtLoginStatus != null)
            {
                TxtLoginStatus.Text = string.Empty;
            }
        }

        private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_vm == null || PwdBox == null)
            {
                return;
            }

            _vm.Password = PwdBox.Password;

            if (!_settingSavedPassword && TxtLoginStatus != null)
            {
                TxtLoginStatus.Text = string.Empty;
            }
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null)
            {
                return;
            }

            if (TxtLoginStatus != null)
            {
                TxtLoginStatus.Text = string.Empty;
            }

            _vm.Username = EmailBox != null ? (EmailBox.Text ?? string.Empty).Trim() : string.Empty;
            _vm.Password = PwdBox != null ? (PwdBox.Password ?? string.Empty) : string.Empty;

            try
            {
                var commandProp = _vm.GetType().GetProperty("LoginCommand", BindingFlags.Public | BindingFlags.Instance);
                var command = commandProp != null ? commandProp.GetValue(_vm, null) as System.Windows.Input.ICommand : null;

                if (command != null && command.CanExecute(null))
                {
                    command.Execute(null);
                    return;
                }

                var loginMethod = _vm.GetType().GetMethod("LoginAsync", BindingFlags.Public | BindingFlags.Instance);
                if (loginMethod != null)
                {
                    var task = loginMethod.Invoke(_vm, null) as Task;
                    if (task != null)
                    {
                        await task;
                        return;
                    }
                }

                if (TxtLoginStatus != null)
                {
                    TxtLoginStatus.Text = "No encontré el flujo de login del ViewModel. Revisa LoginViewModel.";
                }
            }
            catch (Exception ex)
            {
                if (TxtLoginStatus != null)
                {
                    TxtLoginStatus.Text = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                }
            }
        }

        private void HandleLoginSuccess()
        {
            try
            {
                if (ChkRemember != null && ChkRemember.IsChecked == true)
                {
                    LoginRememberStore.Save(
                        EmailBox != null ? (EmailBox.Text ?? string.Empty).Trim() : string.Empty,
                        PwdBox != null ? (PwdBox.Password ?? string.Empty) : string.Empty);
                }
                else
                {
                    LoginRememberStore.Clear();
                }
            }
            catch
            {
            }

            OpenMain();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (ShouldIgnoreWindowDrag(e.OriginalSource as DependencyObject))
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
            }
        }

        private static bool ShouldIgnoreWindowDrag(DependencyObject source)
        {
            while (source != null)
            {
                if (source is TextBoxBase ||
                    source is PasswordBox ||
                    source is ButtonBase ||
                    source is CheckBox ||
                    source is ComboBox)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
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
                    ? string.Format("{0}: {1}\n\nInnerException:\n{2}: {3}",
                        ex.GetType().Name, ex.Message, ex.InnerException.GetType().Name, ex.InnerException.Message)
                    : string.Format("{0}: {1}", ex.GetType().Name, ex.Message);

                MessageBox.Show(
                    string.Format("No pude abrir la ventana principal.\n\n{0}", detail),
                    "Patagonia Wings ACARS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private sealed class LoginRememberStore
        {
            public sealed class Payload
            {
                public string Email { get; set; } = string.Empty;
                public string Password { get; set; } = string.Empty;
            }

            private static string FolderPath
            {
                get
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    return Path.Combine(appData, "PatagoniaWings", "Acars", "auth");
                }
            }

            private static string FilePath
            {
                get { return Path.Combine(FolderPath, "remembered-login.dat"); }
            }

            public static void Save(string email, string password)
            {
                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    Clear();
                    return;
                }

                Directory.CreateDirectory(FolderPath);

                var raw = string.Format("{0}\n{1}", email.Trim(), password);
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                File.WriteAllText(FilePath, encoded, Encoding.UTF8);
            }

            public static Payload Load()
            {
                if (!File.Exists(FilePath))
                {
                    return null;
                }

                var encoded = File.ReadAllText(FilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(encoded))
                {
                    return null;
                }

                var plainBytes = Convert.FromBase64String(encoded.Trim());
                var raw = Encoding.UTF8.GetString(plainBytes);
                var parts = raw.Split(new[] { '\n' }, 2);

                return new Payload
                {
                    Email = parts.Length > 0 ? parts[0] : string.Empty,
                    Password = parts.Length > 1 ? parts[1] : string.Empty
                };
            }

            public static void Clear()
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
        }
    }
}
