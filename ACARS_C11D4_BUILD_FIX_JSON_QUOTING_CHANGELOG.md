# ACARS C11D4 Build Fix — JSON quoting compatibility

## Objetivo
Corregir errores de compilación CS1003 en `SimConnectService.cs` provocados por literales JSON mal escapados dentro de `DetectProfileName`.

## Archivo modificado
- `PatagoniaWings.Acars.SimConnect/SimConnectService.cs`

## Cambio
Se reemplazaron strings inválidos como:

```csharp
json.IndexOf(""exact_titles"", StringComparison.OrdinalIgnoreCase)
```

por strings C# válidos:

```csharp
json.IndexOf("\"exact_titles\"", StringComparison.OrdinalIgnoreCase)
```

También se corrigieron las búsquedas de `name` y `matches`.

## Alcance
- No cambia score.
- No cambia cierre.
- No toca Web/Supabase.
- No cambia versión.
- Solo corrige build de C11D4.

## Validación esperada
Compilar Release x64 sin los errores CS1003 de líneas 2877, 2884, 2900, 2904, 2911 y 2928.
