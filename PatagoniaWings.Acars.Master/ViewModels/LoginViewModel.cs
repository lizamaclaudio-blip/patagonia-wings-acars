using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isLoading;
        private bool _rememberMe = true;
        private const string SavedUsernameFile = "saved_username.txt";

        public string Username { get => _username; set => SetField(ref _username, value); }
        public string Password { get => _password; set => SetField(ref _password, value); }
        public string ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
        public bool RememberMe { get => _rememberMe; set => SetField(ref _rememberMe, value); }

        /// <summary>
        /// Badge de versión dinámico: lee la versión real del App.config en tiempo de ejecución.
        /// Siempre refleja la versión instalada actual sin necesidad de recompilar.
        /// Ejemplo: "v3.1.2  ·  SimConnect + FSUIPC7"
        /// </summary>
        public string VersionBadge => $"v{UpdateService.CurrentVersion} | SimConnect + FSUIPC7 + Supabase";
        public string FooterVersion => $"Patagonia Wings Virtual Airline | ACARS {UpdateService.CurrentVersion} | Web 2.0";

        public ICommand LoginCommand { get; }
        public Action? OnLoginSuccess { get; set; }

        public LoginViewModel()
        {
            LoadSavedUsername();
            LoginCommand = new AsyncRelayCommand(
                async _ =>
                {
                    if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                    {
                        ErrorMessage = "Ingresa tu correo de Patagonia Wings junto con la contraseña.";
                        return;
                    }

                    IsLoading = true;
                    ErrorMessage = string.Empty;
                    WriteAuthLog("LoginViewModel.ExecuteLoginAsync started. UserInput=" + Username.Trim());

                    try
                    {
                        var result = await AcarsContext.Api.LoginAsync(Username, Password);
                        WriteAuthLog("LoginAsync finished. Success=" + result.Success);

                        if (result.Success && result.Data != null && result.Data.Pilot != null)
                        {
                            var pilot = result.Data.Pilot;
                            var token = (result.Data.Token ?? string.Empty).Trim();

                            if (string.IsNullOrWhiteSpace(token))
                            {
                                token = (pilot.Token ?? string.Empty).Trim();
                                WriteAuthLog("LoginResponse.Token vacío. Fallback a Pilot.Token => " +
                                             (string.IsNullOrWhiteSpace(token) ? "VACIO" : "OK"));
                            }

                            if (string.IsNullOrWhiteSpace(token))
                            {
                                ErrorMessage = "Supabase autenticó, pero la respuesta del login no trajo token usable. Revisa auth.log";
                                WriteAuthLog("Login failed after success: both LoginResponse.Token and Pilot.Token were empty.");
                                return;
                            }

                            pilot.Token = token;
                            AcarsContext.Api.SetAuthToken(pilot.Token);
                            AcarsContext.Auth.SetCurrentPilot(pilot);
                            AcarsContext.Runtime.SetCurrentPilot(pilot);
                            AcarsContext.TriggerPendingCloseoutRetry("login_success", 500);

                            if (RememberMe)
                            {
                                AcarsContext.Auth.SaveSession(pilot);
                                SaveUsername(Username.Trim());
                                WriteAuthLog("Session saved.");
                            }
                            else
                            {
                                AcarsContext.Auth.ClearSavedSession();
                                ClearSavedUsername();
                                WriteAuthLog("Session kept in memory only.");
                            }

                            AcarsContext.Sound.PlayDing();
                            WriteAuthLog("Login success. Opening main window.");
                            Application.Current.Dispatcher.Invoke(() => OnLoginSuccess?.Invoke());
                        }
                        else
                        {
                            var error = (result.Error ?? string.Empty).Trim();

                            if (error.IndexOf("invalid login credentials", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                ErrorMessage = "Correo o contraseña incorrectos.";
                            }
                            else if (string.IsNullOrWhiteSpace(error))
                            {
                                ErrorMessage = "Error de conexión. Revisa el log de autenticación en " + GetResolvedLogPath();
                            }
                            else
                            {
                                ErrorMessage = error.StartsWith("No pude resolver", StringComparison.OrdinalIgnoreCase)
                                    ? error + " Verifica tu correo y contraseña."
                                    : "Error de conexión: " + error;
                            }

                            WriteAuthLog("LoginAsync returned failure. Error=" + error);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = "Error inesperado: " + ex.Message;
                        WriteAuthLog("Unexpected exception: " + ex);
                    }
                    finally
                    {
                        IsLoading = false;
                    }
                });
        }

        private static void WriteAuthLog(string message)
        {
            try
            {
                var path = GetResolvedLogPath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(
                    path,
                    "[" + DateTime.UtcNow.ToString("o") + "] " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static string GetResolvedLogPath()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "PatagoniaWings", "Acars", "logs", "auth.log");
            }
            catch
            {
                return "auth.log";
            }
        }

        private void LoadSavedUsername()
        {
            try
            {
                var path = GetSavedUsernamePath();
                if (File.Exists(path))
                {
                    var saved = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(saved))
                    {
                        Username = saved;
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        private void SaveUsername(string username)
        {
            try
            {
                var path = GetSavedUsernamePath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, username);
            }
            catch
            {
                // Ignore errors
            }
        }

        private void ClearSavedUsername()
        {
            try
            {
                var path = GetSavedUsernamePath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        private static string GetSavedUsernamePath()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "PatagoniaWings", "Acars", SavedUsernameFile);
            }
            catch
            {
                return SavedUsernameFile;
            }
        }
    }
}
