# Plan: Integrar MobiFlight WASM con ACARS

## Objetivo
Que el ACARS instale automáticamente el WASM Module necesario para leer LVARs de aviones como el A319 Headwind, sin que el usuario tenga que instalar MobiFlight por separado.

## Opciones

### Opción 1: Bundled WASM Module (Recomendada)
Incluir el WASM Module de MobiFlight directamente en la instalación del ACARS.

**Ventajas:**
- Usuario instala ACARS y ya tiene todo
- Funciona "out of the box"
- No requiere conocimiento técnico del usuario

**Implementación:**
1. Incluir `mobiflight-event-module` en el installer del ACARS
2. Copiar a `Community\mobiflight-event-module` en MSFS
3. Configurar variables automáticamente para aviones soportados

### Opción 2: Custom WASM Module
Crear nuestro propio WASM Module específico para el ACARS.

**Ventajas:**
- Control total sobre el código
- Optimizado solo para lo que necesitamos
- Sin dependencias externas

**Desventajas:**
- 2-3 semanas de desarrollo en C++/WASM
- Mantenimiento continuo
- Complejidad alta

## Decisión: Opción 1 - Bundled MobiFlight

## Estructura de Archivos

```
ACARS Installer/
├── PatagoniaWings.ACARS.msi
└── bundled/
    └── mobiflight-wasm/
        ├── manifest.json
        ├── module.config
        └── mobiflight-event-module.wasm
```

## Instalación Automática

El installer del ACARS deberá:
1. Detectar ruta de MSFS Community Folder
2. Copiar `mobiflight-event-module` a Community
3. Configurar variables LVAR para aviones soportados
4. Registrar en el sistema que WASM está instalado

## Variables a Configurar

### A319 Headwind
```json
{
  "A319_Light_Beacon": { "type": "LVAR", "address": "0x1234" },
  "A319_Light_Strobe": { "type": "LVAR", "address": "0x1235" },
  "A319_Light_Landing": { "type": "LVAR", "address": "0x1236" },
  "A319_Light_Nav": { "type": "LVAR", "address": "0x1237" },
  "A319_Light_Taxi": { "type": "LVAR", "address": "0x1238" },
  "A319_Engine_N1_1": { "type": "LVAR", "address": "0x1239" },
  "A319_Engine_N1_2": { "type": "LVAR", "address": "0x123A" },
  "A319_Transponder_Code": { "type": "LVAR", "address": "0x123B" }
}
```

## Cambios Necesarios en el Código

### 1. Installer Project (.msi)
Agregar custom action para instalar WASM:

```csharp
[CustomAction]
public static ActionResult InstallWasmModule(Session session)
{
    string msfsPath = DetectMsfsCommunityFolder();
    string wasmSource = Path.Combine(session["INSTALLFOLDER"], "bundled\mobiflight-wasm");
    string wasmTarget = Path.Combine(msfsPath, "patagonia-acars-wasm");
    
    CopyDirectory(wasmSource, wasmTarget);
    ConfigureWasmVariables(wasmTarget);
    
    return ActionResult.Success;
}
```

### 2. Detección en ACARS
Verificar que el WASM Module está instalado:

```csharp
public bool IsWasmModuleInstalled()
{
    string communityPath = GetMsfsCommunityPath();
    string wasmPath = Path.Combine(communityPath, "patagonia-acars-wasm");
    return Directory.Exists(wasmPath);
}
```

### 3. UI de Instalación
Preguntar al usuario si quiere instalar soporte WASM:

```
☑ Instalar soporte para aviones complejos (A319 Headwind, Fenix, PMDG)
   Requiere reiniciar MSFS
```

## Notas Legales

MobiFlight es open source (GPL v3). Podemos redistribuir el WASM Module manteniendo:
- Crédito a MobiFlight
- Licencia GPL v3
- Source code disponible si es requerido

## Timeline

1. **Semana 1:** Configurar installer con WASM bundled
2. **Semana 2:** Testing con A319 Headwind
3. **Semana 3:** Documentación y release

## Archivos Creados
- `MobiFlightBundleInstaller.cs` - Custom action para WiX/MSI
- `WasmConfiguration.json` - Variables LVAR por avión
- `License-MobiFlight-GPLv3.txt` - Licencia requerida
