#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Master.Helpers;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private SimulatorCoordinator? _coordinator;
        private Timer?  _reconnectTimer;
        private bool    _isConnecting;
        private readonly string _simLogFile;

        public MainWindow()
        {
            InitializeComponent();

            var appData   = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logFolder = Path.Combine(appData, "PatagoniaWings", "Acars", "logs");
            Directory.CreateDirectory(logFolder);
            _simLogFile = Path.Combine(logFolder, "simconnect.log");

            _vm = new MainViewModel();
            _vm.OnLogout = () =>
            {
                var login = new LoginWindow();
                login.Show();
                Close();
            };

            DataContext = _vm;
            _vm.LoadPilot();

            Loaded += OnWindowLoaded;
            Closed += OnWindowClosed;
        }

        // ── Startup ──────────────────────────────────────────────────────────

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(2000);
            _vm.SimStatusText = "Buscando simulador...";
            TryConnectQuiet();

            _reconnectTimer = new Timer(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!_vm.SimConnected && !_isConnecting)
                    {
                        _vm.SimStatusText = "Reintentando...";
                        TryConnectQuiet();
                    }
                });
            }, null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _reconnectTimer?.Dispose();
            _coordinator?.Dispose();
        }

        // ── Conexión ──────────────────────────────────────────────────────────

        private void TryConnectQuiet()
        {
            if (_isConnecting) return;
            _isConnecting = true;
            try   { ConnectSim(silent: true); }
            finally { _isConnecting = false; }
        }

        public void ConnectSim(bool silent = false)
        {
            _vm.SimConnected  = false;
            _vm.SimType       = SimulatorType.None;
            _vm.SimStatusText = "Buscando simulador...";

            _coordinator?.Dispose();

            _coordinator = new SimulatorCoordinator(_simLogFile);
            var firstFrameLogged = false;

            _coordinator.Connected += () =>
                Dispatcher.Invoke(() =>
                {
                    _vm.SimConnected  = true;
                    _vm.SimType       = SimulatorType.MSFS2020;
                    _vm.SimStatusText = "Conectado · " + _coordinator.ActiveBackend;
                    WriteSimLog("Conectado via " + _coordinator.ActiveBackend);
                });

            _coordinator.Disconnected += () =>
                Dispatcher.Invoke(() =>
                {
                    _vm.SimConnected  = false;
                    _vm.SimType       = SimulatorType.None;
                    _vm.SimStatusText = "Simulador desconectado";
                });

            _coordinator.DataReceived += data =>
            {
                Dispatcher.Invoke(() =>
                {
                    _vm.SimConnected  = true;
                    _vm.SimType       = data.SimulatorType;
                    _vm.SimStatusText = "Conectado · " + _coordinator.ActiveBackend;
                });

                if (!firstFrameLogged)
                {
                    firstFrameLogged = true;
                    WriteSimLog(string.Format(
                        "Primer frame [{0}]: IAS={1} GS={2} ALT={3}",
                        _coordinator.ActiveBackend,
                        Math.Round(data.IndicatedAirspeed, 0),
                        Math.Round(data.GroundSpeed, 0),
                        Math.Round(data.AltitudeFeet, 0)));
                }

                OnSimulatorData(data);
            };

            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                _coordinator.TryConnect(hwnd);
            }
            catch (Exception ex)
            {
                _vm.SimConnected  = false;
                _vm.SimType       = SimulatorType.None;
                _vm.SimStatusText = "Sin simulador";
                WriteSimLog("Conexión fallida: " + ex.Message);

                if (!silent)
                {
                    MessageBox.Show(
                        "No se pudo conectar con MSFS ni FSUIPC7.\n" +
                        "Asegurate de tener el simulador abierto.\n\n" +
                        ex.Message + "\n\nLog: " + _simLogFile,
                        "Simulador",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private static void OnSimulatorData(PatagoniaWings.Acars.Core.Models.SimData data)
        {
            AcarsContext.FlightService.UpdateSimData(data);
        }

        // ── Click manual en el indicador de sim ──────────────────────────────

        private void SimStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_vm.SimConnected)
                ConnectSim(silent: false);
        }

        // ── Barra de título ──────────────────────────────────────────────────

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else DragMove();
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

        // ── Log ───────────────────────────────────────────────────────────────

        private void WriteSimLog(string message)
        {
            try
            {
                File.AppendAllText(_simLogFile,
                    "[" + DateTime.UtcNow.ToString("o") + "] " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
