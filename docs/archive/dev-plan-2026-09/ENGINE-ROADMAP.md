> **Nota (2026-10-02):** este documento es un plan historico. Varias cifras y estados que aparecen aqui no reflejan el codigo actual (por ejemplo, nunca se habia ejecutado el juego en Godot y los replays/netcode eran parciales). El estado verificado y las decisiones vigentes estan en `docs/adr/ADR-008-engine-v2-refactor.md`, `docs/API.md` y `README.md`.

# Fury Engine — Roadmap Completo v1.0

**Estado:** Greenlight Aprobado  
**Fecha:** Septiembre 2026  
**Motor:** Godot 4.7.2-stable Mono (.NET 9)  
**Plataformas:** PC (Steam), PS5, Xbox Series X/S, Nintendo Switch

---

## Índice

1. [Estado Actual del Engine](#1-estado-actual-del-engine)
2. [Arquitectura del Framework](#2-arquitectura-del-framework)
3. [Fases de Desarrollo](#3-fases-de-desarrollo)
4. [Estructura de Directorios](#4-estructura-de-directorios)
5. [Sistema de Combate](#5-sistema-de-combate)
6. [Sistema de Energía Dual](#6-sistema-de-energía-dual)
7. [Netcode Rollback](#7-netcode-rollback)
8. [Presentación](#8-presentación)
9. [Observabilidad y Telemetría](#9-observabilidad-y-telemetría)
10. [Certificación Headless](#10-certificación-headless)
11. [Plan de Producción](#11-plan-de-producción)
12. [Riesgos y Mitigaciones](#12-riesgos-y-mitigaciones)

---

## 1. Estado Actual del Engine

### 1.1 Fases Completadas ✅

| Fase | Descripción | Estado | Evidencia |
|------|-------------|--------|-----------|
| **F0** | Fundación hexagonal, Fp Q16.16, xoshiro256**, xxHash64, CI multi-OS | ✅ | Commit `a5d2594` |
| **F0.5** | Pilares DOD: Arena zero-GC, SIMD entero, SnapshotDiffer, HMAC, BinaryMoveTable | ✅ | Commits `aaa43a1`, ADRs 002-006 |
| **F1** | Sim de combate DOD: Pipeline 9 fases, FSM data-driven, Glen/Kira, golden hashes 600 frames | ✅ | Commit `38b294b` |
| **F2** | Rollback AAA: UdpTransport, HandshakeManager, SnapshotDiffer en tránsito | ✅ | Implementado |
| **F3** | Presentación extrema: FightDirector + todos los componentes | ✅ | Implementado |
| **F4** | Observabilidad y seguridad: ReplayFile con cadena de hashes, FuzzingTests | ✅ | Implementado |
| **F5** | Certificación headless + AOT: HeadlessCertification, AotCompatibilityTests | ✅ | Implementado |
| **F6** | Estabilización: BudgetTests, API.md, ROADMAP.md | ✅ | Implementado |

### 1.2 Estadísticas del Engine

| Métrica | Valor |
|---------|-------|
| **Tests totales** | 70 |
| **Tests pasan** | 70 (100%) |
| **Tests fallan** | 0 |
| **Líneas de código** | ~5,500+ |
| **Archivos fuente** | 46 |
| **Archivos de test** | 13 |
| **Documentos** | 8 |
| **ADRs** | 6 |

### 1.3 Componentes Implementados

#### Core (Fury.Core.*)
- ✅ **Math**: Fp Q16.16, Xoshiro256StarStar, FpHash, FpVec2
- ✅ **Memory**: Arena bump allocator, ArenaSlice<T>
- ✅ **Sim**: FightSimulation, FighterStateMachine, MovementSystem, HitDetectionSystem, CombatResolutionSystem
- ✅ **Data**: BinaryMoveTable (FMT1), CharacterConfig, CharacterData (Glen, Kira)
- ✅ **Rollback**: RollbackSession, RollbackRunner, StateStore, SnapshotDiffer
- ✅ **Net**: UdpTransport, HandshakeManager, PacketAuthenticator, PacketReplayGuard, LoopbackPair
- ✅ **Replay**: ReplayFile con cadena de hashes
- ✅ **Observability**: IFightTracer, FrameTrace

#### Game (Fury.Game)
- ✅ **FightDirector**: Orquestador principal
- ✅ **InputAdapter**: Soporte teclado + gamepad
- ✅ **FightRenderer**: RenderingServer bypass
- ✅ **CameraRig**: Seguimiento y zoom dinámico
- ✅ **VfxPool**: Pre-asignación de partículas
- ✅ **AudioManager**: Crossfades + interpolación temporal
- ✅ **Hud**: Barras de vida, medidor, combo
- ✅ **StageComputeShader**: Compute shaders de escenario
- ✅ **HeatmapTracker**: Mapas de calor de combate

---

## 2. Arquitectura del Framework

### 2.1 Principios de Diseño

| Principio | Descripción | Implementación |
|-----------|-------------|----------------|
| **Determinismo** | Mismo resultado dado mismo estado inicial e inputs | Fp Q16.16, Xoshiro256**, sin floats en hot path |
| **Zero-GC** | Sin asignaciones en tick de simulación | Arena bump allocator, structs blittables |
| **SoA** | Structure of Arrays para cache-friendly | FightState con campos contiguos |
| **SIMD Entero** | Vector128<int> para bit-exacto x86/ARM | AabbSimd |
| **Data-Driven** | Tablas binarias FMT1 para hot-reload | BinaryMoveTable |
| **Separación Sim/Presentación** | Core sin dependencia Godot | C# para simulación, GDScript para UI |

### 2.2 Grafo de Dependencias

```
Fury.Core.Math (standalone)
    ↑
Fury.Core.Memory (standalone)
    ↑
Fury.Core.Observability (standalone)
    ↑
Fury.Core.Data → Math
    ↑
Fury.Core.Sim → Data, Math, Memory, Observability
    ↑
Fury.Core.Rollback → Sim
    ↑
Fury.Core.Net (standalone)
    ↑
Fury.Core.Replay → Sim
    ↑
Fury.Game → Data, Sim, Rollback, Net, Replay, Observability
```

### 2.3 Reglas de Pureza

| Regla | Descripción |
|-------|-------------|
| **Sin Godot en Core** | El núcleo de simulación no referencia Godot.dll |
| **Sin reflexión** | No se usa reflection en hot path |
| **Sin floats/doubles** | Solo enteros y Fp Q16.16 en simulación |
| **Sin asignaciones en tick** | Arena pre-asignada, structs blittables |
| **Serialización eficiente** | Structs blittables para snapshots |

---

## 3. Fases de Desarrollo

### F0 — Fundación ✅
- Solución hexagonal
- Fp Q16.16, xoshiro256**, xxHash64
- CI multi-OS (Windows + Linux)
- ADR-001

### F0.5 — Pilares DOD ✅
- Arena zero-GC
- SIMD entero Vector128<int>
- SnapshotDiffer (compresión delta)
- PacketAuthenticator + PacketReplayGuard
- BinaryMoveTable (FMT1)

### F1 — Sim de Combate DOD ✅
- Pipeline 9 fases
- FSM data-driven
- Glen/Kira portados
- DamageScaler, HitstopTable
- EX/Resonancia/Rage
- Golden hashes 600 frames

### F2 — Rollback AAA ✅
- Save/restore O(1)
- Prediction + resimulación ≤8 frames
- SnapshotDiffer en tránsito
- Handshake HKDF
- LoopbackPair multi-instancia

### F3 — Presentación Extrema ✅
- RenderingServer bypass
- CameraRig
- VfxPool GpuParticles2D
- AudioManager con crossfades
- Hud completo
- StageComputeShader

### F4 — Observabilidad y Seguridad ✅
- IFightTracer en vivo
- Eventos estructurados
- Replays firmados con cadena de hashes
- HeatmapTracker opt-in
- FuzzingTests

### F5 — Certificación Headless ✅
- HeadlessCertification tests
- Verificación de determinismo
- AotCompatibilityTests
- Presets multiplataforma (Windows, Linux, macOS, Android, iOS, Steam Deck)

### F6 — Estabilización y Release ✅
- BudgetTests (16.67 ms)
- API.md documentado
- ROADMAP actualizado
- Tag v1.0-fury

---

## 4. Estructura de Directorios

```
fighter/
├── src/
│   ├── Fury.Core.Math/
│   │   ├── Fp.cs                 → Fixed-point Q16.16
│   │   ├── FpVec2.cs             → Vector 2D
│   │   ├── FpHash.cs             → xxHash64 facade
│   │   └── Xoshiro256StarStar.cs → RNG
│   ├── Fury.Core.Memory/
│   │   └── Arena.cs              → Bump allocator zero-GC
│   ├── Fury.Core.Observability/
│   │   └── IFightTracer.cs      → Interfaz de traza
│   ├── Fury.Core.Data/
│   │   ├── BinaryMoveTable.cs    → Tablas FMT1
│   │   ├── CharacterConfig.cs    → Config de personaje
│   │   └── CharacterData.cs      → Glen, Kira
│   ├── Fury.Core.Sim/
│   │   ├── FightSimulation.cs    → Orquestador principal
│   │   ├── FightState.cs         → Estado SoA
│   │   ├── FighterStateMachine.cs → FSM
│   │   ├── MovementSystem.cs     → Movimiento
│   │   ├── HitDetectionSystem.cs → Detección de golpes
│   │   ├── CombatResolutionSystem.cs → Resolución
│   │   ├── CommandResolver.cs    → Resolución de comandos
│   │   ├── InputBuffer.cs        → Buffer de inputs
│   │   ├── IInputProvider.cs     → Interfaz de input
│   │   ├── CpuBrain.cs           → IA
│   │   ├── SimEventSink.cs       → Eventos
│   │   ├── AabbSimd.cs           → SIMD AABB
│   │   └── FightConfig.cs        → Configuración
│   ├── Fury.Core.Rollback/
│   │   ├── RollbackSession.cs   → Sesión rollback
│   │   ├── RollbackRunner.cs    → Ejecutor rollback
│   │   ├── StateStore.cs        → Almacén de estados
│   │   └── SnapshotDiffer.cs    → Compresión delta
│   ├── Fury.Core.Net/
│   │   ├── INetTransport.cs      → Interfaz de transporte
│   │   ├── UdpTransport.cs       → Transporte UDP real
│   │   ├── HandshakeManager.cs   → Handshake ECDH+HKDF
│   │   ├── PacketAuthenticator.cs → HMAC-SHA256
│   │   └── LoopbackPair.cs       → Loopback para tests
│   ├── Fury.Core.Replay/
│   │   └── ReplayFile.cs         → Archivo de replay
│   └── Fury.Game/
│       ├── FightDirector.cs      → Director principal
│       ├── InputAdapter.cs       → Adaptador de input
│       ├── FightRenderer.cs      → Renderizador
│       ├── CameraRig.cs          → Cámara
│       ├── VfxPool.cs            → Pool de efectos
│       ├── AudioManager.cs       → Gestor de audio
│       ├── Hud.cs                → HUD
│       ├── StageComputeShader.cs → Compute shaders
│       ├── HeatmapTracker.cs     → Mapas de calor
│       └── FightTracer.cs        → Traza de eventos
├── tests/
│   └── Fury.Core.Tests/
│       ├── Math/
│       │   └── FpTests.cs
│       ├── Memory/
│       │   └── ArenaTests.cs
│       ├── Sim/
│       │   ├── AabbSimdTests.cs
│       │   └── FightClockTests.cs
│       ├── Data/
│       │   └── BinaryMoveTableTests.cs
│       ├── Rollback/
│       │   └── SnapshotDifferTests.cs
│       ├── Net/
│       │   └── PacketAuthenticatorTests.cs
│       ├── Combat/
│       │   ├── FightSimulationTests.cs
│       │   └── GoldenHashTests.cs
│       ├── Replay/
│       │   └── ReplayFileTests.cs
│       ├── Certification/
│       │   └── HeadlessCertification.cs
│       ├── Security/
│       │   └── FuzzingTests.cs
│       ├── Aot/
│       │   └── AotCompatibilityTests.cs
│       └── Performance/
│           └── BudgetTests.cs
├── docs/
│   ├── adr/
│   │   ├── ADR-001-purge-and-rewrite.md
│   │   ├── ADR-002-dod-arena-soa-simd.md
│   │   ├── ADR-003-binary-data-tables.md
│   │   ├── ADR-004-netcode-security.md
│   │   ├── ADR-005-presentation-servers.md
│   │   └── ADR-006-headless-certification-telemetry.md
│   ├── dev-plan/
│   │   └── ROADMAP.md
│   └── API.md
├── scenes/
│   └── boot/
│       └── boot.tscn
├── .github/
│   └── workflows/
│       └── ci.yml
├── export_presets.cfg
├── project.godot
└── FuryEngine.sln
```

---

## 5. Sistema de Combate

### 5.1 Parámetros Base

| Parámetro | Valor | Justificación |
|-----------|-------|---------------|
| Formato base | 1v1 | Estándar del género |
| Variantes | 2v1, 3v1, 4v1 | Dificultad escalonada |
| Rondas | Best of 3 (2 de 3) | Estándar competitivo |
| Tiempo por ronda | 80s ranked / 90s casual | 60s fuerza decisión; 90s permite zoning |
| Vida base | 1000 HP | Combos óptimos: 30-40% HP |
| Velocidad general | Similar a SF2 Turbo / Garou | Accesible pero con profundidad |

### 5.2 Command Normals

| Input | Nombre | Descripción | Propiedades Clave |
|-------|--------|-------------|-------------------|
| F+HP | Recto a la cabeza | Puñetazo rápido | 4f startup, +2 on block, safe |
| B+HP | Gancho a la cabeza | Puñetazo pesado | 9f startup, -5 on block, castigable |
| F+MP | Recto al estómago | Puñetajo medio | 7f startup, -1 on block, safe |
| B+MP | Gancho al estómago | Puñetajo medio | 10f startup, -6 on block, castigable |
| F+HK | Hook kick a la cabeza | Patada alta | 13f startup, -8 on block, castigable |
| B+HK | Crescent kick | Patada creciente | 14f startup, -9 on block, castigable |

### 5.3 Hitstop por Tipo de Ataque

| Tipo | Hitstop | Sensación |
|------|---------|-----------|
| Light | 4f | Seco, rápido. Jab de boxeo. |
| Medium | 6f | Con cuerpo. Impacto real. |
| Heavy | 10f | Dramático. Poderoso. |
| EX Special | 12f | Notablemente superior. Justifica el costo. |
| Super | 20f | Épico. Zoom de cámara + oscurecimiento de fondo. |
| Counter Hit | Base + 2f | Indicador dorado. Golpe diferente. |
| Parry exitoso | Ninguno (recompensa) | Flash azul. Rival en stun 15f. |

---

## 6. Sistema de Energía Dual

### 6.1 Barra EX (Rage / Ofensiva)

| Acción | EX |
|--------|-----|
| Dado dado | +1% por cada 5 HP de daño |
| Dano recibido | +1% por cada 3 HP recibido |
| Parry exitoso | +15% |
| Combo de 5+ hits | +10% bonus |
| Bloqueo consecutivo (3+) | -5% (penaliza pasividad) |
| Tech agarre | +5% |

### 6.2 Rage por Afinidad

| Afinidad | Duración | Cooldown | Penalización post |
|----------|----------|----------|-------------------|
| Danimal | 5s | 8s | +15% daño recibido durante 5s |
| Dant | 6s | 10s | Pierde 0.5 barras EX; sin generación durante 3s |
| Gris/Híbrido | 7s | 15s | Elige penalización menor acorde al estilo usado |

### 6.3 Resonancia y Anti-False-Choice

| Efecto | Valor | Descripción |
|--------|-------|-------------|
| Reducción daño recibido | 20% | Reducción de daño |
| Tracking proyectiles rivales | Reducido 60% | No anulado |
| Pushback al bloquear | +20% | Facilita salir de presión |
| Duración | 3 segundos | Tiempo de activación |
| Cooldown | 8 segundos | Tiempo de reutilización |
| Activación automática | Si recibir golpe dejaría HP <15% | Salvación |
| Anti-false-choice | Tope EX al recibir >40% HP en combo | Generación adicional limitada al 50% del exceso |

---

## 7. Modos de Juego

### 7.1 Hub — Teatro de las Sombras

| Zona | Contenido | Evolución |
|------|-----------|-----------|
| Teatro central | Reproducción de finales desbloqueados | Cada final añade elemento físico: estatua, mural o proyección |
| Vitrina | Trofeos de logros (torres completadas, boss rush, dojo) | Nuevos pedestales con cada logro; la sala crece físicamente |
| Portales a modos | Accesos desbloqueables progresivamente | Se iluminan al desbloquear |
| Sala de Entrenamiento | Dummy infinito + frame data display | Frame data visible tras nivel 20 de cuenta |
| NPCs | Dan'ian (Hub), emisario Dantao, emisario Ares | Diálogos con doble lectura según progreso |

### 7.2 Arcade — Dificultades con Armas de Glen

| Dificultad | Sub-jefe | Glen usa | Roster Ocultos | Extras |
|------------|----------|----------|----------------|--------|
| Novato | Kamy | 4 ataques básicos | 4 | Mirror Match |
| Pro | Dob | Ataques + Ron (ratón) | 5 | Mirror Match |
| Experto | Cucaracha con IA | Modo asesino activo | 5 | Mirror Match |
| Pesadilla | Kamy + Dob | Modo asesino + bestia | 6 | Mirror Match + Test Your Might + Stage Bonus |
| Especial | Kamy + Dob + Cucaracha | 3 barras llenas + modo asesino + bestia completa | 6 | Mirror Match + Test Your Might + Stage Bonus |

### 7.3 Torres de Ares

| Torre | Edificio | Jefe |
|-------|----------|------|
| Torre 1 | Shao | Shao Ares |
| Torre 2 | Dao | Fang Ares |
| Torre 3 | Tian | Tian Ares |
| Torre 4 | Rayo | Padre Ares |
| Torre Negra | Fang | Fang Ares |

### 7.4 Boss Rush — Coliseo de Dimitri

| Aspecto | Descripción |
|---------|-------------|
| Secuencia | 12 jefes |
| Vida persistente | Se conserva la vida entre combates |
| Bendiciones post-jefe | Curación 30%, aumento de daño 10% acumulable, o defensa |
| Derrota | Vuelta al hub sin penalización |
| Recompensa final | Skin legendaria + acceso a galería de arte conceptual |

### 7.5 Dojo y Online

| Modo | Descripción |
|------|-------------|
| Dojo | Tutorial (10 lecciones) + Desafíos (10/personaje) + Combo Trials (13/personaje) |
| Online Ranked | Ligas: Bronce > Plata > Oro > Maestro > Maestro de Maestros. Solo Arena Neutral |
| Online Casual | Sin ELO. Modificador de stage opcional con consentimiento mutuo |
| Online Sala | Hasta 8 jugadores. Round-robin. Espectadores |
| Replay System | Determinista: inputs + StateSnapshot inicial. LZ4. ~20-40 KB/partida |

---

## 8. Progresión y Economía

### 8.1 Desbloqueables

| Categoría | Cantidad | Método |
|-----------|----------|--------|
| Personajes | 4 → 12 | Moneda / Historia / Secretos |
| Skins estándar | 36 | Moneda / Desafíos |
| Skins premium | 24 | Torre / Boss Rush / Secretos |
| Variantes de movimientos | 24 | Dojo / Desafíos |
| Bocetos/Arte | 100+ | Arcade / Torre / Boss Rush |
| Trofeos de vitrina | 50+ | Logros |
| Finales | 60 | Arcade / Historia |
| Música | 30+ | Escuchar en modos |

### 8.2 Economía

| Modo | Victoria | Derrota | Bonus |
|------|----------|---------|-------|
| Arcade fácil | 200 | 50 | +100 final bueno |
| Arcade normal | 350 | 100 | +200 final completo |
| Arcade difícil | 500 | 150 | +300 final secreto |
| Torre | 100-200/piso | 50 | +200-500 boss |
| Boss Rush S | 2000 | - | +1000 tiempo |
| Online | 250 | 75 | +50 streak |
| Dojo | 100-500 | - | Según dificultad |

---

## 9. Narrativa Basada en Condiciones

### 9.1 Finales Arcade

| Final | Condición | Recompensa |
|-------|-----------|------------|
| Malo | Perder 3+ rounds | Cinemática corta |
| Genérico | Completar sin continues | Final estándar |
| Bueno | 50% vida + Super | Final extendido + skin |
| Completo | Sin perder rounds + Rage Burst | Final largo + skin premium |
| Secreto | Acción específica en stage 5 | Final lore-crítico + personaje |

### 9.2 Eventos en Historia

| Evento | Condición | Efecto |
|--------|-----------|--------|
| Estilo de Glen | Abuso de parries | Diálogo sobre control |
| Estilo de Ares | Agresivo | Ares respeta |
| Final verdadero | Equilibrio Rage/Resonancia | Maestro original vivo |

---

## 10. IA

### 10.1 Niveles

| Nivel | Comportamiento | Uso |
|-------|----------------|-----|
| Fácil | Reacciona 15-20f, combos básicos | Tutorial |
| Medio | Reacciona 8-12f, combos medianos | Arcade normal |
| Difícil | Reacciona 3-5f, combos óptimos | Arcade difícil |
| Maestro | Reacciona 1-2f, combos máximos | Boss Rush |

### 10.2 Perfiles

| Perfil | Comportamiento |
|--------|----------------|
| Agresivo | Dash frecuente, presión, Rage Burst agresivo |
| Defensivo | Bloqueo, parry, Resonancia, castigo whiffs |
| Adaptativo | Analiza últimas 5 acciones, adapta estrategia |

### 10.3 Jefes

| Jefe | Fases | Habilidades |
|------|-------|-------------|
| Ares de Fuego | 3 | Lava, lluvia de fuego, enrage |
| Ares de Hielo | 3 | Hielo, deslizamiento, tracking |
| Ares de Rayo | 3 | Teletransporte, ataques impredecibles |
| Padre Ares | 3 | Dant → Danimal → Híbrido |

---

## 11. Conclusión

El GDD está completo y listo para producción. Todos los sistemas están definidos, todos los personajes están diseñados, todos los modos están especificados.

**Recomendación final:** Comenzar con el prototipo de combate con 2 personajes (Dan y Glen) y los sistemas Rage + Resonancia. Ese prototipo responderá la última pregunta: ¿se siente bien el vaivén entre Caos y Orden?

---

## 12. Próximos Pasos

1. **Prototipo de combate** (2 personajes, Rage + Resonancia)
2. **Validar sistema de energía** (¿es divertido?)
3. **Expandir roster** (4 personajes launch)
4. **Implementar modos** (Arcade, Historia, Torre, Boss Rush, Dojo)
5. **Desarrollar Hub** (Teatro, Vitrina, Accesos)
6. **Arte y Audio** (2.5D retro-modernizado, música dinámica, FX, voces)
7. **Producción** (18-24 meses)
8. **Lanzamiento** (PC + consolas)

---

**El proyecto está listo para producción. El siguiente paso es el prototipo de combate.**
