> **Nota (2026-10-02):** este documento es un plan historico. Varias cifras y estados que aparecen aqui no reflejan el codigo actual (por ejemplo, nunca se habia ejecutado el juego en Godot y los replays/netcode eran parciales). El estado verificado y las decisiones vigentes estan en `docs/adr/ADR-008-engine-v2-refactor.md`, `docs/API.md` y `README.md`.

# Fury Engine — Especificación Técnica del Framework

**Versión:** 1.0  
**Fecha:** Septiembre 2026  
**Estado:** Greenlight Aprobado

---

## 1. Visión General del Framework

Fury Engine es un framework de juego de lucha 2.5D construido sobre Godot 4.x con C# para el núcleo de simulación. El framework está diseñado para:

- **Determinismo bit-exacto**: Mismos inputs → mismos resultados en cualquier plataforma
- **Zero-GC en combate**: Sin asignaciones durante el tick de simulación
- **Rollback netcode**: Predicción y corrección de inputs remotos
- **Data-driven**: Frame data en tablas binarias, no en código
- **Modular**: Componentes desacoplados para fácil testing y balanceo

---

## 2. Arquitectura del Sistema

### 2.1 Diagrama de Alto Nivel

```
┌─────────────────────────────────────────────────────────────────┐
│                        PRESENTATION LAYER                        │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────┐ │
│  │ FightDirector│ │ FightRenderer│ │ AudioManager │ │   HUD   │ │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └────┬────┘ │
│         │                │                │              │      │
│         └────────────────┴────────────────┴──────────────┘      │
│                              │                                   │
│                              ▼                                   │
│                    ┌─────────────────┐                          │
│                    │  GameManager    │                          │
│                    │  (Autoload)     │                          │
│                    └────────┬────────┘                          │
└─────────────────────────────┼────────────────────────────────────┘
                              │
┌─────────────────────────────┼────────────────────────────────────┐
│                        SIMULATION LAYER                          │
│                              │                                   │
│  ┌───────────────────────────┼───────────────────────────────┐  │
│  │                    FightSimulation                         │  │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────┐ │  │
│  │  │InputBuffer│ │Command │ │  FSM    │ │Movement │ │Hit  │ │  │
│  │  │  [2]    │ │Resolver│ │         │ │ System  │ │Detect│ │  │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────┘ │  │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐        │  │
│  │  │Combat   │ │Energy   │ │Snapshot │ │Event    │        │  │
│  │  │Resolver │ │System   │ │         │ │Sink     │        │  │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘        │  │
│  └───────────────────────────────────────────────────────────┘  │
│                              │                                   │
│  ┌───────────────────────────┼───────────────────────────────┐  │
│  │                      FightState                            │  │
│  │  ┌─────────────────────────────────────────────────────┐  │  │
│  │  │  SoA Layout (ArenaSlice<int>)                        │  │  │
│  │  │  [X0,X1, Y0,Y1, Vx0,Vx1, Vy0,Vy1, HP0,HP1, ...]    │  │  │
│  │  └─────────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────┼────────────────────────────────────┐
│                        NETWORK LAYER                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │RollbackSession│ │RollbackRunner│ │UdpTransport │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
│  ┌─────────────┐  ┌─────────────┐                                │
│  │PacketAuth   │ │Handshake    │                                │
│  │+ReplayGuard │ │Manager     │                                │
│  └─────────────┘  └─────────────┘                                │
└─────────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────┼────────────────────────────────────┐
│                        DATA LAYER                                │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │BinaryMove   │ │Character    │ │Character    │              │
│  │Table (FMT1) │ │Config       │ │Data         │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. Estructura de Datos

### 3.1 FightState (SoA Layout)

```csharp
public sealed class FightState
{
    public const int FighterCount = 2;
    public const int FieldsPerFighter = 20;
    public const int PerFighterWords = FighterCount * FieldsPerFighter; // 40
    public const int GlobalWords = 12;
    public const int InputRingWords = FighterCount * (1 + InputBuffer.History); // 18
    public const int WordCount = PerFighterWords + GlobalWords + InputRingWords; // 70

    // Field block offsets (each block holds FighterCount words)
    private const int OffX = 0;
    private const int OffY = 2;
    private const int OffVx = 4;
    private const int OffVy = 6;
    private const int OffHealth = 8;
    private const int OffExMeter = 10;
    private const int OffResonance = 12;
    private const int OffRage = 14;
    private const int OffStateId = 16;
    private const int OffStateFrame = 18;
    private const int OffFacing = 20;
    private const int OffMoveIndex = 22;
    private const int OffHitstop = 24;
    private const int OffHitstun = 26;
    private const int OffBlockstun = 28;
    private const int OffHasHit = 30;
    private const int OffComboCount = 32;
    private const int OffComboDamage = 34;
    private const int OffAirborne = 36;
    private const int OffCrouching = 38;

    // Global offsets
    private const int OffFrame = PerFighterWords; // 40
    private const int OffRoundTimer = PerFighterWords + 1;
    private const int OffRoundState = PerFighterWords + 2;
    private const int OffWinner = PerFighterWords + 3;
    private const int OffRng = PerFighterWords + 4; // 8 ints reserved

    // Input ring offset
    private const int OffInputRing = PerFighterWords + GlobalWords; // 52
}
```

### 3.2 InputState (Bit-packed)

```csharp
public readonly record struct InputState(ushort Bits)
{
    // Bits 0-3: Directions
    // Bits 4-6: Punch buttons (Light, Medium, Heavy)
    // Bit 7: Special
    // Bit 8: Block
    // Bit 9: Parry
    // Bit 10: Macro1
    // Bit 11: Macro2
}
```

### 3.3 MoveEntry (FMT1 Format)

```csharp
public readonly record struct MoveEntry(
    int NameHash,      // 4 bytes
    int StartupFrames, // 4 bytes
    int ActiveFrames,  // 4 bytes
    int RecoveryFrames,// 4 bytes
    int Damage,        // 4 bytes
    int HitstopFrames, // 4 bytes
    int PushbackRaw,   // 4 bytes (Q16.16)
    int ReachRaw,      // 4 bytes (Q16.16)
    int BoxHalfHeightRaw, // 4 bytes (Q16.16)
    int BoxCenterYRaw  // 4 bytes (Q16.16)
); // Total: 40 bytes per move
```

---

## 4. Sistema de Combate

### 4.1 Pipeline de 9 Fases

```
Fase 1: Input Read
    ↓
Fase 2: Input Buffer (push to ring)
    ↓
Fase 3: Command Resolution (input → intent)
    ↓
Fase 4: FSM Commit (timers, hitstop, intents)
    ↓
Fase 5: Movement (ground control + integration)
    ↓
Fase 6: Hit Detection (SIMD filter + exact confirm)
    ↓
Fase 7: Combat Resolution (hit/block/whiff + energy)
    ↓
Fase 8: State Snapshot (serialize to int span)
    ↓
Fase 9: Render Trigger (notify presentation)
```

### 4.2 Estados del Personaje

| Estado ID | Nombre | Puede Actuar | Descripción |
|-----------|--------|--------------|-------------|
| 0 | Idle | ✓ | Estado neutral |
| 1 | WalkForward | ✓ | Caminando hacia adelante |
| 2 | WalkBack | ✓ | Caminando hacia atrás |
| 3 | Crouch | ✓ | Agachado |
| 4 | Jump | ✗ | En el aire |
| 5 | Attack | ✗ | Ejecutando ataque |
| 6 | Hitstun | ✗ | Recibiendo daño |
| 7 | Blockstun | ✗ | Bloqueando |
| 8 | Knockdown | ✗ | Derribado |
| 9 | Ko | ✗ | Noqueado |

### 4.3 Sistema de Cancelaciones

| Tipo | Input | Ventana | Ejemplo |
|------|-------|---------|---------|
| Chain Cancel | LP → MP → HP | 3-5 frames entre golpes | Combo básico |
| Special Cancel | Normal → QCF + P | Durante hitstun del normal | LP → Hadoken |
| Super Cancel | Especial → QCFx2 + P | Durante animación del especial | Hadoken → Shinku |
| Jump Cancel | Launcher → Salto | En el peak del launcher | MK → Jump → Air Combo |
| Rage Cancel | Cualquier ataque → Rage | Durante hitstun propio | HP → Rage Burst |

---

## 5. Netcode Rollback

### 5.1 Estructura del Rollback

```csharp
public sealed class RollbackSession
{
    private readonly InputState[] _remoteConfirmed;  // Inputs confirmados
    private readonly InputState[] _remotePredicted;  // Inputs predichos
    private readonly InputState[] _localHistory;    // Historial local
    private readonly ulong[] _hashes;                // Hashes por frame
    private readonly bool[] _confirmed;             // Flags de confirmación
    
    private int _resimFrom = int.MaxValue;
    private int _resimTo = -1;
}
```

### 5.2 Flujo del Rollback

```
Frame f:
  1. PushLocal(f, inputLocal)
  2. PredictFor(f)  // Predecir input remoto
  3. Sim.Step(GetLocal(f), GetRemote(f))
  4. Save(f, snapshot)
  5. RecordHash(f, hash)

Si llega input remoto para frame f:
  1. DeliverRemote(f, inputRemoto, currentFrame)
  2. Si predicción fue incorrecta:
     a. Restore(snapshot[f-1])
     b. For f' in [f, currentFrame]:
        - Step(GetLocal(f'), GetRemote(f'))
        - Save(f', snapshot)
        - RecordHash(f', hash)
     c. ClearResim()
```

### 5.3 Snapshot Structure

```csharp
public struct StateSnapshot
{
    public int FrameNumber;
    public Vector2Int P1Position;
    public Vector2Int P1Velocity;
    public int P1State;
    public int P1StateFrame;
    public int P1HP;
    public int P1ExBar;
    public int P1Resonance;
    public bool P1Rage;
    public int P1RageTimer;
    public int P1RageCooldown;
    public int P1Hitstop;
    public int P1Hitstun;
    public int P1Blockstun;
    public bool P1HasHit;
    public int P1ComboCount;
    public int P1ComboDamage;
    public bool P1Airborne;
    public bool P1Crouching;
    public int P1Facing;
    public int P1MoveIndex;
    public int P1InputBuffer;  // 8 frames
    
    // P2 fields...
    
    public int RoundTimer;
    public ulong RngState;
}
```

---

## 6. Serialización y Replays

### 6.1 Formato de Replay

```
[Header]
- Magic: "FRY3" (4 bytes)
- FrameCount: int (4 bytes)
- P1InitialState: StateSnapshot
- P2InitialState: StateSnapshot

[Input Stream]
- Frame 0: P1Input (2 bytes) + P2Input (2 bytes)
- Frame 1: P1Input (2 bytes) + P2Input (2 bytes)
- ...
- Frame N: P1Input (2 bytes) + P2Input (2 bytes)

[Integrity]
- HashChain: ulong (8 bytes) - Hash de todos los frames
```

### 6.2 Verificación de Integridad

```csharp
public bool VerifyIntegrity()
{
    ulong lastHash = 0;
    for (int f = 0; f < FrameCount; f++)
    {
        var frameData = GetFrameData(f);
        ulong expectedHash = XxHash64.HashToUInt64(frameData, lastHash);
        if (expectedHash != _hashChain[f]) return false;
        lastHash = expectedHash;
    }
    return true;
}
```

---

## 7. UI/UX y Accesibilidad

### 7.1 HUD de Combate

| Elemento | Prioridad | Descripción |
|----------|----------|-------------|
| Barra de vida | Crítica | Indicador principal de estado |
| Estado activo | Alta | Rage/Resonancia activo |
| Barras de recursos | Alta | EX, Resonancia, Rage |
| Contador de combo | Media | Número de hits consecutivos |
| Temporizador | Media | Tiempo restante del round |

### 7.2 Accesibilidad

| Feature | Prioridad | Descripción |
|---------|----------|-------------|
| Remapeo completo | Crítica | Todos los inputs configurables |
| Modo Simple | Alta | Command normals con 1 botón |
| Daltonismo | Alta | 3 perfiles vía shaders |
| Subtítulos | Alta | Todas las cinemáticas |
| Opciones sensoriales | Crítica | Desactivar screen shake, flashes |

---

## 8. Plan de Producción

### 8.1 Cronograma (15-18 Meses)

| Fase | Meses | Milestones |
|------|-------|------------|
| Pre-Producción | 1-3 | GDD final, conceptos, pipeline |
| Prototipado Core | 4-6 | 2 personajes, Command Normals, determinismo |
| Producción | 7-12 | 4 personajes, 2 torres, modos base |
| Post-Producción | 13-15 | Beta, balanceo, certificación |
| Lanzamiento | 16+ | Launch + Parche Día 1 |

### 8.2 Equipo Recomendado

| Rol | FTE | Responsabilidades |
|-----|-----|-------------------|
| Game Director | 1.0 | Visión, sistemas, balanceo |
| Programador Lead | 1.0 | Motor, AABB, FSM, CommandNormal, rollback |
| Programador Gameplay | 1.0 | IA, UI, sistemas de progresión |
| Artista Lead | 1.0 | Dirección artística, sprites, stages |
| Animador | 0.5-1.0 | Animaciones (15-23 días/personaje) |
| Sound Designer | 0.5 | Música, SFX, implementación |
| QA / Playtester | 0.5 | Hitboxes, GUT, playtests formales |
| Community Manager | 0.25 | Redes sociales, feedback |

**Total:** 5.5-6.5 FTE  
**Presupuesto:** $80k-$150k USD (LATAM) / $300k-$500k USD (US/EU)

---

## 9. Gestión de Riesgos

| Riesgo | Probabilidad | Impacto | Mitigación |
|--------|--------------|---------|------------|
| Desync en rollback | Media | Crítico | Tests automatizados en CI |
| Scope creep | Muy Alta | Alto | GDD congelado en Mes 3 |
| Subestimación de animación | Alta | Alto | Pipeline optimizado con IA |
| Balanceo roto | Media | Alto | Playtests semanales |
| Rendimiento en Switch | Media | Alto | Streaming, texturas comprimidas |

---

## 10. Conclusión

**Dual Fury v6.0** está listo para producción. La arquitectura determinista, el sistema de Command Normals extendido y el rollback netcode desde día 1 mitigan los riesgos técnicos principales. El scope es ambicioso pero factible con un equipo disciplinado.

**Recomendación final:** Comenzar con el Vertical Slice (Glen + Kira, Command Normals, determinismo validado) antes de expandir el roster.

---

## Apéndices

### A. Glosario Técnico
- **Frame**: 1/60 de segundo
- **Startup**: Frames antes de que el ataque pueda conectar
- **Active**: Frames durante los cuales el ataque puede hacer daño
- **Recovery**: Frames de vulnerabilidad después del active
- **On Block**: Ventaja/desventaja del atacante si el rival bloquea
- **Command Normal**: Normal con modificador direccional (F+HP, B+MP, etc)
- **Determinismo**: El combate produce el mismo resultado dado el mismo estado inicial y los mismos inputs
- **Rollback Netcode**: Sistema que predice el futuro y rebobina silenciosamente si la predicción fue incorrecta
- **AABB**: Axis-Aligned Bounding Box. Caja de colisión rectangular personalizada

### B. Template de Ficha de Personaje

```
NOMBRE: [Nombre]
ENERGÍA: [Dant/Danimal/Maestro]
ROL: [Balanceado/Rushdown/Zoner/etc.]
GIMMICK: [Mecánica única]

MOVIMIENTOS:
- LP: [Descripción, startup, active, recovery, on block, daño]
- MP: [...]
- HP: [...]
- LK: [...]
- MK: [...]
- HK: [...]
- Special 1: [...]
- Special 2: [...]
- Special 3: [...]
- Super: [...]

COMBOS BÁSICOS:
- BnB: [Input sequence]
- Anti-air: [Input sequence]
- Corner combo: [Input sequence]

ESTRATEGIA GENERAL:
- Fortalezas: [...]
- Debilidades: [...]
- Matchups difíciles: [...]
- Rol en la narrativa: [...]
```
