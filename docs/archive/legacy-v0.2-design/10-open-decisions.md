# 10 — Open Decisions + Contradiction Register

Every entry: ID · question · why it matters · affected systems · blocking phase · options ·
recommendation · owner · status · date. Blocking technical ambiguities are CONTAINED via
seams (`IExPolicy`, `IResonancePolicy`, `NullCommandNormalResolver`) — never hardcoded.

## Contradictions

### DF-CON-002 — Per-move hitstop (§3.2) vs system table (§4.3)
- Source A: GDD §3.2 Glen table lists hitstop **8f** for B+HP and B+MP.
- Source B: GDD §4.3 system table allows only Light 4 / Medium 6 / Heavy 10 / EX 12 / Super 20.
- Type: C3 Design · Severity: MEDIUM · Blocking: NO (choice is explicit and tested).
- Resolution (2026-09-13): **system table wins** — B+HP/B+MP are Heavy (dmg 72/78) → 10f.
  Rationale: §4.3 is the combat-system rule; §3.2 column predates it and has no 8f tier definition.
  Flagged for Systems Designer confirmation in the feel pass. Status: RESOLVED.

### DF-CON-001 — dev-plan filenames vs Master Prompt §6 pattern
- Source A: Master Prompt §6 lists `01-architecture.md`, `02-combat.md`, `03-energy.md`, `04-roster.md`.
- Source B: repo has `01-architecture-loop-snapshot.md`, `02-combat-system.md`, `03-energy-dual.md`,
  `04-roster-gimmicks-pipeline.md` (+ `00/05/06/07/08/09` matching by topic).
- Type: C1 Documentation · Severity: LOW · Blocking: NO.
- Resolution (2026-09-13): actual filenames are authoritative; §6 read as topic pattern, not literal.
  Mapping: architecture→01, combat→02, energy→03, roster→04, stages-modes→05, rollback-qa→06,
  mvp-roadmap→07, lore-bible→08, glossary→09. Status: CONTAINED (no rename churn during F0).

### OPEN-007 — Parry input mapping (§4.6 gap)
- GDD specifies window (6f), effects (+15% EX/+10% RES/stun 15f, fail ×1.2) but NO input.
- Down-tap kills QCF motions; forward-tap kills DP motions and walk initiation.
- **RESOLVED (2026-09-13, owner: user/Game Director): LP+MP chord edge** (both pressed, fresh edge
  while actionable). No conflicts with motions, walk, throws (HP+HK) or tech (LP+LK).
- Type: C3 Design · Severity: HIGH · Blocking: F1 (was). Status: RESOLVED.

## Environment

### ENV-001 — No Godot editor binary in environment
- **RESOLVED (2026-09-13): installed Godot 4.7.2-stable Mono via winget**
  (`4.7.2.stable.mono.official.ed1daf0bf` — matches ADR-001 ref).
  Verified: headless `--import` clean, `--build-solutions` exit 0,
  headless auto-test (600f Glen-vs-Kira, restore+resim+replay) verdict PASS, stderr empty.
  Remaining (non-blocking): editor-GUI run, export templates. Status: VERIFIED (headless axis).

## Technical OPENs (blocking F1 behavior, contained by interfaces)
### OPEN-001 — Chip damage base (§26)
Q: is `chip 10%` = 10% of attack damage or of another quantity? Affects: combat resolution, tests.
**RESOLVED (2026-09-13, owner: user/Game Director): chip = 10% of the attack's damage** (integer
division, round down). Implemented in `CombatResolution` + `DamageTests`. Status: RESOLVED.

### OPEN-002 — EX zero-generation semantics (§28)
Q: does post-Rage *cooldown* also mean ×0 generation, or only the active state? Affects: energy, economy sims.
**RESOLVED (2026-09-13): by explicit source — GDD v8.0 §5.1 table states `0% (pausa)` for BOTH
active Rage AND post-Rage cooldown.** No assumption was made; the approved spec decides.
Implemented in `EnergySystem`. Status: RESOLVED.

### OPEN-003 — Resonance timing matrix (§29)
Q: cooldown origin, multi-hit behavior, chip interaction, KO ordering, round reset, auto-trigger exact timing.
**RESOLVED (2026-09-13, owner: user/Game Director) — "paquete estándar":**
- Cooldown 8s runs **from activation** (ready again 5s after the 3s duration ends).
- Duration fixed 3s; further defensive actions do NOT extend/refresh it.
- Chip damage neither builds nor breaks Resonance; it is reduced by the −20% like any damage.
- Auto-trigger (1×/round, would-be HP<15%) intercepts BEFORE damage: Resonance activates, then
  the same hit is resolved with −20%. It consumes the round's use (manual activation blocked after).
- KO ordering: if damage still KOs through active Resonance, KO wins (no post-mortem activation).
- Round reset: timers, cooldown AND the auto-trigger use all reset each round.
Implemented in `EnergySystem` + `CombatResolution`. Status: RESOLVED.

### OPEN-004 — "First Parry guaranteed" (§30, Dant Rage)
Q: does it mean first parry attempt auto-succeeds, costs nothing, or cannot be baited/punished?
**RESOLVED (2026-09-13, owner: user/Game Director): auto-éxito de timing** — the first tap-down
attempt of each Dant Rage cannot fail due to timing: it grants the 10f stance AND a 2f post-expiry
grace (`Calibration.ParryGrace`, centralized) in which an incoming attack still counts as parried.
It still requires a parryable incoming attack; it does not remove the whiff-recovery risk if
nothing comes. Normal attempts: 6f stance (10f in Dant Rage), no grace.
Implemented in `CombatResolution`. Status: RESOLVED.

### OPEN-005 — Anti-snowball cap formula (§31)
Q: exact formula for resource-gain cap after >40% HP combo + exact Dant +15% punish situations list.
**RESOLVED — generation part (2026-09-13, owner: user/Game Director): damage received beyond
40% of max HP within a single combo generates EX at 50%** (integer halving, round down).
Implemented in `EnergySystem` (tracked per-victim per-combo). The Dant +15% punish-situations list
remains a combat-design task for the feel pass (flagged, non-blocking for F1 infrastructure).
Status: PARTIALLY RESOLVED (formula done; situations list OPEN).

### OPEN-006 — Serialization: MessagePack/LZ4 (§64)
Q: adopt or keep manual LE layout? Needs benchmark: size, ser/deser time, allocations, CPU.
Status: OPEN, non-blocking (baseline manual layout measured: 324B, 0.0327 ms). Blocking: Beta perf pass.

## Technical OPENs (all F1-blocking items RESOLVED 2026-09-13; see above)

### TECH-DEBT-001 — F0 ICommandNormalResolver seam superseded
- F0 left `ICommandNormalResolver` (button+dir only) + `NullCommandNormalResolver`.
- F1 authority is `MoveResolver` (full context: motions, meter, chains, facing).
- Type: C1 · Severity: LOW · Blocking: NO.
- **RESOLVED in refactor R1 (2026-09-13): seam DELETED** (`Contracts.cs` removed after
  verifying zero references). Full suite green after removal. Status: RESOLVED + VERIFIED.

### OPEN-008 — Kira frame data authorship (F2a)
- GDD v8.0 specifies Kira's gimmick (Acceleration), HP (900) and affinity (Dant) but NO frame data.
- Interim (2026-09-13): `KiraData` implemented as explicitly-marked `[KIRA-CAL]` placeholder
  (Glen light/medium skeleton, 2 motion specials + super, Energy-flagged specials so Dant rage
  matters). The Acceleration mechanic itself is fully specified and tested. All placeholder
  numbers are centralized and must be replaced by the Combat Designer before any balance claim.
- Type: C6 Production · Severity: MEDIUM · Blocking: balance claims (not infra). Status: OPEN.

### DF-CON-004 — Launcher route SP1 ender vs knockdown physics
- Source A: GDD §3.2 launcher route ends "...j.HP > SP.1".
- Source B: j.HP is Heavy → victim lands in hitstun → Knockdown (invulnerable until wakeup,
  standard F1 rule); the post-j.HP window (victim lands ~7f after impact + 10f heavy freeze
  for the attacker) cannot fit SP1's motion + 10f startup.
- Type: C3 Design · Severity: MEDIUM · Blocking: NO (4-hit air route executes fully).
- Resolution (2026-09-13): test the executable 4-hit route (MP>HK>j.MP>j.HP = 257 dmg);
  the SP1 ender needs a Systems decision (revise route, or approve an OTG rule — NOT added
  silently). Status: RESOLVED (test pins the executable part; ender flagged).

### DF-CON-003 — GDD combo totals are estimates
- GDD §3.2 combo table gives totals with "~" (BnB ~175, launcher ~280...). The specified
  scaling formula (§4.6) computes different exact values (BnB = 28+50+71 = 149).
- Type: C1 Documentation · Severity: LOW · Blocking: NO.
- Resolution (2026-09-13): routes must be EXECUTABLE (every hit connects); the formula's exact
  totals rule over "~" estimates. Status: RESOLVED.

### DF-CON-005 — Intent window (10f) vs authority current-frame
- `CommandNormalResolver` (informational intent): direction counts if held within the last
  10 frames (may carry a stale direction, e.g. walk released 7f ago + neutral punch edge
  → ForwardMP intent).
- `MoveResolver` (fight authority): command normals require the direction HELD NOW.
- Type: C1 Documentation · Severity: LOW · Blocking: NO.
- Resolution (2026-09-13, review): specified difference of purpose (intent log vs authority),
  both documented, parity pinned by `IntentAuthorityParityTests` on clean histories.
  Status: RESOLVED.

### DF-CON-006 — ADR numbering: session brief vs published repo sequence- A parallel workstream's brief asked for "ADR-002 Fixed-Point" and "ADR-003 RNG",
  but the repo already publishes ADR-002 (F1 combat model) and ADR-003 (Godot presentation).
- Its ADRs originally cited a different DF-CON-005; that id already means
  "intent-window vs authority current-frame" here. Dangling/duplicated ids are forbidden.
- Type: C1 Documentation · Severity: LOW · Blocking: NO.
- Resolution (2026-09-13, review): keep both meanings separate — numbering note lives HERE
  as DF-CON-006; ADR-004/ADR-005 references retargeted to it; published ADRs never renumbered.
  Status: RESOLVED.

### OPEN-009 — Exhaustion trigger undefined (F2a)
- GDD §4.4 lists EXHAUSTION ("sin EX 4s") but no rule anywhere defines what triggers it.
- Unimplementable without inventing a trigger. Deferred; no stub, no dead state.
- Type: C3 Design · Severity: LOW · Blocking: NO (nothing references it). Status: OPEN.

### DF-CON-013 — Prompt de sesión G3 vs DOCX GDD v8.0 (deltas verificados 2026-09-14)
Verificado contra `docs/Dual_Fury_GDD_v8.0_Definitiva.docx`. El prompt trae datos
que el DOCX NO contiene; por instrucción del usuario (DOCX = verdad de diseño),
el DOCX gana y la implementación probada se mantiene:
- Actives/recoveries por movimiento (prompt A1/A2): el DOCX solo da
  startup/onBlock/daño/hitstop. NO adoptados (los [CAL] vigentes sostienen rutas
  probadas; p. ej. HK recovery 24f rompería la ruta launcher).
- Variante SP1-HP (14f/95): DOCX trae una sola fila (10f/-4/85). NO adoptada.
- Estrangula `B+HP+HK`: DOCX dice `Detras+HP+HK` posicional. NO adoptado.
- A5 numérico y A6 (burst manual, exhaustion 8%, super 90f): NO en DOCX. Burst
  manual además EXCLUIDO del MVP (GDD §9.1); exhaustion es OPEN-009.
- B1–B5 Kira: solo HP 900 + gimmick existen en DOCX. OPEN-008 ampliado.
- §9.3 audio / §12.3 métricas / Apéndice C: NO EXISTEN en el DOCX (§8.3 vacío).
  D7/E2 se implementan desde el prompt con procedencia marcada (OPEN-011).
- `01-architecture.md`: alias no materializado, ver DF-CON-001.
- Type: C3 · Severity: HIGH (datos) · Blocking: NO. Status: RESOLVED.

### OPEN-011 — Ratificación de director: D7 audio dinámico + E2 métricas G3
- Implementados desde especificación en-prompt (completa, no ambigua) pero sin
  fuente DOCX. Owner: Game Director. Blocking: NO (implementado + flaggeado).

## Lore OPENs (NON-BLOCKING for Core/F0; blocking for story content)
Pointer to `08-lore-bible.md` pending list: Wu/Xi'an relation, Xi'an wife name, Kamy wrist price,
Fang family relation, Glen forbidden library, Ellena/Emmperium/Ares link, Num Ba style,
Du Lang motive, 'ian ceremony scope, Wu playable/DLC scope, Arcade secret endings, tower floors,
Boss Rush fight 11. Status: OPEN / LORE. Owner: Game Director.

### DF-CON-005 — Numeración ADR del brief vs ADRs existentes (2026-09-14)
SUPERSEDED by DF-CON-006 (mismo tema, registro consolidado; este id ya significaba
"intent window vs authority" desde 2026-09-13 con referencias vivas en `12-patterns.md`).
Se conserva el texto original abajo como historia, sin autoridad normativa.
- Source A: brief REFACTOR+HARDEN pide ADR-002 (fixed-point), ADR-003 (RNG),
  ADR-004 (xxHash), ADR-005 (AOT).
- Source B: repo tiene ADR-002 (F1 combat model) y ADR-003 (Godot presentation).
- Type: C1 Documentation · Severity: LOW · Blocking: NO.
- Resolution: no renumerar (rompe trazabilidad); decisiones nuevas en
  ADR-004..007 con nota de mapeo en cada ADR. Status: RESOLVED.

### DF-CON-011 — Supuestos del brief vs realidad del repo (2026-09-14, renumerado
desde DF-CON-006 para liberar el id: DF-CON-006 ya era "ADR numbering")
- Brief asume: CRC32 (repo usa FNV-1a), `StateSnapshot` vigente v1 como
  snapshot de pelea (la autoridad es `FightSnapshot`, schema 4→5 en la
  sesión), ausencia de `CommandNormalResolver` (existe `MoveResolver`;
  el resolver del brief se añadió como intent Phase-4 sin quitar autoridad),
  MessagePack obligatorio (OPEN-006 lo difiere a benchmark), `01-architecture.md`
  et al. (ya contenido en DF-CON-001: nombres reales mandan).
- Type: C1/C6 · Severity: MEDIUM · Blocking: NO (contenido por diseño).
- Resolution: cada divergencia documentada en su ADR; ningún cambio
  silencioso de formato/hash/stream. Status: RESOLVED.

### DF-CON-007 — Trabajo paralelo no coordinado en el mismo árbol (2026-09-14)
- Dos líneas de autoría (esta sesión + proceso concurrente no identificado)
  editaron los mismos archivos sin commits intermedios: colisiones reales
  resueltas por verificación (renames de clases de test duplicadas,
  `stackalloc` en lambdas, constantes pineadas no ejecutadas, bug de test
  en ventana de dirección, medición de alloc bajo profiler).
- Type: C1 Process · Severity: HIGH · Blocking: NO (convergido y verde).
- Resolution: todo cambio adoptado solo tras build+tests; autoría deliberada
  NO atribuida por archivo en este changelog (el árbol es la evidencia).
  Recomendación: commits pequeños inmediatos + `git status` antes de editar.
  Status: RESOLVED (proceso), OPEN (hábito).

### DF-CON-008 — SingleThreadGuard no cableado a Step (auditoría GATE-G4)
- El guard existe (DEBUG-only, costo cero en RELEASE) pero es process-wide: cablearlo
  en `Step` rompería la suite xUnit paralela. Los sims son thread-confined por convención;
  el determinismo se prueba por ejecución, no por el guard.
- Type: C4 · Severity: LOW · Blocking: NO. Status: RESOLVED (DOCUMENTED, no wiring).

### DF-CON-009 — `Fighter.Game` listado como existente (auditoría GATE-G4)
- El prompt maestro lo lista en el estado conocido; nunca existió como proyecto.
- Type: C1 · Severity: MEDIUM · Blocking: NO. Status: RESOLVED (P1-01: extracción planeada).

### DF-CON-010 — Match score en presenter vs "match state en snapshot" (auditoría GATE-G4)
- Round state (`RoundNumber`) SÍ está en snapshot; match score (Bo3) vive en presenter
  y los replays lo puentean con marks (probado en auto-test con rounds).
- Type: C4 · Severity: LOW · Blocking: NO. Status: RESOLVED (DOCUMENTED).

### DF-CON-012 — Target histórico 8 B vs InputState 16 B medidos (auditoría GATE-G4)
- Peor caso rollback 256 B/frame. Empaquetar deltas de frame no se justifica.
- Type: C1 · Severity: LOW · Blocking: NO. Status: RESOLVED (DEFER documentado).

### DF-CON-014 — Módulos y presentación listados que no existen (auditoría MASTER)
- El prompt lista `Fighter.Data`, `Fighter.Net`, `Fighter.Telemetry` como módulos
  target y `Godot/GDScript` como presentación: ninguno existe (datos viven en Core,
  cero GDScript en el repo, telemetría en tests/harness).
- Type: C1 Documentation · Severity: LOW · Blocking: NO. Status: RESOLVED
  (la arquitectura real es Core+Godot+Harness+Tests; la extracción es P1-01/P1-02).

### OPEN-010 — Runbook de ejecución móvil del harness (D4 docs)
- `Fighter.DeterminismHarness` existe y reproduce el golden en CI host.
- RESOLVED-docs 2026-09-14: `docs/dev-plan/exports.md` redactado (matriz de
  presets, toolchain, comandos adb/iOS, todo marcado authored-not-verified).
  Resta la EJECUCIÓN humana en dispositivos (no configuración).
- Type: C6 · Severity: MEDIUM · Blocking: ejecución móvil (no configuración).
- Owner: Lead Programmer. Status: PARTIAL.
