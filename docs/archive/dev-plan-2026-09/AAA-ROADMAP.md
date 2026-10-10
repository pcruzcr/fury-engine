> **Nota (2026-10-02):** este documento es un plan historico. Varias cifras y estados que aparecen aqui no reflejan el codigo actual (por ejemplo, nunca se habia ejecutado el juego en Godot y los replays/netcode eran parciales). El estado verificado y las decisiones vigentes estan en `docs/adr/ADR-008-engine-v2-refactor.md`, `docs/API.md` y `README.md`.

# Fury Engine — Roadmap AAA+ Framework

**Versión:** 1.0  
**Fecha:** Septiembre 2026  
**Estado:** Greenlight Aprobado  
**Filosofía:** Motor primero, contenido después. Arquitectura robusta antes que features.

---

## Resumen Ejecutivo

Este roadmap define la transformación de Fury Engine desde un motor funcional de juego de lucha hasta un framework AAA+ completo, comparable a motores como Unreal, Unity o RE Engine en capacidades específicas del género.

**Principios rectores:**
1. **Determinismo absoluto** — Cada frame debe ser reproducible bit a bit
2. **Zero-GC en combate** — Sin asignaciones durante el gameplay
3. **Data-driven** — Todo contenido se edita sin recompilar
4. **Modularidad** — Cada sistema es independiente y testeable
5. **Performance first** — 120+ FPS en hardware de gama baja

---

## Fase 7: Herramientas de Editor AAA

### 7.1 Frame Data Editor (Visual)
**Estado:** 🔄 En progreso  
**Prioridad:** Crítica  
**Esfuerzo:** 3-4 semanas

#### Features:
- [ ] Timeline visual con keyframes por frame
- [ ] Editor de hitboxes/hurtboxes drag & drop
- [ ] Preview de animación en tiempo real
- [ ] Sistema de tags por frame (cancel windows, invulnerability)
- [ ] Comparación lado a lado entre versiones
- [ ] Undo/Redo con historial completo
- [ ] Validación automática de balance

#### Estructura de datos extendida:
```csharp
public readonly record struct MoveEntry(
    int NameHash,
    int StartupFrames,
    int ActiveFrames,
    int RecoveryFrames,
    int Damage,
    int HitstopFrames,
    int PushbackRaw,
    int ReachRaw,
    int BoxHalfHeightRaw,
    int BoxCenterYRaw,
    // NUEVO: Frame data extendido
    int CancelWindowStart,
    int CancelWindowEnd,
    int InvulnerabilityStart,
    int InvulnerabilityEnd,
    int ArmorFrames,
    int ChipDamagePercent,
    int MeterGainOnHit,
    int MeterGainOnBlock,
    int Hitstun,
    int Blockstun,
    int KnockdownDuration,
    int JuggleLimit,
    int WeightClass,
    MoveProperties Properties,
    CancelRoute[] CancelRoutes
);

[Flags]
public enum MoveProperties : uint
{
    None = 0,
    IsOverhead = 1 << 0,
    IsLow = 1 << 1,
    IsMid = 1 << 2,
    IsUnblockable = 1 << 3,
    IsThrow = 1 << 4,
    IsCommandGrab = 1 << 5,
    IsProjectile = 1 << 6,
    IsReversal = 1 << 7,
    IsSuper = 1 << 8,
    IsEX = 1 << 9,
    CausesKnockdown = 1 << 10,
    WallBounce = 1 << 11,
    GroundBounce = 1 << 12,
    Crumple = 1 << 13,
    HardKnockdown = 1 << 14,
    InvincibleStartup = 1 << 15,
    Armored = 1 << 16,
    Parryable = 1 << 17,
    Feintable = 1 << 18,
    IsCrossup = 1 << 19,
    TracksAirborne = 1 << 20,
    IsAntiAir = 1 << 21,
    IsSweep = 1 << 22,
    IsLauncher = 1 << 23,
    IsSpike = 1 << 24,
    IsWallSplat = 1 << 25,
    IsGroundBounce = 1 << 26,
    IsHardKnockdown = 1 << 27,
    IsSoftKnockdown = 1 << 28,
    IsSweepKnockdown = 1 << 29,
    IsCrossupKnockdown = 1 << 30,
    IsUnblockableLow = 1 << 31
}
```

### 7.2 Character Editor
**Estado:** 🔲 Pendiente  
**Prioridad:** Alta  
**Esfuerzo:** 2-3 semanas

- [ ] Editor de stats base (vida, velocidad, peso)
- [ ] Asignación de movimientos por slot
- [ ] Configuración de walk/dash/jump physics
- [ ] Editor de hurtboxes por estado
- [ ] Sistema de paletas de color
- [ ] Preview de combos automáticos

### 7.3 Stage Editor
**Estado:** 🔲 Pendiente  
**Prioridad:** Media  
**Esfuerzo:** 3-4 semanas

- [ ] Editor de layout de escenario
- [ ] Sistema de paredes y esquinas
- [ ] Configuración de hazard zones
- [ ] Editor de música y SFX por zona
- [ ] Preview de interacciones con gameplay

### 7.4 Combo Trial System
**Estado:** 🔲 Pendiente  
**Prioridad:** Alta  
**Esfuerzo:** 2 semanas

- [ ] Sistema de grabación de inputs
- [ ] Playback frame-perfect
- [ ] Visualización de damage scaling
- [ ] Análisis de rutas de combo
- [ ] Sugerencias automáticas de optimización

---

## Fase 8: Optimización y Rendimiento AAA

### 8.1 Sistema de Object Pooling
**Estado:** 🔲 Pendiente  
**Prioridad:** Crítica  
**Esfuerzo:** 1-2 semanas

```csharp
public sealed class ObjectPool<T> where T : class, new()
{
    private readonly Stack<T> _pool;
    private readonly int _maxSize;
    
    public T Rent() => _pool.Count > 0 ? _pool.Pop() : new T();
    public void Return(T obj) { if (_pool.Count < _maxSize) _pool.Push(obj); }
}
```

### 8.2 Sistema de Spatial Hashing
**Estado:** 🔲 Pendiente  
**Prioridad:** Alta  
**Esfuerzo:** 1 semana

```csharp
public sealed class SpatialHash
{
    private readonly Dictionary<int, List<int>> _cells;
    private readonly int _cellSize;
    
    public void Insert(int id, FpVec2 position);
    public List<int> Query(FpVec2 min, FpVec2 max);
    public void Clear();
}
```

### 8.3 Sistema de LOD (Level of Detail)
**Estado:** 🔲 Pendiente  
**Prioridad:** Media  
**Esfuerzo:** 2 semanas

- [ ] LOD para partículas (reducir count en gama baja)
- [ ] LOD para sombras
- [ ] LOD para efectos de post-procesado
- [ ] Sistema de calidad automática

### 8.4 Sistema de Culling Avanzado
**Estado:** 🔲 Pendiente  
**Prioridad:** Alta  
**Esfuerzo:** 1-2 semanas

- [ ] Frustum culling
- [ ] Occlusion culling
- [ ] Distance culling
- [ ] Sistema de prioridad de renderizado

### 8.5 Sistema de Compresión de Datos
**Estado:** 🔲 Pendiente  
**Prioridad:** Media  
**Esfuerzo:** 1 semana

- [ ] Compresión de replays (LZ4)
- [ ] Compresión de snapshots de red
- [ ] Sistema de delta compression optimizado

---

## Fase 9: Networking AAA

### 9.1 Sistema de Matchmaking
**Estado:** 🔲 Pendiente  
**Prioridad:** Alta  
**Esfuerzo:** 3-4 semanas

- [ ] Sistema de colas por skill
- [ ] Matchmaking por región
- [ ] Sistema de prioridad de conexión
- [ ] Reemplazo de jugadores desconectados

### 9.2 Sistema de Replay Netcode
**Estado:** 🔲 Pendiente  
**Prioridad:** Alta  
**Esfuerzo:** 2-3 semanas

- [ ] Sincronización de replays online
- [ ] Sistema de spectate
- [ ] Delay configurable para broadcasts
- [ ] Sistema de anti-desync

### 9.3 Sistema de Anti-Cheat
**Estado:** 🔲 Pendiente  
**Prioridad:** Crítica  
**Esfuerzo:** 4-6 semanas

- [ ] Validación de inputs en servidor
- [ ] Detección de speed hacks
- [ ] Detección de teleport hacks
- [ ] Sistema de reportes automáticos
- [ ] Replay analysis para detección de patrones

### 9.4 Sistema de Relay/TURN
**Estado:** 🔲 Pendiente  
**Prioridad:** Media  
**Esfuerzo:** 3-4 semanas

- [ ] Integración con servicios TURN
- [ ] Fallback automático a relay
- [ ] Sistema de calidad de conexión
- [ ] Optimización de rutas de red

---

## Fase 10: Observabilidad AAA

### 10.1 Sistema de Telemetría en Vivo
**Estado:** 🔲 Pendiente  
**Prioridad:** Alta  
**Esfuerzo:** 2-3 semanas

```csharp
public sealed class TelemetrySystem
{
    // Métricas en tiempo real
    public void RecordFrameTime(double ms);
    public void RecordRollbackCount(int count);
    public void RecordNetworkLatency(int ms);
    public void RecordMemoryUsage(long bytes);
    
    // Eventos estructurados
    public void RecordCombatEvent(CombatEvent evt);
    public void RecordPlayerAction(PlayerAction action);
}
```

### 10.2 Sistema de Dashboards
**Estado:** 🔲 Pendiente  
**Prioridad:** Media  
**Esfuerzo:** 2 semanas

- [ ] Dashboard de rendimiento en tiempo real
- ] Dashboard de balance de personajes
- [ ] Dashboard de uso de mecánicas
- [ ] Alertas automáticas de anomalías

### 10.3 Sistema de A/B Testing
**Estado:** 🔲 Pendiente  
**Prioridad:** Baja  
**Esfuerzo:** 2-3 semanas

- [ ] Sistema de flags de features
- [ ] Asignación de variantes
- [ ] Métricas de conversión
- [ ] Análisis estadístico automático

---

## Fase 11: Contenido (AL FINAL)

### 11.1 Roster Completo (12 personajes)
**Estado:** 🔲 Pendiente  
**Prioridad:** Alta  
**Esfuerzo:** 6-12 meses

### 11.2 Stages (10+)
**Estado:** 🔲 Pendiente  
**Prioridad:** Media  
**Esfuerzo:** 4-6 meses

### 11.3 Modos de Juego (8)
**Estado:** 🔲 Pendiente  
**Prioridad:** Alta  
**Esfuerzo:** 6-9 meses

### 11.4 Sistema de Progresión
**Estado:** 🔲 Pendiente  
**Prioridad:** Media  
**Esfuerzo:** 3-4 meses

---

## Resumen de Prioridades

### Inmediato (Semanas 1-4)
1. **F7.1** — Frame Data Editor visual
2. **F8.1** — Object pooling para VFX
3. **F9.1** — Sistema de matchmaking básico
4. **F10.1** — Telemetría en vivo

### Corto Plazo (Meses 2-4)
1. **F7.2** — Character Editor
2. **F8.2** — Spatial hashing
3. **F9.2** — Replay netcode
4. **F10.2** — Dashboards

### Medio Plazo (Meses 5-8)
1. **F9.3** — Anti-cheat
2. **F10.3** — A/B testing
3. **F8.3** — LOD system

### Largo Plazo (Meses 9+)
1. **F11** — Contenido completo

---

## Conclusión

El motor base está **completo y robusto**. Para alcanzar AAA+ se necesita:

1. **Herramientas de editor** (2-3 meses)
2. **Optimización avanzada** (2-3 meses)
3. **Networking robusto** (3-4 meses)
4. **Observabilidad completa** (2-3 meses)
5. **Contenido extenso** (6-12 meses)

**Total estimado:** 15-25 meses para AAA+ completo.

El engine está listo para soportar un juego de lucha competitivo de alto nivel. La arquitectura determinista y modular es la base correcta para un producto AAA.
