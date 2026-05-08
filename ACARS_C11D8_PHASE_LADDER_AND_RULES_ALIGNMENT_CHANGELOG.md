# ACARS C11D8 — Phase Ladder and Procedure Rules Alignment

Base: local C10/C11 accumulated work after C11D7.

## Files changed
- PatagoniaWings.Acars.Core/Services/FlightService.cs
- PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs

## Purpose
- Stabilize the operational flight-phase ladder so short tests do not jump directly from takeoff to approach.
- Expose Takeoff -> Climb -> Cruise/Descent -> Approach evidence more consistently.
- Add READY_TAXI_OUT evidence when TAXI light is on and brake is released/movement starts at origin gate.
- Add door-open evidence flags for taxi/runway/airborne phases.
- Keep ACARS as recorder/evidence only; Web/Supabase remains the official score authority.

## Validation required
- MSBuild Release x64.
- Gate origin: TAXI light + brake release should show Listo rodaje / READY_TAXI_OUT, then Rodaje salida.
- Runway: should show Pista salida / RUNWAY_DEPARTURE before airborne.
- Airborne: should show Despegue briefly, then Ascenso; cruise/descent/approach should no longer skip all intermediate evidence on short flights.
