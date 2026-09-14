# ADR-001: Purge total y rewrite desde cero

- **Estado:** Aceptado (2026-09-14)
- **Decisores:** Equipo de desarrollo Dual Fury

## Contexto

El proyecto previo (v0.2.0, post-GATE-G4) tenía un Core determinista verificado (314 tests,
paridad bit-exacta, export Windows), pero acumulaba deuda: SimRunner monolítico de 984 líneas,
dualidad de primitivas (Vector2I vs Vector2Fixed), Replay v1 muerto, cutover pendientes de
xoshiro256**/xxHash64, lockstep sin rollback, y capa de presentación sin sistemas separados.

## Decisión

Purgar el repositorio por completo (incluido el historial git) y reconstruir desde cero como
**Fury Engine**: arquitectura hexagonal modular (Math, Sim, Data, Rollback, Net, Replay,
Observability + capa Godot delgada), con xoshiro256** y xxHash64 nativos desde el día 1,
rollback real como objetivo de F2 y multiplataforma AOT-safe como restricción transversal.

## Consecuencias

- Se pierden los certificados de determinismo previos; la paridad deberá re-certificarse
  en cada gate con nuevos golden hashes.
- Respaldo del proyecto viejo en `fighter_backup_20260914.tar.gz` (fuera del repo) por si
  se necesita recuperar datos (GDD v8, frame data de personajes).
- Todo código nuevo nace bajo las reglas de pureza: sin Godot en Core, sin reflexión,
  sin float/double en hot path, presupuesto de alocaciones verificado en CI.
