# ACARS C11C2 — Facility Payload Introspection + Runway Struct Tolerance

Fecha: 2026-05-03
Base: cambios locales C10/C11 acumulados sobre ACARS publicado 7.0.17.
Release objetivo futura: 7.0.18, no publicar todavía.

## Motivo

C11B5 confirmó que SimConnect Facilities ya entrega `FacilityData` tipo `RUNWAY` y records reales, pero C11C todavía no mostró geometría usable en UI (`C11C pista/TDZ`). Esto indica que el payload llega, pero el parser runway puede estar recibiendo una estructura o alineación distinta a la esperada.

## Archivos modificados

- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`
- `PatagoniaWings.Acars.SimConnect/SimConnectStructs.cs`

## Cambios técnicos

### SimConnectStructs.cs

- Ajusta `FacilityRunwayDataStruct` para mantener `HeadingDegrees`, `LengthMeters` y `WidthMeters` como `double` en vez de `float`.
- Razón: algunos campos FacilityData del wrapper managed llegan como valores de 64 bits; usar `float` puede desplazar campos posteriores y dejar lat/lon/length/width no parseables.

### SimConnectService.cs

- Agrega parser tolerante por índice y por nombre de miembro para runway FacilityData.
- Agrega introspección de payload cuando llega `FacilityData` pero no se puede convertir en runway geométrica.
- La línea C11 bridge ahora puede mostrar:
  - `runway_geometry_cached=...` si la geometría se logró parsear.
  - `runway_geometry_unparsed=...` con tipo de objeto/campos/valores si aún no se logra parsear.
- Actualiza `FacilityBridgeVersion` a `C11C2`.

## Validación esperada

1. Compilar ACARS Release x64.
2. Abrir MSFS en SCTB/SCEL.
3. Conectar ACARS y esperar 30–60 segundos.
4. Revisar línea C11 bridge:
   - Caso ideal: `runway_geometry_cached=SCEL:...`
   - Caso diagnóstico: `runway_geometry_unparsed=SCEL;item=...;...`
5. Si aparece `unparsed`, copiar la línea completa para ajustar C11C3 con el layout exacto que MSFS está devolviendo.

## Alcance

- No cambia Web.
- No toca economía, wallet, salary, ledger, SimBrief ni HUD.
- No versiona a 7.0.18.
- No publica release.
