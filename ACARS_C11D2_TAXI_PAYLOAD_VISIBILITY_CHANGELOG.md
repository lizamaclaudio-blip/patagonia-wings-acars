# ACARS C11D2 - Taxi/Parking payload visibility

Fecha: 2026-05-03
Base: ACARS 7.0.17 publicado + cambios locales C10/C11 no publicados.

## Objetivo

C11D1 ya estaba recibiendo miles de registros FacilityData, pero la UI podía volver a mostrar un estado de suscripción/reintento y ocultar el último payload real. Este bloque no cambia score ni cierre: solo expone evidencia de taxi/parking en UI y XML.

## Archivos modificados

- `PatagoniaWings.Acars.Core/Models/SimData.cs`
  - Agrega campos de trazabilidad C11D2: último FacilityData, histograma de tipos, estado taxi/parking y contadores de TAXI_PARKING/TAXI_POINT/TAXI_PATH.

- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`
  - Preserva `_facilityBridgeLastDataStatus` aunque el estado general sea sobrescrito por suscripción/reintento.
  - Acumula histograma de tipos FacilityData recibidos.
  - Expone contadores taxi/parking al `SimData`.

- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
  - Muestra en la línea C11 bridge: `lastData`, `types`, `taxi_geometry_payload_cached` y `taxi payload parking/points/paths`.

- `PatagoniaWings.Acars.Core/Services/PirepXmlBuilder.cs`
  - Actualiza `FacilityBridgeAuditReport` a `PIREP_PERFECT_C11D2`.
  - Incluye últimos datos y contadores taxi/parking en XML como evidencia raw.

## No modificado

- No cambia Web/Supabase.
- No cambia economía/wallet/salary/ledger.
- No cambia versión visible.
- No publica release.
- No hace scoring local.

## Validación esperada

Compilar Release x64 y revisar la línea C11 bridge. Debe mostrar alguno de estos datos si MSFS entregó taxi payload:

- `types RUNWAY=...,TAXI_POINT=...,TAXI_PATH=...,TAXI_PARKING=...`
- `taxi_geometry_payload_cached_SCTB:parking=...;points=...;paths=...`
- `taxi payload parking=... points=... paths=...`
