# ACARS C11D3 Build Fix 2 - List dedupe without LINQ

## Base
- Continúa sobre C11D3 Alternate/Diversion Facility Scope.
- No cambia score, cierre, Web, Supabase, installer ni versión.

## Archivo modificado
- PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs

## Motivo
- El fix anterior reemplazó `Contains(..., StringComparer...)` por `Any(...)`, pero el proyecto no tenía `System.Linq` disponible en ese archivo/build target.
- Para evitar depender de LINQ, se reemplaza por un `foreach` explícito con `string.Equals(..., StringComparison.OrdinalIgnoreCase)`.

## Validación esperada
- Debe eliminar el error CS1061: `List<string>` no contiene definición para `Any`.
- Los avisos CS8602/CS8625/CS0162 pueden permanecer como warnings legacy no bloqueantes.

## Publicación
- No publicar ni versionar a 7.0.18 todavía.
