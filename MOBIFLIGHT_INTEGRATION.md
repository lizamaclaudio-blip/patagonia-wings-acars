# Integración MobiFlight WASM Module

## ¿Qué es MobiFlight?

MobiFlight es una plataforma de hardware y software para MSFS que incluye un **WASM Module** que puede leer y escribir LVARs (Local Variables).

## Ventajas

- ✅ Ya existe y está probado
- ✅ Lee LVARs de cualquier avión WASM
- ✅ Expone variables vía SimConnect
- ✅ No requiere desarrollo de módulo WASM propio

## Instalación

1. Descargar MobiFlight desde: https://www.mobiflight.com/
2. Instalar el WASM Module en MSFS
3. Ejecutar MobiFlight Connector

## Configuración para A319 Headwind

En MobiFlight, configurar variables para leer:

```
Variable: A319_Light_Beacon
Tipo: LVAR

Variable: A319_Light_Strobe  
Tipo: LVAR

Variable: A319_Light_Landing
Tipo: LVAR

Variable: A319_Engine_N1_1
Tipo: LVAR

Variable: A319_Engine_N1_2
Tipo: LVAR

Variable: A319_Transponder_Code
Tipo: LVAR
```

## Integración con ACARS

MobiFlight expone las LVARs como variables SimConnect estándar con prefijo:

```csharp
// Variables que MobiFlight expone
MOBIFLIGHT_A319_Light_Beacon
MOBIFLIGHT_A319_Light_Strobe
MOBIFLIGHT_A319_Light_Landing
MOBIFLIGHT_A319_Engine_N1_1
MOBIFLIGHT_A319_Engine_N1_2
MOBIFLIGHT_A319_Transponder_Code
```

## Implementación

Ver `MobiFlightService.cs` para la implementación.
