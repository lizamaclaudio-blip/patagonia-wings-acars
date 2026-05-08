# ACARS C11D3 — Alternate/Diversion Facility Scope

Base: ACARS local C10/C11 accumulated over published 7.0.17. Not a release.

## Goal
Ensure SimConnect Facilities evidence is not limited to origin/destination. ACARS must prepare facility data for the planned alternate and dynamically request nearby airports so Web/Supabase can later evaluate diversions or landings at an unplanned airport using raw geometry evidence.

## Files changed

- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
  - Route facility request now includes:
    - origin ICAO
    - destination ICAO
    - dispatch alternate ICAO
    - dispatch current airport ICAO
  - De-duplicates ICAOs before requesting SimConnect FacilityData.
  - Keeps retry throttling at 30 seconds per request set.

- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`
  - When MSFS sends the nearest airport minimal list, ACARS now requests direct FacilityData for up to 6 nearest airports.
  - Requests are de-duplicated by existing `_facilityBridgeRequestedIcaos` guard.
  - Facility bridge version updated to `C11D3`.

## Operational rule
- Planned route: origin + destination are always requested.
- Planned alternate: alternate is requested from dispatch when available.
- Real diversion: any nearby airport reported by MSFS Facilities may be requested dynamically.
- ACARS remains raw evidence only; official diversion validation, score and economy stay in Web/Supabase.

## Validation required
1. Build Release x64.
2. Start at SCTB with route SCTB-SCEL and alternate if available.
3. Confirm C11 bridge requested list includes SCTB/SCEL and alternate when dispatch has one.
4. Confirm nearest airport auto requests do not spam; requests should increase only for new ICAOs.
5. Do not publish/version until full C11D taxi/parking resolver is validated.
