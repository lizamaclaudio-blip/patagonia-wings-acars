using System;
using System.Threading.Tasks;
using System.Windows;
using PatagoniaWings.Acars.Master.Helpers;
using PatagoniaWings.Acars.Master.Views;

namespace PatagoniaWings.Acars.Master
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            LoadGlobalStyles();
            base.OnStartup(e);
            AcarsContext.Initialize();

            if (await RunStartupUpdateFlowAsync().ConfigureAwait(true))
            {
                return;
            }

            ShowMainShell();
        }

        private async Task<bool> RunStartupUpdateFlowAsync()
        {
            try
            {
                var check = await UpdateService.CheckForUpdatesAsync(true).ConfigureAwait(true);
                if (!check.Success || !check.IsUpdateAvailable)
                {
                    return false;
                }

                var updateWindow = new UpdateWindow(check);
                updateWindow.ShowDialog();
                return UpdateService.IsInstallerTakingControl;
            }
            catch
            {
                return false;
            }
        }

        private void ShowMainShell()
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

            // Senal para el relanzado post-update: la UI principal ya quedo operativa.
            UpdateService.NotifyStartupComplete();
            AcarsContext.ScheduleStartupBackgroundWork();
        }

        private void LoadGlobalStyles()
        {
            var candidates = new[]
            {
                "pack://application:,,,/PatagoniaWings.Acars.Master;component/Resources/Styles/AppStyles.xaml",
                "pack://application:,,,/Resources/Styles/AppStyles.xaml",
                "/PatagoniaWings.Acars.Master;component/Resources/Styles/AppStyles.xaml",
                "/Resources/Styles/AppStyles.xaml"
            };

            Exception? lastError = null;

            foreach (var candidate in candidates)
            {
                try
                {
                    Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri(candidate, UriKind.RelativeOrAbsolute)
                    });
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            MessageBox.Show(
                "No se pudo cargar Resources/Styles/AppStyles.xaml.\n\n" +
                "Revisa que AppStyles.xaml exista y tenga Build Action = Page.\n\n" +
                (lastError != null ? lastError.Message : "Sin detalle adicional."),
                "Patagonia Wings ACARS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var reservationId = AcarsContext.Api != null && AcarsContext.Api.ActiveDispatch != null
                    ? AcarsContext.Api.ActiveDispatch.ReservationId
                    : null;

                // Si ya existe closeout pendiente, no degradamos la reserva a interrupted al salir.
                if (!string.IsNullOrWhiteSpace(reservationId) && AcarsContext.Api != null && !AcarsContext.Api.HasPendingCloseout(reservationId!))
                {
                    AcarsContext.Api.CloseReservationAsync(reservationId!, "interrupted")
                        .GetAwaiter().GetResult();
                }
            }
            catch
            {
                // best-effort
            }

            AcarsContext.Shutdown();
            base.OnExit(e);
        }
    }
}
