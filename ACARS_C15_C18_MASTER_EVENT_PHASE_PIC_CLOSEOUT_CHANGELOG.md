# ACARS C15-C18 Master — Event Slimming, Phase Engine, PIC COM2 y Closeout Realista

Base esperada: ACARS local C11E/C11F/C12/C13/C14A pre-7.0.18.
No versiona, no publica, no toca Web/Supabase, no cambia wallet/salary/ledger.

## C15 — XML/EventTimeline limpio estilo SUR Air

Archivos:
- `PatagoniaWings.Acars.Core/Services/PirepXmlBuilder.cs`

Cambios:
- `EventTimeline` pasa a `SchemaVersion=C15_EVENT_SLIM`.
- Deja de registrar COM1/COM2 como eventos por polling.
- Deja de registrar cada muestra como evento operacional.
- Registra solo eventos relevantes:
  - inicio/parada ACARS,
  - perfil aeronave/capabilities,
  - cambios de fase,
  - parking brake,
  - beacon/taxi/strobe/landing,
  - puerta si soportada,
  - AP master,
  - airborne/touchdown,
  - runway entry/takeoff roll/TDZ/runway exit,
  - XPDR si soportado,
  - resumen PIC final.
- `<Vuelo>` conserva formato legacy, pero reduce logs a muestras de borde, cambios de fase, eventos mayores y muestreo periódico cada 30s.

Motivo:
- El XML anterior registraba cientos de cambios COM/radio y miles de líneas repetitivas.
- ACARS debe detectar siempre, pero registrar solo evidencia útil.

## C16 — Phase Engine alineado a reglaje real

Archivos:
- `PatagoniaWings.Acars.Core/Services/FlightService.cs`

Cambios:
- Aproximación exige descenso sostenido, AGL bajo y contexto de llegada.
- Crucero exige estabilidad y altitud planificada si existe `PlannedAltitude` de SimBrief/OFP.
- Ascenso se conserva hasta acercarse al crucero planificado o hasta evidencia clara de descenso posterior.
- Touchdown/arrival ya no se acepta por muestras cortas/ruido justo tras despegue.
- Taxi-in exige haber salido del entorno runway; taxi light en pista no basta.
- `PhaseMatrixVersion` pasa a `C16`.

Motivo:
- Evitar saltos directos a aproximación.
- Evitar crucero falso bajo la altitud de plan de vuelo.
- Evitar taxi-in detectado todavía sobre pista.

## C17 — PIC COM2 real / SPAD.next tolerante

Archivos:
- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`

Cambios:
- PIC sigue siendo solo COM2; COM1 no confirma PIC.
- Acepta COM2 ACTIVE o COM2 STBY como evidencia de sintonización.
- Tolerancia PIC sube a ±0.030 MHz para hardware/radios 8.33/25 kHz/SPAD.next.
- El label muestra COM2 activo y standby.
- `NormalizeComFrequency` filtra ruido 100.000 MHz; solo acepta rango operacional 118–137 MHz.

Motivo:
- Evitar falsos negativos cuando el usuario sintoniza COM2 desde hardware/SPAD.next.
- Evitar eventos repetidos COM2 100.00 en XML.

## C18 — Closeout realista de llegada

Archivos:
- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`

Cambios:
- Ya no exige cold & dark total para cerrar.
- Requiere:
  - gate/plataforma/destino o override telemetry gate,
  - on ground / AGL bajo,
  - GS <= 3,
  - parking brake ON,
  - motores OFF,
  - cooldown de motor 60s,
  - APU OFF al cierre final,
  - beacon/strobe/landing/taxi OFF,
  - NAV puede quedar ON,
  - puerta abierta si el perfil soporta puertas,
  - XPDR 2000 + STBY/OFF si el perfil soporta transponder.

Motivo:
- Reflejar procedimiento real de llegada sin exigir apagar completamente el avión.

## Validación pendiente

- Compilar Release x64.
- Probar un solo vuelo completo gate → taxi → pista → ascenso → crucero/descent → aproximación → llegada → gate.
- Revisar XML final: debe ser mucho más liviano y no repetir COM cada segundo.

## No tocar aún

- Versión 7.0.18.
- Installer.
- Supabase Storage.
- Web downloads.
- Commit/push.
