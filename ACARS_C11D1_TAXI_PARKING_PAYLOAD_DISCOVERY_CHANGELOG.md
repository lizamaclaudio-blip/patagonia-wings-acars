# ACARS C11D1 — Taxi/Parking Facility Payload Discovery

Base: ACARS local C10/C11 acumulado sobre release publicado 7.0.17.
No versiona, no publica, no cambia Web/Supabase/economía/wallet/salary/ledger.

## Objetivo

Validar si MSFS SimConnect Facilities entrega datos reales de rodaje/parking además de runway, usando el mismo bridge C11 confirmado en C11B5/C11C.

## Archivos modificados

- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`
  - Amplía la definición `OPEN AIRPORT` con hijos `TAXI_PARKING`, `TAXI_POINT` y `TAXI_PATH`.
  - Registra structs managed para `SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PARKING`, `TAXI_POINT` y `TAXI_PATH`.
  - Agrega diagnóstico tolerante `taxi_parking_payload`, `taxi_point_payload`, `taxi_path_payload`.
  - Mantiene runway geometry C11C sin cambiar scoring ni cierre.

- `PatagoniaWings.Acars.SimConnect/SimConnectStructs.cs`
  - Agrega structs secuenciales para payloads de `TAXI_PARKING`, `TAXI_POINT` y `TAXI_PATH`.

## Validación esperada

En ACARS, línea C11 bridge debería mostrar al menos uno de estos textos si MSFS entrega la data:

- `taxi_parking_payload=SCTB;items=...`
- `taxi_point_payload=SCTB;items=...`
- `taxi_path_payload=SCTB;items=...`

Si aparecen, C11D2 puede convertir esos puntos/path/parking en geometría usable para detectar taxiway/gate con más precisión.

## Regla de seguridad

ACARS sigue siendo caja negra/telemetría. Este bloque no evalúa score oficial ni escribe Web/Supabase.
