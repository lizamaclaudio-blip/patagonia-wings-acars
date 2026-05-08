#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Core.Services;
using PatagoniaWings.Acars.SimConnect;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Coordinador híbrido por dato/perfil:
    /// - SimConnect = telemetría base
    /// - FSUIPC7 = overlay exclusivo por variable (AP / XPDR) según perfil
    /// - Si SimConnect cae, FSUIPC7 puede quedar como continuidad
    /// </summary>
    public sealed class SimulatorCoordinator : IDisposable
    {
        private const int InitialTelemetryTimeoutSeconds = 6;
        private const int LiveTelemetryTimeoutSeconds = 4;
        private const int OverlayFreshSeconds = 3;

        private readonly object _sync = new object();
        private readonly string _logFile;

        private FsuipcService? _fsuipc;
        private SimConnectService? _simConnect;
        private Timer? _healthTimer;
        private bool _disposed;
        private bool _disconnectRaised;
        private DateTime _connectStartedUtc;
        private DateTime? _lastPrimaryFrameUtc;
        private SimData? _lastSimConnectData;
        private SimData? _lastFsuipcData;
        private DateTime? _lastFsuipcFrameUtc;
        private bool _simConnectOnline;
        private bool _fsuipcOnline;
        private int _consecutiveInvalidSimFrames;
        private const int MaxInvalidFramesBeforeFallback = 3;

        private string _activeProfileCode = string.Empty;
        private string _lastProfileTraceCode = string.Empty;
        private string _lastMergeTraceKey = string.Empty;
        private DateTime _lastMergeTraceUtc = DateTime.MinValue;

        public bool IsConnected { get; private set; }
        public string ActiveBackend { get; private set; } = "None";

        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<SimData>? DataReceived;
        public event Action? Crashed;

        public SimulatorCoordinator(string logFile)
        {
            _logFile = logFile;
        }

        public void TryConnect(IntPtr hwnd)
        {
            StopHealthTimer();
            DisposeProviders();

            IsConnected = false;
            ActiveBackend = "None";
            _disconnectRaised = false;
            _connectStartedUtc = DateTime.UtcNow;
            _lastPrimaryFrameUtc = null;
            _lastSimConnectData = null;
            _lastFsuipcData = null;
            _lastFsuipcFrameUtc = null;
            _simConnectOnline = false;
            _fsuipcOnline = false;
            _consecutiveInvalidSimFrames = 0;
            _activeProfileCode = string.Empty;

            Exception? simEx = null;
            Exception? fsuipcEx = null;

            try
            {
                _simConnect = new SimConnectService();
                _simConnect.Connected += OnSimConnectConnected;
                _simConnect.Disconnected += OnSimConnectDisconnected;
                _simConnect.DataReceived += OnSimConnectDataReceived;
                _simConnect.Crashed += OnProviderCrashed;
                _simConnect.Connect(hwnd);
                _simConnectOnline = true;
                WriteLog("Backend base: SimConnect");
            }
            catch (Exception ex)
            {
                simEx = ex;
                WriteLog("SimConnect no disponible: " + ex.Message);
                DisposeSimConnect();
            }

            try
            {
                _fsuipc = new FsuipcService();
                _fsuipc.Connected += OnFsuipcConnected;
                _fsuipc.Disconnected += OnFsuipcDisconnected;
                _fsuipc.DataReceived += OnFsuipcDataReceived;
                _fsuipc.Connect();
                _fsuipcOnline = true;
                WriteLog(_simConnectOnline
                    ? "Overlay exclusivo FSUIPC7 listo para AP/XPDR"
                    : "Backend principal: FSUIPC7");
            }
            catch (Exception ex)
            {
                fsuipcEx = ex;
                WriteLog("FSUIPC7 no disponible: " + ex.Message);
                DisposeFsuipc();
            }

            UpdateBackendLabel(_activeProfileCode);

            if (!_simConnectOnline && !_fsuipcOnline)
            {
                throw new Exception(
                    "No fue posible conectar SimConnect ni FSUIPC7. " +
                    "SimConnect=" + (simEx != null ? simEx.Message : "n/d") +
                    " | FSUIPC=" + (fsuipcEx != null ? fsuipcEx.Message : "n/d"));
            }

            StartHealthTimer();
        }

        private void OnSimConnectConnected()
        {
            WriteLog("Handshake con SimConnect");
        }

        private void OnFsuipcConnected()
        {
            WriteLog("Handshake con FSUIPC7");
        }

        private void OnSimConnectDisconnected()
        {
            _simConnectOnline = false;
            _lastSimConnectData = null;
            UpdateBackendLabel(_activeProfileCode);

            if (!_fsuipcOnline)
            {
                ForceDisconnect("SimConnect desconectado y sin FSUIPC7 disponible");
            }
        }

        private void OnFsuipcDisconnected()
        {
            _fsuipcOnline = false;
            _lastFsuipcData = null;
            _lastFsuipcFrameUtc = null;
            UpdateBackendLabel(_activeProfileCode);

            if (!_simConnectOnline)
            {
                ForceDisconnect("FSUIPC7 desconectado y sin SimConnect disponible");
            }
        }

        private void OnProviderCrashed()
        {
            WriteLog("Crash detectado desde SimConnect");
            Crashed?.Invoke();
        }

        private void OnSimConnectDataReceived(SimData data)
        {
            if (data == null) return;

            bool latValid = data.Latitude >= -90 && data.Latitude <= 90 && Math.Abs(data.Latitude) > 0.0001;
            bool altValid = data.AltitudeFeet >= -2000 && data.AltitudeFeet < 100000;
            bool speedValid = data.IndicatedAirspeed >= 0 && data.IndicatedAirspeed < 1000;
            bool hasValidData = latValid && (altValid || speedValid);

            if (!hasValidData)
            {
                _consecutiveInvalidSimFrames++;
                Debug.WriteLine("[Coordinator] SimConnect inválido " + _consecutiveInvalidSimFrames + "/" + MaxInvalidFramesBeforeFallback);

                if (_consecutiveInvalidSimFrames >= MaxInvalidFramesBeforeFallback && _fsuipcOnline)
                {
                    _simConnectOnline = false;
                    DisposeSimConnect();
                    UpdateBackendLabel(_activeProfileCode);
                    WriteLog("SimConnect inválido repetidamente - FSUIPC7 queda como continuidad");
                }
                return;
            }

            _consecutiveInvalidSimFrames = 0;
            _lastSimConnectData = data;
            _activeProfileCode = data.DetectedProfileCode ?? string.Empty;
            _lastPrimaryFrameUtc = data.CapturedAtUtc == default(DateTime) ? DateTime.UtcNow : data.CapturedAtUtc;
            UpdateBackendLabel(_activeProfileCode);

            var outbound = MergeByProfile(data, GetFreshFsuipcOverlay(_lastPrimaryFrameUtc.Value));
            RaiseTelemetry(outbound);
        }

        private void OnFsuipcDataReceived(SimData data)
        {
            if (data == null) return;

            _lastFsuipcData = data;
            _lastFsuipcFrameUtc = data.CapturedAtUtc == default(DateTime) ? DateTime.UtcNow : data.CapturedAtUtc;
            UpdateBackendLabel(_activeProfileCode);

            if (_simConnectOnline && _lastSimConnectData != null)
            {
                return;
            }

            _lastPrimaryFrameUtc = _lastFsuipcFrameUtc;
            RaiseTelemetry(data);
        }

        private SimData? GetFreshFsuipcOverlay(DateTime referenceUtc)
        {
            if (!_fsuipcOnline || _lastFsuipcData == null || !_lastFsuipcFrameUtc.HasValue)
                return null;

            if ((referenceUtc - _lastFsuipcFrameUtc.Value).TotalSeconds > OverlayFreshSeconds)
                return null;

            return _lastFsuipcData;
        }

        private SimData MergeByProfile(SimData primary, SimData? overlay)
        {
            if (overlay == null)
                return primary;

            AircraftProfile profile = AircraftNormalizationService.GetProfile(primary.DetectedProfileCode);
            SimData merged = CloneSimData(primary);

            if (!string.Equals(_lastProfileTraceCode, profile.Code, StringComparison.OrdinalIgnoreCase))
            {
                _lastProfileTraceCode = profile.Code ?? string.Empty;
                Debug.WriteLine("[Coordinator] Perfil detectado=" + (profile.Code ?? "UNKNOWN")
                    + " AP(FSUIPC)=" + profile.PreferFsuipcAutopilot
                    + " XPDR(FSUIPC)=" + profile.PreferFsuipcTransponder);
            }

            bool isMaddog = IsMaddogProfile(profile.Code ?? string.Empty);

            if (profile.PreferFsuipcAutopilot || isMaddog)
            {
                merged.AutopilotActive = overlay.AutopilotActive;
            }
            else if (profile.UsesLvarAutopilot)
            {
                merged.AutopilotActive = primary.AutopilotActive;
            }

            if (profile.UsesLvarSeatbelt) merged.SeatBeltSign = primary.SeatBeltSign;
            if (profile.UsesLvarNoSmoking) merged.NoSmokingSign = primary.NoSmokingSign;
            if (profile.UsesLvarDoor) merged.DoorOpen = primary.DoorOpen;
            if (profile.UsesLvarApu)
            {
                merged.ApuAvailable = primary.ApuAvailable;
                merged.ApuRunning = primary.ApuRunning;
            }
            if (profile.UsesLvarBleedAir) merged.BleedAirOn = primary.BleedAirOn;

            if (profile.PreferFsuipcTransponder || isMaddog)
            {
                merged.TransponderStateRaw = overlay.TransponderStateRaw;
                merged.TransponderCharlieMode = overlay.TransponderStateRaw >= 3;
                if (overlay.TransponderCode > 0)
                {
                    merged.TransponderCode = overlay.TransponderCode;
                }
            }

            TraceMergeDecision(profile, primary, overlay, merged);
            return merged;
        }

        private void TraceMergeDecision(AircraftProfile profile, SimData primary, SimData overlay, SimData merged)
        {
            bool isMaddog = IsMaddogProfile(profile.Code);
            string apSource = (profile.PreferFsuipcAutopilot || isMaddog) ? "FSUIPC" : (profile.UsesLvarAutopilot ? "LVAR" : "SimConnect");
            string xpdrSource = (profile.PreferFsuipcTransponder || isMaddog) ? "FSUIPC" : "SimConnect";
            string seatbeltSource = profile.UsesLvarSeatbelt ? "LVAR" : "SimConnect";
            string noSmokingSource = profile.UsesLvarNoSmoking ? "LVAR" : "SimConnect";
            string doorSource = profile.UsesLvarDoor ? "LVAR" : "SimConnect";
            string apuSource = profile.UsesLvarApu ? "LVAR" : "SimConnect";
            string bleedSource = profile.UsesLvarBleedAir ? "LVAR" : "SimConnect";
            string key =
                (profile.Code ?? "UNKNOWN") + "|" +
                apSource + "|" + (primary.AutopilotActive ? "1" : "0") + "|" + (overlay.AutopilotActive ? "1" : "0") + "|" + (merged.AutopilotActive ? "1" : "0") + "|" +
                xpdrSource + "|" + primary.TransponderStateRaw + "|" + overlay.TransponderStateRaw + "|" + merged.TransponderStateRaw + "|" + merged.TransponderCode + "|" +
                seatbeltSource + "|" + (merged.SeatBeltSign ? "1" : "0") + "|" +
                noSmokingSource + "|" + (merged.NoSmokingSign ? "1" : "0") + "|" +
                doorSource + "|" + (merged.DoorOpen ? "1" : "0") + "|" +
                apuSource + "|" + (merged.ApuRunning ? "1" : "0") + "|" +
                bleedSource + "|" + (merged.BleedAirOn ? "1" : "0");

            DateTime now = DateTime.UtcNow;
            if (string.Equals(_lastMergeTraceKey, key, StringComparison.Ordinal) && (now - _lastMergeTraceUtc).TotalSeconds < 2)
            {
                return;
            }

            _lastMergeTraceKey = key;
            _lastMergeTraceUtc = now;

            Debug.WriteLine("[Coordinator] Merge perfil=" + (profile.Code ?? "UNKNOWN")
                + " AP src=" + apSource
                + " sc=" + (primary.AutopilotActive ? "1" : "0")
                + " fs=" + (overlay.AutopilotActive ? "1" : "0")
                + " final=" + (merged.AutopilotActive ? "1" : "0")
                + " | XPDR src=" + xpdrSource
                + " scRaw=" + primary.TransponderStateRaw
                + " fsRaw=" + overlay.TransponderStateRaw
                + " finalRaw=" + merged.TransponderStateRaw
                + " squawk=" + merged.TransponderCode
                + " | Seatbelt src=" + seatbeltSource + " final=" + (merged.SeatBeltSign ? "1" : "0")
                + " | NoSmoking src=" + noSmokingSource + " final=" + (merged.NoSmokingSign ? "1" : "0")
                + " | Door src=" + doorSource + " final=" + (merged.DoorOpen ? "1" : "0")
                + " | APU src=" + apuSource + " final=" + (merged.ApuRunning ? "1" : "0")
                + " | Bleed src=" + bleedSource + " final=" + (merged.BleedAirOn ? "1" : "0"));
        }

        private static bool IsMaddogProfile(string code)
        {
            return string.Equals(code, "MD82_MADDOG", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "MD83_MADDOG", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "MD88_MADDOG", StringComparison.OrdinalIgnoreCase);
        }

        private static SimData CloneSimData(SimData source)
        {
            return new SimData
            {
                CapturedAtUtc = source.CapturedAtUtc,
                AircraftTitle = source.AircraftTitle,
                AircraftProfile = source.AircraftProfile,
                Latitude = source.Latitude,
                Longitude = source.Longitude,
                AltitudeFeet = source.AltitudeFeet,
                AltitudeAGL = source.AltitudeAGL,
                AltitudeMslFeet = source.AltitudeMslFeet,
                AltitudeAglFeet = source.AltitudeAglFeet,
                IndicatedAltitudeFeet = source.IndicatedAltitudeFeet,
                TrueAltitudeFeet = source.TrueAltitudeFeet,
                PressureAltitudeFeet = source.PressureAltitudeFeet,
                RadioAltitudeFeet = source.RadioAltitudeFeet,
                GroundAltitudeFeet = source.GroundAltitudeFeet,
                GroundElevationFeet = source.GroundElevationFeet,
                FlightLevel = source.FlightLevel,
                DisplayAltitudeMode = source.DisplayAltitudeMode,
                DisplayAltitudeText = source.DisplayAltitudeText,
                AltitudeSource = source.AltitudeSource,
                IsAltitudeReliable = source.IsAltitudeReliable,
                TransitionAltitudeFeet = source.TransitionAltitudeFeet,
                OperationalPhaseCode = source.OperationalPhaseCode,
                OperationalPhaseName = source.OperationalPhaseName,
                OperationalPhaseReason = source.OperationalPhaseReason,
                HasBeenAirborne = source.HasBeenAirborne,
                IsAirborneSample = source.IsAirborneSample,
                TouchdownDetected = source.TouchdownDetected,
                GateReadyCandidate = source.GateReadyCandidate,
                PhaseChecklistStatus = source.PhaseChecklistStatus,
                PhaseChecklistSummary = source.PhaseChecklistSummary,
                PhaseChecklistRequired = source.PhaseChecklistRequired,
                PhaseChecklistSatisfied = source.PhaseChecklistSatisfied,
                PhaseChecklistMissing = source.PhaseChecklistMissing,
                PhaseChecklistWarnings = source.PhaseChecklistWarnings,
                PhaseTransitionFromCode = source.PhaseTransitionFromCode,
                PhaseTransitionToCode = source.PhaseTransitionToCode,
                PhaseTransitionReason = source.PhaseTransitionReason,
                PhaseTransitionChanged = source.PhaseTransitionChanged,
                PhaseTransitionIndex = source.PhaseTransitionIndex,
                PhaseStabilitySamples = source.PhaseStabilitySamples,
                PhaseCandidateSamples = source.PhaseCandidateSamples,
                PhaseDwellSeconds = source.PhaseDwellSeconds,
                PhaseDecisionConfidence = source.PhaseDecisionConfidence,
                PhaseMatrixVersion = source.PhaseMatrixVersion,
                PhaseAuditStatus = source.PhaseAuditStatus,
                PhaseAuditSummary = source.PhaseAuditSummary,
                PhaseAuditFlags = source.PhaseAuditFlags,
                PhaseAuditVersion = source.PhaseAuditVersion,
                PhaseExpectedActions = source.PhaseExpectedActions,
                PhaseMeasuredMetrics = source.PhaseMeasuredMetrics,
                PhaseScoringHints = source.PhaseScoringHints,
                PhaseReviewQuestion = source.PhaseReviewQuestion,
                PhaseReviewVersion = source.PhaseReviewVersion,
                PhasePrevalidationStatus = source.PhasePrevalidationStatus,
                PhasePrevalidationSummary = source.PhasePrevalidationSummary,
                PhasePrevalidationFlags = source.PhasePrevalidationFlags,
                PhasePrevalidationVersion = source.PhasePrevalidationVersion,
                SurfaceContextCode = source.SurfaceContextCode,
                SurfaceContextName = source.SurfaceContextName,
                SurfaceContextReason = source.SurfaceContextReason,
                RunwayCandidate = source.RunwayCandidate,
                TaxiwayCandidate = source.TaxiwayCandidate,
                GateAreaCandidate = source.GateAreaCandidate,
                SurfaceContextReliable = source.SurfaceContextReliable,
                SurfaceContextVersion = source.SurfaceContextVersion,
                RunwayContextCode = source.RunwayContextCode,
                RunwayContextName = source.RunwayContextName,
                RunwayContextReason = source.RunwayContextReason,
                EstimatedRunwayIdent = source.EstimatedRunwayIdent,
                EstimatedRunwayReciprocalIdent = source.EstimatedRunwayReciprocalIdent,
                EstimatedRunwayHeadingDeg = source.EstimatedRunwayHeadingDeg,
                RunwayHeadingDeltaDeg = source.RunwayHeadingDeltaDeg,
                RunwayAlignedCandidate = source.RunwayAlignedCandidate,
                RunwayEntryCandidate = source.RunwayEntryCandidate,
                RunwayExitCandidate = source.RunwayExitCandidate,
                TakeoffRollCandidate = source.TakeoffRollCandidate,
                LandingRollCandidate = source.LandingRollCandidate,
                TouchdownZoneCandidate = source.TouchdownZoneCandidate,
                TaxiwayProbable = source.TaxiwayProbable,
                RunwayGeometryAvailable = source.RunwayGeometryAvailable,
                RunwayContextReliable = source.RunwayContextReliable,
                RunwayContextVersion = source.RunwayContextVersion,
                FacilityBridgeAvailable = source.FacilityBridgeAvailable,
                FacilityBridgeSubscribed = source.FacilityBridgeSubscribed,
                FacilityDataReceived = source.FacilityDataReceived,
                FacilityDataSource = source.FacilityDataSource,
                FacilityBridgeStatus = source.FacilityBridgeStatus,
                FacilityBridgeLastIcao = source.FacilityBridgeLastIcao,
                FacilityBridgeLastRegion = source.FacilityBridgeLastRegion,
                FacilityBridgeRecordsReceived = source.FacilityBridgeRecordsReceived,
                FacilityBridgeAirportCount = source.FacilityBridgeAirportCount,
                FacilityBridgeNearestAirports = source.FacilityBridgeNearestAirports,
                FacilityBridgeRequestedIcaos = source.FacilityBridgeRequestedIcaos,
                FacilityBridgeReceivedIcaos = source.FacilityBridgeReceivedIcaos,
                FacilityBridgePendingIcaos = source.FacilityBridgePendingIcaos,
                FacilityBridgeDirectRequestsSent = source.FacilityBridgeDirectRequestsSent,
                FacilityBridgeDataEndCount = source.FacilityBridgeDataEndCount,
                FacilityBridgeExceptionCount = source.FacilityBridgeExceptionCount,
                FacilityBridgeLastException = source.FacilityBridgeLastException,
                FacilityBridgeLastRequestMode = source.FacilityBridgeLastRequestMode,
                FacilityBridgeAwaitingResponse = source.FacilityBridgeAwaitingResponse,
                FacilityBridgeSecondsSinceRequest = source.FacilityBridgeSecondsSinceRequest,
                FacilityBridgeLastRequestUtc = source.FacilityBridgeLastRequestUtc,
                FacilityBridgeLastReceivedUtc = source.FacilityBridgeLastReceivedUtc,
                FacilityBridgeVersion = source.FacilityBridgeVersion,
                FacilityRunwayGeometryAvailable = source.FacilityRunwayGeometryAvailable,
                FacilityRunwayGeometryStatus = source.FacilityRunwayGeometryStatus,
                FacilityNearestRunwayAirportIcao = source.FacilityNearestRunwayAirportIcao,
                FacilityNearestRunwayIdent = source.FacilityNearestRunwayIdent,
                FacilityNearestRunwayReciprocalIdent = source.FacilityNearestRunwayReciprocalIdent,
                FacilityNearestRunwayHeadingDeg = source.FacilityNearestRunwayHeadingDeg,
                FacilityNearestRunwayLengthMeters = source.FacilityNearestRunwayLengthMeters,
                FacilityNearestRunwayWidthMeters = source.FacilityNearestRunwayWidthMeters,
                FacilityNearestRunwayDistanceMeters = source.FacilityNearestRunwayDistanceMeters,
                FacilityRunwayLateralOffsetMeters = source.FacilityRunwayLateralOffsetMeters,
                FacilityRunwayLongitudinalOffsetMeters = source.FacilityRunwayLongitudinalOffsetMeters,
                FacilityRunwayHeadingErrorDeg = source.FacilityRunwayHeadingErrorDeg,
                FacilityRunwayDistanceFromThresholdMeters = source.FacilityRunwayDistanceFromThresholdMeters,
                FacilityOnRunwayCandidate = source.FacilityOnRunwayCandidate,
                FacilityRunwayAlignedCandidate = source.FacilityRunwayAlignedCandidate,
                FacilityTouchdownZoneCandidate = source.FacilityTouchdownZoneCandidate,
                FacilityRunwayGeometrySummary = source.FacilityRunwayGeometrySummary,
                FacilityRunwayGeometryCount = source.FacilityRunwayGeometryCount,
                FacilityRunwayGeometryVersion = source.FacilityRunwayGeometryVersion,
                IndicatedAirspeed = source.IndicatedAirspeed,
                GroundSpeed = source.GroundSpeed,
                VerticalSpeed = source.VerticalSpeed,
                Heading = source.Heading,
                Pitch = source.Pitch,
                Bank = source.Bank,
                FuelTotalLbs = source.FuelTotalLbs,
                FuelKg = source.FuelKg,
                FuelFlowLbsHour = source.FuelFlowLbsHour,
                Engine1N1 = source.Engine1N1,
                Engine2N1 = source.Engine2N1,
                FuelLeftTankLbs = source.FuelLeftTankLbs,
                FuelRightTankLbs = source.FuelRightTankLbs,
                FuelCenterTankLbs = source.FuelCenterTankLbs,
                FuelTotalCapacityLbs = source.FuelTotalCapacityLbs,
                TotalWeightLbs = source.TotalWeightLbs,
                TotalWeightKg = source.TotalWeightKg,
                ZeroFuelWeightKg = source.ZeroFuelWeightKg,
                PayloadKg = source.PayloadKg,
                LandingVS = source.LandingVS,
                LandingG = source.LandingG,
                GForce = source.GForce,
                OnGround = source.OnGround,
                StrobeLightsOn = source.StrobeLightsOn,
                BeaconLightsOn = source.BeaconLightsOn,
                LandingLightsOn = source.LandingLightsOn,
                TaxiLightsOn = source.TaxiLightsOn,
                NavLightsOn = source.NavLightsOn,
                ParkingBrake = source.ParkingBrake,
                AutopilotActive = source.AutopilotActive,
                Pause = source.Pause,
                SeatBeltSign = source.SeatBeltSign,
                NoSmokingSign = source.NoSmokingSign,
                GearDown = source.GearDown,
                GearTransitioning = source.GearTransitioning,
                FlapsDeployed = source.FlapsDeployed,
                FlapsPercent = source.FlapsPercent,
                SpoilersArmed = source.SpoilersArmed,
                ReverserActive = source.ReverserActive,
                TransponderCharlieMode = source.TransponderCharlieMode,
                TransponderCode = source.TransponderCode,
                TransponderStateRaw = source.TransponderStateRaw,
                ApuRunning = source.ApuRunning,
                ApuAvailable = source.ApuAvailable,
                BleedAirOn = source.BleedAirOn,
                CabinAltitudeFeet = source.CabinAltitudeFeet,
                PressureDiffPsi = source.PressureDiffPsi,
                OutsideTemperature = source.OutsideTemperature,
                WindSpeed = source.WindSpeed,
                WindDirection = source.WindDirection,
                QNH = source.QNH,
                QnhInHg = source.QnhInHg,
                IsRaining = source.IsRaining,
                Engine3N1 = source.Engine3N1,
                Engine4N1 = source.Engine4N1,
                EngineOneRunning = source.EngineOneRunning,
                EngineTwoRunning = source.EngineTwoRunning,
                EngineThreeRunning = source.EngineThreeRunning,
                EngineFourRunning = source.EngineFourRunning,
                BatteryMasterOn = source.BatteryMasterOn,
                AvionicsMasterOn = source.AvionicsMasterOn,
                ElectricalMainBusVoltage = source.ElectricalMainBusVoltage,
                DoorOpen = source.DoorOpen,
                InertialSeparatorOn = source.InertialSeparatorOn,
                EmptyWeightLbs = source.EmptyWeightLbs,
                EmptyWeightKg = source.EmptyWeightKg,
                Com1FrequencyMhz = source.Com1FrequencyMhz,
                Com1StandbyFrequencyMhz = source.Com1StandbyFrequencyMhz,
                Com2FrequencyMhz = source.Com2FrequencyMhz,
                Com2StandbyFrequencyMhz = source.Com2StandbyFrequencyMhz,
                DetectedProfileCode = source.DetectedProfileCode,
                AircraftTypeCode = source.AircraftTypeCode,
                AircraftVariantCode = source.AircraftVariantCode,
                AddonSource = source.AddonSource,
                ProfileCode = source.ProfileCode,
                DetectionConfidence = source.DetectionConfidence,
                DetectionReason = source.DetectionReason,
                DetectionSource = source.DetectionSource,
                MatchedTitle = source.MatchedTitle,
                MatchedPattern = source.MatchedPattern,
                FallbackUsed = source.FallbackUsed,
                ProfileStatus = source.ProfileStatus,
                SimulatorType = source.SimulatorType,
                IsConnected = source.IsConnected
            };
        }

        private void RaiseTelemetry(SimData outbound)
        {
            bool shouldRaiseConnected = false;
            lock (_sync)
            {
                if (!IsConnected)
                {
                    IsConnected = true;
                    _disconnectRaised = false;
                    shouldRaiseConnected = true;
                }
            }

            if (shouldRaiseConnected)
            {
                WriteLog("Telemetría real recibida desde " + ActiveBackend);
                Connected?.Invoke();
            }

            DataReceived?.Invoke(outbound);
        }

        private void StartHealthTimer()
        {
            StopHealthTimer();
            _healthTimer = new Timer(_ => HealthTick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void StopHealthTimer()
        {
            if (_healthTimer != null)
            {
                _healthTimer.Dispose();
                _healthTimer = null;
            }
        }

        private void HealthTick()
        {
            if (_disposed) return;

            DateTime now = DateTime.UtcNow;
            bool shouldDisconnect = false;
            string reason = string.Empty;

            lock (_sync)
            {
                if (_disconnectRaised || string.IsNullOrWhiteSpace(ActiveBackend) || ActiveBackend == "None")
                    return;

                if (_lastPrimaryFrameUtc.HasValue)
                {
                    if ((now - _lastPrimaryFrameUtc.Value).TotalSeconds > LiveTelemetryTimeoutSeconds)
                    {
                        if (!_fsuipcOnline && !_simConnectOnline)
                        {
                            shouldDisconnect = true;
                            reason = "Telemetría expirada en " + ActiveBackend;
                        }
                    }
                }
                else if ((now - _connectStartedUtc).TotalSeconds > InitialTelemetryTimeoutSeconds)
                {
                    if (!_simConnectOnline && !_fsuipcOnline)
                    {
                        shouldDisconnect = true;
                        reason = "No llegó telemetría inicial desde ningún backend";
                    }
                }
            }

            if (shouldDisconnect)
            {
                ForceDisconnect(reason);
            }
        }

        private void UpdateBackendLabel(string profileCode)
        {
            bool lvarActive = false;
            try
            {
                AircraftProfile profile = AircraftNormalizationService.GetProfile(profileCode);
                lvarActive = _simConnectOnline
                    && _simConnect != null
                    && _simConnect.IsLvarOverlayActive
                    && profile != null
                    && (profile.RequiresLvars
                        || profile.UsesLvarSeatbelt
                        || profile.UsesLvarNoSmoking
                        || profile.UsesLvarDoor
                        || profile.UsesLvarAutopilot
                        || profile.UsesLvarApu
                        || profile.UsesLvarBleedAir);
            }
            catch
            {
                lvarActive = false;
            }

            if (_simConnectOnline && _fsuipcOnline && lvarActive)
                ActiveBackend = "SimConnect + FSUIPC + MSFS2020 LVAR";
            else if (_simConnectOnline && _fsuipcOnline)
                ActiveBackend = "SimConnect + FSUIPC";
            else if (_simConnectOnline && lvarActive)
                ActiveBackend = "SimConnect + MSFS2020 LVAR";
            else if (_simConnectOnline)
                ActiveBackend = "SimConnect";
            else if (_fsuipcOnline)
                ActiveBackend = "FSUIPC7";
            else
                ActiveBackend = "None";
        }

        private void ForceDisconnect(string reason)
        {
            bool shouldRaise;
            lock (_sync)
            {
                if (_disconnectRaised) return;

                _disconnectRaised = true;
                shouldRaise = IsConnected || !string.IsNullOrWhiteSpace(ActiveBackend);
                IsConnected = false;
                _lastPrimaryFrameUtc = null;
            }

            WriteLog(reason);
            StopHealthTimer();
            DisposeProviders();

            if (shouldRaise)
            {
                Disconnected?.Invoke();
            }
        }

        private void DisposeProviders()
        {
            DisposeFsuipc();
            DisposeSimConnect();
            _simConnectOnline = false;
            _fsuipcOnline = false;
            UpdateBackendLabel(_activeProfileCode);
        }

        private void DisposeFsuipc()
        {
            if (_fsuipc == null) return;
            _fsuipc.Connected -= OnFsuipcConnected;
            _fsuipc.Disconnected -= OnFsuipcDisconnected;
            _fsuipc.DataReceived -= OnFsuipcDataReceived;
            _fsuipc.Dispose();
            _fsuipc = null;
        }

        private void DisposeSimConnect()
        {
            if (_simConnect == null) return;
            _simConnect.Connected -= OnSimConnectConnected;
            _simConnect.Disconnected -= OnSimConnectDisconnected;
            _simConnect.DataReceived -= OnSimConnectDataReceived;
            _simConnect.Crashed -= OnProviderCrashed;
            _simConnect.Dispose();
            _simConnect = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            StopHealthTimer();
            DisposeProviders();
            _disposed = true;
            IsConnected = false;
            ActiveBackend = "None";
        }

        private void WriteLog(string msg)
        {
            try
            {
                File.AppendAllText(_logFile, "[" + DateTime.UtcNow.ToString("o") + "] " + msg + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
