# 11 — Changelog

## 2026-09-14 — G3-FEEL playable demo (BUILD)
- Feel: SFX por nivel (pitch por daño) + counter lift + whiff + parry-fail buzz +
  KO jingle; música procedural 2 capas (base/tension) + buses D7
  (tension/climax/rages/resonance-filter con LowPass, volúmenes por snapshot).
- Testear movimientos: movelist overlay (Tab, desde runtime), dummy
  Stand/Block/Jump/CPU (C, en Core testeado), VS 2P local (opción 5, WASD+FGHVBM).
- D8: pips Kira, segmentos EX, bordes de panel por rage/res, blink de RES llena,
  overlay x0.5 EX, parry-fail FX rojo, super zoom punch, lectura LAST daño/combo.
- Bugs cazados: barra HP de P1 mostraba EX; rollback de ediciones perdidas (ninguno
  en sim). Input-latency pineado en test (edge→acción ≤ 1 frame por construcción).
- Evidencia: 314/314, auto-test CPU PASS, VS2P smoke PASS, .exe re-exportado y PASS.

## 2026-09-14 — GATE-G4 vertical slice presentation (BUILD)
- `CpuBrain` movido a Core (convergencia testeable, SCENARIO-018); dummy calmado
  (parry 1/251, poke 1/67, hop solo en approach — medido: antes vulnerable 24%).
- Telemetría CSV por observer + simulador de economía (matriz 4 configs, tabla medida).
- Select real (personaje × stage), HUD con barras HP/EX/RES + pips + timer,
  siluetas procedurales por estado, cámara con tracking/zoom, stages con identidad,
  DF_DEBUG consolidado (H/pausa/step/R).
- Evidencia: auto-test con CPU 600f PASS en-motor; suite verde.

## 2026-09-13 — Demo visible jugable (BUILD)
- El motor ahora SE VE: hitbox overlay (H, activas rojas/invuln amarillo), frame data
  en vivo, combo + daño, números flotantes, screenshake determinista, KO slow-mo,
  announce, HUD de rounds Bo3, poses (crouch), training (pausa P/step N/rematch R).
- CPU determinista por el mismo input path (acercar, bloquear, pokear, saltar, parry,
  backdash); sus partidas son reproducibles. Menú 1/2/3 (Glen/Kira, Arena/Dojo).
- `FightSimulation.NextRound` (gracia/posiciones/timers; throw sin Over) + 3 tests.
- Evidencia: auto-test con CPU 600f PASS (restore+resim+replay con round marks),
  .exe publicado verificado (menú→pelea→PASS dentro del binario).
- Alcance honesto: cuerpos procedurales, no sprites; SFX sintetizados; sin música/voces.

## 2026-09-14 — REFACTOR+HARDEN cierre de cobertura y gates (BUILD)
- Cobertura: línea **95.37%** / rama **87.53%** (gates C6 PASS: ≥90/≥85) vía
  `HardeningCoverageTests` (ramas de validadores ReplayV2 con footer
  recalculado, las 15 palabras de GimmickData, contratos Vector2/InputState).
  `RingBuffer` 100/100; `CommandNormalResolver` 98.6/96.2; `FightSimulation`
  97.3/92.8. Cobertura excluye `AllocationBudget` (coverlet asigna 50–75KB
  fantasma en los hilos medidos — documentado en el propio test).
- MotionMatcher zero-alloc real: `List`/`HashSet`/LINQ/closures eliminados
  (helpers estáticos + `stackalloc`, cap defensivo 65); D7 con inputs de
  combate ahora <4KB/1000 steps. Budgets medidos con contador por-hilo
  (`GetAllocatedBytesForCurrentThread`) + suite secuencial a nivel assembly.
- Suite: **300/300** (116 originales intactos). Build gate D1:
  `dotnet build Fighter.Godot.sln --no-incremental -warnaserror` → 0/0.
- Godot csproj con `TreatWarningsAsErrors`; presets: Android minSdk 24 +
  armeabi-v7a/x86_64, preset macOS Universal nuevo; `docs/dev-plan/exports.md`
  (matriz, comandos adb/Instruments, tabla PASS/OPEN/BLOCKED sin BLOCKEDs).
- ADRs 004–007 con formato completo (incl. Alternativas).

## 2026-09-13 — R2 integración de trabajo paralelo (REVIEW + VERIFY)
- Detectada modificación concurrente no coordinada en el árbol (nuevos
  `CommandNormalResolver.cs`, `DeterminismDiagnostics.cs`, schema 4→5, hot-path zero-alloc).
  Decisión: revisar e integrar, no pisar (control de contradicciones).
- Aportes integrados tras revisión: intent recording (`LastCommandNormal`, schema 5),
  listas reutilizadas en hot path, matcher indexado/stackalloc, excepciones tipadas,
  guards DEBUG costo-cero, ~171 tests de hardening.
- Hallazgos de revisión: 2 fallos transitorios fueron carreras con guardados a medias
  (probado: re-runs verdes + diag dirigido), no bugs. DF-CON-005 (intent-ventana vs
  autoridad-frame) documentado + `IntentAuthorityParityTests` añadidos por el revisor.
- Evidencia: xUnit **289/289 ×3 corridas**, Arena PASS (0x0380…), Dojo PASS (0xF482…),
  build 0 warn/0 err, export .exe previo ya contenía este árbol (hashes idénticos).

## 2026-09-14 — REFACTOR+HARDEN Architecture Upgrade (BUILD, joint session)

Trabajo paralelo convergente en un solo árbol (dos líneas de autoría no
coordinadas, integradas por verificación, no por historia Git — ver
DF-CON-007). Estado: build 0 warn/0 err, **suite verde**, cobertura
línea 93.97% / rama 83.97%, goldens reproducidos por binario independiente.

- **Core endurecido sin romper hashes vivos**: `Fixed` Q16.16 (política WRAP
  explícita, div0 tipificada, sqrt entera, Min/Max/Abs; bug real de
  `ToString` encontrado por test), `Vector2Fixed`, `RingBuffer` (+`FrameCount`,
  `TryPeekAt`, `CopyTo(Span)`), `Step` zero-alloc (listas reutilizadas,
  `[SingleThreaded]`).
- **Nuevos tipos listos**: `Xoshiro256StarStar` (constantes verificadas
  contra referencia oficial; corte pendiente, ADR-005), `DeterminismHash`
  (xxHash64 hand-rolled a spec, SIN paquete: el PackageReference 9.0.0 se
  eliminó porque rompía el build offline — CS0234 — y el hand-rolled verifica
  contra los vectores oficiales; corte pendiente, ADR-006),
  `CommandNormalResolver` (10 variantes GDD, ventana 10f, prioridad
  documentada) + registro Phase-4 en sim, `GimmickData` (blob 128B) +
  snapshot **schema 5** (88 ints/fighter), `IInputProvider`/`ScriptedInputProvider`,
  `DeterminismProbe`, excepciones tipificadas (derivan de
  `InvalidOperationException`), `SimulationLoopEnforcer` + `SingleThreadGuard`
  (DEBUG-only), `SnapshotDiff` (field-level), `ReplayV2` (.fr2p con footer
  xxHash64; LZ4 OPEN).
- **Evidencia ejecutada**: matriz 10k seed 0xDEADBEEF (xx `0x93CE81B8A9EF7CE7`,
  fnv `0x6D877D2E760F2DC5`, fixture `determinism_input.bin` + harness CLI
  independiente reproduce ambos), round-trip 500+500=1000, replay 300f +
  adversarial 1 byte, budgets (<4KB fight, ~0B sustrato; InputState 16B y
  payload ~1.1KB documentados SOBRE budget 8B/512B — gap explícito, no
  verificado).
- **Docs**: ADR-004..007, DF-CON-005/006/007, OPEN-010, glosario extendido.
- Correcciones a tests con bug real (no a producto): tolerancia sqrt,
  `stackalloc` en lambdas, constantes pineadas no ejecutadas, ventana de
  dirección en test de intent congelado, medición de alloc bajo profiler.

## 2026-09-13 — F5 lockstep loopback (BUILD, time-boxed slice of Fase 1 netcode)
- `NetLink.cs` + `NetTest.tscn`: host/client UDP loopback (PacketPeerUdp 7777/7778),
  wire de 7 bytes con validación, lockstep estricto (sin step sin ambos inputs),
  stall detector 600 ticks (falla ruidoso, no esconde), scripts deterministas distintos
  por rol con mismo mapeo P0/P1 en ambos lados.
- Evidencia: host y client completan 300 frames, hashes idénticos
  (0x36D2BD4A6FA74B23), sin stalls. NO es rollback aún (sin predicción/resim/pérdida).
- Harness `tools/net-run.ps1` (veredicto por finalización + igualdad no vacía).

## 2026-09-13 — F3 stages + presentation depth (BUILD)
- Dojo Central en sim: `StageData` (zonas data-driven, [CAL] geometría central 200k),
  mults por fighter en snapshot (EX ×1.05 / locomoción ×0.9, orden base→accel→zona→rage),
  schema 4, mismatch de stage rechazado ruidosamente (probado: salvó el auto-test Dojo
  de una corrupción silenciosa real).
- Presentación: `SfxSynth` procedural (7 cues, seed fija, sin assets), `Menu.tscn` + handoff
  `RequestedStage`, pausa/step training (P/N), zonas dibujadas + nombre de stage en HUD.
- Evidencia headless: menu→fight PASS (Arena 0x9A41…), Dojo PASS (0xC1B3…, distinto de Arena
  = la ruta de modificadores engancha), stderr vacío salvo ruido ObjectDB al salir.
- Tests: **116/116** (8 suites de stage: presets, validador, mults por posición, EX+5%,
  walk −10%, determinismo+restore en Dojo, mismatch).

## 2026-09-13 — R1 refactor integral (BUILD)
- Semántica: `CombatMath` (mults con nombre), `DataValidator.ValidateFighter` (stats + motion map),
  knockdown-invuln y reglas documentadas donde vivían como comentarios.
- Sintaxis/estilo: superficie pública 100% con XML docs (`GenerateDocumentationFile` +
  `TreatWarningsAsErrors` en Core y Tests; CS1591 es error); ~770 docs añadidos.
- Robustez/seguridad: `SnapshotReader.Require` (bounds) + `SnapshotLimits` (cap 64 KB,
  counts acotados); payloads hostiles → `InvalidOperationException` siempre.
- Patrones: `IFightObserver` (Observer) cableado al pipeline + `12-patterns.md`;
  TECH-DEBT-001 cerrada (seam F0 eliminado, cero referencias).
- Eficiencia medida: writer pre-sizeado + lecturas indexadas sin allocs;
  `Step` 2.24M pasos/s (0.45 µs, presupuesto 16666 µs); Capture+Hash 0.0039 ms (antes 0.0245).
- Tests: **108/108** (Observer, Robustness, Measurement+fuzz incluidos).
- Evidencia en-motor re-verificada post-refactor: hashes idénticos bit a bit (0x9563…), PASS.

## 2026-09-13 — F2a content: Kira + air combat + routes (BUILD)
- OPEN-008 (Kira numbers provisional [KIRA-CAL]) + OPEN-009 (Exhaustion triggerless, deferred);
  DF-CON-003 (combo totals are "~" estimates; formula rules) + DF-CON-004 (SP1 ender needs OTG decision).
- Core: air attacks (AirOK, one/jump, landing-cancel, momentum preserved, immediate liftoff),
  `GimmickId` closed set, Kira Acceleration (stack/cap ×3/reset-on-any-damage),
  data-driven motion map per fighter, juggle continuity (air never resets combo, any air hit counts),
  knockdown/wakeup invuln (no OTG), freeze-buffered cancels, stun tops (L8/M14) + launcher pop.
- Tests: 93/93. Bugs found by tests: stale-MoveId landing, jump-velocity zeroing, 1f liftoff delay,
  State==Jump air mis-detection, launcher horizontal drift, test-side poll/wait/hold artifacts (7 fixed).

## 2026-09-13 — F2b presentation + F4 tools (BUILD)
- `src/Fighter.Godot` (Godot.NET.Sdk 4.7.2, net9.0, → root `.godot` via pinned outputs),
  `scenes/Fight.tscn` + `SimRunner.cs` (1 step/tick, runtime input map, HUD/training from live
  runtime, VFX placeholders from PresEvents, auto-test with in-engine restore+replay proof).
- Evidence (headless, ed1daf0bf): import clean, --build-solutions exit 0,
  auto-test 600f hashA==hashB==hashC==PASS, stderr empty. ENV-001 → VERIFIED (headless axis).
- F4: `FightReplayFile` (.frep v1, roundtrip+resim tested), `FrameDataExport` (byte-stable JSON
  from runtime data; Glen 27 moves, Kira flagged), fight certificate v2 (Glen-vs-Kira 10k:
  A=B=0xC3563CF8B22AB549, snapshot@5000 792B restore+resim PASS).
- Tests: **99/99 PASS**. Export presets still authored-only (templates not installed).

## 2026-09-13 — F1 combat simulation (BUILD)
- OPEN-001/003/004/005 resolved by user; OPEN-002 by explicit GDD §5.1; OPEN-007 (parry input
  gap) resolved by user as **LP+MP chord**; DF-CON-002 (8f vs system hitstop table) resolved:
  system table wins, B+HP/B+MP are Heavy → 10f.
- Core: FighterState, Fighter (+SuppressJump), Calibration ([CAL] centralized), MoveData +
  DataValidator, GlenData (24 moves, GDD §3.2 values, chains LP→LP/MP + F+HP→B+HP),
  Aabb, MotionMatcher (QCF/DP/360/double-QCF), MoveResolver (data-driven, −1 gated sentinel),
  DamageScaler, CombatResolution (priority/clash/counter/block+formula/parry/armor/reflect/
  throws+tech/juggle cap), EnergySystem (EX/Resonance/Rage per resolved spec), Events
  (Sim vs Pres), FightSimulation (§15 pipeline) + FightSnapshot schema 2.
- Tests: **83/83 PASS** (11 F1 suites: StateMachine, CommandNormal, Damage, Priority, Energy,
  Gimmick, Hitstop, AABB, Determinism-fight, Snapshot-fight, Replay-fight, DataValidation).
- Bugs found by tests during F1 (all fixed + regression-covered): cancel gate closed, gated
  super fell through to buttons, throw used strike hitbox, SP3 counted as strike, 360 lost to
  jump, backdash misfired on block-hold resume, snapshot Restore schema offset (F0).
- F1 scope notes: no air normals yet; throws grounded-only; chip feeds dealer meter only;
  manual Resonance activatable when cooldown ready (presenter/AI API); Dant +15% punish list
  still OPEN (non-blocking infra-wise). TECH-DEBT-001: F0 resolver seam to clean in F2.

## 2026-09-13 — F0 deterministic foundation (BUILD)
- Installed .NET SDK 9.0.318 (was 8.0.425 only) + verified runtime 9.0.20.
- Added `DualFury.sln`, `src/Fighter.Core` (net9.0, Godot-guard target), `tests/Fighter.Core.Tests` (xUnit).
- Core: SimulationUnits, Fixed Q16.16, Vector2I, SimulationClock, InputState, RingBuffer\<8\>,
  DeterministicRng, DeterministicSim, StateSnapshot schema 1, StateHasher FNV-1a, Replay .DFRP v1,
  behavior-free seams (NullCommandNormalResolver, IExPolicy, IResonancePolicy).
- Tests: 24/24 PASS (10k determinism, restore+resim, replay, RNG, ring, fixed, clock, purity, benchmark).
- Authored (NOT TESTED, no editor binary): `project.godot` 4.7, `export_presets.cfg` (Win/Linux/Android/iOS).
- Docs: `adr/ADR-001.md`, `10-open-decisions.md` (DF-CON-001, ENV-001, OPEN-001..006), this changelog.
- Fixed during F0: snapshot Restore schema-offset bug (shifted stream → dirX OOR); regression-covered.
- Evidence: build 0 warn/0 err; cert seed 0xD0A1F00D20260913 → A=B=0x0C9B7104B261E631 PASS;
  snapshot@5000 restore+resim PASS; Capture+Hash avg 0.0327 ms (goal <0.5 ms).
