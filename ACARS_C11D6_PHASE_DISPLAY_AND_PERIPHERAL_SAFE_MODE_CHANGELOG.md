# ACARS C11D6 — Surface phase display + peripheral-safe SimConnect mode

Base: acumulado local C10/C11 posterior a 7.0.17, sin publicar ni versionar.

## Archivos modificados

- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
  - Agrega `RefreshSurfaceAwarePhaseLabel()` y `ResolveSurfaceAwarePhaseLabel()`.
  - La UI deja de mostrar `Prevuelo` cuando la evidencia C11D5 ya demuestra `TAXI_OUT`, `RUNWAY_DEPARTURE`, `RUNWAY_ARRIVAL`, `TAXI_IN` o gate.
  - Cambio solo visual/de fase mostrada: no altera `FlightService.CurrentPhase`, score, cierre, XML oficial ni Web/Supabase.

- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`
  - Refuerza modo read-only/peripheral-safe.
  - Si `EnableLvarReadBridge=false`, ya no inicializa MobiFlight ni canales ClientData.
  - Evita tocar canales compartidos que pueden convivir con SPAD.next, Logitech/Saitek o perfiles externos.
  - Mantiene lectura estándar SimConnect/FSUIPC y Facilities C11.

## Motivo

- En pista, C11D5 detectaba correctamente `RUNWAY_DEPARTURE`, pero el encabezado visual seguía mostrando `Prevuelo` porque la fase base aún no había pasado a takeoff/airborne.
- El usuario reportó posible conflicto con yoke/quadrant gestionados por SPAD.next. ACARS no debe controlar periféricos ni escribir canales de hardware; este bloque evita inicializar MobiFlight/ClientData cuando el bridge LVAR está apagado.

## Validación esperada

- Build MSBuild Release x64: 0 errores.
- En gate salida debe mostrar `Gate salida` o fase equivalente.
- En rodaje debe mostrar `Rodaje salida`.
- En runway antes de despegar debe mostrar `Pista salida`, no `Prevuelo`.
- En el log debe aparecer `MobiFlight/LVAR bridge no inicializado: modo read-only/peripheral-safe activo.`
- SPAD.next debe mantener yoke/quadrant sin desconexión provocada por ACARS.

## No incluido

- No cambia score.
- No toca Web/Supabase.
- No publica ni versiona 7.0.18.
- No modifica installer/autoupdate.
