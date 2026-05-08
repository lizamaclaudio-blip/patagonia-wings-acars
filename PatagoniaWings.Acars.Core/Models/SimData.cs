using System;
using PatagoniaWings.Acars.Core.Enums;

namespace PatagoniaWings.Acars.Core.Models
{
    public class SimData
    {
        public DateTime CapturedAtUtc { get; set; }
        
        // Identificación del avión
        public string AircraftTitle { get; set; } = string.Empty;
        public string AircraftProfile { get; set; } = "MSFS Native";

        // Posicion
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        /// <summary>
        /// Altitud operacional principal normalizada. Desde C0 corresponde a MSL real
        /// (PLANE ALTITUDE / fallback indicado), no a AGL. Se mantiene el nombre legacy
        /// para compatibilidad con UI, FlightService y PIREP.
        /// </summary>
        public double AltitudeFeet { get; set; }
        /// <summary>Altura sobre terreno normalizada. En tierra debe ser 0.</summary>
        public double AltitudeAGL { get; set; }
        /// <summary>Alias explícito de AltitudeFeet para consumidores nuevos.</summary>
        public double AltitudeMslFeet { get; set; }
        /// <summary>Alias explícito de AltitudeAGL para consumidores nuevos.</summary>
        public double AltitudeAglFeet { get; set; }
        public double IndicatedAltitudeFeet { get; set; }
        public double TrueAltitudeFeet { get; set; }
        public double PressureAltitudeFeet { get; set; }
        public double RadioAltitudeFeet { get; set; }
        public double GroundAltitudeFeet { get; set; }
        public double GroundElevationFeet { get; set; }
        public string FlightLevel { get; set; } = string.Empty;
        public string DisplayAltitudeMode { get; set; } = "MSL";
        public string DisplayAltitudeText { get; set; } = string.Empty;
        public string AltitudeSource { get; set; } = "unknown";
        public bool IsAltitudeReliable { get; set; }
        public double TransitionAltitudeFeet { get; set; } = 10000d;

        // C1 Phase Resolver evidence. ACARS stores this as RAW operational evidence;
        // Web/Supabase remains the official scoring authority.
        public string OperationalPhaseCode { get; set; } = "PRE";
        public string OperationalPhaseName { get; set; } = "Preflight";
        public string OperationalPhaseReason { get; set; } = string.Empty;
        public bool HasBeenAirborne { get; set; }
        public bool IsAirborneSample { get; set; }
        public bool TouchdownDetected { get; set; }
        public bool GateReadyCandidate { get; set; }

        // C2 Phase checklist evidence: raw operational checklist for UI, XML and later Web/Supabase scoring.
        // This is not official scoring; it only explains what each phase is measuring.
        public string PhaseChecklistStatus { get; set; } = "PENDING";
        public string PhaseChecklistSummary { get; set; } = string.Empty;
        public string PhaseChecklistRequired { get; set; } = string.Empty;
        public string PhaseChecklistSatisfied { get; set; } = string.Empty;
        public string PhaseChecklistMissing { get; set; } = string.Empty;
        public string PhaseChecklistWarnings { get; set; } = string.Empty;

        // C3 Phase transition matrix evidence. These fields make phase changes auditable
        // and help Web/Supabase evaluate by phase without trusting client-side score.
        public string PhaseTransitionFromCode { get; set; } = string.Empty;
        public string PhaseTransitionToCode { get; set; } = string.Empty;
        public string PhaseTransitionReason { get; set; } = string.Empty;
        public bool PhaseTransitionChanged { get; set; }
        public int PhaseTransitionIndex { get; set; }
        public int PhaseStabilitySamples { get; set; }
        public int PhaseCandidateSamples { get; set; }
        public int PhaseDwellSeconds { get; set; }
        public string PhaseDecisionConfidence { get; set; } = "initial";
        public string PhaseMatrixVersion { get; set; } = "C3";

        // C4 Phase audit: read-only QA evidence for validating phase detection before Web/Supabase scoring.
        public string PhaseAuditStatus { get; set; } = "PENDING";
        public string PhaseAuditSummary { get; set; } = string.Empty;
        public string PhaseAuditFlags { get; set; } = string.Empty;
        public string PhaseAuditVersion { get; set; } = "C4";

        // C5 Phase review contract: explains what ACARS expects the pilot/system to do,
        // what metrics are measured in the current phase and what Web/Supabase may evaluate.
        // It is audit evidence only; it is not client-side scoring.
        public string PhaseExpectedActions { get; set; } = string.Empty;
        public string PhaseMeasuredMetrics { get; set; } = string.Empty;
        public string PhaseScoringHints { get; set; } = string.Empty;
        public string PhaseReviewQuestion { get; set; } = string.Empty;
        public string PhaseReviewVersion { get; set; } = "C5";

        // C6 Phase prevalidation: compact readiness contract for the final simulator test.
        // This is still RAW evidence only. It tells us whether the current phase sample is
        // ready for later Web/Supabase review, without calculating official score in ACARS.
        public string PhasePrevalidationStatus { get; set; } = "PENDING";
        public string PhasePrevalidationSummary { get; set; } = string.Empty;
        public string PhasePrevalidationFlags { get; set; } = string.Empty;
        public string PhasePrevalidationVersion { get; set; } = "C6";

        // C9 Surface/Gate audit: inference only. ACARS does not know exact airport
        // taxiway/runway geometry here; these fields describe operational surface
        // candidates based on phase, speed, touchdown and gate evidence.
        public string SurfaceContextCode { get; set; } = "UNKNOWN";
        public string SurfaceContextName { get; set; } = string.Empty;
        public string SurfaceContextReason { get; set; } = string.Empty;
        public bool RunwayCandidate { get; set; }
        public bool TaxiwayCandidate { get; set; }
        public bool GateAreaCandidate { get; set; }
        public bool SurfaceContextReliable { get; set; }
        public string SurfaceContextVersion { get; set; } = "C9";

        // C10 Runway/Taxiway/TDZ audit: inferred operational geometry.
        // Without airport geometry/navdata ACARS does not claim exact taxiway/runway names;
        // it estimates runway intent, alignment and TDZ candidate from position, heading,
        // speed, phase and touchdown evidence. Web/Supabase remains the scoring authority.
        public string RunwayContextCode { get; set; } = "UNKNOWN";
        public string RunwayContextName { get; set; } = string.Empty;
        public string RunwayContextReason { get; set; } = string.Empty;
        public string EstimatedRunwayIdent { get; set; } = string.Empty;
        public string EstimatedRunwayReciprocalIdent { get; set; } = string.Empty;
        public double EstimatedRunwayHeadingDeg { get; set; }
        public double RunwayHeadingDeltaDeg { get; set; }
        public bool RunwayAlignedCandidate { get; set; }
        public bool RunwayEntryCandidate { get; set; }
        public bool RunwayExitCandidate { get; set; }
        public bool TakeoffRollCandidate { get; set; }
        public bool LandingRollCandidate { get; set; }
        public bool TouchdownZoneCandidate { get; set; }
        public bool TaxiwayProbable { get; set; }
        public bool RunwayGeometryAvailable { get; set; }
        public bool RunwayContextReliable { get; set; }
        public string RunwayContextVersion { get; set; } = "C10";

        // C11A SimConnect Facilities bridge: raw discovery evidence only.
        // This confirms whether MSFS facility APIs are available and whether ACARS
        // receives airport/facility data. C11B/C11C will transform this into
        // runway/taxiway/TDZ geometry. No client-side scoring is performed here.
        public bool FacilityBridgeAvailable { get; set; }
        public bool FacilityBridgeSubscribed { get; set; }
        public bool FacilityDataReceived { get; set; }
        public string FacilityDataSource { get; set; } = string.Empty;
        public string FacilityBridgeStatus { get; set; } = string.Empty;
        public string FacilityBridgeLastIcao { get; set; } = string.Empty;
        public string FacilityBridgeLastRegion { get; set; } = string.Empty;
        public int FacilityBridgeRecordsReceived { get; set; }
        public int FacilityBridgeAirportCount { get; set; }
        public string FacilityBridgeNearestAirports { get; set; } = string.Empty;
        public string FacilityBridgeRequestedIcaos { get; set; } = string.Empty;
        public string FacilityBridgeReceivedIcaos { get; set; } = string.Empty;
        public string FacilityBridgePendingIcaos { get; set; } = string.Empty;
        public int FacilityBridgeDirectRequestsSent { get; set; }
        public int FacilityBridgeDataEndCount { get; set; }
        public int FacilityBridgeExceptionCount { get; set; }
        public string FacilityBridgeLastException { get; set; } = string.Empty;
        public string FacilityBridgeLastRequestMode { get; set; } = string.Empty;
        public bool FacilityBridgeAwaitingResponse { get; set; }
        public double FacilityBridgeSecondsSinceRequest { get; set; }
        public DateTime? FacilityBridgeLastRequestUtc { get; set; }
        public DateTime? FacilityBridgeLastReceivedUtc { get; set; }
        public string FacilityBridgeVersion { get; set; } = "C11B2";

        // C11C Facilities runway geometry resolver: exact airport/runway geometry
        // from SimConnect FacilityData. These fields are still raw evidence only;
        // Web/Supabase remains the official scoring authority.
        public bool FacilityRunwayGeometryAvailable { get; set; }
        public string FacilityRunwayGeometryStatus { get; set; } = string.Empty;
        public string FacilityNearestRunwayAirportIcao { get; set; } = string.Empty;
        public string FacilityNearestRunwayIdent { get; set; } = string.Empty;
        public string FacilityNearestRunwayReciprocalIdent { get; set; } = string.Empty;
        public double FacilityNearestRunwayHeadingDeg { get; set; }
        public double FacilityNearestRunwayLengthMeters { get; set; }
        public double FacilityNearestRunwayWidthMeters { get; set; }
        public double FacilityNearestRunwayDistanceMeters { get; set; }
        public double FacilityRunwayLateralOffsetMeters { get; set; }
        public double FacilityRunwayLongitudinalOffsetMeters { get; set; }
        public double FacilityRunwayHeadingErrorDeg { get; set; }
        public double FacilityRunwayDistanceFromThresholdMeters { get; set; }
        public bool FacilityOnRunwayCandidate { get; set; }
        public bool FacilityRunwayAlignedCandidate { get; set; }
        public bool FacilityTouchdownZoneCandidate { get; set; }
        public string FacilityRunwayGeometrySummary { get; set; } = string.Empty;
        public int FacilityRunwayGeometryCount { get; set; }
        public string FacilityRunwayGeometryVersion { get; set; } = "C11C";

        // C11D Facilities taxiway/parking discovery: raw payload counters only.
        // These fields expose MSFS taxi/parking evidence to UI/XML without changing scoring.
        public string FacilityBridgeLastDataStatus { get; set; } = string.Empty;
        public string FacilityBridgeDataTypeHistogram { get; set; } = string.Empty;
        public string FacilityTaxiGeometryStatus { get; set; } = string.Empty;
        public int FacilityTaxiParkingPayloadCount { get; set; }
        public int FacilityTaxiPointPayloadCount { get; set; }
        public int FacilityTaxiPathPayloadCount { get; set; }
        public string FacilityTaxiGeometryVersion { get; set; } = "C11D2";

        // C11D4 Facilities taxiway/parking geometry resolver: approximate MSFS
        // airport-local taxi/parking geometry transformed into evidence fields.
        // ACARS still records evidence only; Web/Supabase owns official scoring.
        public bool FacilityTaxiGeometryAvailable { get; set; }
        public string FacilityNearestTaxiAirportIcao { get; set; } = string.Empty;
        public string FacilityNearestTaxiParkingLabel { get; set; } = string.Empty;
        public double FacilityNearestTaxiParkingDistanceMeters { get; set; }
        public double FacilityNearestTaxiPointDistanceMeters { get; set; }
        public double FacilityNearestTaxiPathDistanceMeters { get; set; }
        public bool FacilityGateAreaCandidate { get; set; }
        public bool FacilityTaxiwayCandidate { get; set; }
        public string FacilityTaxiGeometrySummary { get; set; } = string.Empty;
        public int FacilityTaxiParkingGeometryCount { get; set; }
        public int FacilityTaxiPointGeometryCount { get; set; }
        public int FacilityTaxiPathGeometryCount { get; set; }

        // C11D5 Surface procedure evidence. This is recorder evidence only;
        // Web/Supabase owns the official score/reglaje.
        public string SurfaceProcedurePhaseCode { get; set; } = string.Empty;
        public string SurfaceProcedurePhaseName { get; set; } = string.Empty;
        public string SurfaceProcedureEvidenceStatus { get; set; } = string.Empty;
        public string SurfaceProcedureEvidenceSummary { get; set; } = string.Empty;
        public string SurfaceProcedureEvidenceFlags { get; set; } = string.Empty;
        public bool SurfaceProcedureTaxiLightExpected { get; set; }
        public bool SurfaceProcedureStrobeExpected { get; set; }
        public bool SurfaceProcedureLandingLightExpected { get; set; }
        public bool SurfaceProcedureXpdrAltExpected { get; set; }
        public bool SurfaceProcedureBeaconExpected { get; set; }
        public bool SurfaceProcedureNavExpected { get; set; }
        public string SurfaceProcedureEvidenceVersion { get; set; } = "C11D5";

        // Velocidad
        public double IndicatedAirspeed { get; set; }
        public double GroundSpeed { get; set; }
        public double VerticalSpeed { get; set; }

        // Cabeceo / Rumbo
        public double Heading { get; set; }
        public double Pitch { get; set; }
        public double Bank { get; set; }

        // Motor / Combustible
        /// <summary>Combustible en la unidad nativa del backend (lbs para SimConnect, kg para FSUIPC).
        /// Usar siempre FuelKg para comparaciones y display.</summary>
        public double FuelTotalLbs { get; set; }
        /// <summary>Combustible total en kg, normalizado por el backend (SimConnect convierte lbs→kg).</summary>
        public double FuelKg { get; set; }
        public double FuelFlowLbsHour { get; set; }
        public double Engine1N1 { get; set; }
        public double Engine2N1 { get; set; }
        
        // Tanques individuales (para aviones complejos como A319 Headwind)
        public double FuelLeftTankLbs { get; set; }
        public double FuelRightTankLbs { get; set; }
        public double FuelCenterTankLbs { get; set; }
        public double FuelTotalCapacityLbs { get; set; }
        public double TotalWeightLbs { get; set; }
        public double TotalWeightKg { get; set; }
        public double ZeroFuelWeightKg { get; set; }
        public double PayloadKg { get; set; }

        // Aterrizaje
        public double LandingVS { get; set; }
        public double LandingG { get; set; }
        /// <summary>G instantanea leida desde el simulador (SimConnect G FORCE). Normal en vuelo estable ≈ 1.0.</summary>
        public double GForce { get; set; }
        public bool OnGround { get; set; }

        // Luces / Sistemas
        public bool StrobeLightsOn { get; set; }
        public bool BeaconLightsOn { get; set; }
        public bool LandingLightsOn { get; set; }
        public bool TaxiLightsOn { get; set; }
        public bool NavLightsOn { get; set; }
        public bool ParkingBrake { get; set; }
        public bool AutopilotActive { get; set; }
        public bool Pause { get; set; }

        // Sistemas de cabina
        public bool SeatBeltSign { get; set; }
        public bool NoSmokingSign { get; set; }
        public bool GearDown { get; set; }
        public bool GearTransitioning { get; set; }
        public bool FlapsDeployed { get; set; }
        public double FlapsPercent { get; set; }
        public bool SpoilersArmed { get; set; }
        public bool ReverserActive { get; set; }

        // Aviónica / Pressurización
        public bool TransponderCharlieMode { get; set; }
        public int TransponderCode { get; set; }
        public int TransponderStateRaw { get; set; }
        public bool ApuRunning { get; set; }
        public bool ApuAvailable { get; set; }
        public bool BleedAirOn { get; set; }
        public double CabinAltitudeFeet { get; set; }
        public double PressureDiffPsi { get; set; }

        // Ambiente
        public double OutsideTemperature { get; set; }
        public double WindSpeed { get; set; }
        public double WindDirection { get; set; }
        public double QNH { get; set; }
        public double QnhInHg { get; set; }
        public bool IsRaining { get; set; }

        // Motores extendidos (soporte 4 motores – 737/A320 usan 1-2, B744/A340 usan 1-4)
        public double Engine3N1 { get; set; }
        public double Engine4N1 { get; set; }

        // Sistemas adicionales (arquitectura SUR Air)
        public bool EngineOneRunning { get; set; }
        public bool EngineTwoRunning { get; set; }
        public bool EngineThreeRunning { get; set; }
        public bool EngineFourRunning { get; set; }
        public bool BatteryMasterOn { get; set; }
        public bool AvionicsMasterOn { get; set; }
        /// <summary>Voltaje real del bus electrico principal. En algunos addons Black Square el switch de bateria nativo puede quedar en 1 aunque el avion este sin energia; este valor permite inferir Cold & Dark con mas confianza.</summary>
        public double ElectricalMainBusVoltage { get; set; }
        public bool DoorOpen { get; set; }
        public bool InertialSeparatorOn { get; set; }
        public double EmptyWeightLbs { get; set; }
        public double EmptyWeightKg { get; set; }

        // Radios
        public double Com1FrequencyMhz { get; set; }
        public double Com1StandbyFrequencyMhz { get; set; }
        public double Com2FrequencyMhz { get; set; }
        public double Com2StandbyFrequencyMhz { get; set; }

        // Perfil de aeronave normalizado (código estable Patagonia Wings)
        // Ejemplos: C208_MSFS, C208_BLACKSQUARE, B738_PMDG, A320_FENIX, A20N_FBW
        public string DetectedProfileCode { get; set; } = "MSFS_NATIVE";
        public string AircraftTypeCode { get; set; } = string.Empty;
        public string AircraftVariantCode { get; set; } = string.Empty;
        public string AddonSource { get; set; } = string.Empty;
        public string ProfileCode { get; set; } = string.Empty;
        public string DetectionConfidence { get; set; } = "unknown";
        public string DetectionReason { get; set; } = string.Empty;
        public string DetectionSource { get; set; } = "simconnect_title";
        public string MatchedTitle { get; set; } = string.Empty;
        public string MatchedPattern { get; set; } = string.Empty;
        public bool FallbackUsed { get; set; }
        public string ProfileStatus { get; set; } = "unknown_profile";

        // Sim
        public SimulatorType SimulatorType { get; set; }
        public bool IsConnected { get; set; }
    }
}
