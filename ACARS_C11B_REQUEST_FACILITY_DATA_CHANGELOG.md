# ACARS C11B — RequestFacilityData directo por ICAO

Base: C10 + C11A/C11A2/C11A3 local sobre main 7.0.17.

## Cambios

- Agrega solicitud directa de Facilities por ICAO desde el despacho activo.
- InFlightViewModel solicita origen/destino normalizados al bridge activo de SimConnect.
- SimConnectService expone `RequestAirportFacilitiesForActive(...)` sin tocar scoring.
- Configura definición mínima de AIRPORT (`ICAO`, `LATITUDE`, `LONGITUDE`, `ALTITUDE`).
- Usa `RequestFacilityData_EX1` con fallback a `RequestFacilityData` legacy.
- Deduplica ICAO solicitados para no saturar SimConnect.
- Actualiza estado C11 en UI/XML: requested / received / failed.
- Mantiene C10 probable como fallback si Facilities no entrega payload.

## No cambia

- No penaliza.
- No calcula score local.
- No cambia Web/Supabase/economía/wallet.
- No publica versión.

## Validación esperada

- Build Release x64 sin errores.
- UI C11 debe mostrar `direct_airport_facility_requested:SCTB,SCEL` o `facility_data_received_*`.
