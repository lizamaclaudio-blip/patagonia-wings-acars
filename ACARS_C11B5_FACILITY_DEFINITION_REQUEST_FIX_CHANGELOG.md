# ACARS C11B5 — Facility Definition/Request Correction

## Objetivo
Corregir el bridge SimConnect Facilities cuando queda en `esperando respuesta` después de pedir ICAO origen/destino.

## Base
- Base local: ACARS 7.0.17 publicado + cambios locales C10/C11 no publicados.
- No versiona a 7.0.18.
- No toca Web, Supabase, economía, wallet, salary, ledger, HUD ni SimBrief.

## Archivos modificados
- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`
- `PatagoniaWings.Acars.SimConnect/SimConnectStructs.cs`

## Cambios
- Reemplaza la definición plana de airport facilities por árbol SimConnect `OPEN AIRPORT` / `OPEN RUNWAY` / `CLOSE RUNWAY` / `CLOSE AIRPORT`.
- Registra estructuras managed para `SIMCONNECT_FACILITY_DATA_TYPE.AIRPORT` y `RUNWAY`.
- Cambia el request directo por ICAO para preferir `RequestFacilityData` legacy/documentado y dejar `RequestFacilityData_EX1` solo como fallback.
- Genera `UserRequestId` único por ICAO y mantiene mapa request→ICAO para diagnosticar respuestas aunque el payload no incluya ICAO parseable.
- Actualiza diagnóstico a `C11B5` y agrega `req=<id>` en el resumen del payload recibido.

## Validación requerida
1. Compilar Release x64 con MSBuild oficial.
2. Abrir MSFS en SCTB o SCEL.
3. Conectar ACARS y esperar 30–60 segundos.
4. Confirmar que C11 cambia desde `esperando respuesta` a `facility_data_received...` o `facility_data_end...`.
5. Si no responde, capturar línea C11 completa con scroll C11B4.

## Rollback
Restaurar solamente:
- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`
- `PatagoniaWings.Acars.SimConnect/SimConnectStructs.cs`
