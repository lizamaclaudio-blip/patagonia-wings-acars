# ACARS C7 — Final Phase Acceptance Matrix

Base esperada: ACARS 7.0.15 con C0+C1+C2+C3+C4+C5+C6 aplicados.

## Cambios
- Agrega `<PhaseAcceptanceMatrix>` al PIREP XML.
- Define una matriz explícita PRE/TAX_OUT/TO/CLB/CRZ/DES/APP/LDG/TAX_IN/GATE.
- Para cada fase guarda evidencia requerida, señales críticas, estado OK/REVIEW/BLOCK/NOT_OBSERVED y pregunta de revisión.
- Agrega protocolo final de prueba de vuelo para validar altitud, fases, touchdown, gate y variables unsupported.

## Alcance
- Solo XML/evidencia ACARS.
- No toca Web/Supabase.
- No toca economía, wallet, salary, ledger.
- No toca HUD, SayIntentions, SimBrief ni Route Finder.

## Validación requerida
- Compilar Release x64 con MSBuild.
- Hacer prueba completa al final del paquete C0-C7.
- Revisar en el XML: `<Altitude>`, `<FlightPhaseSummary>`, `<PhaseAuditReport>`, `<PhasePrevalidationPackage>` y `<PhaseAcceptanceMatrix>`.
