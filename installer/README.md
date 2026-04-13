# Instalador Patagonia Wings ACARS

## Descripción
Este instalador incluye:
- Aplicación ACARS principal
- MobiFlight WASM Module (para leer LVARs de aviones complejos)

## Requisitos
- Windows 10/11
- Microsoft Flight Simulator 2020 (Steam o MS Store)
- WiX Toolset 3.11 o superior (para compilar el instalador)

## Estructura del Instalador

```
PatagoniaWings.ACARS.msi
├── Aplicación ACARS
│   ├── PatagoniaWings.Acars.Master.exe
│   ├── AircraftProfiles.json
│   └── Dependencias
│
└── WASM Module (MSFS Community Folder)
    └── patagonia-acars-wasm/
        ├── manifest.json
        ├── module.config
        └── patagonia-acars-wasm.wasm
```

## Cómo Compilar el Instalador

### 1. Preparar Archivos

Primero, publicar la aplicación:
```bash
cd ..\PatagoniaWings.Acars.Master
dotnet publish -c Release -o ..\Installer\Publish
```

### 2. Copiar WASM Module

Nota: El archivo `patagonia-acars-wasm.wasm` debe obtenerse de:
- Compilar desde fuentes de MobiFlight (GPL v3)
- O usar el módulo de MobiFlight directamente

Copiar a:
```
Installer\WASM\patagonia-acars-wasm.wasm
```

### 3. Compilar con WiX

```bash
cd Installer

candle PatagoniaWings.ACARS.wxs -dPublishDir=Publish -dWasmDir=WASM

light PatagoniaWings.ACARS.wixobj -ext WixUIExtension -ext WixUtilExtension -o PatagoniaWings.ACARS.msi
```

### 4. Instalar

Ejecutar `PatagoniaWings.ACARS.msi`

El instalador detectará automáticamente:
- Ruta de instalación de MSFS (Steam o MS Store)
- Community Folder
- Instalará el WASM Module en la ubicación correcta

## Funcionamiento

### Detección de MSFS

El instalador busca MSFS en:
1. Steam (registry)
2. Microsoft Store (Packages folder)
3. Rutas comunes de fallback

### Instalación del WASM Module

1. Detecta Community Folder
2. Crea subdirectorio `patagonia-acars-wasm/`
3. Copia archivos manifest.json, module.config, .wasm
4. Configura variables LVAR para aviones soportados

### Uso

Después de instalar:
1. Iniciar MSFS (o reiniciar si ya estaba abierto)
2. Cargar avión (A319 Headwind, Fenix A320, etc.)
3. Iniciar ACARS
4. Conectar al simulador
5. ACARS leerá automáticamente las LVARs

## Licencia

### ACARS
Propio - Patagonia Wings Virtual Airline

### MobiFlight WASM Module
GPL v3 - https://www.mobiflight.com/
- Se redistribuye respetando la licencia GPL
- Source code disponible en repositorio de MobiFlight
- Créditos incluidos en el módulo

## Notas

- El WASM Module se instala silenciosamente
- No requiere acción del usuario
- Compatible con MobiFlight instalado por separado
- Si el usuario ya tiene MobiFlight, ambos pueden coexistir
