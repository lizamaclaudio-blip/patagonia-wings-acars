# ACARS C15-C18 Build Fix — TransponderCode CSV Export

Fecha: 2026-05-03
Base: C15-C18 Master Event/Phase/PIC/Closeout local pre-7.0.18

## Motivo
El build fallaba en `InFlightViewModel.cs` porque el exportador CSV de evidencia local referenciaba `SimData.Squawk`, propiedad que no existe en el modelo actual.

## Cambio aplicado
- Archivo: `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
- Se reemplaza `s.Squawk` por `s.TransponderCode` en la línea de exportación CSV de snapshots.

## Alcance
- No cambia lógica de score.
- No cambia fases.
- No toca Web/Supabase.
- No toca economía, wallet, salary ni ledger.
- No publica ni versiona.

## Validación esperada
Compilar ACARS Release x64 sin el error:
`CS1061: SimData no contiene una definición para Squawk`.
