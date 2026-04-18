using System;
using System.Windows;
using PatagoniaWings.Acars.Master.Helpers;
using Velopack;

namespace PatagoniaWings.Acars.Master
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Velopack: inicialización temprana, sin Main custom para no romper el pipeline XAML del proyecto WPF clásico.
            VelopackApp.Build().Run();

            LoadGlobalStyles();
            base.OnStartup(e);
            AcarsContext.Initialize();
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

                if (!string.IsNullOrWhiteSpace(reservationId) && AcarsContext.Api != null)
                {
                    AcarsContext.Api.CloseReservationAsync(reservationId!, "cancelled")
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
