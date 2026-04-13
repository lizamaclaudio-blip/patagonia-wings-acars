# Leer WASM LVARs en MSFS

## ¿Qué son las LVARs?

Las **Local Variables (LVARs)** son variables internas usadas por aviones WASM (WebAssembly) como el A319 Headwind, Fenix A320, PMDG, etc. No son accesibles directamente desde SimConnect o FSUIPC estándar.

## Por qué el A319 Headwind no reporta datos correctos

| Sistema | Variable SimConnect | Valor reportado | Problema |
|---------|-------------------|-----------------|----------|
| Luces | `LIGHT BEACON` | 0 | Usa LVAR interna |
| Luces | `LIGHT STROBE` | 0 | Usa LVAR interna |
| Luces | `LIGHT LANDING` | 0 | Usa LVAR interna |
| Motores | `ENG N1 RPM` | 0 | Usa sistema WASM propio |
| Motores | `TURB ENG N1` | 0 | Usa sistema WASM propio |
| Transponder | `TRANSPONDER CODE` | 1059651583 | Formato diferente |
| Cabina | `PRESSURIZATION CABIN ALTITUDE` | Basura | No expone esta variable |

## Solución: WASM Module + SimConnect

Para leer LVARs necesitamos:
1. **WASM Module** que corra dentro del simulador
2. **Comunicación vía SimConnect** para leer las variables

### Opción 1: MobiFlight WASM Module (Recomendada)

MobiFlight ya tiene un módulo WASM que expone LVARs. Podemos usarlo:

```csharp
// En SimConnectService.cs

// Registrar eventos para leer LVARs
_simConnect.MapClientEventToSimEvent(EventId.ReadLvar, "LVAR_ACCESS");
_simConnect.AddClientEventToNotificationGroup(NotificationGroupId.Lvars, EventId.ReadLvar);
_simConnect.SetNotificationGroupPriority(NotificationGroupId.Lvars, SimConnect.SIMCONNECT_GROUP_PRIORITY_HIGHEST);

// Leer una LVAR específica
_simConnect.TransmitClientEvent(0, EventId.ReadLvar, (uint)lvarIndex, ...);
```

### Opción 2: Implementar nuestro propio WASM Module

Esto requiere crear un archivo `.wasm` en C++ que:
1. Se registre con el simulador
2. Lea las LVARs del A319
3. Las exponga vía SimConnect

**Archivo: PatagoniaWings.WASM.cpp**
```cpp
#include <MSFS/Legacy/gauges.h>
#include <MSFS/MSFS.h>

// Leer LVAR del A319 Headwind
double ReadHeadwindLvar(const char* lvarName) {
    return get_named_variable_value(get_named_variable(lvarName));
}

// Variables del A319 Headwind que queremos leer
extern "C" MSFS_CALLBACK void module_init() {
    // Registrar variables para ACARS
    SimConnect_RegisterDataDefine(...);
}
```

### LVARs del A319 Headwind conocidas:

```
A319_Light_Beacon - Estado del beacon
A319_Light_Strobe - Estado del strobe  
A319_Light_Landing - Estado de landing lights
A319_Light_Nav - Estado de nav lights
A319_Light_Taxi - Estado de taxi lights
A319_Engine_N1_1 - N1 motor 1
A319_Engine_N1_2 - N1 motor 2
A319_Transponder_Code - Código del transponder
A319_Cabin_Altitude - Altitud de cabina
```

## Implementación en ACARS

Para soportar LVARs, agregaríamos a `SimConnectService.cs`:

```csharp
public class LvarService : IDisposable
{
    private SimConnect _simConnect;
    
    // Diccionario de LVARs por avión
    private readonly Dictionary<string, string[]> _aircraftLvars = new()
    {
        ["A319 Headwind"] = new[] {
            "A319_Light_Beacon",
            "A319_Light_Strobe", 
            "A319_Light_Landing",
            "A319_Engine_N1_1",
            "A319_Engine_N1_2"
        }
    };
    
    public void RequestLvarData(string aircraftType)
    {
        if (!_aircraftLvars.ContainsKey(aircraftType))
            return;
            
        // Solicitar cada LVAR al WASM Module
        foreach (var lvar in _aircraftLvars[aircraftType])
        {
            _simConnect.TransmitClientEvent(...);
        }
    }
}
```

## Complejidad

Implementar soporte WASM/LVAR requiere:
1. **Tiempo:** 2-3 semanas de desarrollo
2. **Conocimiento:** C++ para WASM, SimConnect avanzado
3. **Mantenimiento:** Actualizar para cada nuevo avión

## Alternativa temporal

Por ahora, documentamos las limitaciones:

```csharp
// En SimConnectService.cs - MapToSimData
// Validar si los datos son basura y marcar como "No disponible"

var hasValidData = ValidateAircraftData(r);
if (!hasValidData)
{
    simData.DataQuality = DataQuality.Partial;
    simData.Notes = "Este avión usa WASM, algunos datos no disponibles vía SimConnect";
}
```

## Conclusión

Para aviones WASM como el A319 Headwind, tenemos opciones:
1. **Ahora:** Usar SimConnect básico (Fuel, Posición, Altitud funcionan bien)
2. **Futuro:** Implementar módulo WASM para LVARs (Luces, Motores detallados)
3. **Alternativa:** Usar aviones nativos MSFS que sí exponen todas las variables

**Recomendación:** Documentar claramente qué datos funcionan y cuáles no para cada avión.
