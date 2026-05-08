# ACARS C11B4 — Diagnostic Scroll UI

Fecha: 2026-05-03
Base: C10 + C11A/A2/A3 + C11B/B2/B3 local, sin publicar.

## Motivo
La tarjeta superior de vuelo cortaba las líneas largas de diagnóstico C4/C6/C9/C10/C11, especialmente cuando Facilities Bridge agregaba estado extendido de requests, ICAO pendientes, timeout o excepciones benignas.

## Cambios
- `PatagoniaWings.Acars.Master/Views/Pages/InFlightPage.xaml`
  - Agrupa las líneas de diagnóstico operacional en una bitácora interna con `ScrollViewer`.
  - Mantiene visible fase/reloj/fuente distancia.
  - Reduce levemente la fuente del log operacional para mejorar legibilidad.
  - Agrega `ToolTip` por línea para ver el texto completo al pasar el mouse.
  - Evita que C9/C10/C11 queden truncados por `MaxHeight` individual.

## No toca
- Lógica ACARS.
- SimConnect Facilities.
- Scoring.
- Web/Supabase.
- Economía/wallet/salary/ledger.
- Versionado/publicación.

## Validación esperada
- Build Release x64 OK.
- En vuelo, el panel superior muestra una bitácora con scroll.
- La línea C11 completa puede revisarse con rueda del mouse o tooltip.
