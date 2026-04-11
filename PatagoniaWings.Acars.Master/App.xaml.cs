using System.Windows;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AcarsContext.Initialize();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AcarsContext.Shutdown();
            base.OnExit(e);
        }
    }
}
