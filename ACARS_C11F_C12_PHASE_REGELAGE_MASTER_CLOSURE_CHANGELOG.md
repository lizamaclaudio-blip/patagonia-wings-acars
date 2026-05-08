# ACARS C11F/C12 — Phase reglaje master closure (no release)

Base: ACARS local C10/C11 sobre 7.0.17 publicado, acumulado para futura 7.0.18.

## Objetivo
Cerrar la alineación de fases y evidencia de reglaje antes de una prueba integral única, sin publicar, sin subir versión y sin mover scoring oficial al cliente.

## Archivos tocados
- `PatagoniaWings.Acars.Core/Services/FlightService.cs`
  - Endurece la máquina de fases para evitar salto prematuro a aproximación/llegada.
  - Agrega estabilización de contexto origen/destino usando `FacilityNearestRunwayAirportIcao`.
  - No acepta touchdown/llegada desde muestras ruidosas de `SIM ON GROUND` durante ventana de salida.
  - `READY_TAXI_OUT` queda ligado a TAXI light + freno liberado/movimiento inicial, no a BCN/NAV solas.
  - Agrega `STARTUP_AT_GATE` para preparación/encendido en gate sin marcar listo rodaje.
  - Amplía resumen de evidencia con ICAO/RWY MSFS, offset lateral y heading error.
  - Mantiene ACARS como caja negra/evidencia; Web/Supabase conserva score oficial.

- `PatagoniaWings.Acars.Core/Services/PirepXmlBuilder.cs`
  - Actualiza contrato de evidencia a C11F/C12 para Web/Supabase.
  - No cambia estructura oficial de cierre ni economía.

## No tocado
- Web/Supabase/economía/wallet/salary/ledger.
- Versionado/installer/autoupdate.
- Publicación.
- Score oficial en ACARS.

## Notas de WPF binding diagnostics
Los mensajes `RelativeSource FindAncestor Window` son diagnósticos de binding WPF, no errores de compilación. Quedan para limpieza final BIND-CLEAN cuando se trabaje con todos los XAML de `PreFlightPage`, `PostFlightPage` y `SupportPage`; no se modifican en este bloque para evitar romper navegación.

## Validación esperada
Compilar Release x64 al final del bloque maestro completo. Luego prueba integral única gate → taxi → runway → takeoff → climb → cruise/descent → approach → landing → taxi-in → gate.
