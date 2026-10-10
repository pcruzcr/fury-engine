# 01 — Arquitectura: Core determinista + Loop 9 fases + Snapshot

> Estado: corregido contra implementación real en auditoría GATE-G4 (2026-09-13).
> La implementación manda; este documento la describe.

## Hexagonal (regla de oro)
`Fighter.Core` (C# puro, **sin** `Godot.dll`) ←→ `Fighter.Godot` (adaptadores/presentación C#).
Cero GDScript en el proyecto (todo UI/IA/tools es C#). Permite testear sin editor,
portear y hacer rollback real.

```
/DualFury.sln → Fighter.Godot.sln (el editor exige <assembly_name>.sln)
Fighter.Godot.csproj (root: GodotSharpDirs lo exige junto a project.godot)
src/Fighter.Core/      Simulación, FSM, AABB, energía, snapshot, RNG, replay, stages
src/Fighter.Godot/     SimRunner, Menu, NetLink, SfxSynth (solo presentación)
src/Fighter.DeterminismHarness/  Probe de paridad multiplataforma (consola, sin Godot)
tests/Fighter.Core.Tests/  xUnit: 300+ tests (determinismo, combate, robustez, medición)
/tools/net-run.ps1     Harness lockstep loopback (2 instancias, compara hashes)
```

`Fighter.Game` como proyecto NO existe (ver DF-CON-010): las reglas de round viven
en el sim (`NextRound`), el marcador de match (Bo3) en presentación. Extracción
futura planificada, no prematura (§61).

## Loop determinista 9 fases (GDD §7.3)
| Fase | Qué hace | Implementación real |
|------|----------|---------------------|
| 1 InputRead | Lee inputs del frame, **nunca procesa** | `Step(p1, p2)` recibe `InputState` ya normalizados |
| 2 Buffer | `RingBuffer[8]` de `InputState` | `Push` siempre, incluso en freeze (los cancels se bufferean) |
| 3 FSM | Transiciona estado | `StartActions` (gate actionable + ventana de cancel) |
| 4 CommandNormal | Intent (`CommandNormalResolver`, informativo) + autoridad (`MoveResolver` data-driven) | Divergencia ventana-vs-frame documentada (DF-CON-005) |
| 5 AABB | `position += velocity` en `Vector2I`, sin delta | `BodyCollision` (enteros, sin float) |
| 6 HitDetection | hitbox vs hurtbox + throws + clash | `CombatResolution` |
| 7 Energy | EX + Resonancia + Rage (OPEN-001…005 resueltos) | `EnergySystem` + `CombatMath` |
| 8 Snapshot | `FightSnapshot` schema 5, manual LE, 1068 B | `<0.5 ms` medido (0.0039–0.03 ms) |
| 9 Events | `SimEvent` (verdad) + `PresEvent` (cosmético) → `IFightObserver` | Presentación nunca escribe sim |

Descartado histórico de v3/v4: `CharacterBody2D`, física Godot, `InputBuffer[5]`, GDScript.

## Snapshots: dos niveles (ver glosario)
- `StateSnapshot` (schema 1): sustrato F0 (`DeterministicSim`) — infraestructura, no combate.
- `FightSnapshot` (schema 5): pelea real — §33 completo + stage + gimmicks + intent.
- Serialización: layout manual little-endian versionado (MessagePack/LZ4 diferidos, OPEN-006).
- `StageData` (zonas) dentro del snapshot vía stage id; hazards futuros entran igual o no entran.

## IA determinista
CPU = función pura (frame, estado) → `InputState` por el MISMO camino que un humano
(RingBuffer → resolver → sim). Sin Behavior Trees, sin GDScript. Replays vs CPU reproducibles.
