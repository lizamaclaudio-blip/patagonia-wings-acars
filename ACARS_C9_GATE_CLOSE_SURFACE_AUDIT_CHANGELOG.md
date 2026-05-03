# ACARS C9 — Gate Close + Surface Audit Hotfix

Base: 7.0.16 C0-C8.

## Cambios
- Gate close deja de depender estrictamente de fase visual Taxi/Arrived si la telemetria viva confirma gate.
- El cierre sigue exigiendo vuelo activo, telemetria viva, OnGround, AGL <= 15, GS <= 3, parking brake, motores OFF y Cold & Dark.
- Si la distancia al destino viene stale o poco confiable, permite enviar el PIREP con override de telemetria gate; Web/Supabase evaluara oficialmente destino/fases.
- Corrige inferencia OnGround cuando MSFS/addon mantiene SIM ON GROUND=true o AGL=0 en vuelo con alta energia.
- Agrega C9 SurfaceContext: PARKING_GATE, TAXIWAY_OUT, RUNWAY_TAKEOFF_ROLL, AIRBORNE, RUNWAY_LANDING_ROLL, TAXIWAY_IN, GATE_AREA.
- Agrega SurfaceContext a UI y XML PIREP para auditar entrada a pista/rodaje sin usar geometria aeroportuaria.

## No toca
- Web, Supabase, economia, wallet, salary, ledger, HUD, SayIntentions ni SimBrief.

## Nota tecnica
ACARS no puede saber el nombre exacto de pista/taxiway solo con simvars basicas; C9 etiqueta superficie operacional inferida por fase, velocidad, touchdown, freno y gate. La evaluacion oficial queda en Web/Supabase.
