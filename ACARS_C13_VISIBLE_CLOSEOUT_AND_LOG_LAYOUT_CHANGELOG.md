# ACARS C13 — Visible closeout and compact operational log

Base: C11F/C12 phase/reglaje master closure.

## Archivos tocados

- `PatagoniaWings.Acars.Master/Views/Pages/InFlightPage.xaml`

## Motivo

Durante pruebas reales, el registro C9/C10/C11 puede crecer bastante y en ventanas pequeñas puede empujar el área inferior, dejando menos visible el botón de cierre.

## Cambios

- Reduce el alto máximo del scroll operacional de 108 a 82 px para liberar espacio vertical.
- Agrega botón superior contextual `FINALIZAR EN GATE` dentro de la tarjeta principal, visible solo cuando `CanManualCloseout=true`.
- Mantiene los botones inferiores existentes; no cambia comandos, scoring ni closeout backend.
- Reduce levemente el alto del texto Radio ACARS para evitar que tape combustible/botones.

## Validación esperada

- Build MSBuild Release x64 debe compilar sin errores.
- En gate llegada con condiciones válidas, el cierre queda visible arriba aunque la bitácora sea larga.
- ACARS sigue sin publicar ni versionar a 7.0.18 en este bloque.
