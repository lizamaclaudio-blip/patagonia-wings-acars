using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Core.Services;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class InFlightViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private double _altitude;
        private double _altitudeAgl;
        private double _ias;
        private double _gs;
        private double _vs;
        private double _heading;
        private double _fuelLbs;
        private double _fuelLeftTank;
        private double _fuelRightTank;
        private double _fuelCenterTank;
        private double _fuelCapacity;
        private double _n1Eng1;
        private double _n1Eng2;
        private double _oat;
        private double _windSpeed;
        private double _windDir;
        private double _lat;
        private double _lon;
        private string _aircraftTitle = string.Empty;
        private string _aircraftStatus = string.Empty;
        private bool _requiresLvars;
        private bool _autopilotOn;
        private bool _parkingBrakeOn;
        private bool _doorOpen;
        private bool _onGround;
        private bool _strobeOn;
        private bool _beaconOn;
        private bool _landingOn;
        private bool _taxiOn;
        private bool _navOn;
        private bool _seatBeltSign;
        private bool _noSmokingSign;
        private bool _gearDown;
        private bool _gearTransitioning;
        private double _flapsPercent;
        private bool _spoilersArmed;
        private bool _reverserActive;
        private bool _charlieMode;
        private int _transponderStateRaw;
        private int _squawk;
        private bool _apuRunning;
        private bool _apuAvailable;
        private bool _bleedAirOn;
        private bool _supportsSeatbeltSystem;
        private bool _supportsNoSmokingSystem;
        private bool _supportsBleedAirSystem;
        private bool _hasPressurization;
        private bool _hasApu;
        private bool _isSingleEngine;
        private double _cabinAlt;
        private double _pressDiff;
        private FlightPhase _phase = FlightPhase.Disconnected;
        private string _phaseLabel = "Desconectado";
        private string _elapsedTime = "00:00:00";
        private DateTime _startTime;
        private bool _alertNoStrobe;
        private bool _alertPause;
        private bool _above10000;
        private ImageSource? _aircraftImageSource;
        private string _routeOrigin = "----";
        private string _routeDestination = "----";
        private string _officialPhaseCode = "PRE";
        private string _routeStatusLabel = "Esperando inicio oficial";
        private double _routeProgressPercent;
        private const double RouteCanvasWidth = 320;
        private const double RoutePlaneVisualWidth = 42;

        // ── Pesos / comparación SimBrief vs Sim ──────────────────────────────────
        private double _fuelAtEngineStartKg = -1; // -1 = no capturado aún
        private string _pirepPreview = string.Empty;

        // ── Perfil de aeronave normalizado ────────────────────────────────────
        private string _detectedProfileCode = "MSFS_NATIVE";

        // ── Log de eventos de procedimiento (para PIREP en tiempo real) ───────
        private readonly System.Collections.Generic.List<string> _eventLog = new();
        private bool _prevBeaconOn;
        private bool _prevStrobeOn;
        private bool _prevLandingOn;
        private bool _prevTaxiOn;
        private bool _prevNavOn;
        private bool _prevApOn;
        private bool _prevSeatBelt;

        // ── Orden de arranque de motores (arquitectura PRE/IGN) ──────────────
        private readonly System.Collections.Generic.List<int> _engineStartOrder = new();
        private bool _eng1Started;
        private bool _eng2Started;
        private bool _eng3Started;
        private bool _eng4Started;
        private const double EngineN1StartThreshold = 15.0;

        public double Altitude    { get => _altitude;    set { if (SetField(ref _altitude,    value)) OnPropertyChanged(nameof(AltitudeDisplay)); } }
        public double AltitudeAGL { get => _altitudeAgl; set { if (SetField(ref _altitudeAgl, value)) OnPropertyChanged(nameof(AglDisplay)); } }
        public double IAS { get => _ias; set { if (SetField(ref _ias, value)) { OnPropertyChanged(nameof(IasDisplay)); OnPropertyChanged(nameof(IASDisplay)); } } }
        public double GS { get => _gs; set { if (SetField(ref _gs, value)) { OnPropertyChanged(nameof(GsDisplay)); OnPropertyChanged(nameof(GSDisplay)); } } }
        public double VS { get => _vs; set { if (SetField(ref _vs, value)) { OnPropertyChanged(nameof(VsDisplay)); OnPropertyChanged(nameof(VSDisplay)); } } }
        public double Heading { get => _heading; set { if (SetField(ref _heading, value)) OnPropertyChanged(nameof(HeadingDisplay)); } }
        public double FuelLbs { get => _fuelLbs; set { if (SetField(ref _fuelLbs, value)) { OnPropertyChanged(nameof(FuelKg)); OnPropertyChanged(nameof(FuelDisplay)); OnPropertyChanged(nameof(FuelKgDisplay)); OnPropertyChanged(nameof(FuelLbsDisplay)); } } }
        // FuelKg: normalizado en kg por el backend (SimConnect convierte lbs→kg, FSUIPC es nativo en kg)
        // Usamos _fuelKgNorm que se actualiza en OnTelemetry desde data.FuelKg
        private double _fuelKgNorm;
        private double _totalWeightKg;
        private double _zeroFuelWeightKg;
        public double FuelKg => Math.Round(_fuelKgNorm, 0);
        public double ActualTotalWeightKg => Math.Round(_totalWeightKg, 0);
        public double ActualZeroFuelWeightKg => Math.Round(_zeroFuelWeightKg, 0);
        
        // Propiedades de tanques individuales (visibles para diagnóstico de aviones complejos)
        public double FuelLeftTank { get => _fuelLeftTank; set { if (SetField(ref _fuelLeftTank, value)) OnPropertyChanged(nameof(FuelLeftTankDisplay)); } }
        public double FuelRightTank { get => _fuelRightTank; set { if (SetField(ref _fuelRightTank, value)) OnPropertyChanged(nameof(FuelRightTankDisplay)); } }
        public double FuelCenterTank { get => _fuelCenterTank; set { if (SetField(ref _fuelCenterTank, value)) OnPropertyChanged(nameof(FuelCenterTankDisplay)); } }
        public double FuelCapacity { get => _fuelCapacity; set { if (SetField(ref _fuelCapacity, value)) OnPropertyChanged(nameof(FuelCapacityDisplay)); } }
        
        public string FuelLeftTankDisplay => HasLiveTelemetry ? $"{Math.Round(FuelLeftTank, 0):F0}" : "---";
        public string FuelRightTankDisplay => HasLiveTelemetry ? $"{Math.Round(FuelRightTank, 0):F0}" : "---";
        public string FuelCenterTankDisplay => HasLiveTelemetry ? $"{Math.Round(FuelCenterTank, 0):F0}" : "---";
        public string FuelCapacityDisplay => HasLiveTelemetry ? $"{Math.Round(FuelCapacity, 0):F0}" : "---";
        public double N1Eng1 { get => _n1Eng1; set { if (SetField(ref _n1Eng1, value)) OnPropertyChanged(nameof(N1Eng1Display)); } }
        public double N1Eng2 { get => _n1Eng2; set { if (SetField(ref _n1Eng2, value)) OnPropertyChanged(nameof(N1Eng2Display)); } }
        public double OAT { get => _oat; set => SetField(ref _oat, value); }
        public double WindSpeed { get => _windSpeed; set => SetField(ref _windSpeed, value); }
        public double WindDir { get => _windDir; set => SetField(ref _windDir, value); }
        public double Lat { get => _lat; set => SetField(ref _lat, value); }
        public double Lon { get => _lon; set => SetField(ref _lon, value); }
        public string AircraftTitle
        {
            get => _aircraftTitle;
            set
            {
                if (SetField(ref _aircraftTitle, value))
                {
                    _aircraftImageSource = null;
                    DetectAircraftType(value);
                    OnPropertyChanged(nameof(AircraftStatusDisplay));
                    OnPropertyChanged(nameof(ShowDoors));
                    OnPropertyChanged(nameof(AircraftImageInitials));
                    OnPropertyChanged(nameof(AircraftImageUrl));
                    OnPropertyChanged(nameof(AircraftImageSource));
                    OnPropertyChanged(nameof(HasAircraftImage));
                    OnPropertyChanged(nameof(DetectedAddonLabel));
                    OnPropertyChanged(nameof(VariantMatchOk));
                    OnPropertyChanged(nameof(VariantMatchDisplay));
                    OnPropertyChanged(nameof(IsC208Family));
                    OnPropertyChanged(nameof(ShowSeatbeltSystem));
                    OnPropertyChanged(nameof(ShowAutopilotSystem));
            OnPropertyChanged(nameof(ShowTransponderSystem));
                    OnPropertyChanged(nameof(ShowDoorsSystem));
                }
            }
        }

        /// <summary>
        /// Ruta absoluta a la imagen de la aeronave en Assets\Aircraft\.
        /// Nombres admitidos (en orden de prioridad):
        ///   1. {ICAO_detectado}.png  → ej. A320.png, B738.png, C208.png
        ///   2. {ICAO_detectado}.jpg
        /// Si no existe ningún archivo, retorna null y el XAML muestra el placeholder de iniciales.
        /// </summary>
        public string? AircraftImageUrl
        {
            get
            {
                var code = AircraftImageInitials; // ej. "A320", "B738", "C208"
                if (string.IsNullOrEmpty(code) || code == "---") return null;

                var localUserFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PatagoniaWings", "Acars", "Aircraft");
                var roamingUserFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PatagoniaWings", "Acars", "Aircraft");
                var exeFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Aircraft");

                var folders = new[] { localUserFolder, roamingUserFolder, exeFolder };

                // 1. Buscar por código ICAO detectado
                foreach (var folder in folders)
                    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
                    {
                        var path = Path.Combine(folder, code + ext);
                        if (File.Exists(path))
                            return path;
                    }

                // 2. Fallback: usar ImageAsset del perfil detectado
                var profile = AircraftNormalizationService.GetProfile(_detectedProfileCode);
                var asset = profile?.ImageAsset;
                if (!string.IsNullOrWhiteSpace(asset))
                {
                    foreach (var folder in folders)
                    {
                        var path = Path.Combine(folder, asset);
                        if (File.Exists(path))
                            return path;
                        // también probar el nombre sin prefijo de addon (ej. "a320_fenix.png" → "A320.png")
                        var stem = System.IO.Path.GetFileNameWithoutExtension(asset).ToUpperInvariant();
                        var ext  = System.IO.Path.GetExtension(asset).ToLowerInvariant();
                        if (!string.IsNullOrEmpty(stem))
                            foreach (var e2 in new[] { ext, ".png", ".jpg" })
                            {
                                var p2 = Path.Combine(folder, stem + e2);
                                if (File.Exists(p2)) return p2;
                            }
                    }
                }

                return null;
            }
        }

        public ImageSource? AircraftImageSource
        {
            get
            {
                if (_aircraftImageSource != null)
                {
                    return _aircraftImageSource;
                }

                var path = AircraftImageUrl;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return null;
                }

                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _aircraftImageSource = bitmap;
                    return _aircraftImageSource;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[InFlightVM] Error cargando imagen de aeronave: " + ex.Message);
                    return null;
                }
            }
        }

        /// <summary>True cuando existe una imagen real para la aeronave activa.</summary>
        public bool HasAircraftImage => AircraftImageSource != null;

        /// <summary>Addon/variante esperado según el despacho activo.</summary>
        public string ExpectedVariantLabel
        {
            get
            {
                var flight = AcarsContext.FlightService.CurrentFlight;
                if (flight == null) return "—";
                var addon   = (flight.AddonProvider      ?? string.Empty).Trim();
                var variant = (flight.AircraftVariantCode ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(addon) && string.IsNullOrEmpty(variant)) return "Estándar";
                if (!string.IsNullOrEmpty(addon) && !string.IsNullOrEmpty(variant))
                    return $"{addon} · {variant}";
                return !string.IsNullOrEmpty(addon) ? addon : variant;
            }
        }

        /// <summary>Addon detectado en el título del avión del simulador.</summary>
        public string DetectedAddonLabel
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_aircraftTitle)) return "—";
                var t = _aircraftTitle.ToUpperInvariant();
                if (t.Contains("PMDG"))                                   return "PMDG";
                if (t.Contains("FENIX"))                                  return "Fenix";
                if (t.Contains("HEADWIND") || t.Contains("HJETS"))       return "Headwind";
                if (t.Contains("INIBUILDS"))                              return "iniBuilds";
                if (t.Contains("FLYBYWIRE") || t.Contains("A32NX") || t.Contains("FBW")) return "FlyByWire";
                if (t.Contains("TOLISS"))                                 return "ToLiSS";
                if (t.Contains("MAJESTIC"))                               return "Majestic";
                if (t.Contains("QUALITYWINGS"))                           return "QualityWings";
                if (t.Contains("LEONARDO"))                               return "Leonardo";
                if (t.Contains("FEELTHERE"))                              return "FeelThere";
                if (t.Contains("ROTATE"))                                 return "Rotate";
                if (t.Contains("BLACK SQUARE") || t.Contains("BLACKSQUARE")) return "Black Square";
                if (t.Contains("LATINVFR") || t.Contains("LVFR"))         return "LVFR";
                if ((t.Contains("AIRBUS A319") || t.Contains("AIRBUS A320") || t.Contains("AIRBUS A321"))
                    && (t.Contains("CFM") || t.Contains("IAE"))
                    && !t.Contains("FENIX") && !t.Contains("FLYBYWIRE") && !t.Contains("A32NX")
                    && !t.Contains("HEADWIND") && !t.Contains("INIBUILDS"))
                    return "LVFR";
                if (t.Contains("CARENADO"))                               return "Carenado";
                if (t.Contains("MILVIZ"))                                 return "MilViz";
                if (t.Contains("JUST FLIGHT") || t.Contains("JUSTFLIGHT")) return "Just Flight";
                if (t.Contains("SALTY"))                                  return "Salty";
                if (t.Contains("CAPTAIN SIM"))                            return "Captain Sim";
                return "Asobo";
            }
        }

        /// <summary>True si el addon detectado en el simulador coincide con el del despacho.</summary>
        public bool VariantMatchOk
        {
            get
            {
                var flight = AcarsContext.FlightService.CurrentFlight;
                if (flight == null) return true;
                var expected = (flight.AddonProvider ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(expected)) return true; // sin restricción
                var detected = DetectedAddonLabel;
                return detected.IndexOf(expected, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || expected.IndexOf(detected, System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public string VariantMatchDisplay => VariantMatchOk ? "✓ OK" : "⚠ Verificar";

        /// <summary>
        /// Iniciales para mostrar en el placeholder de foto de aeronave.
        /// Ej: "A320" → "A32", "B738" → "B73", "C208" → "C20"
        /// </summary>
        public string AircraftImageInitials
        {
            get
            {
                if (!HasLiveTelemetry || string.IsNullOrWhiteSpace(_aircraftTitle)) return "---";
                // Tomar los primeros 4 caracteres del título en mayúsculas, eliminando espacios
                var t = _aircraftTitle.ToUpperInvariant();
                // Intentar extraer el tipo ICAO del título
                // Mapa de palabras clave a código de imagen (orden importa: más específico primero)
                var imageMap = new (string keyword, string code)[]
                {
                    ("A318","A318"),("A319","A319"),("A320","A320"),("A321","A321"),
                    ("A330","A330"),("A350","A350"),("A380","A380"),
                    ("B737","B737"),("B738","B738"),("B739","B739"),
                    ("737-7","B737"),("737-8","B738"),("737-9","B739"),("737","B737"),
                    ("B747","B747"),("B757","B757"),("B767","B767"),
                    ("B777","B777"),("B787","B787"),("B78X","B78X"),
                    ("777-2","B777"),("777-3","B777"),("777","B777"),
                    ("787-10","B78X"),("787-8","B787"),("787-9","B787"),("787","B787"),
                    ("CRJ","CRJ"),("E170","E170"),("E175","E175"),
                    ("E190","E190"),("E195","E195"),
                    ("ATR","ATR"),("DHC","DHC"),("Q400","Q400"),
                    ("TBM9","TBM9"),("TBM","TBM"),("PC12","PC12"),
                    // Cessna 208 Caravan — incluye "208B", "208", "CARAVAN"
                    ("208","C208"),("CARAVAN","C208"),
                    ("C172","C172"),("C152","C152"),("SR22","SR22"),
                    ("DA40","DA40"),("BE58","BE58"),("B350","B350"),
                    ("MD-82","MD82"),("MD-83","MD82"),("MD-88","MD88"),
                    ("MD80","MD80"),("MD82","MD82"),("MD88","MD88"),("MD11","MD11"),
                };
                foreach (var (kw, code) in imageMap)
                    if (t.Contains(kw)) return code;
                return t.Length >= 4 ? t.Substring(0, 4) : t;
            }
        }

        public bool ShowDoors
        {
            get
            {
                var t = (_aircraftTitle ?? string.Empty).ToUpperInvariant();
                return t.Contains("A319") || t.Contains("A320") || t.Contains("A321") ||
                       t.Contains("B737") || t.Contains("B738") || t.Contains("B777") || t.Contains("B787") || t.Contains("B78X") ||
                       t.Contains("CRJ") || t.Contains("E170") || t.Contains("E175") || t.Contains("E190") ||
                       t.Contains("ATR") || t.Contains("DHC-8") || t.Contains("DASH 8") || t.Contains("DASH8") ||
                       t.Contains("PMDG") || t.Contains("FENIX") || t.Contains("HEADWIND") || t.Contains("INIBUILDS") ||
                       t.Contains("FLYBYWIRE") || t.Contains("ACJ") || t.Contains("BBJ") ||
                       t.Contains("MADDOG") || t.Contains("MD-82") || t.Contains("MD-83") || t.Contains("MD-88") ||
                       t.Contains("MD80") || t.Contains("MD82") || t.Contains("MD83") || t.Contains("MD88");
            }
        }
        public string AircraftStatus { get => _aircraftStatus; set => SetField(ref _aircraftStatus, value); }
        public bool RequiresLvars { get => _requiresLvars; set => SetField(ref _requiresLvars, value); }
        public string AircraftStatusDisplay => GetAircraftStatusDisplay();

        /// <summary>Código estable normalizado. Ej: C208_MSFS, B738_PMDG, A320_FENIX</summary>
        public string DetectedProfileCode => _detectedProfileCode;

        /// <summary>Nombre de display del perfil normalizado. Ej: "Boeing 737-800 (PMDG)"</summary>
        public string DetectedDisplayName
        {
            get
            {
                var profile = AircraftNormalizationService.GetProfile(_detectedProfileCode);
                if (profile == null)
                {
                    return string.IsNullOrWhiteSpace(_aircraftTitle) ? "MSFS Native" : _aircraftTitle;
                }

                return profile.Code == "MSFS_NATIVE" && !string.IsNullOrWhiteSpace(_aircraftTitle)
                    ? _aircraftTitle
                    : profile.DisplayName;
            }
        }
        
        private string GetAircraftStatusDisplay()
        {
            if (!HasLiveTelemetry) return "Sin conexión";
            return AircraftTitle;
        }
        public bool AutopilotOn { get => _autopilotOn; set { if (SetField(ref _autopilotOn, value)) OnPropertyChanged(nameof(LiveAutopilotOn)); } }
        public bool ParkingBrakeOn { get => _parkingBrakeOn; set { if (SetField(ref _parkingBrakeOn, value)) OnPropertyChanged(nameof(LiveParkingBrakeOn)); } }
        public bool DoorOpen { get => _doorOpen; set { if (SetField(ref _doorOpen, value)) { OnPropertyChanged(nameof(LiveDoorOpen)); OnPropertyChanged(nameof(DoorOpenPercentDisplay)); } } }
        public bool OnGround { get => _onGround; set => SetField(ref _onGround, value); }
        public bool StrobeOn { get => _strobeOn; set => SetField(ref _strobeOn, value); }
        public bool BeaconOn { get => _beaconOn; set => SetField(ref _beaconOn, value); }
        public bool LandingOn { get => _landingOn; set => SetField(ref _landingOn, value); }
        public bool TaxiOn { get => _taxiOn; set => SetField(ref _taxiOn, value); }
        public bool NavOn { get => _navOn; set => SetField(ref _navOn, value); }
        public bool SeatBeltSign { get => _seatBeltSign; set { if (SetField(ref _seatBeltSign, value)) OnPropertyChanged(nameof(LiveSeatBeltSign)); } }
        public bool NoSmokingSign { get => _noSmokingSign; set { if (SetField(ref _noSmokingSign, value)) OnPropertyChanged(nameof(LiveNoSmokingSign)); } }

        public bool IsC208Family => _detectedProfileCode == "C208_MSFS" || _detectedProfileCode == "C208_BLACKSQUARE" || (!string.IsNullOrWhiteSpace(_aircraftTitle) && _aircraftTitle.ToUpperInvariant().Contains("CARAVAN"));
        public bool ShowGroundSpeedMetric => false;
        public bool ShowSeatbeltSystem => _supportsSeatbeltSystem;
        public bool ShowNoSmokingSystem => _supportsNoSmokingSystem;
        public bool ShowAutopilotSystem => true;
        public bool ShowDoorsSystem => ShowDoors && !IsC208Family;
        public bool ShowTransponderSystem => true;
        public bool ShowBleedAirSystem => _supportsBleedAirSystem;

        // Props con guardia HasLiveTelemetry para el panel SISTEMAS
        public bool LiveParkingBrakeOn => HasLiveTelemetry && _parkingBrakeOn;
        public bool LiveAutopilotOn => ShowAutopilotSystem && HasLiveTelemetry && _autopilotOn && !OnGround;
        public bool LiveSeatBeltSign => ShowSeatbeltSystem && HasLiveTelemetry && _seatBeltSign;
        public bool LiveNoSmokingSign => ShowNoSmokingSystem && HasLiveTelemetry && _noSmokingSign;
        public bool LiveDoorOpen => ShowDoorsSystem && HasLiveTelemetry && _doorOpen;
        public string DoorOpenPercentDisplay => HasLiveTelemetry ? (_doorOpen ? "100%" : "0%") : "---";
        public bool LiveCharlieMode => ShowTransponderSystem && HasLiveTelemetry && _charlieMode;
        public bool LiveTransponderOff => ShowTransponderSystem && HasLiveTelemetry && _transponderStateRaw <= 0;
        public bool LiveTransponderStandby => false;
        public bool LiveTransponderTest => false;
        public bool LiveTransponderOn => ShowTransponderSystem && HasLiveTelemetry && _transponderStateRaw > 0;
        public bool LiveTransponderModeC => false;
        public bool GearDown { get => _gearDown; set => SetField(ref _gearDown, value); }
        public bool GearTransitioning { get => _gearTransitioning; set => SetField(ref _gearTransitioning, value); }
        public double FlapsPercent { get => _flapsPercent; set { if (SetField(ref _flapsPercent, value)) OnPropertyChanged(nameof(FlapsDisplay)); } }
        public bool SpoilersArmed { get => _spoilersArmed; set => SetField(ref _spoilersArmed, value); }
        public bool ReverserActive { get => _reverserActive; set => SetField(ref _reverserActive, value); }
        public bool CharlieMode { get => _charlieMode; set { if (SetField(ref _charlieMode, value)) { OnPropertyChanged(nameof(LiveCharlieMode)); OnPropertyChanged(nameof(TransponderModeDisplay)); } } }
        public int TransponderStateRaw
        {
            get => _transponderStateRaw;
            set
            {
                if (SetField(ref _transponderStateRaw, value))
                {
                    OnPropertyChanged(nameof(LiveTransponderOff));
                    OnPropertyChanged(nameof(LiveTransponderStandby));
                    OnPropertyChanged(nameof(LiveTransponderTest));
                    OnPropertyChanged(nameof(LiveTransponderOn));
                    OnPropertyChanged(nameof(LiveTransponderModeC));
                    OnPropertyChanged(nameof(TransponderModeDisplay));
                }
            }
        }
        public int Squawk { get => _squawk; set { if (SetField(ref _squawk, value)) OnPropertyChanged(nameof(SquawkDisplay)); } }
        public bool ApuRunning   { get => _apuRunning;   set => SetField(ref _apuRunning,   value); }
        public bool ApuAvailable { get => _apuAvailable; set => SetField(ref _apuAvailable, value); }

        /// <summary>Fila APU visible solo en jets comerciales/ejecutivos que tienen APU.</summary>
        public bool ShowApu => _hasApu;
        /// <summary>Fila Bleed Air visible en aeronaves presurizadas (jets + turbohélices presurizados).</summary>
        public bool HasPressurization
        {
            get => _hasPressurization;
            set { if (SetField(ref _hasPressurization, value)) { OnPropertyChanged(nameof(ShowApu)); OnPropertyChanged(nameof(ShowBleedAirSystem)); } }
        }
        /// <summary>True en jets comerciales/ejecutivos que tienen APU estándar.</summary>
        public bool HasApu
        {
            get => _hasApu;
            set { if (SetField(ref _hasApu, value)) OnPropertyChanged(nameof(ShowApu)); }
        }
        /// <summary>True para monomotores (C208, TBM, SR22, C172…). Oculta el N1 del motor 2 en la UI.</summary>
        public bool IsSingleEngine { get => _isSingleEngine; set => SetField(ref _isSingleEngine, value); }
        public bool BleedAirOn { get => _bleedAirOn; set => SetField(ref _bleedAirOn, value); }
        public double CabinAlt { get => _cabinAlt; set => SetField(ref _cabinAlt, value); }
        public double PressDiff { get => _pressDiff; set => SetField(ref _pressDiff, value); }
        public FlightPhase Phase { get => _phase; set { if (SetField(ref _phase, value)) PhaseLabel = GetPhaseLabel(value); } }
        public string PhaseLabel { get => _phaseLabel; set { if (SetField(ref _phaseLabel, value)) OnPropertyChanged(nameof(PhaseLabelDisplay)); } }
        public string ElapsedTime { get => _elapsedTime; set { if (SetField(ref _elapsedTime, value)) OnPropertyChanged(nameof(ElapsedTimeDisplay)); } }
        public bool AlertNoStrobe { get => _alertNoStrobe; set => SetField(ref _alertNoStrobe, value); }
        public bool AlertPause { get => _alertPause; set => SetField(ref _alertPause, value); }
        public bool HasLiveTelemetry => AcarsContext.Runtime.IsSimulatorConnected && AcarsContext.Runtime.LastTelemetry != null;
        public string RouteOrigin => _routeOrigin;
        public string RouteDestination => _routeDestination;
        public string RouteDisplay => $"{RouteOrigin} → {RouteDestination}";
        public string OfficialPhaseCode
        {
            get => _officialPhaseCode;
            private set
            {
                if (SetField(ref _officialPhaseCode, value))
                {
                    OnPropertyChanged(nameof(OfficialPhaseDisplay));
                }
            }
        }
        public string OfficialPhaseDisplay => $"{OfficialPhaseCode} · {PhaseLabel}";
        public string RouteStatusLabel
        {
            get => _routeStatusLabel;
            private set => SetField(ref _routeStatusLabel, value);
        }
        public double RouteProgressPercent
        {
            get => _routeProgressPercent;
            private set
            {
                if (SetField(ref _routeProgressPercent, value))
                {
                    OnPropertyChanged(nameof(RouteProgressDisplay));
                    OnPropertyChanged(nameof(RouteTrackWidth));
                    OnPropertyChanged(nameof(RoutePlaneLeft));
                    OnPropertyChanged(nameof(RouteDistanceFromOriginDisplay));
                    OnPropertyChanged(nameof(RouteDistanceToDestinationDisplay));
                    OnPropertyChanged(nameof(RouteDistanceDisplay));
                }
            }
        }
        public string RouteProgressDisplay
        {
            get
            {
                if (OfficialPhaseCode == "PRE" && AcarsContext.FlightService.TotalDistanceNm <= 0)
                    return "LISTO";

                return $"{Math.Round(RouteProgressPercent, 0):F0}%";
            }
        }
        public double RouteTrackWidth => Math.Max(26, RouteCanvasWidth * (RouteProgressPercent / 100.0));
        public double RoutePlaneLeft => Math.Max(0, Math.Min(RouteCanvasWidth - RoutePlaneVisualWidth, RouteTrackWidth - (RoutePlaneVisualWidth * 0.6)));
        public string RouteDistanceFromOriginDisplay => $"{ComputeDistanceFromOriginNm():F0} NM";
        public string RouteDistanceToDestinationDisplay => $"{ComputeDistanceToDestinationNm():F0} NM";
        public string RouteDistanceDisplay
        {
            get
            {
                var flown = ComputeDistanceFromOriginNm();
                var remaining = ComputeDistanceToDestinationNm();
                if (flown <= 0.1 && remaining <= 0.1)
                    return "Esperando recorrido";

                return $"{flown:F1} nm recorridas · {remaining:F0} nm restantes";
            }
        }

        // ── Propiedades de pesos (Plan SimBrief vs Actual Sim) ───────────────────

        private PreparedDispatch? Dispatch => AcarsContext.Runtime.CurrentDispatch;

        /// <summary>True cuando hay un despacho SimBrief activo con datos de combustible.</summary>
        public bool HasDispatchWeights => Dispatch != null && Dispatch.FuelPlannedKg > 0;

        // Plan (desde SimBrief / despacho)
        public double WbPlanFuelKg    => Dispatch?.FuelPlannedKg      ?? 0;
        public double WbPlanPayloadKg => Dispatch?.PayloadKg          ?? 0;
        public double WbPlanZfwKg     => Dispatch?.ZeroFuelWeightKg   ?? 0;

        // Actual del simulador (combustible actual en pantalla)
        public double WbActualFuelKg => HasLiveTelemetry ? FuelKg : 0;

        /// <summary>Combustible capturado en el primer arranque de motores (N1 > 15%).</summary>
        public double WbFuelAtEngineStartKg => _fuelAtEngineStartKg > 0 ? _fuelAtEngineStartKg : WbActualFuelKg;

        /// <summary>True si el combustible al arranque está dentro del ±10% del plan.</summary>
        public bool WbFuelMatchOk
        {
            get
            {
                if (WbPlanFuelKg <= 0) return true;
                var actual = WbFuelAtEngineStartKg;
                if (actual <= 0) return true;
                return Math.Abs(actual - WbPlanFuelKg) / WbPlanFuelKg <= 0.10;
            }
        }

        /// <summary>Diferencia porcentual entre combustible real al arranque y el plan.</summary>
        public string WbFuelDiffDisplay
        {
            get
            {
                if (WbPlanFuelKg <= 0 || WbFuelAtEngineStartKg <= 0) return "—";
                var pct = (WbFuelAtEngineStartKg - WbPlanFuelKg) / WbPlanFuelKg * 100.0;
                return $"{pct:+0.0;-0.0;0.0}%";
            }
        }

        /// <summary>
        /// Etiqueta de estado del combustible al arranque:
        ///   "Esperando arranque" → motores no iniciados aún
        ///   "✓ ±4.2%  OK"       → dentro del ±10%
        ///   "⚠ +14.5%  EXCESO"  → fuera del ±10%
        /// </summary>
        public string WbFuelStatusLabel
        {
            get
            {
                if (WbPlanFuelKg <= 0)          return "Sin plan SimBrief";
                if (_fuelAtEngineStartKg < 0)   return "Esperando arranque de motores";
                return WbFuelMatchOk
                    ? $"✓  {WbFuelDiffDisplay}  COMBUSTIBLE OK"
                    : $"⚠  {WbFuelDiffDisplay}  FUERA DE RANGO (±10%)";
            }
        }

        public string WbPlanFuelDisplay    => WbPlanFuelKg    > 0 ? $"{WbPlanFuelKg:F0} kg"    : "—";
        public string WbPlanPayloadDisplay => WbPlanPayloadKg > 0 ? $"{WbPlanPayloadKg:F0} kg"  : "—";
        public string WbPlanZfwDisplay     => WbPlanZfwKg     > 0 ? $"{WbPlanZfwKg:F0} kg"     : "—";
        public string WbActualFuelDisplay  => HasLiveTelemetry    ? $"{WbActualFuelKg:F0} kg"   : "—";
        public string WbStartFuelDisplay   => _fuelAtEngineStartKg > 0 ? $"{_fuelAtEngineStartKg:F0} kg" : "—";

        // ── Log de PIREP en tiempo real ──────────────────────────────────────────

        public string WbActualZfwDisplay   => HasLiveTelemetry && ActualZeroFuelWeightKg > 0 ? $"{ActualZeroFuelWeightKg:F0} kg" : "â€”";

        public string PirepPreview
        {
            get => _pirepPreview;
            private set => SetField(ref _pirepPreview, value);
        }

        private void UpdatePirepPreview()
        {
            var fs     = AcarsContext.FlightService;
            var flight = fs.CurrentFlight;
            var sb     = new StringBuilder();

            if (flight == null)
            {
                sb.AppendLine("Sin vuelo activo. Inicia un despacho desde la página de Despacho.");
                PirepPreview = sb.ToString();
                return;
            }

            var dep = flight.DepartureIcao ?? "????";
            var arr = flight.ArrivalIcao   ?? "????";
            var fn  = flight.FlightNumber  ?? "—";
            var ac  = !string.IsNullOrWhiteSpace(flight.AircraftName)
                          ? flight.AircraftName : (flight.AircraftIcao ?? "—");

            sb.AppendLine($"╔  PIREP PRELIMINAR  ·  {fn}");
            sb.AppendLine($"   {dep} → {arr}   |   {ac}");
            sb.AppendLine($"   Fase: {PhaseLabel}   |   Tiempo vuelo: {ElapsedTime}");
            sb.AppendLine($"───────────────────────────────────────────");

            if (fs.MaxAltitudeFeet > 0)
                sb.AppendLine($"   Alt máx  : {fs.MaxAltitudeFeet:F0} ft");
            if (fs.MaxSpeedKts > 0)
                sb.AppendLine($"   Vel máx  : {fs.MaxSpeedKts:F0} kt");
            if (fs.TotalDistanceNm > 0)
                sb.AppendLine($"   Distancia: {fs.TotalDistanceNm:F1} nm");

            // Combustible usado (si el vuelo ya inició con combustible registrado)
            if (fs.FuelAtStartLbs > 0 && HasLiveTelemetry)
            {
                var fuelUsed = Math.Max(0, (fs.FuelAtStartLbs / 2.20462) - FuelKg);
                sb.AppendLine($"   Comb. usado: {fuelUsed:F0} kg   (actual: {FuelKg:F0} kg)");
            }

            // V/S de aterrizaje (solo si ya aterrizó)
            if (fs.LastLandingVS != 0)
            {
                var vsStr = fs.LastLandingVS < 0
                    ? $"{fs.LastLandingVS:F0} fpm"
                    : $"+{fs.LastLandingVS:F0} fpm";
                var rating = fs.LastLandingVS >= -180 ? "✓ Suave"
                           : fs.LastLandingVS >= -350 ? "▲ Normal"
                           : "⚠ Duro";
                sb.AppendLine($"   V/S aterrizaje: {vsStr}  {rating}");
            }

            // Entorno actual
            if (HasLiveTelemetry)
            {
                sb.AppendLine($"───────────────────────────────────────────");
                sb.AppendLine($"   OAT: {OAT:F0}°C   Viento: {WindDir:000}°/{WindSpeed:F0}kt");
            }

            // Log de eventos de procedimiento
            if (_eventLog.Count > 0)
            {
                sb.AppendLine($"───────────────────────────────────────────");
                sb.AppendLine($"   LOG PROCEDIMIENTOS:");
                // Mostrar los últimos 12 eventos
                var start = Math.Max(0, _eventLog.Count - 12);
                for (int i = start; i < _eventLog.Count; i++)
                    sb.AppendLine($"   · {_eventLog[i]}");
            }

            PirepPreview = sb.ToString().TrimEnd();
        }

        private static string NormalizeIcao(string? value)
        {
            var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
            return string.IsNullOrWhiteSpace(normalized) ? "----" : normalized;
        }

        private string GetOfficialPhaseCode(FlightPhase phase, SimData? data)
        {
            var sample = data ?? AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData;
            var maxEngineN1 = Math.Max(sample?.Engine1N1 ?? 0, sample?.Engine2N1 ?? 0);
            var groundSpeed = sample?.GroundSpeed ?? 0;

            return phase switch
            {
                FlightPhase.Disconnected => "PRE",
                FlightPhase.PreFlight when maxEngineN1 >= 15 => "IGN",
                FlightPhase.PreFlight => "PRE",
                FlightPhase.Boarding when maxEngineN1 >= 15 => "IGN",
                FlightPhase.Boarding => "PRE",
                FlightPhase.PushbackTaxi when groundSpeed < 3 && maxEngineN1 >= 15 => "IGN",
                FlightPhase.PushbackTaxi => "TAX",
                FlightPhase.Takeoff => "TO",
                FlightPhase.Climb => "ASC",
                FlightPhase.Cruise => "CRU",
                FlightPhase.Descent => "DES",
                FlightPhase.Approach => "LDG",
                FlightPhase.Landing => "LDG",
                FlightPhase.Taxi => "TAG",
                FlightPhase.Arrived => "PAR",
                FlightPhase.Deboarding => "PAR",
                _ => "PRE",
            };
        }

        private double ComputeRouteProgressPercent()
        {
            var sample = AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData;
            var altitudeAgl = sample?.AltitudeAGL ?? 0;
            var altitudeFeet = sample?.AltitudeFeet ?? 0;
            var groundSpeed = sample?.GroundSpeed ?? 0;
            var verticalSpeed = sample?.VerticalSpeed ?? 0;
            var maxEngineN1 = Math.Max(sample?.Engine1N1 ?? 0, sample?.Engine2N1 ?? 0);
            var plannedCruise = Math.Max(8000, AcarsContext.FlightService.CurrentFlight?.PlannedAltitude ?? 30000);

            double progress = OfficialPhaseCode switch
            {
                "PRE" => maxEngineN1 > 5 ? 12 : 8,
                "IGN" => 15 + Math.Min(7, maxEngineN1 / 4.0),
                "TAX" => 22 + Math.Min(10, Math.Max(0, groundSpeed) / 3.0),
                "TO" => 34 + Math.Min(8, Math.Max(0, altitudeAgl) / 250.0),
                "ASC" => 42 + Math.Min(18, Math.Max(0, altitudeFeet) / plannedCruise * 18.0),
                "CRU" => 62 + Math.Min(14, Math.Max(0, AcarsContext.FlightService.TotalDistanceNm) / 70.0),
                "DES" => 80 + Math.Min(8, Math.Max(0, Math.Abs(verticalSpeed)) / 300.0),
                "LDG" => 90 + Math.Min(6, Math.Max(0, 3000 - altitudeAgl) / 500.0),
                "TAG" => 97 + Math.Min(2, Math.Max(0, 30 - groundSpeed) / 12.0),
                "PAR" => 100,
                _ => 8,
            };

            return Math.Max(0, Math.Min(100, progress));
        }

        private double ComputeEstimatedRouteDistanceNm()
        {
            var flown = Math.Max(0, AcarsContext.FlightService.TotalDistanceNm);
            var dispatch = AcarsContext.Runtime.CurrentDispatch;
            var sample = AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData;
            var plannedSpeed = Math.Max(0, AcarsContext.FlightService.CurrentFlight?.PlannedSpeed ?? 0);
            var referenceSpeed = plannedSpeed > 0
                ? plannedSpeed
                : Math.Max(220, sample?.GroundSpeed ?? 0);

            var plannedMinutes = Math.Max(
                dispatch?.ExpectedBlockP50Minutes ?? 0,
                dispatch?.ScheduledBlockMinutes ?? 0);

            var timeEstimate = plannedMinutes > 0
                ? referenceSpeed * Math.Max(0.35, plannedMinutes / 60.0)
                : 0;

            var baseline = OfficialPhaseCode switch
            {
                "PRE" => 90,
                "IGN" => 100,
                "TAX" => 120,
                "TO" => 140,
                "ASC" => Math.Max(180, flown + 90),
                "CRU" => Math.Max(260, flown + 120),
                "DES" => Math.Max(220, flown + 70),
                "LDG" => Math.Max(140, flown + 25),
                "TAG" => Math.Max(110, flown + 10),
                "PAR" => Math.Max(1, flown),
                _ => 110,
            };

            return Math.Max(flown, Math.Max(timeEstimate, baseline));
        }

        private double ComputeDistanceFromOriginNm()
        {
            return Math.Max(0, AcarsContext.FlightService.TotalDistanceNm);
        }

        private double ComputeDistanceToDestinationNm()
        {
            var flown = ComputeDistanceFromOriginNm();
            var estimated = ComputeEstimatedRouteDistanceNm();
            return Math.Max(0, estimated - flown);
        }

        private string BuildRouteStatusLabel()
        {
            return OfficialPhaseCode switch
            {
                "PRE" => "Cabina y despacho listos para salida",
                "IGN" => "Secuencia de arranque observada",
                "TAX" => "Rodaje y salida en curso",
                "TO" => "Despegue oficial detectado",
                "ASC" => "Ascenso estabilizado",
                "CRU" => "Crucero y telemetría oficial activa",
                "DES" => "Descenso y preparación de llegada",
                "LDG" => "Aproximación y aterrizaje en evaluación",
                "TAG" => "Taxi in y llegada al stand",
                "PAR" => "Vuelo listo para cierre oficial",
                _ => "Monitoreando vuelo",
            };
        }

        private void RefreshRouteSnapshot(SimData? data = null)
        {
            var flight = AcarsContext.FlightService.CurrentFlight;
            _routeOrigin = NormalizeIcao(flight?.DepartureIcao);
            _routeDestination = NormalizeIcao(flight?.ArrivalIcao);
            OfficialPhaseCode = GetOfficialPhaseCode(Phase, data);
            RouteProgressPercent = ComputeRouteProgressPercent();
            RouteStatusLabel = BuildRouteStatusLabel();

            OnPropertyChanged(nameof(RouteOrigin));
            OnPropertyChanged(nameof(RouteDestination));
            OnPropertyChanged(nameof(RouteDisplay));
            OnPropertyChanged(nameof(RouteDistanceDisplay));
            OnPropertyChanged(nameof(RouteDistanceFromOriginDisplay));
            OnPropertyChanged(nameof(RouteDistanceToDestinationDisplay));
        }

        private void LogLightEvent(string name, bool prev, bool current, bool onGround)
        {
            if (prev == current) return;
            var ctx = onGround ? "(suelo)" : "(vuelo)";
            _eventLog.Add($"{PhaseLabel}: {name} {(current ? "ON" : "OFF")} {ctx}");
            // Mantener máximo 30 entradas
            if (_eventLog.Count > 30) _eventLog.RemoveAt(0);
        }

        public string IasDisplay => HasLiveTelemetry ? Math.Round(IAS, 0).ToString("F0") : "---";
        public string IASDisplay => IasDisplay;
        public string GsDisplay => HasLiveTelemetry ? Math.Round(GS, 0).ToString("F0") : "---";
        public string GSDisplay => GsDisplay;
        public string AltitudeDisplay => HasLiveTelemetry ? Math.Round(Altitude, 0).ToString("F0") : "---";
        public string AglDisplay      => HasLiveTelemetry ? Math.Round(AltitudeAGL, 0).ToString("F0") : "---";
        // V/S: clamp a 0 cuando en tierra (SimConnect puede retornar ±1 fpm en suelo)
        public string VsDisplay => HasLiveTelemetry ? (OnGround ? "0" : Math.Round(VS, 0).ToString("+#;-#;0")) : "---";
        public string VSDisplay => VsDisplay;
        public string HeadingDisplay => HasLiveTelemetry ? Math.Round(Heading, 0).ToString("000") + "°" : "---";
        public string FuelDisplay => HasLiveTelemetry ? Math.Round(FuelKg, 0).ToString("F0") : "---";
        public string FuelKgDisplay => FuelDisplay;
        public string FuelLbsDisplay => HasLiveTelemetry ? Math.Round(FuelLbs, 0).ToString("F0") : "---";
        public string FlapsDisplay => HasLiveTelemetry ? Math.Round(FlapsPercent, 0).ToString("F0") + "%" : "---";
        public string N1Eng1Display => HasLiveTelemetry ? Math.Round(N1Eng1, 1).ToString("F1") : "---";
        public string N1Eng2Display => HasLiveTelemetry ? Math.Round(N1Eng2, 1).ToString("F1") : "---";

        /// <summary>Orden de arranque registrado (ej. "2 → 1" para bimotor, "4 → 2 → 3 → 1" para cuadrimotor).</summary>
        public string EngineStartOrderDisplay
        {
            get
            {
                if (_engineStartOrder.Count == 0) return "—";
                return string.Join(" → ", _engineStartOrder);
            }
        }

        /// <summary>True si el orden de arranque coincide con el estándar Patagonia Wings
        /// (2-1 para bimotores, 4-2-3-1 para cuadrimotores).</summary>
        public bool EngineStartOrderCorrect
        {
            get
            {
                var order = _engineStartOrder;
                if (order.Count == 2) return order[0] == 2 && order[1] == 1;
                if (order.Count == 4) return order[0] == 4 && order[1] == 2 && order[2] == 3 && order[3] == 1;
                return order.Count > 0;
            }
        }
        public string SquawkDisplay => !HasLiveTelemetry ? "----" : (Squawk > 0 ? Squawk.ToString("0000") : "----");

        public string TransponderModeDisplay
        {
            get
            {
                if (!ShowTransponderSystem || !HasLiveTelemetry) return "OFF";
                return _transponderStateRaw > 0 ? "ON" : "OFF";
            }
        }

        public string PhaseLabelDisplay => PhaseLabel;
        public string ElapsedTimeDisplay => ElapsedTime;
        public string SimBackendDisplay
        {
            get
            {
                var runtime = AcarsContext.Runtime;
                var backend = runtime.SimulatorBackend;
                if (runtime.IsSimulatorConnected)
                {
                    return string.IsNullOrWhiteSpace(backend)
                        ? "Conectado"
                        : backend + " · " + runtime.SimulatorType;
                }

                if (string.IsNullOrWhiteSpace(backend))
                {
                    return "Sin conexión";
                }

                return runtime.HasTelemetry
                    ? "Sin telemetría · " + backend
                    : "Esperando · " + backend;
            }
        }
        public bool IsSimConnected => AcarsContext.Runtime.IsSimulatorConnected;

        public ICommand ConnectMsfsCommand { get; }
        public ICommand ConnectXPlaneCommand { get; }
        public ICommand DisconnectSimCommand { get; }
        public ICommand FinishFlightCommand { get; }

        private readonly System.Windows.Threading.DispatcherTimer _elapsedTimer;

        public InFlightViewModel(MainViewModel main)
        {
            _main = main;
            ConnectMsfsCommand = new RelayCommand(() => _main.NavigateToSimulatorConnect());
            ConnectXPlaneCommand = new RelayCommand(() => _main.NavigateToSimulatorConnect());
            DisconnectSimCommand = new RelayCommand(() => { });
            FinishFlightCommand = new RelayCommand(() => FinishFlight(), () => AcarsContext.FlightService.IsFlightActive && (Phase == FlightPhase.Arrived || (OnGround && GS < 3)));

            _elapsedTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (_, __) =>
            {
                if (_startTime != default(DateTime))
                {
                    ElapsedTime = (DateTime.UtcNow - _startTime).ToString(@"hh\:mm\:ss");
                }
            };

            AcarsContext.FlightService.TelemetryUpdated += OnTelemetry;
            AcarsContext.FlightService.PhaseChanged += OnPhaseChanged;
            AcarsContext.Runtime.Changed += OnRuntimeChanged;
            ApplyRuntimeState();
            RefreshRouteSnapshot();
        }

        public void StartElapsedTimer()
        {
            if (_startTime != default(DateTime)) return; // ya está corriendo
            _startTime = DateTime.UtcNow;
            _elapsedTimer.Start();
        }

        private void OnRuntimeChanged()
        {
            Application.Current.Dispatcher.Invoke(ApplyRuntimeState);
        }

        private void ApplyRuntimeState()
        {
            OnPropertyChanged(nameof(HasLiveTelemetry));
            OnPropertyChanged(nameof(ShowGroundSpeedMetric));
            OnPropertyChanged(nameof(IsC208Family));
            OnPropertyChanged(nameof(ShowSeatbeltSystem));
            OnPropertyChanged(nameof(ShowAutopilotSystem));
            OnPropertyChanged(nameof(ShowTransponderSystem));
            OnPropertyChanged(nameof(ShowDoorsSystem));
            OnPropertyChanged(nameof(SimBackendDisplay));
            OnPropertyChanged(nameof(IsSimConnected));
            
            // Notificar a todas las propiedades Display que dependen de HasLiveTelemetry
            OnPropertyChanged(nameof(IasDisplay));
            OnPropertyChanged(nameof(IASDisplay));
            OnPropertyChanged(nameof(GsDisplay));
            OnPropertyChanged(nameof(GSDisplay));
            OnPropertyChanged(nameof(AltitudeDisplay));
            OnPropertyChanged(nameof(VsDisplay));
            OnPropertyChanged(nameof(VSDisplay));
            OnPropertyChanged(nameof(HeadingDisplay));
            OnPropertyChanged(nameof(FuelDisplay));
            OnPropertyChanged(nameof(FuelKgDisplay));
            OnPropertyChanged(nameof(FuelLbsDisplay));
            OnPropertyChanged(nameof(FlapsDisplay));
            OnPropertyChanged(nameof(N1Eng1Display));
            OnPropertyChanged(nameof(N1Eng2Display));
            OnPropertyChanged(nameof(SquawkDisplay));
            OnPropertyChanged(nameof(TransponderModeDisplay));
            OnPropertyChanged(nameof(LiveTransponderOff));
            OnPropertyChanged(nameof(LiveTransponderStandby));
            OnPropertyChanged(nameof(LiveTransponderTest));
            OnPropertyChanged(nameof(LiveTransponderOn));
            OnPropertyChanged(nameof(LiveTransponderModeC));
            OnPropertyChanged(nameof(FuelLeftTankDisplay));
            OnPropertyChanged(nameof(FuelRightTankDisplay));
            OnPropertyChanged(nameof(FuelCenterTankDisplay));
            OnPropertyChanged(nameof(FuelCapacityDisplay));
            // Live* props del panel SISTEMAS dependen de HasLiveTelemetry
            OnPropertyChanged(nameof(LiveParkingBrakeOn));
            OnPropertyChanged(nameof(LiveAutopilotOn));
            OnPropertyChanged(nameof(LiveSeatBeltSign));
            OnPropertyChanged(nameof(LiveNoSmokingSign));
            OnPropertyChanged(nameof(LiveDoorOpen));
            OnPropertyChanged(nameof(LiveCharlieMode));

            // Pesos
            OnPropertyChanged(nameof(HasDispatchWeights));
            OnPropertyChanged(nameof(WbPlanFuelKg));
            OnPropertyChanged(nameof(WbPlanPayloadKg));
            OnPropertyChanged(nameof(WbPlanZfwKg));
            OnPropertyChanged(nameof(WbPlanFuelDisplay));
            OnPropertyChanged(nameof(WbPlanPayloadDisplay));
            OnPropertyChanged(nameof(WbPlanZfwDisplay));
            OnPropertyChanged(nameof(WbActualFuelDisplay));
            OnPropertyChanged(nameof(WbActualZfwDisplay));
            OnPropertyChanged(nameof(WbFuelStatusLabel));
            OnPropertyChanged(nameof(RouteDisplay));
            OnPropertyChanged(nameof(OfficialPhaseDisplay));
            OnPropertyChanged(nameof(RouteStatusLabel));
            OnPropertyChanged(nameof(RouteProgressDisplay));
            OnPropertyChanged(nameof(RouteTrackWidth));
            OnPropertyChanged(nameof(RoutePlaneLeft));
            OnPropertyChanged(nameof(RouteDistanceDisplay));

            UpdatePirepPreview();
            RefreshRouteSnapshot();

            if (!AcarsContext.Runtime.IsSimulatorConnected)
            {
                ClearTelemetrySnapshot();
            }
        }

        private void OnTelemetry(SimData data)
        {
            Debug.WriteLine($"[InFlightVM] OnTelemetry - ACFT:{data.AircraftTitle} ALT={data.AltitudeFeet:F0} FUEL={data.FuelTotalLbs:F0}");
            Application.Current.Dispatcher.Invoke(() =>
            {
                // ── Perfil normalizado ─────────────────────────────────────────
                if (_detectedProfileCode != data.DetectedProfileCode)
                {
                    _detectedProfileCode = data.DetectedProfileCode;
                    OnPropertyChanged(nameof(DetectedProfileCode));
                    OnPropertyChanged(nameof(DetectedDisplayName));
                }
                AircraftTitle = data.AircraftTitle;
                Altitude    = data.AltitudeFeet;
                AltitudeAGL = data.AltitudeAGL;
                IAS = data.IndicatedAirspeed;
                GS = data.GroundSpeed;
                VS = data.VerticalSpeed;
                Heading = data.Heading;
                FuelLbs = data.FuelTotalLbs;
                // Combustible normalizado en kg (SimConnect convierte lbs→kg en SimConnectService)
                _fuelKgNorm = data.FuelKg > 0 ? data.FuelKg : (data.FuelTotalLbs / 2.20462);
                _totalWeightKg = data.TotalWeightKg > 0 ? data.TotalWeightKg : 0;
                _zeroFuelWeightKg = data.ZeroFuelWeightKg > 0
                    ? data.ZeroFuelWeightKg
                    : Math.Max(0, _totalWeightKg - _fuelKgNorm);
                OnPropertyChanged(nameof(FuelKg));
                OnPropertyChanged(nameof(ActualTotalWeightKg));
                OnPropertyChanged(nameof(ActualZeroFuelWeightKg));

                // Tanques individuales (para diagnóstico)
                FuelLeftTank = data.FuelLeftTankLbs;
                FuelRightTank = data.FuelRightTankLbs;
                FuelCenterTank = data.FuelCenterTankLbs;
                FuelCapacity = data.FuelTotalCapacityLbs;
                
                N1Eng1 = data.Engine1N1;
                N1Eng2 = data.Engine2N1;
                OAT = Math.Round(data.OutsideTemperature, 1);
                WindSpeed = Math.Round(data.WindSpeed, 0);
                WindDir = Math.Round(data.WindDirection, 0);
                Lat = data.Latitude;
                Lon = data.Longitude;
                AutopilotOn = data.AutopilotActive;
                ParkingBrakeOn = data.ParkingBrake;
                DoorOpen = data.DoorOpen;
                OnGround = data.OnGround;
                StrobeOn = data.StrobeLightsOn;
                BeaconOn = data.BeaconLightsOn;
                LandingOn = data.LandingLightsOn;
                TaxiOn = data.TaxiLightsOn;
                NavOn = data.NavLightsOn;
                
                Debug.WriteLine($"[InFlightVM] Luces - STROBE:{data.StrobeLightsOn} BEACON:{data.BeaconLightsOn} LANDING:{data.LandingLightsOn} TAXI:{data.TaxiLightsOn} NAV:{data.NavLightsOn}");
                SeatBeltSign = data.SeatBeltSign;
                NoSmokingSign = data.NoSmokingSign;
                GearDown = data.GearDown;
                GearTransitioning = data.GearTransitioning;
                FlapsPercent = Math.Round(data.FlapsPercent, 0);
                SpoilersArmed = data.SpoilersArmed;
                ReverserActive = data.ReverserActive;
                CharlieMode = data.TransponderCharlieMode;
                TransponderStateRaw = data.TransponderStateRaw;
                Squawk = data.TransponderCode;
                Debug.WriteLine($"[InFlightVM] XPDR state:{data.TransponderStateRaw} squawk:{data.TransponderCode} mode:{TransponderModeDisplay}");
                ApuRunning = data.ApuRunning;
                ApuAvailable = data.ApuAvailable;
                BleedAirOn = data.BleedAirOn;
                CabinAlt = Math.Round(data.CabinAltitudeFeet, 0);
                PressDiff = Math.Round(data.PressureDiffPsi, 2);
                // ── Detectar primer arranque de motores ────────────────────────
                // Cuando N1 supera el 15% por primera vez se captura el combustible actual.
                // Ese valor se usa para comparar con el plan SimBrief (±10% de tolerancia).
                if (_fuelAtEngineStartKg < 0 && (data.Engine1N1 > 15 || data.Engine2N1 > 15))
                {
                    _fuelAtEngineStartKg = FuelKg;
                    OnPropertyChanged(nameof(WbFuelAtEngineStartKg));
                    OnPropertyChanged(nameof(WbFuelMatchOk));
                    OnPropertyChanged(nameof(WbFuelDiffDisplay));
                    OnPropertyChanged(nameof(WbFuelStatusLabel));
                    OnPropertyChanged(nameof(WbStartFuelDisplay));
                }

                // ── Orden de arranque de motores (IGN 2-1 / 4-2-3-1) ──────────
                if (!_eng2Started && data.Engine2N1 >= EngineN1StartThreshold) { _eng2Started = true; _engineStartOrder.Add(2); OnPropertyChanged(nameof(EngineStartOrderDisplay)); }
                if (!_eng1Started && data.Engine1N1 >= EngineN1StartThreshold) { _eng1Started = true; _engineStartOrder.Add(1); OnPropertyChanged(nameof(EngineStartOrderDisplay)); }
                if (!_eng4Started && data.Engine4N1 >= EngineN1StartThreshold) { _eng4Started = true; _engineStartOrder.Add(4); OnPropertyChanged(nameof(EngineStartOrderDisplay)); }
                if (!_eng3Started && data.Engine3N1 >= EngineN1StartThreshold) { _eng3Started = true; _engineStartOrder.Add(3); OnPropertyChanged(nameof(EngineStartOrderDisplay)); }

                // ── Detectar y registrar eventos de procedimiento ──────────────
                LogLightEvent("BEACON",  _prevBeaconOn,  data.BeaconLightsOn,  data.OnGround);
                LogLightEvent("STROBE",  _prevStrobeOn,  data.StrobeLightsOn,  data.OnGround);
                LogLightEvent("LANDING", _prevLandingOn, data.LandingLightsOn, data.OnGround);
                LogLightEvent("TAXI",    _prevTaxiOn,    data.TaxiLightsOn,    data.OnGround);
                LogLightEvent("NAV",     _prevNavOn,     data.NavLightsOn,     data.OnGround);
                if (_prevApOn != data.AutopilotActive)
                    _eventLog.Add($"{PhaseLabel}: AP {(data.AutopilotActive ? "ENGAG." : "DESCONECT.")}");
                if (ShowSeatbeltSystem && _prevSeatBelt != data.SeatBeltSign)
                    _eventLog.Add($"{PhaseLabel}: Cinturones {(data.SeatBeltSign ? "ON" : "OFF")}");
                _prevBeaconOn  = data.BeaconLightsOn;
                _prevStrobeOn  = data.StrobeLightsOn;
                _prevLandingOn = data.LandingLightsOn;
                _prevTaxiOn    = data.TaxiLightsOn;
                _prevNavOn     = data.NavLightsOn;
                _prevApOn      = data.AutopilotActive;
                _prevSeatBelt  = data.SeatBeltSign;

                // ── Notificar pesos actuales y log PIREP ────────────────────────
                OnPropertyChanged(nameof(WbActualFuelKg));
                OnPropertyChanged(nameof(WbActualFuelDisplay));
                OnPropertyChanged(nameof(WbActualZfwDisplay));
                OnPropertyChanged(nameof(WbFuelStatusLabel));
                UpdatePirepPreview();
                RefreshRouteSnapshot(data);

                ApplyRuntimeState();
                CheckAlerts(data);
            });
        }

        private void ClearTelemetrySnapshot()
        {
            Altitude = 0;
            IAS = 0;
            GS = 0;
            VS = 0;
            Heading = 0;
            FuelLbs = 0;
            N1Eng1 = 0;
            N1Eng2 = 0;
            OAT = 0;
            WindSpeed = 0;
            WindDir = 0;
            Lat = 0;
            Lon = 0;
            AutopilotOn = false;
            ParkingBrakeOn = false;
            DoorOpen = false;
            OnGround = false;
            StrobeOn = false;
            BeaconOn = false;
            LandingOn = false;
            TaxiOn = false;
            NavOn = false;
            SeatBeltSign = false;
            NoSmokingSign = false;
            GearDown = false;
            GearTransitioning = false;
            FlapsPercent = 0;
            SpoilersArmed = false;
            ReverserActive = false;
            CharlieMode = false;
            TransponderStateRaw = 0;
            Squawk = 0;
            ApuRunning = false;
            ApuAvailable = false;
            BleedAirOn = false;
            CabinAlt = 0;
            PressDiff = 0;
            AlertNoStrobe = false;
            AlertPause = false;
            _fuelAtEngineStartKg = -1;    // resetear al desconectar
            _fuelKgNorm = 0;
            _totalWeightKg = 0;
            _zeroFuelWeightKg = 0;
            _startTime = default(DateTime);
            _elapsedTimer.Stop();
            ElapsedTime = "00:00:00";
            _eventLog.Clear();
            _prevBeaconOn = _prevStrobeOn = _prevLandingOn = _prevTaxiOn = _prevNavOn = _prevApOn = _prevSeatBelt = false;
            _eng1Started = _eng2Started = _eng3Started = _eng4Started = false;
            _engineStartOrder.Clear();
            RefreshRouteSnapshot();
        }

        private void CheckAlerts(SimData data)
        {
            if (!data.OnGround && !data.StrobeLightsOn && data.IndicatedAirspeed > 60)
            {
                if (!AlertNoStrobe)
                {
                    AlertNoStrobe = true;
                    _ = AcarsContext.Sound.PlayGroundNoLightsAsync();
                }
            }
            else
            {
                AlertNoStrobe = false;
            }

            var nowAbove = data.AltitudeFeet > 10000;
            if (nowAbove && !_above10000)
            {
                _ = AcarsContext.Sound.PlayCopilot10000PiesAscAsync();
            }
            else if (!nowAbove && _above10000 && data.VerticalSpeed < -100)
            {
                _ = AcarsContext.Sound.PlayCopilot10000PiesDescAsync();
            }
            _above10000 = nowAbove;

            if (Phase == FlightPhase.Approach && data.AltitudeFeet < 1000 && !data.OnGround)
            {
                AcarsContext.Runtime.SetTelemetry(data, AcarsContext.Runtime.SimulatorBackend);
                
                // Forzar actualización de todas las propiedades Display
                OnPropertyChanged(nameof(IasDisplay));
                OnPropertyChanged(nameof(IASDisplay));
                OnPropertyChanged(nameof(GsDisplay));
                OnPropertyChanged(nameof(GSDisplay));
                OnPropertyChanged(nameof(AltitudeDisplay));
                OnPropertyChanged(nameof(VsDisplay));
                OnPropertyChanged(nameof(VSDisplay));
                OnPropertyChanged(nameof(HeadingDisplay));
                OnPropertyChanged(nameof(FuelDisplay));
                OnPropertyChanged(nameof(FuelKgDisplay));
                OnPropertyChanged(nameof(FuelLbsDisplay));
                OnPropertyChanged(nameof(FlapsDisplay));
                OnPropertyChanged(nameof(N1Eng1Display));
                OnPropertyChanged(nameof(N1Eng2Display));
                OnPropertyChanged(nameof(SquawkDisplay));
                OnPropertyChanged(nameof(FuelLeftTankDisplay));
                OnPropertyChanged(nameof(FuelRightTankDisplay));
                OnPropertyChanged(nameof(FuelCenterTankDisplay));
                OnPropertyChanged(nameof(FuelCapacityDisplay));
            }
        }

        private void OnPhaseChanged(FlightPhase newPhase)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Phase = newPhase;
                // Registrar cambio de fase en el log de eventos
                if (newPhase != FlightPhase.Disconnected && newPhase != FlightPhase.PreFlight)
                    _eventLog.Add($"▶ FASE: {GetPhaseLabel(newPhase)}");
                UpdatePirepPreview();
                RefreshRouteSnapshot();
                CommandManager.InvalidateRequerySuggested();
                switch (newPhase)
                {
                    case FlightPhase.Boarding:
                        _ = AcarsContext.Sound.PlayGroundBoardingAsync();
                        break;
                    case FlightPhase.PushbackTaxi:
                        _ = AcarsContext.Sound.PlayGroundDoorClosedAsync();
                        // Arrancar cronómetro al block-out (inicio oficial del vuelo)
                        if (_startTime == default(DateTime)) StartElapsedTimer();
                        break;
                    case FlightPhase.Takeoff:
                        _ = AcarsContext.Sound.PlayGroundEnginesAsync();
                        // Arrancar cronómetro si aún no inició (vuelos sin rodaje previo)
                        if (_startTime == default(DateTime)) StartElapsedTimer();
                        StartElapsedTimer();
                        break;
                    case FlightPhase.Arrived:
                        _ = AcarsContext.Sound.PlayGroundArrivedAsync();
                        _elapsedTimer.Stop();
                        break;
                }
            });
        }

        private void FinishFlight()
        {
            var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            if (pilot == null)
            {
                return;
            }

            var report = AcarsContext.FlightService.GenerateReport(pilot.CallSign);
            _main.ShowPostFlightReport(report);
        }

        private static string GetPhaseLabel(FlightPhase phase)
        {
            return phase switch
            {
                FlightPhase.PreFlight => "Prevuelo",
                FlightPhase.Boarding => "Embarque",
                FlightPhase.PushbackTaxi => "Rodaje",
                FlightPhase.Takeoff => "Despegue",
                FlightPhase.Climb => "Ascenso",
                FlightPhase.Cruise => "Crucero",
                FlightPhase.Descent => "Descenso",
                FlightPhase.Approach => "Aproximación",
                FlightPhase.Landing => "Aterrizaje",
                FlightPhase.Taxi => "Taxi in",
                FlightPhase.Arrived => "Arribado",
                _ => "Desconectado"
            };
        }
        
        private void DetectAircraftType(string title)
        {
            // ── Perfil normalizado via AircraftNormalizationService ────────────────
            var profile = AircraftNormalizationService.ResolveProfile(title);
            var t = (title ?? string.Empty).ToUpperInvariant();

            // Usar capacidades del perfil para determinar APU, presurización y motores
            HasApu            = profile.HasApu;
            HasPressurization = profile.IsPressurized;
            IsSingleEngine    = profile.EngineCount == 1;

            // Si el perfil es MSFS_NATIVE (fallback), mantener la detección manual
            // para aeronaves que aún no estén en el catálogo
            if (profile.Code == "MSFS_NATIVE")
            {
                bool isJet =
                    t.Contains("A318") || t.Contains("A319") || t.Contains("A320") || t.Contains("A321") ||
                    t.Contains("A330") || t.Contains("A339") || t.Contains("A340") || t.Contains("A350") ||
                    t.Contains("A380") || t.Contains("ACJ") || t.Contains("BBJ") || t.Contains("AIRBUS") ||
                    t.Contains("B717") || t.Contains("B727") || t.Contains("B737") || t.Contains("B738") ||
                    t.Contains("B739") || t.Contains("B38M") || t.Contains("B747") || t.Contains("B757") ||
                    t.Contains("B767") || t.Contains("B777") || t.Contains("B787") || t.Contains("B78X") ||
                    t.Contains("B77W") || t.Contains("B772") || t.Contains("B789") || t.Contains("BOEING") ||
                    t.Contains("CRJ") || t.Contains("E170") || t.Contains("E175") || t.Contains("E190") ||
                    t.Contains("E195") || t.Contains("MD80") || t.Contains("MD82") || t.Contains("MD83") ||
                    t.Contains("MD88") || t.Contains("MD90") || t.Contains("MD11") || t.Contains("DC9") ||
                    t.Contains("FENIX") || t.Contains("HEADWIND") || t.Contains("FLYBYWIRE") ||
                    t.Contains("PMDG") || t.Contains("LEONARDO");

                bool isPressurizedTurboprop =
                    t.Contains("ATR") || t.Contains("DHC-8") || t.Contains("DASH 8") || t.Contains("DASH8") ||
                    t.Contains("Q400") || t.Contains("TBM") || t.Contains("KING AIR") || t.Contains("B350") ||
                    t.Contains("PC-12") || t.Contains("PC12") || t.Contains("PILATUS") ||
                    t.Contains("M600") || t.Contains("MERIDIAN");

                HasApu            = profile.HasApu || (profile.Code == "MSFS_NATIVE" && isJet);
                HasPressurization = profile.IsPressurized || (profile.Code == "MSFS_NATIVE" && (isJet || isPressurizedTurboprop));
                _supportsSeatbeltSystem = profile.SupportsSeatbeltSystem;
                _supportsNoSmokingSystem = profile.SupportsNoSmokingSystem;
                _supportsBleedAirSystem = profile.SupportsBleedAirSystem;
                IsSingleEngine    =
                    t.Contains("C208") || t.Contains("CARAVAN") || t.Contains("GRAND CARAVAN") ||
                    t.Contains("TBM") || t.Contains("PC-12") || t.Contains("PC12") || t.Contains("PILATUS") ||
                    t.Contains("M600") || t.Contains("MERIDIAN") || t.Contains("SR20") || t.Contains("SR22") ||
                    t.Contains("CIRRUS") || t.Contains("DA40") || t.Contains("C172") || t.Contains("C152") ||
                    t.Contains("C182") || t.Contains("C206") || t.Contains("PA28") || t.Contains("PA-28");
            }

            RequiresLvars =
                profile.RequiresLvars
                || profile.UsesLvarSeatbelt
                || profile.UsesLvarNoSmoking
                || profile.UsesLvarDoor
                || profile.UsesLvarAutopilot
                || profile.UsesLvarApu
                || profile.UsesLvarBleedAir
                || (profile.Code == "MSFS_NATIVE" && (
                    t.Contains("FENIX") || t.Contains("FLYBYWIRE") || t.Contains("A32NX") ||
                    t.Contains("HEADWIND") || t.Contains("TOLISS")));

            AircraftStatus = RequiresLvars
                ? $"{profile.AddonProvider} · Algunos datos via LVARs"
                : $"{profile.AddonProvider} · SimConnect completo";

            Debug.WriteLine($"[InFlightVM] '{title}' → Code:{profile.Code} APU:{HasApu} Presurizado:{HasPressurization} Monomotor:{IsSingleEngine} LVARs:{RequiresLvars}");
            OnPropertyChanged(nameof(RequiresLvars));
            OnPropertyChanged(nameof(AircraftStatus));
            OnPropertyChanged(nameof(ShowApu));
            OnPropertyChanged(nameof(HasPressurization));
            OnPropertyChanged(nameof(IsSingleEngine));
            OnPropertyChanged(nameof(IsC208Family));
            OnPropertyChanged(nameof(ShowSeatbeltSystem));
            OnPropertyChanged(nameof(ShowNoSmokingSystem));
            OnPropertyChanged(nameof(ShowAutopilotSystem));
            OnPropertyChanged(nameof(ShowTransponderSystem));
            OnPropertyChanged(nameof(ShowDoorsSystem));
            OnPropertyChanged(nameof(ShowBleedAirSystem));
        }
    }
}
