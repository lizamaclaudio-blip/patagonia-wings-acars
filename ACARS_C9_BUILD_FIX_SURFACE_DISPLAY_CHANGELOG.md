# ACARS C9 Build Fix — SurfaceContextDisplay

Fecha: 2026-05-03
Base: ACARS 7.0.16 C9 Gate Close + Surface Audit

## Corrección

- Agrega `BuildSurfaceContextDisplay(SimData data)` en `InFlightViewModel.cs`.
- Corrige el error de compilación `CS0103: BuildSurfaceContextDisplay no existe`.
- No cambia lógica de fases, gate, score, Web/Supabase, economía ni HUD.

## Validación esperada

- `MSBuild.exe .\PatagoniaWings.Acars.Master\PatagoniaWings.Acars.Master.csproj /p:Configuration=Release /p:Platform=x64` debe quedar con 0 errores.
