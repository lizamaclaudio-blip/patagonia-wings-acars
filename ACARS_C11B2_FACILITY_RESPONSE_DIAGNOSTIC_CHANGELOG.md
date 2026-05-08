# ACARS C11B2 — FacilityData Response Diagnostic

Base: C10 + C11A/A2/A3 + C11B local sobre ACARS 7.0.17.

## Objetivo

Agregar diagnóstico fino de respuesta SimConnect Facilities después de solicitar aeropuertos por ICAO.

## Cambios

- Registra request mode por ICAO: `EX1` o `LEGACY_AFTER_EX1_FAIL`.
- Registra ICAO solicitados, recibidos y pendientes.
- Registra `OnRecvFacilityDataEnd`.
- Registra excepciones SimConnect posteriores a requests Facilities.
- Agrega timeout visual si SCTB/SCEL fueron solicitados pero no llega payload.
- Propaga estos campos por `SimulatorCoordinator` para no perderlos al mezclar SimConnect + FSUIPC.
- Expone diagnóstico en UI C11 bridge y en XML `<FacilityBridgeAuditReport>`.

## No cambia

- No calcula score oficial.
- No penaliza.
- No reemplaza C10 probable.
- No publica versión.

## Próximo bloque

C11C: si hay `facility_data_received_*`, convertir payloads confirmados en geometría runway/taxiway/parking.
