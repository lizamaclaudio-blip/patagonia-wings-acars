# AGENTS - Patagonia Wings ACARS

## Regla permanente de release ACARS
Todo cambio que modifique ACARS debe validar tambien autoupdate antes de cerrar release:
- actualizar version central (App.config/AssemblyInfo/fuente visible UI);
- actualizar manifests/canal/autoupdater (acars-update.json, channel.json, autoupdater.xml);
- actualizar metadata de descarga web si aplica;
- generar o documentar artefacto oficial (installer/exe) de la version publicada;
- validar version compare desde la version anterior (ej: 6.0.1 -> 7.0.1);
- no marcar release completo si una instalacion anterior no detecta update.

## Build oficial ACARS
- Build oficial: MSBuild VS2022 x64.
- Validacion final WPF legacy: MSBuild (no dotnet build).
- Push solo al final cuando build + autoupdate + version visible sean coherentes.
- No subir binarios grandes fuera del flujo oficial de releases.
