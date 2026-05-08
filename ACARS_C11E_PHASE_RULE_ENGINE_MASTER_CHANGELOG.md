# ACARS C11E — Phase Rule Engine Master Alignment

Base esperada: cambios locales C10/C11 hasta C11D8, sin publicar ni versionar 7.0.18.

## Objetivo
Cerrar una sola capa maestra de fases y reglaje de evidencia para evitar microparches y pruebas intermedias. ACARS sigue siendo caja negra: registra evidencia operacional; Web/Supabase mantiene el score oficial.

## Archivos modificados

- `PatagoniaWings.Acars.Core/Services/FlightService.cs`
  - Reescribe la escalera operacional C11E.
  - Evita regresión visual/lógica a PreFlight cuando el avión ya entró en preparación de rodaje.
  - Introduce transición lista para rodaje basada en TAXI light + freno liberado/movimiento inicial.
  - Endurece touchdown para no convertir falsos `SIM ON GROUND` en Pista llegada durante despegue/ascenso.
  - Obliga ladder: Gate salida -> Listo/Rodaje salida -> Pista salida -> Despegue -> Ascenso -> Crucero/Descenso -> Aproximación -> Pista llegada -> Rodaje llegada -> Gate llegada.
  - Da prioridad a TAXI_IN tras runway-exit/taxiway/taxi light post-touchdown.
  - Cambia versión de matriz de fase y evidencia a `C11E`.
  - Corrige interpretación `afterTouchdown` para que `HasBeenAirborne` no convierta una pista de salida en pista de llegada.

- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
  - Cambia etiqueta de bitácora de `C11D5 reglaje evidencia` a `C11E reglaje evidencia`.
  - Mantiene display surface-aware sin cambiar score ni cierre.

## Reglajes alineados como evidencia, no score

- Gate salida: luces exteriores apagadas antes de iniciar, parking brake ON.
- Listo rodaje: TAXI light ON + freno liberado/movimiento inicial; beacon/nav esperadas; puerta cerrada si el perfil lo reporta.
- Rodaje salida: TAXI/BCN/NAV esperadas; STROBE/LANDING aún no obligatorias.
- Pista salida: STROBE/LANDING/XPDR ALT esperados; usa geometría MSFS Facilities.
- Despegue/ascenso: no puede saltar directo a aproximación sin exponer ascenso.
- Aproximación: requiere descenso + capa baja + geometría/alineamiento o AGL bajo.
- Pista llegada: solo después de touchdown confirmado, no por `HasBeenAirborne` simple.
- Rodaje llegada: prioriza taxiway/runway-exit/taxi light después de aterrizaje.
- Gate llegada: parking brake ON + gate/plataforma antes de finalizar.

## Validación pendiente

No se publica ni versiona. La validación queda para el cierre final con una sola compilación y prueba end-to-end.

