# ACARS C8 — Final Pretest Manifest

Base: ACARS 7.0.15 con C0-C7 aplicado.

## Objetivo

Dejar el PIREP XML listo para la prueba final de simulador, sin tocar Web/Supabase ni score oficial.

## Cambios

- `PatagoniaWings.Acars.Core/Services/PirepXmlBuilder.cs`
  - Agrega `<PhaseTestRunManifest>` al XML.
  - Incluye inventario de evidencia C0-C7: altitude resolver, state machine, checklist, transitions, audit, review contract, prevalidation y acceptance matrix.
  - Agrega plan manual de capturas por fase: PRE, TAX_OUT, TO, CLB, CRZ, DES, APP, LDG, TAX_IN y GATE.
  - Agrega gates de aceptación previos a Web/Supabase: Altitude, PhaseSequence, Touchdown, GateReady y NoClientScore.
  - Agrega checklist de commit seguro para no subir bin/obj/zip/backups/installer.

## No toca

- Web
- Supabase
- score oficial
- economía / wallet / salary / ledger
- HUD
- SayIntentions
- SimBrief
- Route Finder

## Validación requerida

```powershell
MSBuild.exe ".\\PatagoniaWings.Acars.Master\\PatagoniaWings.Acars.Master.csproj" /p:Configuration=Release /p:Platform=x64
```

Esperado: 0 errores.
