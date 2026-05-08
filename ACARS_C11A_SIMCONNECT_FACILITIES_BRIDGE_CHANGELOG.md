# ACARS C11A — SimConnect Facilities Bridge Skeleton

Base: C10 aplicado sobre 7.0.17, sin versionar/publicar.

## Objetivo
Preparar el bridge real de Facilities de MSFS vía SimConnect, sin catálogo manual de aeropuertos.

## Cambios
- Agrega evidencia C11A en `SimData`: disponibilidad del bridge, suscripción, datos recibidos, aeropuertos cercanos y estado.
- `SimConnectService` se suscribe a Facilities EX1 para aeropuertos cercanos y captura `OnRecvFacilityMinimalList`, `OnRecvFacilityData` y `OnRecvFacilityDataEnd`.
- UI InFlight muestra `C11 bridge: ...` como diagnóstico.
- XML PIREP agrega `<FacilityBridgeAuditReport>` con estado del bridge.

## Importante
Este bloque no evalúa ni penaliza. Solo valida si el simulador entrega datos de facilities.
C11B/C11C usarán esos payloads para runway/taxiway/TDZ geométrico real.
