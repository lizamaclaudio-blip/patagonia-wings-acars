# Instalador Patagonia Wings ACARS

## Descripcion

Este instalador incluye:
- Aplicacion ACARS principal
- WASM de soporte para integracion con MSFS y lectura de variables de aviones complejos

## Build del release

- `build-release.ps1` toma la version desde `PatagoniaWings.Acars.Master/App.config`
- esa misma version se usa para:
  - ensamblados
  - instalador
  - manifest JSON
  - XML del updater

## Publish del autoupdate

- `deploy-to-web.ps1` publica dos carriles:
  - `public/downloads` para consumo web/legacy
  - objetos en Supabase Storage para el updater real

## Feed oficial del updater

El cliente desktop debe consultar siempre los objetos genericos:
- `autoupdater.xml`
- `acars-update.json`
- `PatagoniaWingsACARSSetup.exe`

Los archivos versionados quedan como historico del release y auditoria.

## Notas de release

- `release-notes.txt` define el changelog publicado en:
  - `acars-update.json`
  - `autoupdater.xml`

## Secretos de publicacion

- copiar `installer/.publish-secrets.example` a `installer/.publish-secrets.local`
- agregar:
  - `SUPABASE_SERVICE_ROLE_KEY=...`

Ese archivo queda fuera de Git.
