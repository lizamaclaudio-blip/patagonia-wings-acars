# Patagonia Wings ACARS - Versión 2.0.11

## Fecha de Release
12 de Abril, 2025

## Novedades Principales

### 🛩️ Soporte Airbus A319 Headwind
- **Detección automática** del avión por título (`Private ACJ319 CFM`)
- **Integración MobiFlight WASM Module** para lectura de LVARs
- **Datos funcionando:**
  - ✅ Fuel (total + tanques individuales)
  - ✅ Posición (Lat/Lon/Alt)
  - ✅ Velocidad (IAS/GS/VS)
  - ✅ Heading, Flaps, Gear
  - ✅ Luces (Beacon, Strobe, Landing, Nav, Taxi) - vía LVARs
  - ✅ N1 Motores - vía LVARs
  - ✅ Transponder - vía LVARs

### 🔧 Sistema de Perfiles de Avión
- Archivo `AircraftProfiles.json` configurable
- Perfiles para: A319 Headwind, Fenix A320, PMDG 737/777, BlackSquare TBM8
- Detección automática del tipo de avión
- Indicador de compatibilidad en UI

### 🔄 Fallback Inteligente
- **FSUIPC7 → SimConnect** automático cuando FSUIPC no lee datos válidos
- Validación de rangos para evitar datos basura
- Contador de frames inválidos antes de cambiar backend

### 📡 Mejoras en Telemetría
- Lectura de título del avión vía SimConnect (`TITLE` variable)
- Múltiples offsets de fuel para compatibilidad con diferentes aviones
- Decodificación BCO16 para transponder
- Validación de cabin altitude y pressure differential

### 🎨 Mejoras en UI
- Login: campo cambiado a "EMAIL" (antes "Email o Callsign")
- Login: texto ya no se corta en pantallas pequeñas
- Login: funcionalidad "Recordar email" implementada
- InFlight: muestra título del avión detectado
- InFlight: indicador si el avión requiere LVARs

### 🐛 Correcciones
- Squawk code ahora se decodifica correctamente desde BCO16
- Luces ya no parpadean (validación de valores negativos)
- Cabin altitude validada para evitar valores imposibles
- DataContext binding corregido en `InFlightPage`

## Archivos Modificados

### Core Changes
- `AssemblyInfo.cs` - Versión 2.0.11
- `LoginWindow.xaml/.cs` - Activada verificación de actualizaciones
- `LoginViewModel.cs` - Email only + remember me
- `InFlightViewModel.cs` - Aircraft detection + LVAR support
- `InFlightPage.xaml/.cs` - UI bindings

### SimConnect
- `SimConnectService.cs` - MobiFlight integration + nested class
- `SimConnectStructs.cs` - Added Title field
- `AircraftProfiles.json` - Aircraft configurations

### Services
- `SimulatorCoordinator.cs` - Fallback logic + validation
- `FsuipcService.cs` - Multiple fuel offsets
- `FlightService.cs` - Telemetry logging
- `UpdateService.cs` - Version check (ya existente, activado)

### New Files
- `MobiFlightIntegration.cs` (nested en SimConnectService)
- `WasmInstallerService.cs` - WASM module installer
- `LvarService.cs` - LVAR reading service
- `AircraftProfiles.json` - Aircraft database
- `MOBIFLIGHT_BUNDLE_PLAN.md` - Documentación
- `WASM_LVAR_README.md` - Documentación técnica

## Instalación

### Para Desarrolladores
```bash
git add .
git commit -m "Version 2.0.11 - Soporte A319 Headwind con MobiFlight"
git push origin main
```

### Para Usuarios
1. Descargar `PatagoniaWingsACARSSetup-2.0.11.exe`
2. Ejecutar instalador
3. El WASM Module se instala automáticamente en MSFS Community
4. Reiniciar MSFS si estaba abierto
5. Listo!

## Compatibilidad

### Aviones Testeados
- ✅ **A319 Headwind** - Full support con MobiFlight
- ✅ **C208 Grand Caravan** - Nativo MSFS (sin LVARs necesarios)
- ✅ **TBM9** - Nativo MSFS
- ⚠️ **Fenix A320** - Requiere LVARs (perfil configurado)
- ⚠️ **PMDG 737** - Requiere LVARs (perfil configurado)

### Simuladores
- ✅ Microsoft Flight Simulator 2020 (Steam)
- ✅ Microsoft Flight Simulator 2020 (MS Store)
- ✅ Microsoft Flight Simulator 2024

### Backend
- ✅ FSUIPC7 (primario)
- ✅ SimConnect (fallback automático)

## Configuración Web

Archivo `acars-update.json`:
```json
{
  "version": "2.0.11",
  "downloadUrl": "https://www.patagoniaw.com/downloads/PatagoniaWingsACARSSetup-2.0.11.exe",
  "mandatory": false,
  "notes": "Soporte A319 Headwind + MobiFlight WASM Module"
}
```

## Notas Técnicas

### MobiFlight WASM Module
- Se instala silenciosamente con el ACARS
- Ubicación: `MSFS Community/patagonia-acars-wasm/`
- Lee LVARs y las expone como variables SimConnect
- Licencia: GPL v3 (compatible con redistribución)

### Sistema de Detección
```
1. ACARS lee TITLE del avión
2. Detecta tipo: "A319 Headwind"
3. Verifica si MobiFlight está disponible
4. Si sí: Lee LVARs + SimConnect
5. Si no: Solo SimConnect (datos parciales)
```

## Próximos Pasos (Roadmap)

### Versión 2.0.12
- Soporte completo Fenix A320
- Soporte PMDG 737/777
- Más variables LVAR

### Versión 2.1.0
- Soporte X-Plane 12
- Integración con otras aerolíneas
- App móvil companion

## Créditos
- Desarrollo: Patagonia Wings Tech Team
- MobiFlight: GPL v3 - https://www.mobiflight.com/
- Beta testers: Comunidad Patagonia Wings

## Soporte
- Discord: https://discord.patagoniawings.com
- Email: soporte@patagoniawings.com
- Wiki: https://wiki.patagoniawings.com/acars
