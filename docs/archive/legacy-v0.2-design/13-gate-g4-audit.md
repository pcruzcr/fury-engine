# 13 — Auditoría GATE-G4 (PHASE A, read-only + evidencia ejecutada)

Fecha: 2026-09-13/14 · Base: `master` limpio · Godot 4.7.2-stable Mono `ed1daf0bf` · .NET 9.0.318 · net9.0.
Método: inspección + ejecución (build, 310 tests, cobertura, benchmarks, runs headless).
Implementación manda sobre documentación (§2: corregir docs, hecho en `01-*` y glosario).

## A–Z (veredicto + evidencia)

| # | Área | Estado | Evidencia |
|---|---|---|---|
| A | Core isolation | VERIFIED | `ForbidGodotReference` (MSBuild) + `CorePurityTests` (sin Godot.dll, sin float/double en estado) |
| B | Godot isolation | VERIFIED | Cero GDScript; `Fighter.Godot` solo lee sim (SimRunner/Menu/NetLink/SfxSynth) |
| C | Simulation authority | VERIFIED | Daño/movimiento/recursos/timers solo en Core; presentación sin escrituras |
| D | Presentation authority | VERIFIED | VFX/audio/cámara/HUD/menús solo en `Fighter.Godot` |
| E | Input authority | VERIFIED | HW→`InputState`→ring→resolver→sim; CPU por el mismo camino (paridad testeada) |
| F | AI convergence | VERIFIED | CPU determinista; auto-test con CPU: restore+resim+replay PASS en-motor |
| G | Determinism | VERIFIED | Cert 10k A=B (`0xC356…`), cert pelea, suites deterministas 310/310 |
| H | Snapshot completeness | VERIFIED | §33 + stage + gimmicks + intent; fuzz restore multi-punto; mismatch rechazado |
| I | Replay | VERIFIED | `.frep` v1 roundtrip+resim; ReplayV2 STAGED (no live) |
| J | RNG | VERIFIED | SplitMix64 live canónico; xoshiro STAGED (ADR-005, corte pendiente no bloqueante) |
| K | Hashing | VERIFIED | FNV-1a live canónico; xxHash wrapper STAGED (ADR-006, sin doble-oficialidad) |
| L | Collision | VERIFIED | AABB entera + `AABBTests` (overlap, separación, clamp, sin tunneling 10k) |
| M | Combat | VERIFIED | 11 suites fight-level + rutas BnB/launcher ejecutables con matemática exacta |
| N | Energy | VERIFIED | `EnergyTests` (EX/Res/Rage/auto/snowball/gracia) + probe de economía (F4) |
| O | Stage simulation | VERIFIED | `StageData` en snapshot vía id; Dojo probado (determinismo+restore+mismatch) |
| P | Camera | READY | Fija + shake determinista; tracking/zoom propuestos, no implementados |
| Q | VFX events | VERIFIED | `PresEvents` → sparks/flash/aura; KO slow-mo solo fuera de auto-test |
| R | Audio events | PARTIAL | 7 cues procedurales cableados; sin música/voces (alcance declarado) |
| S | HUD data flow | VERIFIED | Todo número viene del runtime (`TrainingText`, `GetHistory`); sin duplicados |
| T | Debug instrumentation | VERIFIED | Hitbox overlay, frame data, histories, pausa/step, `DF_DEBUG` vía H; DF_DEBUG build separado: PLANNED |
| U | Allocation | VERIFIED | Step 0.45 µs; D7 0 B hot-path; fuzz + benchmarks en suite |
| V | AOT/IL2CPP | PARTIAL | Core limpio (sin reflection/LINQ/dynamic/unsafe/P-Invoke); ADR-007; ejecución en dispositivo: BLOCKED (sin hardware) |
| W | Platform abstractions | READY | net9.0; tiers Forward+/Compatibility; consola = porteo, no premisa |
| X | CI | PARTIAL | `ci.yml` escrito; sin ejecución (sin remoto). Veredicto honesto, no VERIFIED |
| Y | Test coverage | VERIFIED | 303/303 + cobertura 95.4% líneas / 87.6% ramas (coverlet, ejecutado) |
| Z | Documentation | IN PROGRESS | Este ciclo corrige §2 (01-*, glosario); resto verificado contra código |

## §53 — ítems arquitectónicos abiertos (veredicto requerido)

| # | Ítem | Veredicto | Fundamento |
|---|---|---|---|
| 1 | Vector2Fixed migration | DEFER (documentado) | `Vector2I` cubre posiciones/velocidades enteras; `Fixed` Q16.16 existe para fracciones; `Vector2Fixed` STAGED sin consumidor. Migrar sería estética. |
| 2 | xoshiro256** live | DEFER (ADR-005) | SplitMix64 live, probado, snapshotable. Corte = regen de goldens; sin beneficio medido. |
| 3 | xxHash64 canónico | DEFER (ADR-006) | FNV-1a live y probado. CANONICAL_STATE_HASH = FNV-1a. Sin doble oficialidad. |
| 4 | MessagePack | DEFER (OPEN-006) | Layout manual LE medido 0.0039 ms; añadir dependencia sin necesidad viola §61. |
| 5 | LZ4 | DEFER (OPEN-006) | Replays 20–40 KB medidos; sin requisito de tamaño que lo exija. |
| 6 | InputState 16B vs 8B | DEFER (DF-CON-012) | Medido 16 B (test). Peor caso rollback 256 B/frame. Empaquetar deltas no se justifica. |
| 7 | Snapshot ~1068B vs 512B | DEFER | Medido 1068 B (schema 5). Anillo rollback 8×1068 B = 8.5 KB. Trivial. |
| 8 | SingleThreadGuard wiring | DOCUMENTED (DF-CON-008) | NO cableado a `Step` a propósito: es process-wide e incompatible con xUnit paralelo; los sims son thread-confined por convención + determinismo probado. |
| 9 | AOT ejecución | BLOCKED (sin hardware) | Configuración y código listos; verificación en dispositivo pendiente. |
| 10 | Replay final | PARTIAL | `.frep` v1 live y probado; ReplayV2 STAGED; footer-hash: PLANNED. |

## Deuda (P0–P3 + gating)

| ID | Hallazgo | Sev | Gate |
|---|---|---|---|
| P1-01 | `Fighter.Game` no existe (el prompt lo lista como existente) | P1 | MUST FIX BEFORE PRODUCTION (extracción de modos; hoy split sim/presenter documentado, DF-CON-010) |
| P1-02 | `IRollbackTransport` ausente (§22) | P1 | MUST FIX BEFORE ONLINE (no bloquea lockstep local; abstracción diferida por §61) |
| P2-01 | Footer-hash en replay | P2 | SAFE TO DEFER (corrupción ya detectada por magic/versión/longitud) |
| P2-02 | DF_DEBUG build separado | P2 | MUST FIX BEFORE VERTICAL SLICE (hoy H + pausa/step cubren; separar build comercial después) |
| P2-03 | Match score solo en presenter | P2 | DOCUMENTED (round en sim; marks en replay; probado) |
| P3-01 | Dant +15% punish list | P3 | SAFE TO DEFER (fórmula hecha; lista de situaciones pendiente de diseño) |

P0 abiertos: NINGUNO.

## Contradicciones (delta de este ciclo)
- DF-CON-007 (nuevo): target histórico 8 B vs 16 B medidos → DEFER documentado.
- DF-CON-008 (nuevo): guard process-wide vs suite paralela → DOCUMENTED, no cableado.
- DF-CON-009 (nuevo): prompt lista `Fighter.Game` como existente → no existe; DF-CON-010.
- DF-CON-010 (nuevo): match-score en presenter vs "match state en snapshot" (§12) → round sí, score no; marks lo cubren.
- DF-CON-001…006: ya resueltas, sin cambios.
