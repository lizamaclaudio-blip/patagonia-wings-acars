# AGENTS - Patagonia Wings ACARS

## Regla permanente de release ACARS
Todo cambio que modifique ACARS debe validar tambien autoupdate antes de cerrar release:
- actualizar version central (App.config/AssemblyInfo/fuente visible UI);
- actualizar manifests/canal/autoupdater (acars-update.json, channel.json, autoupdater.xml);
- publicar instalador oficial en Supabase Storage/Web y apuntar manifests publicos a esa URL;
- no depender de Control Center como unica fuente de actualizacion;
- actualizar metadata de descarga web si aplica;
- generar o documentar artefacto oficial (installer/exe) de la version publicada;
- validar version compare desde la version anterior (ej: 6.0.1 -> 7.0.1);
- no marcar release completo si una instalacion anterior no detecta update.
- validar en cliente instalado real o simulado: ruta exe, config instalada, feed publico, comparacion de version, descarga de instalador y version final visible.
- no basta actualizar App.config del repo: hay que verificar el comportamiento del cliente instalado.
- toda version ACARS desactualizada, desde cualquier release anterior, debe actualizar a la version estable actual (sin limitarse a 6.x).
- antes de marcar release completo, ejecutar matriz legacy (ej: 1.0.0/2.0.0/5.0.0/6.x/7.0.0 -> estable) y validar fallback manual para clientes incompatibles.

## Build oficial ACARS
- Build oficial: MSBuild VS2022 x64.
- Validacion final WPF legacy: MSBuild (no dotnet build).
- Push solo al final cuando build + autoupdate + version visible sean coherentes.
- No subir binarios grandes fuera del flujo oficial de releases.
