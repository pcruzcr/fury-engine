# 07 — MVP 10 semanas + Producción + Equipo + Riesgos

## Pregunta única MVP
**¿Rage/Resonancia + Command Normals crea tensión táctica real y se siente bien?**

## Scope MVP (GDD §9.1)
In: Glen + Kira · Command Normals validados · EX + Rage auto + Resonancia + costo oportunidad · 6 normales + Parry + Juggle + Clash + Hitstop · Arena Neutral + Dojo · VS Local + Training frame-data · determinismo + GUT CI · SFX + 1 track.
Out: Burst manual, EX Specials, 14 pj, 11 stages, Arcade/Historia/Torres/BossRush/Online/Hub, rollback (M4–6), música dinámica, voces.

## Cronograma MVP
| Sem | Objetivo | Done |
|-----|----------|------|
| 1–2 | timestep + AABB + RingBuffer[8] + Resolver + Snapshot + Replay | replay 2 min x10 100% |
| 3–4 | Glen 6 normales + CommandNormals + FSM + hitstop, GUT verde | peso + hitstop perceptible, CN ≠ base |
| 5–6 | EX + Rage auto + Resonancia + costo | Rage cambia gameplay, Resonancia decide |
| 7 | Kira + Parry + Clash + juggle | Dant vs Danimal dinámicas distintas |
| 8 | VS Local + Training frame-data | 2 pers 20 min sin romper |
| 9–10 | polish + playtests 10–20 | ≥70% “se siente fighting” espontáneo |
**Go/No-Go Sem4:** golpes sin peso / sin tensión / CN iguales → PAUSAR, no escalar.

## Producción 16 meses
Pre 1–3 Vertical Slice (Glen+Kira, GDD congelado M3) · Alpha 4–7 (4 pj, 2 torres, Arcade, Caps 1–4, rollback F1–2) · Beta 8–12 (roster, torres, modos, historia, rollback F3–4) · Post 13–15 (beta 500–1000, cert consolas, trailer) · Launch 16 + Día1 + DLCs.

## Equipo (FTE) y presupuesto
Director 1.0 · Lead C# 1.0 · Systems 1.0 · Art Lead 1.0 · Junior/FX 0.5→1.0 · Animador 0.5→1.0 · Sound 0.5 · QA 0.5 · CM 0.25. **$80–150k LATAM** (US/EU $300–500k).

## Riesgos (Prob/Impacto→mitigación)
Feel MVP Media/CRÍT→GoNoGo · CN iguales Media/ALTO→GUT S1 · False-choice Danimal Media/CRÍT→analytics + bonus Dant · Rollback Media/CRÍT→determinismo día1 · ARM drift Media/CRÍT→Vector2I + freeze Godot · Scope MuyAlta/CRÍT→freeze M3 · Roster grande Alta/ALTO→ocultos a DLC · Vippers Media/ALTO→revisión sensibilidad · Snapshot lento Media/ALTO→bench pre-M4.
Audio capas: base 120–140 synth+orquesta, +10 BPM tensión (<50%), +5 coros clímax (<25%/10s), synth Rage, filtro metálico Resonancia. SFX refs: SF2/Garou/GGS/SF6/SF3 ping/MvC2.
