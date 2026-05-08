# ACARS C11A2 — Facilities bridge init/retry hotfix

Base: C10 + C11A aplicado, sin versionar/publicar.

## Motivo
En prueba local el UI mostró `C11 bridge: Sin fuente · sin estado · API no disponible`, aunque la DLL de SimConnect expone Facilities. Esto indica que la inicialización del bridge no alcanzó a ejecutarse o quedó sin retry desde la primera muestra real.

## Cambios
- `SimConnectService.cs` inicializa Facilities inmediatamente después de registrar eventos.
- Mantiene inicialización desde `OnRecvOpen`.
- Agrega retry desde la primera muestra real de telemetría si el bridge sigue sin disponible.
- Además de `SubscribeToFacilities_EX1`, solicita una lista puntual con `RequestFacilitiesList_EX1` para evitar que la suscripción quede silenciosa cuando el avión ya está estacionado.
- Agrega fallback a `RequestFacilitiesList` legacy si EX1 falla.
- Mejora `FacilityDataSource` para distinguir `SIMCONNECT_FACILITIES_INIT_FAILED` de `UNAVAILABLE`.

## Seguridad
- No cambia scoring.
- No cambia economía.
- No cambia Web/Supabase.
- No publica versión.
