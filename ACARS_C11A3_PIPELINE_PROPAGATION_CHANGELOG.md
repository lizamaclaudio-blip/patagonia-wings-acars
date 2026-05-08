# ACARS C11A3 — Facilities bridge pipeline propagation

Base: after C10 + C11A + C11A2 local patches, before 7.0.18 release.

## Files changed

- `PatagoniaWings.Acars.Master/Helpers/SimulatorCoordinator.cs`

## Why

When SimConnect and FSUIPC are both active, `SimulatorCoordinator.MergeByProfile()` clones the SimConnect primary sample before overlaying selected FSUIPC fields. The legacy clone only copied basic telemetry and dropped newer audit fields. This caused C11 Facilities bridge evidence to appear in `SimConnectService`, but disappear before reaching `InFlightViewModel` and XML/UI, showing:

`C11 bridge: Sin fuente · sin estado · API no disponible · sin aeropuertos cercanos`

## What changed

- Expanded `CloneSimData()` to preserve all current `SimData` fields, including:
  - C0 altitude aliases and reliability fields.
  - C1-C6 phase/checklist/audit/prevalidation evidence.
  - C9 surface context.
  - C10 runway/TDZ context.
  - C11A Facilities bridge state.
  - Fuel/payload/G-force, COM frequencies and detection metadata.

## Operational effect

C11 Facilities state now survives the SimConnect + FSUIPC merge pipeline. The UI should show real C11 states such as:

- `SIMCONNECT_FACILITIES_INIT_FAILED` with a concrete status if init fails.
- `SIMCONNECT_FACILITIES · airport_facility_subscription_active...` when subscribed/requested.
- `SIMCONNECT_FACILITIES · airport_minimal_list_received...` when airport list data reaches ACARS.

## Safety

- No scoring changed.
- No Web/Supabase/economy touched.
- No version/publication changes.
- FSUIPC overlay rules for AP/XPDR are preserved.
