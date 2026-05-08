# ACARS C11D4C — Force compact Facility status

Base: cambios locales C10/C11 acumulados sobre ACARS 7.0.17, después de C11D4/D4B.

## Archivos tocados

- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`
  - Motivo: el registro operacional seguía mostrando `facility_data_received_...` completo aunque D4B compactaba en ViewModel. Ahora el estado fuente `_facilityBridgeStatus` se emite corto: `facility data SCEL TAXI_PATH taxi-payload`.
  - El dato largo no se pierde: queda en `_facilityBridgeLastDataStatus` para XML/diagnóstico.

- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
  - Motivo: reentrega el D4B con `Replace("\\r", " ").Replace("\\n", " ")` correcto y prioridad visual `C11D4 taxi/gate`, `D4 cache`, `taxi payload`, `types`.

## Validación esperada

- Build `MSBuild ... /p:Configuration=Release /p:Platform=x64` sin errores.
- La línea C11 bridge debe dejar de mostrar `facility_data_received_SCEL_type=TAXI_PATH...` como texto principal.
- Debe mostrar texto compacto tipo `facility data SCEL TAXI_PATH taxi-payload` y, si hay geometría, `C11D4 taxi/gate ...`.

## Restricciones

- No cambia scoring.
- No cambia cierre.
- No toca Web/Supabase.
- No versiona ni publica 7.0.18.
