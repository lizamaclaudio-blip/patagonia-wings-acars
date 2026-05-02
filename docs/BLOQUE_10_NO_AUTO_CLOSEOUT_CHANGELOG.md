# BLOQUE 10 — No auto-closeout / cierre manual obligatorio

Base: Bloque 9 C208 Cold & Dark + PIC solo COM2.

## Objetivo
Evitar definitivamente que ACARS abra la pantalla de cierre o genere PIREP al aterrizar.

## Cambios

- `PatagoniaWings.Acars.Core/Models/FlightReport.cs`
  - Agrega `ManualCloseoutConfirmed`.
  - Sirve como candado local: solo el cierre manual en gate puede abrir PostFlight para vuelos completados.

- `PatagoniaWings.Acars.Core/Services/FlightService.cs`
  - Elimina transición automática `Taxi -> Arrived`.
  - El avión permanece en Taxi/operación post-landing hasta que el piloto cierre manualmente.

- `PatagoniaWings.Acars.Master/ViewModels/MainViewModel.cs`
  - `ShowPostFlightReport()` bloquea reportes completed sin `ManualCloseoutConfirmed`.
  - Protege contra cualquier llamada legacy o accidental a PostFlight.

- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
  - `FinishFlight()` setea `ManualCloseoutConfirmed = true` solo después del checklist manual gate/cold dark.
  - Mantiene PIC solo COM2 del Bloque 9.
  - Mantiene cierre manual en gate del Bloque 8B.
  - Quita atajos por fase `Arrived` en la validación de destino/gate.

## No tocado

- Web/Supabase/finalize Bloque 7
- HUD MSFS2020
- SayIntentions
- wallet/salary/ledger/economía
- autoupdate/installer

## Validación esperada

1. Al aterrizar, ACARS NO debe abrir `Cerrar vuelo`.
2. Debe permanecer en `Vuelo en vivo`/post-landing.
3. El botón solo debe habilitarse con: tierra, GS <= 3 kt, parking brake, motores OFF, Cold & Dark y destino/gate.
4. Solo tras presionar `FINALIZAR EN GATE` debe abrir Paso 5/6.
