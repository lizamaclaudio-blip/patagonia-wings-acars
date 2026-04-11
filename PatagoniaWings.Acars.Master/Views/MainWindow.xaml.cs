using System;
using System.Windows;
using PatagoniaWings.Acars.Master.Helpers;
using PatagoniaWings.Acars.Master.ViewModels;
using PatagoniaWings.Acars.SimConnect;
using PatagoniaWings.Acars.XPlane;

namespace PatagoniaWings.Acars.Master.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private SimConnectService? _simConnect;
        private XPlaneService? _xPlane;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            _vm.OnLogout = () =>
            {
                var login = new LoginWindow();
                login.Show();
                Close();
            };
            DataContext = _vm;
            _vm.LoadPilot();

            // Conectar comandos del InFlightVM a los servicios reales
            WireSimServices();

            Closed += OnWindowClosed;
        }

        private void WireSimServices()
        {
            // Reemplazamos los métodos vacíos con implementación real
        }

        public void ConnectMsfs()
        {
            try
            {
                _simConnect?.Dispose();
                _simConnect = new SimConnectService();
                _simConnect.Connected += () =>
                    Dispatcher.Invoke(() => _vm.SimConnected = true);
                _simConnect.Disconnected += () =>
                    Dispatcher.Invoke(() => _vm.SimConnected = false);
                _simConnect.DataReceived += data =>
                    AcarsContext.FlightService.UpdateSimData(data);

                _simConnect.Connect(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al conectar con MSFS:\n{ex.Message}",
                    "SimConnect", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void ConnectXPlane()
        {
            try
            {
                _xPlane?.Dispose();
                _xPlane = new XPlaneService();
                _xPlane.Connected += () =>
                    Dispatcher.Invoke(() => _vm.SimConnected = true);
                _xPlane.Disconnected += () =>
                    Dispatcher.Invoke(() => _vm.SimConnected = false);
                _xPlane.DataReceived += data =>
                    AcarsContext.FlightService.UpdateSimData(data);

                _xPlane.Connect();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al conectar con X-Plane:\n{ex.Message}",
                    "FSUIPC", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _simConnect?.Dispose();
            _xPlane?.Dispose();
        }

        // ── Drag & controles de ventana ────────────────────────────────────────
        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                ToggleMaximize();
            else
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();

        private void ToggleMaximize()
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }
}
