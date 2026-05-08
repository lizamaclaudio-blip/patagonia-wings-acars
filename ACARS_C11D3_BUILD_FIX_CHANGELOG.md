# ACARS C11D3 Build Fix - List.Contains compatibility

## Estado
Fix de compilación para el bloque C11D3 Alternate/Diversion Facility Scope.

## Archivo modificado
- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`

## Motivo
El proyecto ACARS/WPF compila sobre una base .NET/C# donde `List<string>.Contains(value, comparer)` no está disponible como sobrecarga válida.

## Cambio aplicado
- Reemplaza:
  - `target.Contains(icao, StringComparer.OrdinalIgnoreCase)`
- Por:
  - `target.Any(existing => string.Equals(existing, icao, StringComparison.OrdinalIgnoreCase))`

## Impacto funcional
- Mantiene deduplicación case-insensitive de ICAO.
- No cambia score.
- No cambia cierre.
- No toca Web/Supabase.
- No publica ni versiona ACARS.

## Validación esperada
- `PatagoniaWings.Acars.Master` deja de fallar con CS1501 en `InFlightViewModel.cs` línea ~1430.
- Los avisos de nulabilidad legacy pueden permanecer; no son bloqueo si no se tratan como errores.
