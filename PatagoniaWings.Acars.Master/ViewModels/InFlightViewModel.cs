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
        private double _groundElevation;
        private double _pressureAltitude;
        private string _flightLevel = string.Empty;
        private string _displayAltitudeMode = "MSL";
        private string _displayAltitudeText = string.Empty;
        private bool _isAltitudeReliable;
        private string _altitudeSource = string.Empty;
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
        private double _n1Eng3;
        private double _n1Eng4;
        private bool _batteryMasterOn;
        private bool _avionicsMasterOn;
        private double _electricalMainBusVoltage;
        private string _manualCloseoutStatus = "Cierre manual: aterrice, llegue a gate, freno parking, motores OFF y Cold & Dark.";
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
        private double _qnh;
        private string _radioAcarsMessage = string.Empty;

        // â”€â”€ PIC Radio Check â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private bool _picConfirmed;
        private double _picFrequency;
        private double _com1FrequencyMhz;
        private double _com1StandbyFrequencyMhz;
        private double _com2FrequencyMhz;
        private double _com2StandbyFrequencyMhz;
        private string _lastPicMatchedRadio = string.Empty;
        private bool _picCheckActive;
        private int _picSecondsLeft;
        private int _picChecksTotal;
        private int _picChecksDone;
        private int _picPenaltyPoints;
        private DateTime _lastPicCheckTime = DateTime.MinValue;
        private static readonly Random _picRandom = new Random();
        private static readonly double[] _picFrequencies = {
            118.000, 119.500, 121.700, 122.800, 125.000, 127.000, 128.300, 133.000, 135.000
        };
        private const double PicFrequencyToleranceMhz = 0.015d; // Â±15 kHz para radios 25/8.33 kHz y redondeo SimConnect
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
        private bool _supportsTransponderSystem = true;
        private bool _supportsDoorSystem;
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
        private string _phaseChecklistStatus = "PENDING";
        private string _phaseChecklistSummary = string.Empty;
        private string _phaseChecklistMissing = string.Empty;
        private string _phaseTransitionDisplay = string.Empty;
        private string _phaseAuditStatus = "PENDING";
        private string _phaseAuditSummary = string.Empty;
        private string _phaseAuditFlags = string.Empty;
        private string _phaseReviewQuestion = string.Empty;
        private string _phaseMeasuredMetrics = string.Empty;
        private string _phasePrevalidationStatus = "PENDING";
        private string _phasePrevalidationSummary = string.Empty;
        private string _phasePrevalidationFlags = string.Empty;
        private string _routeStatusLabel = "Esperando inicio oficial";
        private double _routeProgressPercent;
        private const double RouteCanvasWidth = 320;
        private const double RoutePlaneVisualWidth = 20;

        // Patagonia Wings 7.0.14 hotfix:
        // La UI/recorder de un vuelo activo no debe caer a Desconectado tras
        // touchdown o microcortes del simulador. Solo se limpia al finalizar en gate
        // o al cancelar explicitamente.
        private FlightPhase _lastValidActivePhase = FlightPhase.PreFlight;
        private bool _hasBeenAirborne;

        // â”€â”€ Pesos / comparaciÃ³n SimBrief vs Sim â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private double _fuelAtEngineStartKg = -1; // -1 = no capturado aÃºn
        private string _pirepPreview = string.Empty;

        // â”€â”€ Perfil de aeronave normalizado â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private string _detectedProfileCode = "MSFS_NATIVE";

        // â”€â”€ Log de eventos de procedimiento (para PIREP en tiempo real) â”€â”€â”€â”€â”€â”€â”€
        private readonly System.Collections.Generic.List<string> _eventLog = new();
        private bool _prevBeaconOn;
        private bool _prevStrobeOn;
        private bool _prevLandingOn;
        private bool _prevTaxiOn;
        private bool _prevNavOn;
        private bool _prevApOn;
        private bool _prevSeatBelt;

        // â”€â”€ Orden de arranque de motores (arquitectura PRE/IGN) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private readonly System.Collections.Generic.List<int> _engineStartOrder = new();
        private bool _eng1Started;
        private bool _eng2Started;
        private bool _eng3Started;
        private bool _eng4Started;
        private const double EngineN1StartThreshold = 15.0;

        public double Altitude    { get => _altitude;    set { if (SetField(ref _altitude,    value)) { OnPropertyChanged(nameof(AltitudeDisplay)); OnPropertyChanged(nameof(AltitudeMslDisplay)); } } }
        public double AltitudeAGL { get => _altitudeAgl; set { if (SetField(ref _altitudeAgl, value)) { OnPropertyChanged(nameof(AglDisplay)); OnPropertyChanged(nameof(AltitudeAglDisplay)); RefreshManualCloseoutState(); } } }
        public double GroundElevation { get => _groundElevation; set { if (SetField(ref _groundElevation, value)) OnPropertyChanged(nameof(GroundElevationDisplay)); } }
        public double PressureAltitude { get => _pressureAltitude; set { if (SetField(ref _pressureAltitude, value)) OnPropertyChanged(nameof(PressureAltitudeDisplay)); } }
        public string FlightLevel { get => _flightLevel; set { if (SetField(ref _flightLevel, value ?? string.Empty)) OnPropertyChanged(nameof(FlightLevelDisplay)); } }
        public string DisplayAltitudeMode { get => _displayAltitudeMode; set { if (SetField(ref _displayAltitudeMode, value ?? "MSL")) OnPropertyChanged(nameof(AltitudeDisplay)); } }
        public string DisplayAltitudeText { get => _displayAltitudeText; set { if (SetField(ref _displayAltitudeText, value ?? string.Empty)) OnPropertyChanged(nameof(AltitudeDisplay)); } }
        public bool IsAltitudeReliable { get => _isAltitudeReliable; set { if (SetField(ref _isAltitudeReliable, value)) OnPropertyChanged(nameof(AltitudeReliabilityDisplay)); } }
        public string AltitudeSource { get => _altitudeSource; set { if (SetField(ref _altitudeSource, value ?? string.Empty)) OnPropertyChanged(nameof(AltitudeReliabilityDisplay)); } }
        public double IAS { get => _ias; set { if (SetField(ref _ias, value)) { OnPropertyChanged(nameof(IasDisplay)); OnPropertyChanged(nameof(IASDisplay)); } } }
        public double GS { get => _gs; set { if (SetField(ref _gs, value)) { OnPropertyChanged(nameof(GsDisplay)); OnPropertyChanged(nameof(GSDisplay)); RefreshManualCloseoutState(); } } }
        public double VS { get => _vs; set { if (SetField(ref _vs, value)) { OnPropertyChanged(nameof(VsDisplay)); OnPropertyChanged(nameof(VSDisplay)); } } }
        public double Heading { get => _heading; set { if (SetField(ref _heading, value)) OnPropertyChanged(nameof(HeadingDisplay)); } }
        public double FuelLbs { get => _fuelLbs; set { if (SetField(ref _fuelLbs, value)) { OnPropertyChanged(nameof(FuelKg)); OnPropertyChanged(nameof(FuelDisplay)); OnPropertyChanged(nameof(FuelSourceDisplay)); OnPropertyChanged(nameof(FuelKgDisplay)); OnPropertyChanged(nameof(FuelLbsDisplay)); } } }
        // FuelKg: normalizado en kg por el backend (SimConnect convierte lbsâ†’kg, FSUIPC es nativo en kg)
        // Usamos _fuelKgNorm que se actualiza en OnTelemetry desde data.FuelKg
        private double _fuelKgNorm;
        private double _totalWeightKg;
        private double _zeroFuelWeightKg;
        public double FuelKg => Math.Round(_fuelKgNorm, 0);
        public double ActualTotalWeightKg => Math.Round(_totalWeightKg, 0);
        public double ActualZeroFuelWeightKg => Math.Round(_zeroFuelWeightKg, 0);
        
        // Propiedades de tanques individuales (visibles para diagnÃ³stico de aviones complejos)
        public double FuelLeftTank { get => _fuelLeftTank; set { if (SetField(ref _fuelLeftTank, value)) OnPropertyChanged(nameof(FuelLeftTankDisplay)); } }
        public double FuelRightTank { get => _fuelRightTank; set { if (SetField(ref _fuelRightTank, value)) OnPropertyChanged(nameof(FuelRightTankDisplay)); } }
        public double FuelCenterTank { get => _fuelCenterTank; set { if (SetField(ref _fuelCenterTank, value)) OnPropertyChanged(nameof(FuelCenterTankDisplay)); } }
        public double FuelCapacity { get => _fuelCapacity; set { if (SetField(ref _fuelCapacity, value)) { OnPropertyChanged(nameof(FuelCapacityDisplay)); OnPropertyChanged(nameof(FuelDisplay)); OnPropertyChanged(nameof(FuelSourceDisplay)); } } }
        
        public string FuelLeftTankDisplay => HasLiveTelemetry ? $"{Math.Round(FuelLeftTank, 0):F0}" : "---";
        public string FuelRightTankDisplay => HasLiveTelemetry ? $"{Math.Round(FuelRightTank, 0):F0}" : "---";
        public string FuelCenterTankDisplay => HasLiveTelemetry ? $"{Math.Round(FuelCenterTank, 0):F0}" : "---";
        public string FuelCapacityDisplay => HasLiveTelemetry ? $"{Math.Round(FuelCapacity, 0):F0}" : "---";
        public double N1Eng1 { get => _n1Eng1; set { if (SetField(ref _n1Eng1, value)) OnPropertyChanged(nameof(N1Eng1Display)); } }
        private string FuelCapacityForDisplay
        {
            get
            {
                if (FuelCapacity > 10)
                {
                    return Math.Round(FuelCapacity, 0).ToString("F0");
                }

                return "N/D";
            }
        }
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
        ///   1. {ICAO_detectado}.png  â†’ ej. A320.png, B738.png, C208.png
        ///   2. {ICAO_detectado}.jpg
        /// Si no existe ningÃºn archivo, retorna null y el XAML muestra el placeholder de iniciales.
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

                // 1. Buscar por cÃ³digo ICAO detectado
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
                        // tambiÃ©n probar el nombre sin prefijo de addon (ej. "a320_fenix.png" â†’ "A320.png")
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

        /// <summary>Addon/variante esperado segÃºn el despacho activo.</summary>
        public string ExpectedVariantLabel
        {
            get
            {
                var flight = AcarsContext.FlightService.CurrentFlight;
                if (flight == null) return "â€”";
                var addon   = (flight.AddonProvider      ?? string.Empty).Trim();
                var variant = (flight.AircraftVariantCode ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(addon) && string.IsNullOrEmpty(variant)) return "EstÃ¡ndar";
                if (!string.IsNullOrEmpty(addon) && !string.IsNullOrEmpty(variant))
                    return $"{addon} Â· {variant}";
                return !string.IsNullOrEmpty(addon) ? addon : variant;
            }
        }

        /// <summary>Addon detectado en el tÃ­tulo del aviÃ³n del simulador.</summary>
        public string DetectedAddonLabel
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_aircraftTitle)) return "â€”";
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
                if (string.IsNullOrEmpty(expected)) return true; // sin restricciÃ³n
                var detected = DetectedAddonLabel;
                return detected.IndexOf(expected, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || expected.IndexOf(detected, System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public string VariantMatchDisplay => VariantMatchOk ? "âœ“ OK" : "âš  Verificar";

        /// <summary>
        /// Iniciales para mostrar en el placeholder de foto de aeronave.
        /// Ej: "A320" â†’ "A32", "B738" â†’ "B73", "C208" â†’ "C20"
        /// </summary>
        public string AircraftImageInitials
        {
            get
            {
                if (!HasLiveTelemetry || string.IsNullOrWhiteSpace(_aircraftTitle)) return "---";
                // Tomar los primeros 4 caracteres del tÃ­tulo en mayÃºsculas, eliminando espacios
                var t = _aircraftTitle.ToUpperInvariant();
                // Intentar extraer el tipo ICAO del tÃ­tulo
                // Mapa de palabras clave a cÃ³digo de imagen (orden importa: mÃ¡s especÃ­fico primero)
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
                    // Cessna 208 Caravan â€” incluye "208B", "208", "CARAVAN"
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

        /// <summary>CÃ³digo estable normalizado. Ej: C208_MSFS, B738_PMDG, A320_FENIX</summary>
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
            if (!HasLiveTelemetry) return "Sin conexiÃ³n";
            return AircraftTitle;
        }
        public bool AutopilotOn { get => _autopilotOn; set { if (SetField(ref _autopilotOn, value)) OnPropertyChanged(nameof(LiveAutopilotOn)); } }
        public bool ParkingBrakeOn { get => _parkingBrakeOn; set { if (SetField(ref _parkingBrakeOn, value)) { OnPropertyChanged(nameof(LiveParkingBrakeOn)); RefreshManualCloseoutState(); } } }
        public bool DoorOpen { get => _doorOpen; set { if (SetField(ref _doorOpen, value)) { OnPropertyChanged(nameof(LiveDoorOpen)); OnPropertyChanged(nameof(DoorOpenPercentDisplay)); } } }
        public bool OnGround
        {
            get => _onGround;
            set
            {
                if (SetField(ref _onGround, value))
                {
                    OnPropertyChanged(nameof(LiveAutopilotOn));
                    OnPropertyChanged(nameof(AltitudeAglDisplay));
                    OnPropertyChanged(nameof(AglDisplay));
                    RefreshManualCloseoutState();
                }
            }
        }
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
        public bool ShowDoorsSystem => _supportsDoorSystem;
        public bool ShowTransponderSystem => _supportsTransponderSystem;
        public bool ShowBleedAirSystem => _supportsBleedAirSystem;

        // Props con guardia HasLiveTelemetry para el panel SISTEMAS
        public bool LiveParkingBrakeOn => HasLiveTelemetry && _parkingBrakeOn;
        public bool LiveAutopilotOn => ShowAutopilotSystem && HasLiveTelemetry && _autopilotOn && !OnGround;
        public bool LiveSeatBeltSign => ShowSeatbeltSystem && HasLiveTelemetry && _seatBeltSign;
        public bool LiveNoSmokingSign => ShowNoSmokingSystem && HasLiveTelemetry && _noSmokingSign;
        public bool LiveDoorOpen => ShowDoorsSystem && HasLiveTelemetry && _doorOpen;
        public string DoorOpenPercentDisplay => HasLiveTelemetry ? (_doorOpen ? "100%" : "0%") : "---";
        public bool LiveCharlieMode => ShowTransponderSystem && HasLiveTelemetry && _charlieMode;
        public bool LiveTransponderOff => ShowTransponderSystem && HasLiveTelemetry && _transponderStateRaw == 0;
        public bool LiveTransponderStandby => ShowTransponderSystem && HasLiveTelemetry && _transponderStateRaw == 1;
        public bool LiveTransponderTest => ShowTransponderSystem && HasLiveTelemetry && _transponderStateRaw == 2;
        public bool LiveTransponderOn => ShowTransponderSystem && HasLiveTelemetry && (_charlieMode || _transponderStateRaw >= 3);
        public bool LiveTransponderModeC => ShowTransponderSystem && HasLiveTelemetry && _charlieMode;
        public bool LiveTransponderNd => !ShowTransponderSystem || !HasLiveTelemetry || _transponderStateRaw < 0;
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
                    OnPropertyChanged(nameof(LiveTransponderNd));
                    OnPropertyChanged(nameof(TransponderModeDisplay));
                }
            }
        }
        public int Squawk { get => _squawk; set { if (SetField(ref _squawk, value)) OnPropertyChanged(nameof(SquawkDisplay)); } }
        public bool ApuRunning   { get => _apuRunning;   set => SetField(ref _apuRunning,   value); }
        public bool ApuAvailable { get => _apuAvailable; set => SetField(ref _apuAvailable, value); }

        /// <summary>Fila APU visible solo en jets comerciales/ejecutivos que tienen APU.</summary>
        public bool ShowApu => _hasApu;
        /// <summary>Fila Bleed Air visible en aeronaves presurizadas (jets + turbohÃ©lices presurizados).</summary>
        public bool HasPressurization
        {
            get => _hasPressurization;
            set { if (SetField(ref _hasPressurization, value)) { OnPropertyChanged(nameof(ShowApu)); OnPropertyChanged(nameof(ShowBleedAirSystem)); } }
        }
        /// <summary>True en jets comerciales/ejecutivos que tienen APU estÃ¡ndar.</summary>
        public bool HasApu
        {
            get => _hasApu;
            set { if (SetField(ref _hasApu, value)) OnPropertyChanged(nameof(ShowApu)); }
        }
        /// <summary>True para monomotores (C208, TBM, SR22, C172â€¦). Oculta el N1 del motor 2 en la UI.</summary>
        public bool IsSingleEngine { get => _isSingleEngine; set => SetField(ref _isSingleEngine, value); }
        public bool BleedAirOn { get => _bleedAirOn; set => SetField(ref _bleedAirOn, value); }
        public double CabinAlt { get => _cabinAlt; set => SetField(ref _cabinAlt, value); }
        public double PressDiff { get => _pressDiff; set => SetField(ref _pressDiff, value); }
        public FlightPhase Phase
        {
            get => _phase;
            set
            {
                if (SetField(ref _phase, value))
                {
                    PhaseLabel = GetPhaseLabel(value);
                    OnPropertyChanged(nameof(CanManualCloseout));
                    OnPropertyChanged(nameof(FinishFlightButtonText));
                }
            }
        }
        public string PhaseLabel { get => _phaseLabel; set { if (SetField(ref _phaseLabel, value)) OnPropertyChanged(nameof(PhaseLabelDisplay)); } }
        public string ElapsedTime { get => _elapsedTime; set { if (SetField(ref _elapsedTime, value)) OnPropertyChanged(nameof(ElapsedTimeDisplay)); } }
        public bool AlertNoStrobe { get => _alertNoStrobe; set => SetField(ref _alertNoStrobe, value); }
        public bool AlertPause { get => _alertPause; set => SetField(ref _alertPause, value); }
        public bool HasLiveTelemetry
        {
            get
            {
                if (AcarsContext.Runtime.IsSimulatorConnected && AcarsContext.Runtime.LastTelemetry != null)
                    return true;

                // Durante un vuelo activo no se deben apagar instrumentos ni UI por microcortes
                // o por el umbral de frescura del runtime. Se conserva la ultima muestra viva
                // hasta FINALIZAR EN GATE o CANCELAR VUELO.
                return AcarsContext.FlightService.IsFlightActive
                       && AcarsContext.FlightService.LastSimData != null
                       && AcarsContext.FlightService.LastSimData.CapturedAtUtc != default(DateTime);
            }
        }
        public string RouteOrigin => _routeOrigin;
        public string RouteDestination => _routeDestination;
        public string RouteDisplay => $"{RouteOrigin} â†’ {RouteDestination}";
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
        public string PhaseChecklistStatus
        {
            get => _phaseChecklistStatus;
            private set
            {
                if (SetField(ref _phaseChecklistStatus, value ?? "PENDING"))
                {
                    OnPropertyChanged(nameof(PhaseChecklistDisplay));
                }
            }
        }
        public string PhaseChecklistSummary
        {
            get => _phaseChecklistSummary;
            private set
            {
                if (SetField(ref _phaseChecklistSummary, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(PhaseChecklistDisplay));
                }
            }
        }
        public string PhaseChecklistMissing
        {
            get => _phaseChecklistMissing;
            private set
            {
                if (SetField(ref _phaseChecklistMissing, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(PhaseChecklistDisplay));
                }
            }
        }
        public string PhaseChecklistDisplay
        {
            get
            {
                var status = string.IsNullOrWhiteSpace(PhaseChecklistStatus) ? "PENDING" : PhaseChecklistStatus;
                var summary = string.IsNullOrWhiteSpace(PhaseChecklistSummary) ? "Checklist de fase pendiente" : PhaseChecklistSummary;
                if (!string.IsNullOrWhiteSpace(PhaseChecklistMissing))
                {
                    return $"{status}: {summary} · Falta: {PhaseChecklistMissing}";
                }
                return $"{status}: {summary}";
            }
        }
        public string PhaseTransitionDisplay
        {
            get => _phaseTransitionDisplay;
            private set => SetField(ref _phaseTransitionDisplay, value ?? string.Empty);
        }

        public string PhaseAuditStatus
        {
            get => _phaseAuditStatus;
            private set
            {
                if (SetField(ref _phaseAuditStatus, value ?? "PENDING"))
                {
                    OnPropertyChanged(nameof(PhaseAuditDisplay));
                }
            }
        }

        public string PhaseAuditSummary
        {
            get => _phaseAuditSummary;
            private set
            {
                if (SetField(ref _phaseAuditSummary, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(PhaseAuditDisplay));
                }
            }
        }

        public string PhaseAuditFlags
        {
            get => _phaseAuditFlags;
            private set
            {
                if (SetField(ref _phaseAuditFlags, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(PhaseAuditDisplay));
                }
            }
        }

        public string PhaseAuditDisplay
        {
            get
            {
                var status = string.IsNullOrWhiteSpace(PhaseAuditStatus) ? "PENDING" : PhaseAuditStatus;
                var summary = string.IsNullOrWhiteSpace(PhaseAuditSummary) ? "Auditoria de fase pendiente" : PhaseAuditSummary;
                if (!string.IsNullOrWhiteSpace(PhaseAuditFlags))
                {
                    return $"{status}: {summary} · Flags: {PhaseAuditFlags}";
                }
                return $"{status}: {summary}";
            }
        }

        public string PhaseReviewQuestion
        {
            get => _phaseReviewQuestion;
            private set
            {
                if (SetField(ref _phaseReviewQuestion, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(PhaseReviewDisplay));
                }
            }
        }

        public string PhaseMeasuredMetrics
        {
            get => _phaseMeasuredMetrics;
            private set
            {
                if (SetField(ref _phaseMeasuredMetrics, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(PhaseReviewDisplay));
                }
            }
        }

        public string PhaseReviewDisplay
        {
            get
            {
                var question = string.IsNullOrWhiteSpace(PhaseReviewQuestion) ? "Pregunta de revision de fase pendiente" : PhaseReviewQuestion;
                if (!string.IsNullOrWhiteSpace(PhaseMeasuredMetrics))
                {
                    return $"REVISION: {question} · Mide: {PhaseMeasuredMetrics}";
                }
                return $"REVISION: {question}";
            }
        }

        public string PhasePrevalidationStatus
        {
            get => _phasePrevalidationStatus;
            private set
            {
                if (SetField(ref _phasePrevalidationStatus, value ?? "PENDING"))
                {
                    OnPropertyChanged(nameof(PhasePrevalidationDisplay));
                }
            }
        }

        public string PhasePrevalidationSummary
        {
            get => _phasePrevalidationSummary;
            private set
            {
                if (SetField(ref _phasePrevalidationSummary, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(PhasePrevalidationDisplay));
                }
            }
        }

        public string PhasePrevalidationFlags
        {
            get => _phasePrevalidationFlags;
            private set
            {
                if (SetField(ref _phasePrevalidationFlags, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(PhasePrevalidationDisplay));
                }
            }
        }

        public string PhasePrevalidationDisplay
        {
            get
            {
                var status = string.IsNullOrWhiteSpace(PhasePrevalidationStatus) ? "PENDING" : PhasePrevalidationStatus;
                var summary = string.IsNullOrWhiteSpace(PhasePrevalidationSummary) ? "Prevalidacion C6 pendiente" : PhasePrevalidationSummary;
                if (!string.IsNullOrWhiteSpace(PhasePrevalidationFlags))
                {
                    return $"C6 {status}: {summary} · Flags: {PhasePrevalidationFlags}";
                }
                return $"C6 {status}: {summary}";
            }
        }

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
                    OnPropertyChanged(nameof(RouteDistanceSourceDisplay));
                }
            }
        }
        public string RouteProgressDisplay
        {
            get
            {
                return $"{Math.Round(RouteProgressPercent, 0):F0}%";
            }
        }
        public double RouteTrackWidth => Math.Max(0, RouteCanvasWidth * (RouteProgressPercent / 100.0));
        public double RoutePlaneLeft => Math.Max(0, Math.Min(RouteCanvasWidth - RoutePlaneVisualWidth, RouteTrackWidth - (RoutePlaneVisualWidth / 2.0)));
        public string RouteDistanceFromOriginDisplay => $"{ComputeDistanceFromOriginNm():F0} NM";
        public string RouteDistanceToDestinationDisplay => $"{ComputeDistanceToDestinationNm():F0} NM";
        public string RouteDistanceSourceDisplay => $"Fuente: {GetDistanceSourceLabel()}";
        public string RouteDistanceDisplay
        {
            get
            {
                var flown = ComputeDistanceFromOriginNm();
                var remaining = ComputeDistanceToDestinationNm();
                if (flown <= 0.1 && remaining <= 0.1)
                    return "Esperando recorrido";

                var planned = ComputeRouteTotalDistanceNm();
                return $"Planificada {planned:F0} nm Â· Recorrida {flown:F1} nm Â· Restante {remaining:F0} nm";
            }
        }

        // â”€â”€ Propiedades de pesos (Plan SimBrief vs Actual Sim) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private PreparedDispatch? Dispatch => AcarsContext.Runtime.CurrentDispatch;

        /// <summary>True cuando hay un despacho SimBrief activo con datos de combustible.</summary>
        public bool HasDispatchWeights => Dispatch != null && Dispatch.FuelPlannedKg > 0;

        // Plan (desde SimBrief / despacho)
        public double WbPlanFuelKg    => Dispatch?.FuelPlannedKg      ?? 0;
        public double WbPlanPayloadKg => Dispatch?.PayloadKg          ?? 0;
        public double WbPlanZfwKg     => Dispatch?.ZeroFuelWeightKg   ?? 0;

        // Actual del simulador (combustible actual en pantalla)
        public double WbActualFuelKg => HasLiveTelemetry ? FuelKg : 0;

        /// <summary>
        /// Payload actual estimado del simulador.
        /// Si no existe OEW directo en telemetrÃ­a, se estima usando el plan activo:
        /// OEW â‰ˆ ZFW planificada - Payload planificado.
        /// Payload actual â‰ˆ ZFW actual - OEW estimado.
        /// </summary>
        public double WbActualPayloadKg
        {
            get
            {
                if (!HasLiveTelemetry || ActualZeroFuelWeightKg <= 0)
                    return 0;

                var estimatedOew = Math.Max(0, WbPlanZfwKg - WbPlanPayloadKg);
                if (estimatedOew <= 0)
                    return 0;

                return Math.Max(0, ActualZeroFuelWeightKg - estimatedOew);
            }
        }

        /// <summary>Combustible capturado en el primer arranque de motores (N1 > 15%).</summary>
        public double WbFuelAtEngineStartKg => _fuelAtEngineStartKg > 0 ? _fuelAtEngineStartKg : WbActualFuelKg;

        /// <summary>True si el combustible al arranque estÃ¡ dentro del Â±10% del plan.</summary>
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
                if (WbPlanFuelKg <= 0 || WbFuelAtEngineStartKg <= 0) return "â€”";
                var pct = (WbFuelAtEngineStartKg - WbPlanFuelKg) / WbPlanFuelKg * 100.0;
                return $"{pct:+0.0;-0.0;0.0}%";
            }
        }

        /// <summary>
        /// Etiqueta de estado del combustible al arranque:
        ///   "Esperando arranque" â†’ motores no iniciados aÃºn
        ///   "âœ“ Â±4.2%  OK"       â†’ dentro del Â±10%
        ///   "âš  +14.5%  EXCESO"  â†’ fuera del Â±10%
        /// </summary>
        public string WbFuelStatusLabel
        {
            get
            {
                if (WbPlanFuelKg <= 0)          return "Sin plan SimBrief";
                if (_fuelAtEngineStartKg < 0)   return "Esperando arranque de motores";
                return WbFuelMatchOk
                    ? $"âœ“  {WbFuelDiffDisplay}  COMBUSTIBLE OK"
                    : $"âš   {WbFuelDiffDisplay}  FUERA DE RANGO (Â±10%)";
            }
        }

        public string WbPlanFuelDisplay    => WbPlanFuelKg    > 0 ? $"{WbPlanFuelKg:F0} kg"    : "â€”";
        public string WbPlanPayloadDisplay => WbPlanPayloadKg > 0 ? $"{WbPlanPayloadKg:F0} kg"  : "â€”";
        public string WbPlanZfwDisplay     => WbPlanZfwKg     > 0 ? $"{WbPlanZfwKg:F0} kg"     : "â€”";
        public string WbActualFuelDisplay    => HasLiveTelemetry && WbActualFuelKg > 0    ? $"{WbActualFuelKg:F0} kg"    : "â€”";
        public string WbActualPayloadDisplay => HasLiveTelemetry && WbActualPayloadKg > 0 ? $"{WbActualPayloadKg:F0} kg" : "â€”";
        public string WbStartFuelDisplay     => _fuelAtEngineStartKg > 0 ? $"{_fuelAtEngineStartKg:F0} kg" : "â€”";

        // â”€â”€ Log de PIREP en tiempo real â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
                sb.AppendLine("Sin vuelo activo. Inicia un despacho desde la pÃ¡gina de Despacho.");
                PirepPreview = sb.ToString();
                return;
            }

            var dep = flight.DepartureIcao ?? "????";
            var arr = flight.ArrivalIcao   ?? "????";
            var fn  = flight.FlightNumber  ?? "â€”";
            var ac  = !string.IsNullOrWhiteSpace(flight.AircraftName)
                          ? flight.AircraftName : (flight.AircraftIcao ?? "â€”");

            sb.AppendLine($"   {dep} â†’ {arr}   Â·   {fn}   Â·   {ac}");
            sb.AppendLine($"â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");

            if (fs.MaxAltitudeFeet > 0)
                sb.AppendLine($"   Alt mÃ¡x  : {fs.MaxAltitudeFeet:F0} ft");
            if (fs.MaxSpeedKts > 0)
                sb.AppendLine($"   Vel mÃ¡x  : {fs.MaxSpeedKts:F0} kt");
            if (fs.TotalDistanceNm > 0)
                sb.AppendLine($"   Distancia: {fs.TotalDistanceNm:F1} nm");

            // Combustible usado (si el vuelo ya iniciÃ³ con combustible registrado)
            if (fs.FuelAtStartLbs > 0 && HasLiveTelemetry)
            {
                var fuelUsed = Math.Max(0, (fs.FuelAtStartLbs / 2.20462) - FuelKg);
                sb.AppendLine($"   Comb. usado: {fuelUsed:F0} kg   (actual: {FuelKg:F0} kg)");
            }

            // V/S de aterrizaje (solo si ya aterrizÃ³)
            if (fs.LastLandingVS != 0)
            {
                var vsStr = fs.LastLandingVS < 0
                    ? $"{fs.LastLandingVS:F0} fpm"
                    : $"+{fs.LastLandingVS:F0} fpm";
                var rating = fs.LastLandingVS >= -180 ? "âœ“ Suave"
                           : fs.LastLandingVS >= -350 ? "â–² Normal"
                           : "âš  Duro";
                sb.AppendLine($"   V/S aterrizaje: {vsStr}  {rating}");
            }

            // Entorno actual
            if (HasLiveTelemetry)
            {
                sb.AppendLine($"â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
                sb.AppendLine($"   OAT: {OAT:F0}Â°C   Viento: {WindDir:000}Â°/{WindSpeed:F0}kt");
            }

            // Log de eventos de procedimiento
            if (_eventLog.Count > 0)
            {
                sb.AppendLine($"â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
                sb.AppendLine($"   LOG PROCEDIMIENTOS:");
                // Mostrar los Ãºltimos 12 eventos
                var start = Math.Max(0, _eventLog.Count - 12);
                for (int i = start; i < _eventLog.Count; i++)
                    sb.AppendLine($"   Â· {_eventLog[i]}");
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
            var total = ComputeRouteTotalDistanceNm();
            if (total <= 0.1)
            {
                return OfficialPhaseCode == "PAR" ? 100 : 0;
            }

            var flown = ComputeDistanceFromOriginNm();
            var normalized = Clamp01(flown / total);
            return normalized * 100.0;
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

            return Math.Max(flown, timeEstimate);
        }

        private double ComputeRouteTotalDistanceNm()
        {
            var planned = GetDispatchPlannedDistanceNm();
            if (planned > 0.1)
            {
                return planned;
            }

            if (TryGetRouteCoordinates(out var depLat, out var depLon, out var arrLat, out var arrLon))
            {
                return CalculateDistanceNm(depLat, depLon, arrLat, arrLon);
            }

            return ComputeEstimatedRouteDistanceNm();
        }

        private double ComputeDistanceFromOriginNm()
        {
            var plannedTotal = GetDispatchPlannedDistanceNm();
            if (TryGetRouteCoordinates(out var depLat, out var depLon, out _, out _)
                && TryGetAircraftCoordinates(out var aircraftLat, out var aircraftLon))
            {
                var geoFromOrigin = CalculateDistanceNm(depLat, depLon, aircraftLat, aircraftLon);
                if (plannedTotal <= 0.1 || geoFromOrigin <= plannedTotal + 10.0)
                {
                    return Math.Max(0, geoFromOrigin);
                }
            }

            var flown = Math.Max(0, AcarsContext.FlightService.TotalDistanceNm);
            return plannedTotal > 0.1 ? Math.Min(flown, plannedTotal) : flown;
        }

        private double ComputeDistanceToDestinationNm()
        {
            var plannedTotal = GetDispatchPlannedDistanceNm();
            if (plannedTotal > 0.1)
            {
                return Math.Max(0, plannedTotal - ComputeDistanceFromOriginNm());
            }

            if (TryGetRouteCoordinates(out _, out _, out var arrLat, out var arrLon)
                && TryGetAircraftCoordinates(out var aircraftLat, out var aircraftLon))
            {
                return CalculateDistanceNm(aircraftLat, aircraftLon, arrLat, arrLon);
            }

            var flown = ComputeDistanceFromOriginNm();
            var total = ComputeRouteTotalDistanceNm();
            return Math.Max(0, total - flown);
        }

        private double GetDispatchPlannedDistanceNm()
        {
            var dispatchDistance = AcarsContext.Runtime.CurrentDispatch?.PlannedDistanceNm ?? 0;
            if (dispatchDistance > 0.1)
            {
                return dispatchDistance;
            }

            return 0;
        }

        private string GetDistanceSourceLabel()
        {
            var dispatch = AcarsContext.Runtime.CurrentDispatch;
            if (dispatch != null && dispatch.PlannedDistanceNm > 0.1)
            {
                var source = (dispatch.PlannedDistanceSource ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(source) ? "preparedDispatch.distanceNm" : source;
            }

            if (TryGetRouteCoordinates(out _, out _, out _, out _))
            {
                return "geodesic.airports";
            }

            return "fallback.local";
        }

        private string ResolveFuelSourceLabel()
        {
            var sample = AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData;
            return sample != null && sample.FuelKg > 0 ? "sim.telemetry" : "no_data";
        }

        private string ResolveFuelCapacitySourceLabel()
        {
            var sample = AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData;
            var simCapacityKg = sample == null ? 0 : Math.Round(sample.FuelTotalCapacityLbs * 0.45359237d, 0);
            if (simCapacityKg > 10)
            {
                return "simconnect.capacity";
            }

            var dispatchFuel = Dispatch?.FuelPlannedKg ?? 0;
            if (dispatchFuel > 10)
            {
                return "dispatch.fuel_plan";
            }

            return "n/d";
        }

        private bool TryGetAircraftCoordinates(out double latitude, out double longitude)
        {
            var sample = AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData;
            latitude = sample?.Latitude ?? Lat;
            longitude = sample?.Longitude ?? Lon;
            return IsValidCoordinate(latitude, longitude);
        }

        private bool TryGetRouteCoordinates(out double depLat, out double depLon, out double arrLat, out double arrLon)
        {
            depLat = depLon = arrLat = arrLon = 0;

            var depOk = TryResolveAirportCoordinate(new[] { "Departure", "Origin" }, out depLat, out depLon);
            var arrOk = TryResolveAirportCoordinate(new[] { "Arrival", "Destination" }, out arrLat, out arrLon);
            return depOk && arrOk;
        }

        private bool TryResolveAirportCoordinate(string[] prefixes, out double latitude, out double longitude)
        {
            foreach (var source in new object?[] { AcarsContext.Runtime.CurrentDispatch, AcarsContext.FlightService.CurrentFlight })
            {
                if (source == null)
                {
                    continue;
                }

                foreach (var prefix in prefixes)
                {
                    if (TryReadCoordinatePair(source, prefix, out latitude, out longitude))
                    {
                        return true;
                    }
                }
            }

            latitude = longitude = 0;
            return false;
        }

        private static bool TryReadCoordinatePair(object source, string prefix, out double latitude, out double longitude)
        {
            latitude = longitude = 0;

            if ((TryReadDouble(source, prefix + "Latitude", out latitude) || TryReadDouble(source, prefix + "Lat", out latitude))
                && (TryReadDouble(source, prefix + "Longitude", out longitude) || TryReadDouble(source, prefix + "Lon", out longitude))
                && IsValidCoordinate(latitude, longitude))
            {
                return true;
            }

            var nestedProperty =
                source.GetType().GetProperty(prefix + "Airport")
                ?? source.GetType().GetProperty(prefix);

            var nested = nestedProperty?.GetValue(source);
            if (nested == null)
            {
                return false;
            }

            if ((TryReadDouble(nested, "Latitude", out latitude) || TryReadDouble(nested, "Lat", out latitude))
                && (TryReadDouble(nested, "Longitude", out longitude) || TryReadDouble(nested, "Lon", out longitude))
                && IsValidCoordinate(latitude, longitude))
            {
                return true;
            }

            latitude = longitude = 0;
            return false;
        }

        private static bool TryReadDouble(object source, string propertyName, out double value)
        {
            value = 0;
            var property = source.GetType().GetProperty(propertyName);
            if (property == null)
            {
                return false;
            }

            var raw = property.GetValue(source);
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private static bool IsValidCoordinate(double latitude, double longitude)
        {
            return latitude >= -90 && latitude <= 90
                && longitude >= -180 && longitude <= 180
                && (Math.Abs(latitude) > 0.0001 || Math.Abs(longitude) > 0.0001);
        }

        private static double CalculateDistanceNm(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusNm = 3440.065;
            var dLat = DegToRad(lat2 - lat1);
            var dLon = DegToRad(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(DegToRad(lat1)) * Math.Cos(DegToRad(lat2))
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return earthRadiusNm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double DegToRad(double degrees) => degrees * Math.PI / 180.0;

        private static double Clamp01(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
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
                "CRU" => "Crucero y telemetrÃ­a oficial activa",
                "DES" => "Descenso y preparaciÃ³n de llegada",
                "LDG" => "AproximaciÃ³n y aterrizaje en evaluaciÃ³n",
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
            OnPropertyChanged(nameof(RouteDistanceSourceDisplay));
        }

        private void LogLightEvent(string name, bool prev, bool current, bool onGround)
        {
            if (prev == current) return;
            var ctx = onGround ? "(suelo)" : "(vuelo)";
            _eventLog.Add($"{PhaseLabel}: {name} {(current ? "ON" : "OFF")} {ctx}");
            // Mantener mÃ¡ximo 30 entradas
            if (_eventLog.Count > 30) _eventLog.RemoveAt(0);
        }

        public string IasDisplay => HasLiveTelemetry ? Math.Round(IAS, 0).ToString("F0") : "---";
        public string IASDisplay => IasDisplay;
        public string GsDisplay => HasLiveTelemetry ? Math.Round(GS, 0).ToString("F0") : "---";
        public string GSDisplay => GsDisplay;
        public string AltitudeDisplay
        {
            get
            {
                if (!HasLiveTelemetry) return "---";
                if (!string.IsNullOrWhiteSpace(DisplayAltitudeText)) return DisplayAltitudeText;
                return Math.Round(Altitude, 0).ToString("F0");
            }
        }
        public string AltitudeMslDisplay => HasLiveTelemetry ? Math.Round(Altitude, 0).ToString("F0") : "---";
        public string FlightLevelDisplay => HasLiveTelemetry && !string.IsNullOrWhiteSpace(FlightLevel) ? FlightLevel : "---";
        public string GroundElevationDisplay => HasLiveTelemetry ? Math.Round(GroundElevation, 0).ToString("F0") : "---";
        public string PressureAltitudeDisplay => HasLiveTelemetry ? Math.Round(PressureAltitude, 0).ToString("F0") : "---";
        public string AltitudeReliabilityDisplay => HasLiveTelemetry ? (IsAltitudeReliable ? "ALT OK" : "ALT N/D") : "---";
        public string AglDisplay
        {
            get
            {
                if (!HasLiveTelemetry) return "---";
                var normalizedAgl = (OnGround || AltitudeAGL <= 5d) ? 0d : AltitudeAGL;
                return Math.Round(normalizedAgl, 0).ToString("F0");
            }
        }
        public string AltitudeAglDisplay   => AglDisplay;
        public string QnhDisplay           => HasLiveTelemetry && _qnh > 0 ? $"{Math.Round(_qnh, 0):F0} hPa" : "â€”";
        public string RadioAcarsMessage
        {
            get => _radioAcarsMessage;
            set => SetField(ref _radioAcarsMessage, value);
        }
        // V/S: clamp a 0 cuando en tierra (SimConnect puede retornar Â±1 fpm en suelo)
        public string VsDisplay => HasLiveTelemetry ? (OnGround ? "0" : Math.Round(VS, 0).ToString("+#;-#;0")) : "---";
        public string VSDisplay => VsDisplay;
        public string HeadingDisplay => HasLiveTelemetry ? Math.Round(Heading, 0).ToString("000") + " deg" : "---";
        public string FuelDisplay => HasLiveTelemetry ? $"{Math.Round(FuelKg, 0):F0} / {FuelCapacityForDisplay} kg" : "---";
        public string FuelSourceDisplay => $"Fuente: {ResolveFuelSourceLabel()} / {ResolveFuelCapacitySourceLabel()}";
        public string FuelKgDisplay => FuelDisplay;
        public string FuelLbsDisplay => HasLiveTelemetry ? Math.Round(FuelLbs, 0).ToString("F0") : "---";
        public string FlapsDisplay => HasLiveTelemetry ? Math.Round(FlapsPercent, 0).ToString("F0") + "%" : "---";
        public string N1Eng1Display => HasLiveTelemetry ? Math.Round(N1Eng1, 1).ToString("F1") : "---";
        public string N1Eng2Display => HasLiveTelemetry ? Math.Round(N1Eng2, 1).ToString("F1") : "---";

        /// <summary>Orden de arranque registrado (ej. "2 â†’ 1" para bimotor, "4 â†’ 2 â†’ 3 â†’ 1" para cuadrimotor).</summary>
        public string EngineStartOrderDisplay
        {
            get
            {
                if (_engineStartOrder.Count == 0) return "â€”";
                return string.Join(" â†’ ", _engineStartOrder);
            }
        }

        /// <summary>True si el orden de arranque coincide con el estÃ¡ndar Patagonia Wings
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
                if (!ShowTransponderSystem || !HasLiveTelemetry || _transponderStateRaw < 0) return "N/D";
                if (_charlieMode || _transponderStateRaw >= 3) return "ALT";
                if (_transponderStateRaw == 2) return "ON";
                if (_transponderStateRaw == 1) return "STBY";
                return "OFF";
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
                        : backend + " Â· " + runtime.SimulatorType;
                }

                if (string.IsNullOrWhiteSpace(backend))
                {
                    return "Sin conexiÃ³n";
                }

                return runtime.HasTelemetry
                    ? "Sin telemetrÃ­a Â· " + backend
                    : "Esperando Â· " + backend;
            }
        }
        public bool IsSimConnected => AcarsContext.Runtime.IsSimulatorConnected;

        public ICommand ConnectMsfsCommand { get; }
        public ICommand ConnectXPlaneCommand { get; }
        public ICommand DisconnectSimCommand { get; }
        public ICommand FinishFlightCommand { get; }
        public ICommand CancelFlightCommand { get; }
        public ICommand ConfirmPicCommand { get; }

        public bool PicConfirmed
        {
            get => _picConfirmed;
            private set
            {
                if (SetField(ref _picConfirmed, value))
                {
                    OnPropertyChanged(nameof(PicCheckLabel));
                    OnPropertyChanged(nameof(PicButtonLabel));
                }
            }
        }
        public string PicCheckLabel
        {
            get
            {
                if (_picConfirmed && !_picCheckActive)
                    return $"âœ“  PIC OK â€” {(_lastPicMatchedRadio == string.Empty ? "radio" : _lastPicMatchedRadio)}: {_picFrequency:F3}";
                if (_picCheckActive)
                    return $"Sintonice COM2: {_picFrequency:F3}  actual {_com2FrequencyMhz:F3} [{_picSecondsLeft}s]";
                if (_picChecksTotal == 0)
                    return "Verificacion PIC activa en crucero";
                return $"Vuelo en curso | checks: {_picChecksDone}/{_picChecksTotal}";
            }
        }
        public string PicButtonLabel => _picCheckActive ? $"{_picSecondsLeft}s" : (_picConfirmed ? "OK" : "-");

        public bool CanManualCloseout
        {
            get
            {
                return AcarsContext.FlightService.IsFlightActive && IsManualCloseoutGateReady(out _);
            }
        }

        public string ManualCloseoutStatus
        {
            get => _manualCloseoutStatus;
            private set => SetField(ref _manualCloseoutStatus, value);
        }

        public string FinishFlightButtonText => CanManualCloseout ? "FINALIZAR EN GATE" : "CIERRE EN GATE";

        private readonly System.Windows.Threading.DispatcherTimer _elapsedTimer;

        public InFlightViewModel(MainViewModel main)
        {
            _main = main;
            ConnectMsfsCommand = new RelayCommand(() => _main.NavigateToSimulatorConnect());
            ConnectXPlaneCommand = new RelayCommand(() => _main.NavigateToSimulatorConnect());
            DisconnectSimCommand = new RelayCommand(() => { });
            FinishFlightCommand = new RelayCommand(() => { RefreshManualCloseoutState(); if (CanManualCloseout) FinishFlight(); }, () => CanManualCloseout);
            CancelFlightCommand = new RelayCommand(() => CancelFlight(), () => AcarsContext.FlightService.IsFlightActive);
            ConfirmPicCommand = new RelayCommand(
                () => { },
                () => false);

            _picFrequency = _picFrequencies[_picRandom.Next(_picFrequencies.Length)];

            _elapsedTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (_, __) =>
            {
                if (_startTime != default(DateTime))
                    ElapsedTime = (DateTime.UtcNow - _startTime).ToString(@"hh\:mm\:ss");

                TickPicCheck();

                // En vuelo: programar checks PIC periÃ³dicos. No depende solo de Cruise, porque vuelos cortos
                // pueden pasar directo de climb a descent sin entrar formalmente en crucero.
                if (IsPicEligiblePhase(Phase))
                {
                    EnsurePicScheduleInitialized();
                    if (!_picCheckActive && _picChecksDone < _picChecksTotal)
                    {
                        var requiredIntervalMinutes = _picChecksTotal >= 3 ? 30 : 10;
                        if (_lastPicCheckTime == DateTime.MinValue ||
                            (DateTime.UtcNow - _lastPicCheckTime).TotalMinutes >= requiredIntervalMinutes)
                            TriggerPicCheck();
                    }
                }
            };

            AcarsContext.FlightService.TelemetryUpdated += OnTelemetry;
            AcarsContext.FlightService.PhaseChanged += OnPhaseChanged;
            AcarsContext.FlightService.FlightOfficiallyStarted += OnFlightOfficiallyStarted;
            AcarsContext.Runtime.Changed += OnRuntimeChanged;
            ApplyRuntimeState();
            RefreshRouteSnapshot();
        }

        private void OnFlightOfficiallyStarted()
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() => StartElapsedTimer());
        }

        public void StartElapsedTimer()
        {
            if (_startTime != default(DateTime)) return; // ya estÃ¡ corriendo
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
            OnPropertyChanged(nameof(AltitudeAglDisplay));
            OnPropertyChanged(nameof(AglDisplay));
            OnPropertyChanged(nameof(QnhDisplay));
            OnPropertyChanged(nameof(VsDisplay));
            OnPropertyChanged(nameof(VSDisplay));
            OnPropertyChanged(nameof(HeadingDisplay));
            OnPropertyChanged(nameof(FuelDisplay));
            OnPropertyChanged(nameof(FuelSourceDisplay));
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
            OnPropertyChanged(nameof(RouteDistanceSourceDisplay));

            UpdatePirepPreview();
            RefreshRouteSnapshot();

            if (!AcarsContext.Runtime.IsSimulatorConnected)
            {
                if (AcarsContext.FlightService.IsFlightActive)
                {
                    var recovered = ResolveActivePhaseFromTelemetry(AcarsContext.FlightService.LastSimData);
                    if (Phase == FlightPhase.Disconnected || recovered != FlightPhase.Disconnected)
                    {
                        Phase = recovered == FlightPhase.Disconnected ? _lastValidActivePhase : recovered;
                        if (Phase == FlightPhase.Disconnected) Phase = FlightPhase.Taxi;
                    }
                    RefreshManualCloseoutState();
                }
                else
                {
                    ClearTelemetrySnapshot();
                }
            }
        }

        private void OnTelemetry(SimData data)
        {
            Debug.WriteLine($"[InFlightVM] OnTelemetry - ACFT:{data.AircraftTitle} ALT={data.AltitudeFeet:F0} FUEL={data.FuelTotalLbs:F0}");
            Application.Current.Dispatcher.Invoke(() =>
            {
                // â”€â”€ Perfil normalizado â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (_detectedProfileCode != data.DetectedProfileCode)
                {
                    _detectedProfileCode = data.DetectedProfileCode;
                    OnPropertyChanged(nameof(DetectedProfileCode));
                    OnPropertyChanged(nameof(DetectedDisplayName));
                }
                AircraftTitle = data.AircraftTitle;
                Altitude    = data.AltitudeMslFeet > 0 ? data.AltitudeMslFeet : data.AltitudeFeet;
                AltitudeAGL = data.AltitudeAglFeet >= 0 ? data.AltitudeAglFeet : data.AltitudeAGL;
                GroundElevation = data.GroundElevationFeet;
                PressureAltitude = data.PressureAltitudeFeet;
                FlightLevel = data.FlightLevel;
                DisplayAltitudeMode = data.DisplayAltitudeMode;
                DisplayAltitudeText = data.DisplayAltitudeText;
                IsAltitudeReliable = data.IsAltitudeReliable;
                AltitudeSource = data.AltitudeSource;
                PhaseChecklistStatus = data.PhaseChecklistStatus;
                PhaseChecklistSummary = data.PhaseChecklistSummary;
                PhaseChecklistMissing = data.PhaseChecklistMissing;
                PhaseTransitionDisplay = BuildPhaseTransitionDisplay(data);
                PhaseAuditStatus = data.PhaseAuditStatus;
                PhaseAuditSummary = data.PhaseAuditSummary;
                PhaseAuditFlags = data.PhaseAuditFlags;
                PhaseReviewQuestion = data.PhaseReviewQuestion;
                PhaseMeasuredMetrics = data.PhaseMeasuredMetrics;
                PhasePrevalidationStatus = data.PhasePrevalidationStatus;
                PhasePrevalidationSummary = data.PhasePrevalidationSummary;
                PhasePrevalidationFlags = data.PhasePrevalidationFlags;
                IAS = data.IndicatedAirspeed;
                GS = data.GroundSpeed;
                VS = data.VerticalSpeed;
                Heading = data.Heading;
                FuelLbs = data.FuelTotalLbs;
                // Combustible normalizado en kg (SimConnect convierte lbsâ†’kg en SimConnectService)
                _fuelKgNorm = data.FuelKg > 0 ? data.FuelKg : (data.FuelTotalLbs / 2.20462);
                _totalWeightKg = data.TotalWeightKg > 0 ? data.TotalWeightKg : 0;
                _zeroFuelWeightKg = data.ZeroFuelWeightKg > 0
                    ? data.ZeroFuelWeightKg
                    : Math.Max(0, _totalWeightKg - _fuelKgNorm);
                OnPropertyChanged(nameof(FuelKg));
                OnPropertyChanged(nameof(ActualTotalWeightKg));
                OnPropertyChanged(nameof(ActualZeroFuelWeightKg));

                // Tanques individuales (para diagnÃ³stico)
                FuelLeftTank = data.FuelLeftTankLbs > 0 ? data.FuelLeftTankLbs * 0.45359237d : 0;
                FuelRightTank = data.FuelRightTankLbs > 0 ? data.FuelRightTankLbs * 0.45359237d : 0;
                FuelCenterTank = data.FuelCenterTankLbs > 0 ? data.FuelCenterTankLbs * 0.45359237d : 0;
                FuelCapacity = data.FuelTotalCapacityLbs > 0 ? data.FuelTotalCapacityLbs * 0.45359237d : 0;
                
                N1Eng1 = data.Engine1N1;
                N1Eng2 = data.Engine2N1;
                _n1Eng3 = data.Engine3N1;
                _n1Eng4 = data.Engine4N1;
                _batteryMasterOn = data.BatteryMasterOn;
                _avionicsMasterOn = data.AvionicsMasterOn;
                _electricalMainBusVoltage = data.ElectricalMainBusVoltage;
                OAT = Math.Round(data.OutsideTemperature, 1);
                WindSpeed = Math.Round(data.WindSpeed, 0);
                WindDir = Math.Round(data.WindDirection, 0);
                if (data.QNH > 0) { _qnh = data.QNH; OnPropertyChanged(nameof(QnhDisplay)); }
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
                _com1FrequencyMhz = data.Com1FrequencyMhz;
                _com1StandbyFrequencyMhz = data.Com1StandbyFrequencyMhz;
                _com2FrequencyMhz = data.Com2FrequencyMhz;
                _com2StandbyFrequencyMhz = data.Com2StandbyFrequencyMhz;
                
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
                // â”€â”€ Detectar primer arranque de motores â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                // Cuando N1 supera el 15% por primera vez se captura el combustible actual.
                // Ese valor se usa para comparar con el plan SimBrief (Â±10% de tolerancia).
                if (_fuelAtEngineStartKg < 0 && (data.Engine1N1 > 15 || data.Engine2N1 > 15))
                {
                    _fuelAtEngineStartKg = FuelKg;
                    OnPropertyChanged(nameof(WbFuelAtEngineStartKg));
                    OnPropertyChanged(nameof(WbFuelMatchOk));
                    OnPropertyChanged(nameof(WbFuelDiffDisplay));
                    OnPropertyChanged(nameof(WbFuelStatusLabel));
                    OnPropertyChanged(nameof(WbStartFuelDisplay));
                }

                // â”€â”€ Orden de arranque de motores (IGN 2-1 / 4-2-3-1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (!_eng2Started && data.Engine2N1 >= EngineN1StartThreshold) { _eng2Started = true; _engineStartOrder.Add(2); OnPropertyChanged(nameof(EngineStartOrderDisplay)); }
                if (!_eng1Started && data.Engine1N1 >= EngineN1StartThreshold) { _eng1Started = true; _engineStartOrder.Add(1); OnPropertyChanged(nameof(EngineStartOrderDisplay)); }
                if (!_eng4Started && data.Engine4N1 >= EngineN1StartThreshold) { _eng4Started = true; _engineStartOrder.Add(4); OnPropertyChanged(nameof(EngineStartOrderDisplay)); }
                if (!_eng3Started && data.Engine3N1 >= EngineN1StartThreshold) { _eng3Started = true; _engineStartOrder.Add(3); OnPropertyChanged(nameof(EngineStartOrderDisplay)); }

                // â”€â”€ Detectar y registrar eventos de procedimiento â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

                // â”€â”€ Notificar pesos actuales y log PIREP â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                OnPropertyChanged(nameof(WbActualFuelKg));
                OnPropertyChanged(nameof(WbActualPayloadKg));
                OnPropertyChanged(nameof(WbActualFuelDisplay));
                OnPropertyChanged(nameof(WbActualPayloadDisplay));
                OnPropertyChanged(nameof(WbActualZfwDisplay));
                OnPropertyChanged(nameof(WbFuelStatusLabel));
                UpdatePirepPreview();
                RefreshRouteSnapshot(data);
                PreserveActiveFlightRecordingState(data);
                RefreshManualCloseoutState();

                ApplyRuntimeState();
                CheckAlerts(data);
            });
        }

        private void ClearTelemetrySnapshot()
        {
            if (AcarsContext.FlightService.IsFlightActive)
            {
                // No borrar caja negra ni UI de un vuelo activo por microcorte.
                // El cierre oficial sigue siendo manual: FINALIZAR EN GATE.
                RefreshManualCloseoutState();
                return;
            }

            Altitude = 0;
            IAS = 0;
            GS = 0;
            VS = 0;
            Heading = 0;
            FuelLbs = 0;
            N1Eng1 = 0;
            N1Eng2 = 0;
            _n1Eng3 = 0;
            _n1Eng4 = 0;
            _batteryMasterOn = false;
            _avionicsMasterOn = false;
            _electricalMainBusVoltage = 0;
            ManualCloseoutStatus = "Cierre manual: aterrice, llegue a gate, freno parking, motores OFF y Cold & Dark.";
            OnPropertyChanged(nameof(CanManualCloseout));
            OnPropertyChanged(nameof(FinishFlightButtonText));
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
            _qnh = 0;
            _radioAcarsMessage = string.Empty;
            OnPropertyChanged(nameof(QnhDisplay));
            OnPropertyChanged(nameof(RadioAcarsMessage));
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
                
                // Forzar actualizaciÃ³n de todas las propiedades Display
                OnPropertyChanged(nameof(IasDisplay));
                OnPropertyChanged(nameof(IASDisplay));
                OnPropertyChanged(nameof(GsDisplay));
                OnPropertyChanged(nameof(GSDisplay));
                OnPropertyChanged(nameof(AltitudeDisplay));
                OnPropertyChanged(nameof(AltitudeMslDisplay));
                OnPropertyChanged(nameof(AltitudeAglDisplay));
                OnPropertyChanged(nameof(FlightLevelDisplay));
                OnPropertyChanged(nameof(VsDisplay));
                OnPropertyChanged(nameof(VSDisplay));
                OnPropertyChanged(nameof(HeadingDisplay));
                OnPropertyChanged(nameof(FuelDisplay));
                OnPropertyChanged(nameof(FuelSourceDisplay));
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

        private void PreserveActiveFlightRecordingState(SimData data)
        {
            if (!AcarsContext.FlightService.IsFlightActive || data == null)
                return;

            if (!data.OnGround || data.AltitudeAGL > 50 || data.IndicatedAirspeed > 80 || data.GroundSpeed > 80)
                _hasBeenAirborne = true;

            if (_startTime == default(DateTime) && (data.GroundSpeed > 2 || !data.OnGround || data.IndicatedAirspeed > 60))
                StartElapsedTimer();
            else if (_startTime != default(DateTime) && !_elapsedTimer.IsEnabled)
                _elapsedTimer.Start();

            var recovered = ResolveActivePhaseFromTelemetry(data);
            if (Phase == FlightPhase.Disconnected || (Phase == FlightPhase.PreFlight && recovered != FlightPhase.PreFlight))
            {
                Phase = recovered;
                _eventLog.Add($"REC: fase reconstruida desde telemetria activa: {GetPhaseLabel(Phase)}");
            }

            if (Phase != FlightPhase.Disconnected)
                _lastValidActivePhase = Phase;
        }

        private FlightPhase ResolveActivePhaseFromTelemetry(SimData? data)
        {
            if (data == null)
                return _lastValidActivePhase == FlightPhase.Disconnected ? FlightPhase.PreFlight : _lastValidActivePhase;

            if (!string.IsNullOrWhiteSpace(data.OperationalPhaseCode))
            {
                var phaseFromSample = ResolvePhaseFromOperationalCode(data.OperationalPhaseCode);
                if (phaseFromSample != FlightPhase.Disconnected)
                    return phaseFromSample;
            }

            var agl = data.AltitudeAglFeet >= 0 ? data.AltitudeAglFeet : data.AltitudeAGL;
            agl = Math.Max(0, agl);

            if (data.OnGround)
            {
                if (_hasBeenAirborne || data.HasBeenAirborne || data.TouchdownDetected || AcarsContext.FlightService.TouchdownTimeUtc != default(DateTime))
                {
                    if (data.GroundSpeed <= 3 && data.ParkingBrake)
                        return FlightPhase.Arrived;

                    return data.GroundSpeed <= 40 ? FlightPhase.Taxi : FlightPhase.Landing;
                }

                if (!data.ParkingBrake && data.GroundSpeed > 2)
                    return FlightPhase.PushbackTaxi;

                return FlightPhase.PreFlight;
            }

            _hasBeenAirborne = true;

            if (agl < 3000 && data.VerticalSpeed < -200)
                return FlightPhase.Approach;

            if (data.VerticalSpeed < -500)
                return FlightPhase.Descent;

            if (data.VerticalSpeed > 450 || agl < 2500)
                return FlightPhase.Climb;

            return FlightPhase.Cruise;
        }

        private static FlightPhase ResolvePhaseFromOperationalCode(string? code)
        {
            switch ((code ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "PRE": return FlightPhase.PreFlight;
                case "BRD": return FlightPhase.Boarding;
                case "TAX_OUT": return FlightPhase.PushbackTaxi;
                case "TO": return FlightPhase.Takeoff;
                case "CLB": return FlightPhase.Climb;
                case "CRZ": return FlightPhase.Cruise;
                case "DES": return FlightPhase.Descent;
                case "APP": return FlightPhase.Approach;
                case "LDG": return FlightPhase.Landing;
                case "TAX_IN": return FlightPhase.Taxi;
                case "GATE": return FlightPhase.Arrived;
                case "DEB": return FlightPhase.Deboarding;
                default: return FlightPhase.Disconnected;
            }
        }

        private static string BuildPhaseTransitionDisplay(SimData data)
        {
            if (data == null)
            {
                return string.Empty;
            }

            var changed = data.PhaseTransitionChanged ? "CAMBIO" : "ESTABLE";
            var from = string.IsNullOrWhiteSpace(data.PhaseTransitionFromCode) ? "—" : data.PhaseTransitionFromCode;
            var to = string.IsNullOrWhiteSpace(data.OperationalPhaseCode) ? data.PhaseTransitionToCode : data.OperationalPhaseCode;
            var confidence = string.IsNullOrWhiteSpace(data.PhaseDecisionConfidence) ? "confirmed" : data.PhaseDecisionConfidence;
            var dwell = data.PhaseDwellSeconds > 0 ? $" · {data.PhaseDwellSeconds}s" : string.Empty;
            var reason = string.IsNullOrWhiteSpace(data.OperationalPhaseReason) ? string.Empty : $" · {data.OperationalPhaseReason}";
            return $"{changed}: {from} → {to} · {confidence}{dwell}{reason}";
        }

        private void OnPhaseChanged(FlightPhase newPhase)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (newPhase == FlightPhase.Disconnected && AcarsContext.FlightService.IsFlightActive)
                {
                    var recovered = ResolveActivePhaseFromTelemetry(AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData);
                    Phase = recovered == FlightPhase.Disconnected ? _lastValidActivePhase : recovered;
                    if (Phase == FlightPhase.Disconnected)
                        Phase = FlightPhase.Taxi;

                    _eventLog.Add($"REC: fase Disconnected ignorada; vuelo activo conserva {GetPhaseLabel(Phase)} hasta FINALIZAR EN GATE");
                    UpdatePirepPreview();
                    RefreshRouteSnapshot();
                    RefreshManualCloseoutState();
                    CommandManager.InvalidateRequerySuggested();
                    return;
                }

                Phase = newPhase;
                if (newPhase != FlightPhase.Disconnected)
                    _lastValidActivePhase = newPhase;

                // Registrar cambio de fase en el log de eventos
                if (newPhase != FlightPhase.Disconnected && newPhase != FlightPhase.PreFlight)
                    _eventLog.Add($"FASE: {GetPhaseLabel(newPhase)}");
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
                        if (_startTime == default(DateTime)) StartElapsedTimer();
                        break;
                    case FlightPhase.Takeoff:
                        _ = AcarsContext.Sound.PlayGroundEnginesAsync();
                        if (_startTime == default(DateTime)) StartElapsedTimer();
                        break;
                    case FlightPhase.Cruise:
                        EnsurePicScheduleInitialized();
                        if (!_picCheckActive && _picChecksDone == 0)
                            TriggerPicCheck();
                        break;
                    case FlightPhase.Approach:
                        _ = AcarsContext.Sound.PlayCopilotAproximacionAsync();
                        break;
                    case FlightPhase.Arrived:
                        _ = AcarsContext.Sound.PlayGroundArrivedAsync();
                        // No detener cronometro ni registro al arribar. El vuelo sigue vivo
                        // hasta que el piloto confirme FINALIZAR EN GATE.
                        RefreshManualCloseoutState();
                        break;
                }
            });
        }

        private void RefreshManualCloseoutState()
        {
            var ready = IsManualCloseoutGateReady(out var reason);
            ManualCloseoutStatus = ready
                ? "Listo para cierre manual: gate/destino, freno parking, motores OFF y Cold & Dark confirmado."
                : reason;
            OnPropertyChanged(nameof(CanManualCloseout));
            OnPropertyChanged(nameof(FinishFlightButtonText));
            CommandManager.InvalidateRequerySuggested();
        }

        private bool IsManualCloseoutGateReady(out string reason)
        {
            reason = "Cierre manual: aterrice, llegue a gate, freno parking, motores OFF y Cold & Dark.";

            if (!AcarsContext.FlightService.IsFlightActive)
            {
                reason = "No hay vuelo activo para cerrar.";
                return false;
            }

            // Bloque 18: cierre manual solo despues de aterrizaje real.
            // Se usa la muestra viva del simulador para evitar estados UI stale o simvars erroneos.
            var liveSample = AcarsContext.Runtime.LastTelemetry;
            if (liveSample == null)
            {
                reason = "Cierre bloqueado: sin telemetria viva del simulador.";
                return false;
            }

            if (Phase != FlightPhase.Taxi && Phase != FlightPhase.Arrived)
            {
                reason = $"Cierre bloqueado: fase actual {PhaseLabel}. Solo disponible en taxi-in/gate despues de aterrizar.";
                return false;
            }

            var liveAgl = liveSample.AltitudeAglFeet >= 0 ? liveSample.AltitudeAglFeet : liveSample.AltitudeAGL;
            if (!liveSample.OnGround || liveAgl > 15)
            {
                reason = $"Cierre bloqueado: aeronave no confirmada en plataforma/suelo. AGL {liveAgl:F0} ft.";
                return false;
            }

            if (liveSample.GroundSpeed > 3)
            {
                reason = $"Cierre bloqueado: velocidad {liveSample.GroundSpeed:F0} kt. Debe estar detenido en gate (<3 kt).";
                return false;
            }

            if (!OnGround || AltitudeAGL > 15)
            {
                reason = $"Cierre bloqueado: la aeronave aun no esta detenida en tierra/gate (AGL {AltitudeAGL:F0} ft).";
                return false;
            }

            if (Phase == FlightPhase.Takeoff || Phase == FlightPhase.Climb || Phase == FlightPhase.Cruise ||
                Phase == FlightPhase.Descent || Phase == FlightPhase.Approach || Phase == FlightPhase.Landing)
            {
                reason = $"Cierre bloqueado: fase {PhaseLabel}. Debe aterrizar, taxear a gate y apagar la aeronave.";
                return false;
            }

            if (GS > 3)
            {
                reason = $"Cierre bloqueado: velocidad {GS:F0} kt. Debe estar detenido en gate (<3 kt).";
                return false;
            }

            if (!ParkingBrakeOn)
            {
                reason = "Cierre bloqueado: active freno de estacionamiento en gate.";
                return false;
            }

            var maxN1 = Math.Max(Math.Max(N1Eng1, N1Eng2), Math.Max(_n1Eng3, _n1Eng4));
            if (maxN1 > 5)
            {
                reason = $"Cierre bloqueado: motores encendidos/N1 {maxN1:F0}%. Apague motores.";
                return false;
            }

            if (!IsColdAndDarkForCloseout())
            {
                reason = BuildColdAndDarkCloseoutReason();
                return false;
            }

            if (!IsAtDestinationOrGateArea())
            {
                reason = $"Cierre bloqueado: aun no esta en destino/gate. Restan {ComputeDistanceToDestinationNm():F1} NM.";
                return false;
            }

            reason = "Listo para cierre manual en gate.";
            return true;
        }

        private static bool IsBlackSquareC208OrCaravan(string profileCode, string title)
        {
            var p = (profileCode ?? string.Empty).ToUpperInvariant();
            var t = (title ?? string.Empty).ToUpperInvariant();

            var profileLooksC208 =
                p.Contains("C208") ||
                p.Contains("C208B") ||
                p.Contains("CARAVAN") ||
                p.Contains("GRAND_CARAVAN") ||
                p.Contains("BLACKSQUARE");

            var titleLooksC208 =
                t.Contains("BLACK SQUARE") &&
                (t.Contains("CARAVAN") || t.Contains("GRAND CARAVAN") || t.Contains("C208") || t.Contains("208"));

            var genericCaravanTitle =
                t.Contains("CARAVAN") ||
                t.Contains("GRAND CARAVAN") ||
                t.Contains("C208") ||
                t.Contains("208B");

            return profileLooksC208 || titleLooksC208 || genericCaravanTitle;
        }
        private bool IsColdAndDarkForCloseout()
        {
            var profileCode = (_detectedProfileCode ?? string.Empty).ToUpperInvariant();
            var title = (AircraftTitle ?? string.Empty).ToUpperInvariant();
            var isBlackSquareC208 = IsBlackSquareC208OrCaravan(profileCode, title);

            var lightsOff = !NavOn && !BeaconOn && !StrobeOn && !LandingOn && !TaxiOn;

            // C208 Black Square: no usar battery/avionics/main bus para C&D.
            // El addon puede mantener HOT BATTERY BUS y simvars nativas vivas aunque el cockpit este apagado.
            // Motores OFF se valida antes; aqui se exige luces exteriores OFF.
            var electricalOff = isBlackSquareC208
                ? true
                : (!_batteryMasterOn && !_avionicsMasterOn);

            return lightsOff && electricalOff;
        }

        private string BuildColdAndDarkCloseoutReason()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (NavOn || BeaconOn || StrobeOn || LandingOn || TaxiOn)
                parts.Add("luces ON");
            var profileCode = (_detectedProfileCode ?? string.Empty).ToUpperInvariant();
            var title = (AircraftTitle ?? string.Empty).ToUpperInvariant();
            var isBlackSquareC208 = IsBlackSquareC208OrCaravan(profileCode, title);

            if (!isBlackSquareC208 && _batteryMasterOn)
                parts.Add("bateria ON");
            if (!isBlackSquareC208 && _avionicsMasterOn)
                parts.Add("avionica ON");
            if (!isBlackSquareC208 && _electricalMainBusVoltage >= 3.0)
                parts.Add($"bus electrico {_electricalMainBusVoltage:F1}V");

            var detail = parts.Count == 0 ? "energia/luces aun no confirman Cold & Dark" : string.Join(", ", parts);
            return "Cierre bloqueado: debe quedar Cold & Dark nuevamente (" + detail + ").";
        }

        private bool IsAtDestinationOrGateArea()
        {
            if (TryGetRouteCoordinates(out _, out _, out var arrLat, out var arrLon)
                && Math.Abs(arrLat) > 0.0001
                && Math.Abs(arrLon) > 0.0001)
            {
                return ComputeDistanceToDestinationNm() <= 3.0;
            }

            return OnGround && GS <= 3 && ParkingBrakeOn;
        }

        private void FinishFlight()
        {
            if (!IsManualCloseoutGateReady(out var closeoutBlockReason))
            {
                RefreshManualCloseoutState();
                MessageBox.Show(
                    closeoutBlockReason + "\n\nEl cierre de vuelo es manual y solo se habilita al quedar detenido en gate con motores apagados y Cold & Dark.",
                    "Cierre de vuelo bloqueado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                "Cerrar vuelo ahora?\n\nConfirme solo si esta en gate/destino, freno de estacionamiento activado, motores apagados y aeronave Cold & Dark.",
                "Confirmar cierre manual",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            if (pilot == null) return;
            var report = AcarsContext.FlightService.GenerateReport(pilot.CallSign);
            report.ResultStatus = "completed";
            report.ManualCloseoutConfirmed = true;
            if (_picCheckActive)
                CompletePicCheck(false);
            ApplyPicSummaryToReport(report);
            AcarsContext.FlightService.ArmCloseoutReset();
            _main.ArmManualCloseoutNavigation();

            if (_picPenaltyPoints > 0)
            {
                report.AirbornePenalty += _picPenaltyPoints;
                report.Violations.Add(new ScoreEvent
                {
                    Code = "CRU-PIC",
                    Phase = "CRU",
                    Description = $"Radio PIC Check fallido ({_picPenaltyPoints / 5} vez/veces) â€” COM2 sin verificar",
                    Points = -_picPenaltyPoints
                });
                report.PatagoniaScore = Math.Max(0, report.PatagoniaScore - _picPenaltyPoints);
                report.ProcedureScore = Math.Max(0, report.ProcedureScore - _picPenaltyPoints);
                report.ApplyLegacyScoreProjection();
            }

            _main.ShowPostFlightReport(report);
        }

        private void CancelFlight()
        {
            var result = MessageBox.Show(
                "Â¿Cancelar el vuelo en curso?\n\nSe enviara como cancelacion operacional (cancelled), no como vuelo completado.",
                "Cancelar vuelo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            if (pilot == null) return;
            var report = AcarsContext.FlightService.GenerateReport(pilot.CallSign);
            report.ResultStatus = "cancelled";
            report.ManualCloseoutConfirmed = true;
            report.Remarks = string.IsNullOrWhiteSpace(report.Remarks)
                ? "Cancelado por tripulacion desde ACARS."
                : report.Remarks + " | Cancelado por tripulacion desde ACARS.";
            AcarsContext.FlightService.ArmCloseoutReset();
            _main.ArmManualCloseoutNavigation();
            _main.ShowPostFlightReport(report);
        }

        // â”€â”€ PIC Check â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static bool IsPicEligiblePhase(FlightPhase phase)
        {
            return phase == FlightPhase.Climb
                || phase == FlightPhase.Cruise
                || phase == FlightPhase.Descent;
        }

        private void EnsurePicScheduleInitialized()
        {
            if (_picChecksTotal > 0) return;

            var blockMin = AcarsContext.Runtime.CurrentDispatch?.ExpectedBlockP50Minutes
                        ?? AcarsContext.Runtime.CurrentDispatch?.ScheduledBlockMinutes
                        ?? 0;
            _picChecksTotal = blockMin >= 120 ? 3 : 1;
            _picChecksDone = 0;
            _picPenaltyPoints = 0;
            _picConfirmed = false;
            _lastPicMatchedRadio = string.Empty;
            _lastPicCheckTime = DateTime.MinValue;
            OnPropertyChanged(nameof(PicConfirmed));
            OnPropertyChanged(nameof(PicCheckLabel));
            OnPropertyChanged(nameof(PicButtonLabel));
        }

        private bool IsPicFrequencyMatched(out string matchedRadio)
        {
            // PIC Patagonia Wings: validacion exclusivamente sobre COM2 activo.
            // COM1 no confirma PIC OK para evitar falsos positivos.
            matchedRadio = string.Empty;

            if (FrequencyMatches(_com2FrequencyMhz, _picFrequency))
            {
                matchedRadio = "COM2";
                return true;
            }

            return false;
        }

        private static bool FrequencyMatches(double actual, double required)
        {
            return actual > 100d && required > 100d && Math.Abs(actual - required) <= PicFrequencyToleranceMhz;
        }

        private void ApplyPicSummaryToReport(FlightReport report)
        {
            if (report == null) return;

            report.PicChecksTotal = _picChecksTotal;
            report.PicChecksCompleted = _picChecksDone;
            report.PicChecksFailed = Math.Max(0, _picPenaltyPoints / 5);
            report.PicChecksSucceeded = Math.Max(0, _picChecksDone - report.PicChecksFailed);
            report.LastPicRequiredFrequencyMhz = _picFrequency;
            report.PicRadioSource = _lastPicMatchedRadio;
        }

        private void TriggerPicCheck()
        {
            _picFrequency    = _picFrequencies[_picRandom.Next(_picFrequencies.Length)];
            _picSecondsLeft  = 120;
            _picCheckActive  = true;
            _picConfirmed    = false;
            _lastPicCheckTime = DateTime.UtcNow;
            RadioAcarsMessage = $"PIC CHECK #{_picChecksDone + 1}/{_picChecksTotal} â€” Sintonice COM2: {_picFrequency:F3} MHz  ({_picSecondsLeft}s)";
            _eventLog.Add($"CRU: PIC CHECK #{_picChecksDone + 1} â€” radio requerida COM2: {_picFrequency:F3}");
            OnPropertyChanged(nameof(PicConfirmed));
            OnPropertyChanged(nameof(PicCheckLabel));
            OnPropertyChanged(nameof(PicButtonLabel));
            CommandManager.InvalidateRequerySuggested();
            AcarsContext.Sound.PlayDing();
        }

        private void TickPicCheck()
        {
            if (!_picCheckActive) return;

            // Verificar si COM2 activo coincide con la frecuencia requerida.
            if (IsPicFrequencyMatched(out var matchedRadio))
            {
                _lastPicMatchedRadio = matchedRadio;
                CompletePicCheck(true);
                return;
            }

            _picSecondsLeft--;
            OnPropertyChanged(nameof(PicCheckLabel));
            OnPropertyChanged(nameof(PicButtonLabel));
            RadioAcarsMessage = $"PIC CHECK #{_picChecksDone + 1}/{_picChecksTotal} - COM2 requerida: {_picFrequency:F3} MHz | actual: {_com2FrequencyMhz:F3} MHz ({_picSecondsLeft}s)";

            if (_picSecondsLeft <= 0)
                CompletePicCheck(false);
        }

        private void CompletePicCheck(bool success)
        {
            _picCheckActive = false;
            _picChecksDone++;

            if (success)
            {
                _picConfirmed = true;
                _eventLog.Add($"CRU: PIC CHECK #{_picChecksDone} OK {_lastPicMatchedRadio} verificado ({_picFrequency:F3})");
                RadioAcarsMessage = $"OK PIC CHECK {_picChecksDone}/{_picChecksTotal} confirmado - {(_lastPicMatchedRadio == string.Empty ? "radio" : _lastPicMatchedRadio)}: {_picFrequency:F3}";
                AcarsContext.Sound.PlayDing();
            }
            else
            {
                _picConfirmed = false;
                _picPenaltyPoints += 5;
                _eventLog.Add($"CRU: PIC CHECK #{_picChecksDone} FALLIDO - penalizacion -5 pts");
                RadioAcarsMessage = $"PIC CHECK fallido - COM2: {_picFrequency:F3} no confirmado (-5 pts)";
            }

            OnPropertyChanged(nameof(PicConfirmed));
            OnPropertyChanged(nameof(PicCheckLabel));
            OnPropertyChanged(nameof(PicButtonLabel));
            CommandManager.InvalidateRequerySuggested();
            UpdatePirepPreview();
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
                FlightPhase.Approach => "Aproximacion",
                FlightPhase.Landing => "Aterrizaje",
                FlightPhase.Taxi => "Taxi in",
                FlightPhase.Arrived => "Arribado",
                _ => "Desconectado"
            };
        }
        
        private void DetectAircraftType(string title)
        {
            // â”€â”€ Perfil normalizado via AircraftNormalizationService â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var profile = AircraftNormalizationService.ResolveProfile(title);
            var t = (title ?? string.Empty).ToUpperInvariant();

            // Usar capacidades del perfil para determinar APU, presurizaciÃ³n y motores
            HasApu            = profile.HasApu;
            HasPressurization = profile.IsPressurized;
            IsSingleEngine    = profile.EngineCount == 1;
            _supportsTransponderSystem = profile.SupportsTransponderModeSystem;
            _supportsDoorSystem = profile.SupportsDoorSystem && !profile.Code.StartsWith("C208", StringComparison.OrdinalIgnoreCase) && !profile.Code.StartsWith("BE58_MSFS", StringComparison.OrdinalIgnoreCase);
            _supportsSeatbeltSystem = profile.SupportsSeatbeltSystem;
            _supportsNoSmokingSystem = profile.SupportsNoSmokingSystem;
            _supportsBleedAirSystem = profile.SupportsBleedAirSystem;

            // Si el perfil es MSFS_NATIVE (fallback), mantener la detecciÃ³n manual
            // para aeronaves que aÃºn no estÃ©n en el catÃ¡logo
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
                HasPressurization = profile.IsPressurized || (profile.Code == "MSFS_NATIVE" && (isJet || isPressurizedTurboprop));                IsSingleEngine    =
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
                ? $"{profile.AddonProvider} Â· Algunos datos via LVARs"
                : $"{profile.AddonProvider} Â· SimConnect completo";

            Debug.WriteLine($"[InFlightVM] '{title}' â†’ Code:{profile.Code} APU:{HasApu} Presurizado:{HasPressurization} Monomotor:{IsSingleEngine} LVARs:{RequiresLvars}");
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


