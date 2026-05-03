# ACARS C6 — Phase Prevalidation Package

Base: C5 Phase Review Contract aplicado y compilado OK por Claudio.

## Objetivo
Agregar una capa final de prevalidación antes de la prueba real de vuelo, sin tocar score oficial ni Web/Supabase.

## Archivos tocados
- `PatagoniaWings.Acars.Core/Models/SimData.cs`
- `PatagoniaWings.Acars.Core/Services/FlightService.cs`
- `PatagoniaWings.Acars.Core/Services/PirepXmlBuilder.cs`
- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
- `PatagoniaWings.Acars.Master/Views/Pages/InFlightPage.xaml`

## Cambios
- Agrega evidencia C6 por muestra:
  - `PhasePrevalidationStatus`
  - `PhasePrevalidationSummary`
  - `PhasePrevalidationFlags`
  - `PhasePrevalidationVersion`
- Calcula estado compacto de prevalidación por fase:
  - `READY`
  - `WARN`
  - `WAIT`
  - `BLOCK`
- Agrega al PIREP XML:
  - `<PhasePrevalidationPackage>`
  - resumen global de readiness
  - readiness por fase
  - secuencia observada
  - instrucciones de prueba operacional
- Agrega línea UI C6 para revisar estado de fase antes de validar Web/Supabase.

## No toca
- Web
- Supabase
- Score oficial
- Economía / wallet / salary / ledger
- HUD
- SayIntentions
- SimBrief / Route Finder

## Validación requerida
Compilar con:

```powershell
MSBuild.exe ".\PatagoniaWings.Acars.Master\PatagoniaWings.Acars.Master.csproj" /p:Configuration=Release /p:Platform=x64
```

