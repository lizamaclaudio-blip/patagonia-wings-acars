# ACARS C11D4B Build Fix - Newline escaping

## Archivo modificado
- PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs

## Motivo
El parche C11D4B dejó literales de retorno de carro/salto de línea como caracteres reales dentro de un string C#, provocando CS1010 `Nueva línea en constante` en el build WPF temporal.

## Cambio
Se corrige `TrimFacilityText` para usar secuencias escapadas compatibles C#:

```csharp
var clean = text.Replace("\r", " ").Replace("\n", " ").Trim();
```

## Alcance
- Solo corrige compilación.
- No cambia score.
- No cambia cierre.
- No toca Web/Supabase.
- No versiona ni publica.
