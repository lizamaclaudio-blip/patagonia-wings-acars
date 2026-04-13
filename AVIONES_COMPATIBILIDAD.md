# Compatibilidad de Aviones - ACARS Patagonia Wings

## Resumen de Métodos de Conexión

El ACARS intenta conectar en este orden:
1. **FSUIPC7** (offsets estándar)
2. **SimConnect** (variables nativas MSFS)
3. Si FSUIPC envía datos vacíos → fallback automático a SimConnect

## Compatibilidad por Avión

### ✅ AVIONES NATIVOS MSFS (FSUIPC7 o SimConnect)
- C208, B350, BE58, TBM9, ATR72
- E175, E190, E195 (FlightSim Studio)
- B78X, B789 (Horizons)

**Método recomendado:** FSUIPC7 (funciona perfecto)
**Offsets usados:** Estándar 0x0B74 (fuel kg), 0x0560-0x0588 (posición)

### ⚠️ AVIONES BLACKSQUARE (Alta fidelidad)
- C208 BS, B350 BS, BE58 BS, BE58 Pro, TBM8 BS

**Método recomendado:** SimConnect
**Nota:** Algunos offsets FSUIPC pueden no funcionar, SimConnect es más confiable

### 🔧 AVIONES COMPLEJOS - WASM/LVAR (SimConnect Requerido)

| Avión | Método | Variables Especiales | Notas |
|-------|--------|---------------------|-------|
| **A319 Headwind** | SimConnect | `FUEL LEFT/RIGHT/CENTER QUANTITY` | No usa offsets estándar |
| **A320/A321 Fenix** | SimConnect | `TURB ENG N1`, tanques separados | Sistema Fenix WASM |
| **A20N FBW** | SimConnect | Variables FBW propietarias | Dev version más compatible |
| **B736-9 PMDG** | SimConnect | `TURB ENG N1` | PMDG usa WASM propio |
| **B77W PMDG** | SimConnect | Variables PMDG específicas | Requiere SimConnect |
| **MD82-88 MadDog** | SimConnect | Motores turbina | Sistema complejo |
| **A339 Headwind** | SimConnect | Ver A319 Headwind | Mismo sistema WASM |
| **A319/320/321 LatinVFR** | SimConnect | Variables nativas | Sistemas WASM |
| **A21N LatinVFR** | SimConnect | Ver arriba | Mismo sistema |
| **B38M IFly** | SimConnect | Variables nativas | 737 MAX |
| **A359 Inbuilds** | SimConnect | Variables nativas | A350 |

## Problemas Conocidos

### A319 Headwind
**Síntoma:** FSUIPC lee todo 0 (LAT=-3.28e+..., ALT=0, FUEL=0)
**Causa:** El avión usa WASM y no expone offsets FSUIPC estándar
**Solución:** El ACARS ahora detecta datos vacíos y cambia automáticamente a SimConnect

**Variables SimConnect que funcionan:**
- Posición: `PLANE LATITUDE`, `PLANE LONGITUDE`, `PLANE ALTITUDE` ✅
- Velocidad: `AIRSPEED INDICATED`, `GROUND VELOCITY` ✅
- Fuel: `FUEL LEFT QUANTITY` + `FUEL RIGHT QUANTITY` + `FUEL CENTER QUANTITY` ✅
- Motores: `TURB ENG N1:1`, `TURB ENG N1:2` ✅
- Sistemas: Luces, Gear, Flaps ✅

### Fenix A320
**Síntoma:** Similar al Headwind, offsets FSUIPC no funcionan
**Solución:** SimConnect con variables `TURB ENG N1` en lugar de `ENG N1 RPM`

### PMDG 737/777
**Síntoma:** Datos parciales o 0 en FSUIPC
**Solución:** SimConnect, algunas variables requieren eventos específicos PMDG

## Configuración Manual (si es necesario)

Si quieres forzar un método específico:

### Forzar SimConnect (saltar FSUIPC):
Editar `SimulatorCoordinator.cs`:
```csharp
// Comentar el intento FSUIPC
// _fsuipc.Connect();
// Directamente usar SimConnect
TrySimConnectFallback();
```

### Verificar qué método está activo:
En el Output de VS verás:
- `[Coordinator] Backend activo: FSUIPC7` → Usando FSUIPC
- `[Coordinator] Backend activo: SimConnect` → Usando SimConnect nativo
- `[Coordinator] SimConnect fallback activado` → Cambió automáticamente

## WASM y LVARs

**¿Qué es WASM?**
WebAssembly modules que usan aviones complejos para sus sistemas. FSUIPC7 NO puede leer variables WASM directamente sin un bridge específico.

**¿Qué son LVARs?**
Local Variables usadas por aviones WASM. Requieren herramientas especiales para leerse:
- FSUIPC7 + WASM Module (complejo)
- SimConnect (nativo, más simple)
- Mobiflight/Event Logger (debug)

**Solución actual:** Usar SimConnect para aviones WASM, que lee las variables nativas del simulador que sí expone el WASM.

## Testing

Para probar compatibilidad:
1. Cargar avión en MSFS (en vuelo, no en menú)
2. Conectar ACARS
3. Ver Output VS:
   - Si FSUIPC lee valores raros → esperar 3-5 segundos
   - Debería ver: `[Coordinator] FSUIPC sin datos - intentando SimConnect...`
   - Luego: `[Coordinator] Backend activo: SimConnect`
   - Y datos válidos: `[SimConnect FUEL] Total=5000...`

## Notas para Desarrollo Futuro

Para soportar más aviones WASM, considerar:
1. Integración con Mobiflight WASM Module
2. Lectura de LVARs vía FSUIPC7 WASM interface
3. Variables específicas por avión (XML configs)

Por ahora, SimConnect cubre la mayoría de casos.
