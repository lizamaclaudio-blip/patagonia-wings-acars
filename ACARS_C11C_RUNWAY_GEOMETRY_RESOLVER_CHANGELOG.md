# ACARS C11C — Runway Geometry Resolver desde SimConnect Facilities

Fecha: 2026-05-03
Base: ACARS 7.0.17 publicado + cambios locales C10/C11B5 no publicados.
Estado: parche de diagnóstico/telemetría. No versionar ni publicar todavía.

## Objetivo

Transformar los `FacilityData` de MSFS/SIMCONNECT recibidos en C11B5 en evidencia geométrica operacional para detectar pista, alineamiento y TDZ con datos reales del simulador.

## Archivos tocados

- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`
  - Agrega caché de geometría de runways por ICAO.
  - Parsea registros `RUNWAY` recibidos por `OnRecvFacilityData`.
  - Calcula runway más cercana a la posición actual del avión.
  - Calcula heading activo, error de alineamiento, offset lateral, offset longitudinal, distancia a pista y distancia desde threshold activo.
  - Marca candidatos: `FacilityOnRunwayCandidate`, `FacilityRunwayAlignedCandidate`, `FacilityTouchdownZoneCandidate`.

- `PatagoniaWings.Acars.Core/Models/SimData.cs`
  - Agrega campos C11C de geometría runway MSFS Facilities.

- `PatagoniaWings.Acars.Master/Helpers/SimulatorCoordinator.cs`
  - Propaga campos C11C al clonar/mezclar telemetría SimConnect + FSUIPC.

- `PatagoniaWings.Acars.Core/Services/FlightService.cs`
  - C10 pasa a usar C11C cuando hay geometría real disponible.
  - `RunwayContextVersion` queda `C11C` si hay geometría runway desde MSFS Facilities.
  - Mantiene C10 como fallback si no hay geometría.

- `PatagoniaWings.Acars.Core/Services/PirepXmlBuilder.cs`
  - Agrega campos C11C al XML RAW.
  - Actualiza `RunwayTdzAuditReport` y `FacilityBridgeAuditReport` a `PIREP_PERFECT_C11C`.

- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
  - Muestra `C11C pista/TDZ` cuando hay geometría MSFS.
  - Agrega resumen de runway/offset/threshold/alineamiento al registro operacional.

## Reglas preservadas

- ACARS sigue siendo caja negra/telemetría.
- No se calcula score oficial en ACARS.
- Web/Supabase sigue siendo autoridad de evaluación.
- No se toca Web, Supabase, wallet, salary, ledger, SimBrief ni release/autoupdate.
- No se versiona a 7.0.18 en este bloque.

## Validación esperada

1. Compilar Release x64 con 0 errores.
2. Abrir MSFS en SCTB o SCEL.
3. Conectar ACARS y esperar 30–60 segundos.
4. Confirmar que C11 bridge siga mostrando `datos recibidos` y `records > 0`.
5. Confirmar que C10/C11 muestre `C11C pista/TDZ` y resumen tipo:
   - `SCEL RWY 17 dist=0m lat=...m thr=...m hdgErr=...deg`
6. Probar taxi/despegue/aterrizaje corto y revisar XML RAW para campos `FacilityRunway*`.

## Próximo bloque

- C11D: Taxiway/Parking resolver si MSFS entrega `TAXI_POINT`, `TAXI_PATH`, `TAXI_PARKING` de forma estable.
- C11E: Parser visual Web/Supabase para mostrar C11C/C11D en resumen de vuelo.
