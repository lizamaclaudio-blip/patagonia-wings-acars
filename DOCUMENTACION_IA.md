# 📋 DOCUMENTACIÓN PARA IA - PATAGONIA WINGS ACARS

## 🎯 Propósito de este documento

Este archivo es para que otras IAs entiendan la arquitectura del ACARS y puedan continuar el trabajo sin romper nada.

---

## 🏗️ ARQUITECTURA GENERAL

```
ACARS NUEVO/
├── PatagoniaWings.Acars.Master/          ← Aplicación WPF principal (UI)
│   ├── Views/                              ← Ventanas y páginas XAML
│   │   ├── LoginWindow.xaml               ← Pantalla de login (PORTADA)
│   │   ├── MainWindow.xaml                ← Ventana principal post-login
│   │   └── Pages/                         ← Páginas de navegación
│   ├── ViewModels/                         ← Lógica de presentación (MVVM)
│   ├── Helpers/                            ← Servicios y utilidades
│   │   ├── UpdateService.cs              ← Sistema de actualización AUTOMÁTICA
│   │   ├── AcarsContext.cs               ← Estado global de la app
│   │   └── ...
│   └── App.config                         ← CONFIGURACIÓN CRÍTICA (versiones)
│
├── PatagoniaWings.Acars.Core/             ← Lógica de negocio compartida
│   ├── Services/
│   │   └── ApiService.cs                  ← Comunicación con Supabase
│   └── Models/                            ← Entidades (PreparedDispatch, etc.)
│
├── PatagoniaWings.Acars.SimConnect/       ← Comunicación con simulador
│
├── Web/                                    ← Archivos públicos para ACARS
│   ├── acars-update.json                  ← Manifiesto legacy (fallback)
│   └── autoupdater.xml                    ← XML para AutoUpdater.NET
│
├── installer/                              ← Scripts de Inno Setup
├── release/                                ← Instaladores generados (.exe)
└── libs/                                   ← Librerías externas (DLLs)
```

---

## 🔢 SISTEMA DE VERSIONES (CRÍTICO)

### ¿Dónde se define la versión?

Hay **3 lugares** que deben actualizarse SIEMPRE juntos:

#### 1. AssemblyInfo.cs (Metadatos del assembly)
```csharp
// PatagoniaWings.Acars.Master/Properties/AssemblyInfo.cs
[assembly: AssemblyVersion("3.0.1.0")]
[assembly: AssemblyFileVersion("3.0.1.0")]
```

#### 2. App.config (Configuración en tiempo de ejecución)
```xml
<!-- PatagoniaWings.Acars.Master/App.config -->
<add key="AppVersion" value="3.0.1" />
```

#### 3. UpdateService.cs (Fallback hardcoded)
```csharp
// PatagoniaWings.Acars.Master/Helpers/UpdateService.cs
public static string CurrentVersion => ReadSetting("AppVersion", "3.0.1");
```

### ¿Dónde se MUESTRA la versión?

#### LoginWindow.xaml (La PORTADA)
```xml
<!-- Views/LoginWindow.xaml -->
<TextBlock Text="v2.0.9" />  ← ESTO DEBE SER DINÁMICO
```

**PROBLEMA ACTUAL:** Está hardcodeado. Debe leer de `UpdateService.CurrentVersion`.

**SOLUCIÓN:**
1. En `LoginWindow.xaml.cs` agregar binding a `UpdateService.CurrentVersion`
2. O usar `TextBlock` con `x:Name` y actualizar en código

---

## 🔄 SISTEMA DE ACTUALIZACIÓN AUTOMÁTICA

### Arquitectura: AutoUpdater.NET (tipo SurAir)

```
Usuario abre ACARS
        ↓
UpdateService.CheckAndStartUpdate() se ejecuta
        ↓
AutoUpdater.NET lee XML desde GitHub:
https://raw.githubusercontent.com/.../autoupdater.xml
        ↓
Compara versión del XML vs versión instalada
        ↓
Si XML > Instalada:
        ↓
Muestra diálogo nativo con changelog
        ↓
Usuario acepta → Descarga con progreso
        ↓
Ejecuta installer automáticamente
        ↓
Cierra app vieja → Instala nueva → Reinicia
```

### Archivos de actualización

#### 1. Web/autoupdater.xml (Fuente primaria)
```xml
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>3.0.1.0</version>
  <url>https://github.com/.../PatagoniaWingsACARSSetup-3.0.1.exe</url>
  <changelog>Fix: GetDispatchPackageStatus...</changelog>
  <mandatory>false</mandatory>
  <hash>sha256:F6BF56D2...</hash>
</item>
```

#### 2. Web/acars-update.json (Fallback legacy)
```json
{
  "version": "3.0.1",
  "downloadUrl": "...",
  "hash": "sha256:..."
}
```

#### 3. Supabase acars_releases (No usado por AutoUpdater, solo para web)

### Proceso para publicar nueva versión

```bash
# 1. Actualizar versión en los 3 archivos:
#    - AssemblyInfo.cs
#    - App.config
#    - UpdateService.cs (fallback)

# 2. Compilar
MSBuild PatagoniaWings.Acars.sln /p:Configuration=Release

# 3. Generar installer
ISCC installer/PatagoniaWingsACARSSetup.iss

# 4. Calcular hash SHA256
certutil -hashfile release/PatagoniaWingsACARSSetup-X.X.X.exe SHA256

# 5. Actualizar Web/autoupdater.xml con nueva versión y hash

# 6. Commit y push
git add -A
git commit -m "Bump version X.X.X"
git push

# 7. Subir a GitHub Releases
gh release create vX.X.X release/PatagoniaWingsACARSSetup-X.X.X.exe
```

---

## 🗄️ COMUNICACIÓN CON SUPABASE

### Endpoints importantes

#### RPC para obtener reserva activa
```
POST /rest/v1/rpc/pw_get_active_reservation_for_pilot
Body: { "p_callsign": "PW-XXX" }
```

#### Tablas principales
- `flight_reservations` - Reservas de vuelo
- `dispatch_packages` - Paquetes de despacho
- `flight_operations` - Operaciones de vuelo (PIREPs)
- `acars_releases` - Versiones del ACARS (para web, no usado por AutoUpdater)
- `aircraft_assignments` - Asignaciones de aeronaves

### Flujo de despacho

```
WEB (Dashboard)
      ↓
Usuario crea reserva → status: "reserved"
      ↓
Usuario despacha → status: "dispatch_ready"
      ↓
Crea/Actualiza dispatch_packages con datos
      ↓
ACARS detecta reserva con status "dispatch_ready"
      ↓
Carga despacho en Pre-Flight
```

---

## 🎨 SISTEMA DE DISEÑO

### Tema actual: White Design (Diseño blanco)

- Fondo blanco/clean
- Accent color: Patagonia Wings teal/celeste
- Referencia: SurAir ACARS

### Archivos de estilos
```
Resources/Styles/AppStyles.xaml  ← Estilos globales
```

---

## ⚠️ COSAS QUE ROMPIERON ANTES (Y CÓMO EVITARLAS)

### 1. Version display mostraba "0.0.0"
**Causa:** AssemblyVersion no leía correctamente
**Fix:** Agregar fallback a AppVersion en App.config

### 2. Botones no funcionaban
**Causa:** `IsDispatchReady` retornaba `false` por `DispatchPackageStatus` vacío
**Fix:** Agregar `GetDispatchPackageStatus()` con fallback a "prepared"

### 3. SimBrief requerido para despachar
**Causa:** `handleDispatchFlight` validaba `simbriefSummary` obligatorio
**Fix:** Hacer SimBrief opcional, datos default desde itinerario

### 4. Actualización manual requerida
**Causa:** Sistema de actualización descargaba installer y usuario debía ejecutar manualmente
**Fix:** Integrar AutoUpdater.NET para actualización automática tipo SurAir

---

## 📚 RECURSOS ÚTILES

### Repositorios
- GitHub ACARS: `lizamaclaudio-blip/patagonia-wings-acars`
- GitHub Web: `lizamaclaudio-blip/patagonia-wings-site`

### Supabase
- URL: `https://qoradagitvccyabfkgkw.supabase.co`
- Tablas clave: `flight_reservations`, `dispatch_packages`, `flight_operations`

### Documentación externa
- AutoUpdater.NET: https://github.com/ravibpatel/AutoUpdater.NET
- Supabase C#: https://supabase.com/docs/reference/csharp/initializing

---

## 📝 CHECKLIST PARA NUEVAS FEATURES

- [ ] ¿Actualizaste los 3 lugares de versión?
- [ ] ¿Probaste en modo Release (no Debug)?
- [ ] ¿El installer genera correctamente?
- [ ] ¿El hash SHA256 está actualizado en autoupdater.xml?
- [ ] ¿Hiciste push de Web/autoupdater.xml a GitHub?
- [ ] ¿Subiste el installer a GitHub Releases?
- [ ] ¿La versión en la portada (LoginWindow) es dinámica?

---

**Última actualización:** 2026-04-13  
**Versión actual:** 3.0.1  
**Sistema de actualización:** AutoUpdater.NET integrado
