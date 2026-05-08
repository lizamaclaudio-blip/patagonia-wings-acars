# ACARS C11D7 - Surface phase/sound/fixed gear/closeout UI hotfix

Base: acumulado local C10/C11D6 sobre ACARS publicado 7.0.17. No versionar ni publicar todavia.

## Archivos tocados
- PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs
  - Evita que la etiqueta visual vuelva a `Prevuelo` cuando C11D5 reporta `GROUND_INTERMEDIATE`/`GROUND_STOPPED` durante vuelo activo.
  - Para aeronaves de tren fijo conocidas (C172/Skyhawk, C208/Caravan), fuerza LED TREN verde y sin transicion visual.
  - Mueve el audio `Encendido de motores` al primer BEACON ON en tierra, evitando que suene recien al despegue.
  - Elimina el audio de motores desde `FlightPhase.Takeoff`.
- PatagoniaWings.Acars.Core/Services/FlightService.cs
  - Da prioridad a `TAXI_IN` post-touchdown cuando hay evidencia taxiway/runway-exit o velocidad taxi post-aterrizaje.
- PatagoniaWings.Acars.Master/Views/Pages/InFlightPage.xaml
  - Reduce altura maxima de la bitacora operacional a 108 px para mantener visibles combustible y botones CANCELAR/CIERRE EN GATE.

## Validacion esperada
- Build Release x64 0 errores.
- En C172/C208: TREN verde fijo.
- Al soltar freno en salida: etiqueta no debe volver a Prevuelo; debe mostrar Superficie/Rodaje salida segun GS.
- Audio `Encendido de motores` debe sonar al encender BCN en tierra, no al entrar/despegar de pista.
- Post aterrizaje: con GS taxi y/o salida de pista, D5 debe tender a `TAXI_IN` antes de `GATE_ARRIVAL`.
- En gate llegada: boton Finalizar/Cierre en Gate debe quedar visible.

## Alcance
- No cambia score oficial.
- No toca Web/Supabase.
- No modifica version visible 7.0.17.
- No publica release.
