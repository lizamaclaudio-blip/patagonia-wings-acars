# ACARS C11C3 — Runway FLOAT32 Struct Fix

## Base
- Se aplica después de C11B5 + C11C + C11C2.
- C11B5 confirmó recepción real de `SIMCONNECT_FACILITIES`.
- C11C2 confirmó payload real `FacilityRunwayDataStruct` desde MSFS, pero mostró campos geométricos corruptos:
  - `HeadingDegrees=E-34`
  - `LengthMeters=E-314`
  - `WidthMeters=E-309`

## Causa
La documentación SDK de MSFS define para RUNWAY:
- `LATITUDE`: FLOAT64
- `LONGITUDE`: FLOAT64
- `ALTITUDE`: FLOAT64
- `HEADING`: FLOAT32
- `LENGTH`: FLOAT32
- `WIDTH`: FLOAT32
- `SURFACE`: INT32

En C11C2 `HEADING`, `LENGTH` y `WIDTH` quedaron como `double`/FLOAT64, desplazando el marshaling de los campos siguientes.

## Archivos modificados
- `PatagoniaWings.Acars.SimConnect/SimConnectStructs.cs`

## Cambio aplicado
- `FacilityRunwayDataStruct.HeadingDegrees`: `double` -> `float`
- `FacilityRunwayDataStruct.LengthMeters`: `double` -> `float`
- `FacilityRunwayDataStruct.WidthMeters`: `double` -> `float`

## Resultado esperado
Después de compilar y abrir ACARS conectado a MSFS, la línea C11 debería dejar de mostrar:
- `runway_geometry_unparsed=... HeadingDegrees=E-34 ... LengthMeters=E-314 ...`

y pasar a:
- `runway_geometry_cached=SCEL:RWY ...`
- o una línea C11C con `RWY`, `dist`, `lat`, `thr`, `hdgErr`.

## Validación pendiente
- Build Release x64 con 0 errores.
- Prueba corta en SCTB/SCEL 30–60 segundos.
- Capturar línea C11 completa.

## Restricciones respetadas
- No versiona a 7.0.18.
- No toca Web/Supabase/economía/wallet/salary/ledger.
- No publica installer/autoupdate.
- No cambia flujo de cierre ni evaluación oficial.
