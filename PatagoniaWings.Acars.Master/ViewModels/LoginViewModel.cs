using System;
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

        public string Username { get => _username; set => SetField(ref _username, value); }
        public string Password { get => _password; set => SetField(ref _password, value); }
        public string ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
        public bool RememberMe { get => _rememberMe; set => SetField(ref _rememberMe, value); }

        public ICommand LoginCommand { get; }
        public Action? OnLoginSuccess { get; set; }

        public LoginViewModel()
        {
            LoginCommand = new RelayCommand(
                async _ =>
                {
                    if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                    {
                        ErrorMessage = "Ingresa tu correo de Supabase o tu callsign, junto con la contraseña.";
                        return;
                    }

                    IsLoading = true;
                    ErrorMessage = string.Empty;

                    try
                    {
                        var result = await AcarsContext.Api.LoginAsync(Username, Password);
                        if (result.Success && result.Data != null)
                        {
                            var pilot = result.Data.Pilot;
                            pilot.Token = result.Data.Token;
                            AcarsContext.Api.SetAuthToken(pilot.Token);
                            if (RememberMe)
                                AcarsContext.Auth.SaveSession(pilot);

                            AcarsContext.Sound.PlayDing();
                            Application.Current.Dispatcher.Invoke(() => OnLoginSuccess?.Invoke());
                        }
                        else
                        {
                            var error = result.Error ?? string.Empty;
                            if (error.IndexOf("invalid login credentials", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                ErrorMessage = "Correo/callsign o contraseña incorrectos.";
                            }
                            else
                            {
                                ErrorMessage = error.StartsWith("No pude resolver", StringComparison.OrdinalIgnoreCase)
                                    ? error + " Si quieres entrar altiro, usa tu correo de acceso en vez del callsign."
                                    : $"Error de conexión: {error}";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Error inesperado: {ex.Message}";
                    }
                    finally
                    {
                        IsLoading = false;
                    }
                });
        }
    }
}
