# DUAL FURY — ARCHITECTURE & VERTICAL SLICE REPORT (DF-MASTER-GATE-G4)

Fecha: 2026-09-14 · Rama `master` limpia · 14 commits · Godot 4.7.2-stable Mono `ed1daf0bf`.
Estados oficiales (§4): PLANNED / IN PROGRESS / PARTIAL / BLOCKED / READY / VERIFIED.

## 1. Executive Summary
Fundación cerrada y verificada por ejecución; vertical slice jugable y medible
construido (select, HUD, siluetas procedurales, stages, CPU, rounds, training,
replay, telemetría). P0 arquitectónicos: ninguno. Puerta a producción masiva:
G3 humano (feel) + modos/contenido. Sin claims sin evidencia.

## 2. Repository State
14 commits lineales (`DF-000`…`DF-GATE-G4-CD`), árbol limpio, 1 rama.
`Fighter.Godot.sln` (editor exige `<assembly_name>.sln`) con Core/Godot/Tests/Harness.
`project.godot` 4.7, `export_presets.cfg` (4 presets), CI yml (authored, sin runs).

## 3. Architecture Status — VERIFIED
Hexagonal real: Core puro (ForbidGodotReference + CorePurityTests, 0 GDScript en
repo), Godot solo presentación/input-adapter/audio/VFX/cámara/HUD. `Fighter.Game`
no existe (DF-CON-010, P1-01): rounds en sim, marcador en presenter.

## 4. Simulation Status — VERIFIED
60 Hz fixed-step, pipeline §15 (buffer incluso en freeze para cancels buffereados),
sin delta/wall-clock/floats en estado (CorePurityTests). 9 fases en orden ADR.

## 5. Determinism Status — VERIFIED
Cert 10k sustrato A=B=`0x0C9B7104B261E631`; cert pelea Glen-vs-Kira A=B=
`0xC3563CF8B22AB549`; restore@5000+resim PASS; fuzz multi-punto; replay 5 refs;
in-engine arena/Dojo PASS; lockstep 2 procesos PASS; .exe publicado PASS.

## 6. Snapshot Status — VERIFIED
`FightSnapshot` schema 5, LE manual, 1068 B medidos, §33 + stage + gimmicks +
intent. Restore/resim igualdad probada (xUnit + en-motor). Mismatch de schema y
de stage rechazados ruidosamente (el segundo salvó un auto-test real).

## 7. Replay Status — VERIFIED
`.frep` v1 (magic/versión/seed/frames/trailing-check) roundtrip+resim probado;
ReplayV2 STAGED (no live). Footer-hash: P2 diferido.

## 8. RNG Status — VERIFIED (SplitMix64 live canónico)
Snapshotable, stream frame-locked, tests de secuencia/restaura. xoshiro STAGED
(ADR-005, corte pendiente no bloqueante).

## 9. Hash Status — VERIFIED (FNV-1a canónico)
`CANONICAL_STATE_HASH` = FNV-1a 64. xxHash wrapper STAGED (ADR-006). Sin doble
oficialidad.

## 10. Input Status — VERIFIED
HW→`InputState`→ring[8]→resolver→sim; CPU por el mismo camino (convergencia
probada: hits + blocks). Parry = acorde LP+MP (OPEN-007). 10 Command Normals
data-driven (paridad intent/autoridad probada).

## 11. Combat Status — VERIFIED
Prioridad/invuln/armadura/proyectiles(N/A: ningún luchador los tiene)/throw/
counter/clash/trade/parry implementados y testeados (11 suites). Hitstop
4/6/10/12/20 +2 counter (medidos en tests). Rutas BnB (149) y launcher (257)
ejecutables con matemática exacta (DF-CON-003/004).

## 12. Resource Status — VERIFIED
EX 0–3000 con todas las generaciones/penalizaciones (OPEN-001/002/005),
Resonancia 0–1000 con matriz completa (OPEN-003), Rage 3 afinidades + auto
(OPEN-004), anti-snowball, gracia 30%. Interacciones Rage×block/parry/super/
combo/KO y Resonance×chip/multi-hit/KO testeadas.

## 13. AI Status — VERIFIED (convergencia, no inteligencia)
`CpuBrain` en Core (puro, determinista): acerca/bloquea/pokea/salta/parry/
backdash por el input común. Cadencia medida (antes vulnerable 24%).
SCENARIO-018 verde.

## 14. Stage Simulation Status — VERIFIED
`StageData` en snapshot vía id; Dojo (EX ×1.05 / loco ×0.9) probado
(determinismo + restore + mismatch). Sin hazards (documentado, no inventados).

## 15. Presentation Status — READY
Select (personaje × stage), siluetas por estado, stages con identidad, cámara
con tracking/zoom, VFX placeholders por `PresEvents`, SFX sintetizados,
menú/HUD/training/pausa/step/rematch. Sin arte final (declarado).

## 16. HUD Status — READY
Barras HP/EX/RES reales del runtime, pips, timer, rage/res, combo, frame data,
historias de input. Cero duplicados manuales (§49).

## 17. VFX/SFX Status — PARTIAL
VFX por evento + 7 cues procedurales. Sin música/voces (alcance declarado).

## 18. Camera Status — READY
Tracking midpoint + zoom por gap + KO punch, presentation-only determinista.

## 19. Debug/Telemetry Status — VERIFIED
Overlay (H/boxes, frame data, histories, timers), pausa/step, `IFightObserver`,
telemetría CSV byte-estable, replay `.frep`, DF_DEBUG separado: P2.

## 20. Performance Status — VERIFIED (medido)
Step 0.45 µs (2.24M/s, presupuesto 16666 µs); Capture+Hash 0.0039 ms;
0 B hot-path (D7); snapshot 1068 B; InputState 16 B pineado.

## 21. AOT/Platform Status — PARTIAL
Core limpio (sin reflection/LINQ/dynamic/unsafe/P-Invoke); ADR-007.
DEVICE VERIFIED: ninguno (sin hardware). Consola = porteo.

## 22. Test Status — 310/310 VERIFIED (ejecutados)
Unit + integración + determinismo + snapshot + replay + escenarios + performance
+ robustez + medición. Tratamiento warnings-as-errors en Core/Tests.

## 23. Coverage — 96.1% líneas / 88.6% ramas (coverlet, ejecutado)

## 24. Contradictions — DF-CON-001…012 (todas RESOLVED salvo 009/OPENs de diseño/lore)
Ver `10-open-decisions.md`. Últimas: 007 (8 B), 008 (guard), 009 (Fighter.Game
→ P1-01), 010 (match score), 011 (brief vs repo), 012 = 007 duplicado
consolidado. Colisión de IDs paralela reconciliada explícitamente.

## 25. Technical Debt — P0: ninguno. P1-01 (Fighter.Game), P1-02 (IRollbackTransport)
PLANNED pre-producción/online. P2: footer-hash, DF_DEBUG build, match-score doc.
P3: lista Dant +15%.

## 26. Production Risks
G3 humano pendiente (feel no medible sin jugadores); contenido 14 luchadores
(estimación sin calibrar); scope creep (GDD freeze Mes 3); Vippers (revisión
sensibilidad); ARM real sin probar.

## 27. Vertical Slice Status — READY (verificable, pendiente G3 humano)
2 luchadores, 2 stages, menú/select, HUD, combate, recursos, defensa, cámara,
VFX, SFX placeholder, training, CPU, replay, determinismo, frame data, debug,
telemetría, presentación coherente. Ver §64: cada pregunta "¿X funciona?"
tiene evidencia salvo "¿es divertido?" (humano).

## 28. Remaining Work
G3 playtests → roster/modos/contenido → rollback 2-5 → exports firmados →
certificación → consolas. Estimación y equipo en `07-mvp-roadmap.md`.

## 29. Explicit Deferrals
Vector2Fixed, xoshiro-live, xxHash-live, MessagePack, LZ4, 8 B, 512 B,
footer-hash, DF_DEBUG build, Dant-list, match-score-en-sim, stage hazards,
música/voces, arte final. Cada uno con dueño conceptual en OPENs/ADR.

## 30. Traceability Matrix (extracto; completa en suites)
| Requisito | Diseño | Impl | Test | Evidencia | Estado |
|---|---|---|---|---|---|
| Determinismo 10k | §13 | substrate+sim | certs | A=B hashes | VERIFIED |
| Snapshot/restore | §33 | schema 5 LE | SnapshotTests+fuzz | igualdad + 1068 B | VERIFIED |
| Replay | §34 | .frep v1 | ReplayTests+File | resim igual | VERIFIED |
| RNG | §36 | SplitMix64 | RngTests | secuencias | VERIFIED |
| Hit/block/parry/dash/tech | §4.6 | resolution | suites defensa | valores exactos | VERIFIED |
| EX/Rage/Res | §5 | EnergySystem | EnergyTests | economía medida | VERIFIED |
| Stages | §7.1 | StageData | StageTests | mults+restore | VERIFIED |
| CPU converge | §44 | CpuBrain | CpuConvergence | hits+blocks | VERIFIED |
| HUD real | §49 | SimRunner | runtime+headless | observación | VERIFIED |
| Rollback online | §7.5 | — | lockstep harness | hashes iguales | PARTIAL (Fase 1) |

## 31. Evidence Index
- `dotnet build Fighter.Godot.sln`: 0 warn/0 err.
- `dotnet test`: 310/310. Cobertura 96.1%/88.6%.
- Certificados: `0x0C9B…`, `0xC356…` + restore/resim.
- Headless: import limpio, build-solutions exit 0, auto-test arena/Dojo PASS,
  menú→pelea PASS, lockstep PASS, `.exe` publicado PASS.
- Benchmarks: 0.45 µs/step, 0.0039 ms snapshot, 1068 B, 16 B.
- Economía (2400f scripted): Glen/Glen 1406f 31 hits rage×2 res×1 KO;
  Glen/Kira 1318f; Kira/Glen 1311f; Dojo 1406f. Sin starvation/overflow.
- Escenarios §48: 001–018 cubiertos (019 N/A sin hazards, 020 certs).
- Juice (§51): INPUT/MOVEMENT/ATTACK/HIT/DEFENSE/RESOURCE/CAMERA/VFX/SFX/HUD/ROUND/
  COMEBACK/IDENTITY = OBSERVED (valores exactos verificados donde aplica: hitstop,
  escalado, economía); todo HUMAN VALIDATED = UNKNOWN (sin playtesters);
  latencia input→sim NO instrumentada (gap explícito, no medido).
