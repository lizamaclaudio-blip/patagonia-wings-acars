# ACARS C11D4 — Taxi/Gate Geometry Resolver

Base: ACARS local 7.0.17 + bloques C10/C11 acumulados, sin publicar 7.0.18.

## Objetivo
Convertir la evidencia `TAXI_PARKING`, `TAXI_POINT` y `TAXI_PATH` recibida por SimConnect Facilities en geometría operacional aproximada de gate/taxiway, sin aplicar score en ACARS.

## Archivos modificados
- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`
  - Cachea geometría de aeropuerto, parking, puntos y caminos de taxi por ICAO.
  - Convierte `BIAS_X`/`BIAS_Z` a lat/lon aproximado usando referencia del aeropuerto.
  - Calcula parking más cercano, punto taxi más cercano y path taxi más cercano.
  - Expone candidatos `gate` y `taxiway` como evidencia cruda.
- `PatagoniaWings.Acars.Core/Models/SimData.cs`
  - Agrega campos C11D4 para distancia a parking/path/punto, resumen y candidatos gate/taxiway.
- `PatagoniaWings.Acars.Core/Services/PirepXmlBuilder.cs`
  - Agrega evidencia C11D4 al `FacilityBridgeAuditReport`.
- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
  - Muestra resumen `C11D4 taxi/gate` en la línea C11 bridge.

## Regla de arquitectura
- ACARS solo registra evidencia operacional.
- Web/Supabase sigue siendo la autoridad oficial de evaluación, score, penalizaciones y validación de desvíos.
- No se toca Web, Supabase, installer, autoupdate, versión ni release.

## Validación esperada
En prueba corta con MSFS + ACARS conectado, la línea C11 debería mostrar algo similar a:

```txt
C11D4 taxi/gate gate MSFS SCTB parking=P.. /..m point=..m path=..m gate=YES taxi=NO
```

o bien:

```txt
C11D4 taxi/gate taxiway MSFS SCTB parking=... point=... path=... gate=NO taxi=YES
```

## Pendiente posterior
C11D5 debe usar esta evidencia para derivar fases terrestres reales:
`GATE -> TAXI_OUT -> RUNWAY_ENTRY -> TAKEOFF_ROLL -> LANDING_ROLL -> RUNWAY_EXIT -> TAXI_IN -> GATE`, todavía sin score cliente.
