# ACARS C11D4B — Facility display compact / taxi visibility

Base: C11D4 Taxi/Gate Geometry Resolver + JSON quoting build fix.

## Objetivo
Evitar que el texto largo `facility_data_received_*` o `taxi_path_payload=*` oculte en la ventana ACARS la evidencia importante de C11D4.

## Archivos modificados
- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`

## Cambios
- Compacta el estado principal del bridge, por ejemplo `facility data SCEL TAXI_PATH`.
- Prioriza visualmente, al inicio de la línea C11 bridge:
  - `C11D4 taxi/gate ...`
  - `D4 cache ...`
  - `taxi payload parking/points/paths`
  - histograma `types ...`
- Recorta textos muy largos para que la línea no esconda la geometría.
- No cambia lógica de score, cierre, Web/Supabase, installer, versión ni publicación.

## Validación esperada
- Build Release x64 sin errores.
- En ACARS, la línea C11 bridge debe mostrar primero la evidencia C11D4/taxi antes del diagnóstico largo.
