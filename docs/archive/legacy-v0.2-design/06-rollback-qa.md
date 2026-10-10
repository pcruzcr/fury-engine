# 06 — Rollback, Tests GUT, QA y Seguridad

## Rollback 5 fases (GDD §7.5)
- **F0 MVP S1–10:** determinismo local (timestep + AABB + RingBuffer[8] + CommandNormal + Snapshot + Replay)
- **F1 M4–5:** lockstep (inputs frame a frame)
- **F2 M5:** predicción local rival
- **F3 M6:** rollback + resimulación desde último frame correcto
- **F4 M6–7:** corrección visual interpolada GDScript (proyectiles fade)
- **F5 Beta M13:** delta compression + predicción adaptativa
Transporte: `IRollbackTransport` UDP/Enet (evaluar Delta-Rollback GDExtension BimDav C++ vs Snopek; abstracción para cambiar sin tocar Core). Checksums XXHash/frame, sin trust-client hit.

## GUT en CI — 10 suites, 0 fallos para playtest
> Nota de vigencia: el framework ejecutado es **xUnit**, no GUT (decision por ejecucion; ver reporte 14, sec 22: 310/310, cobertura 96.1%/88.6%).
StateMachine (transiciones válidas/inválidas) · CommandNormal (100% en 10k inputs) · Damage (escalado/chip/mínimos exactos GDD) · Priority (tabla §4.5) · Energy (llenados, x0.5 Resonancia, cooldowns) · Gimmick (activación/duración por pj) · Replay (5 refs frame-perfect) · Snapshot (serialize→deserialize→resim idéntico, 1000 snaps) · Hitstop (duración exacta ambos pausan igual) · AABB (sin tunneling 10k frames).

## Checklist QA por build (Apéndice C GDD)
Determinismo mismo build 2 min x10 = 100% + ARM↔x86 100% · CommandNormals F+HP ≠ HP, 10k inputs OK · Hitstop 4/6/10/12/20 ambos igual · Cancel normal>special>super · Parry 6f / fallo x1.2 · Clash 12f 0dmg · EX/RES valores exactos + cooldown 8s · Gimmicks Glen/Kira · IA solo a RingBuffer · Switch handheld 60fps hazards máx + carga ≤3s · Audio sync <2f · Remap + modo simple sin bifurcar FSM.

## Observabilidad / trazabilidad / cyberseguridad
Logs estructurados + métricas OpenTelemetry + `fightID+frame+inputHash → .frep` reproducible · Frame-step debugger + Hitbox viewer + Training frame-data · Validación inputs, rate-limit, replays sanitizados sin PII, anti-tamper release, secrets fuera del repo · Benchmark 1000 snapshots antes Mes 4 (<0.5 ms c/u).
