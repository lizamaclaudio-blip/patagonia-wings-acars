# Patagonia Wings ACARS — Bloque 8B Manual Gate Closeout

Base usada: `InFlightViewModel.cs` del Bloque 6 Radio PIC, preservando COM1/COM2 PIC.

## Objetivo
Evitar que el ACARS permita o ejecute cierre de vuelo apenas aterriza. El cierre debe ser siempre manual y solo en gate/destino con la aeronave detenida, freno de estacionamiento activado, motores apagados y Cold & Dark nuevamente.

## Archivo modificado
- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`

## Cambios
- `FinishFlightCommand` ya no se habilita por `Phase == Arrived` ni por `OnGround && GS < 3` solamente.
- Se agrega checklist de cierre manual:
  - vuelo activo;
  - aeronave en tierra;
  - GS menor o igual a 3 kt;
  - parking brake ON;
  - motores apagados, N1 máximo <= 5%;
  - Cold & Dark: luces OFF y energía/aviónica OFF;
  - para C208 Black Square se usa `ElectricalMainBusVoltage < 3.0` como evidencia de energía apagada;
  - cercanía a destino/gate cuando hay coordenadas confiables.
- Si la fase llega a `Arrived`, solo actualiza estado y sonido; no genera PIREP ni navega automáticamente a cierre.
- `FinishFlight()` muestra bloqueo con motivo si falta una condición y pide confirmación manual antes de generar el PIREP.

## No tocado
- Web/Supabase.
- `/api/acars/finalize`.
- HUD MSFS2020.
- SayIntentions.
- Wallet/salary/ledger/economía.
- Autoupdate/installer.

## Validación esperada
Compilar ACARS con MSBuild Debug|x64 o Release|x64. El botón de finalizar debe permanecer deshabilitado después de touchdown y taxi si no hay Cold & Dark. Solo debe habilitarse al quedar detenido en gate con freno, motores apagados y avión sin energía.
