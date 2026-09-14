# ADR-006: Certificación headless y telemetría de balanceo

- **Estado:** Aceptado (2026-09-14)

## Contexto

Ningún cambio humano debe romper la integridad del combate, y el rebalanceo debe basarse en
datos de uso real, no en sensaciones.

## Decisión

1. **Certificación headless en CI:** un harness de consola genera combates ciegos entre IAs
   deterministas (confinadas al ring buffer de 8 frames de input), compara hashes de estado
   por frame entre ejecuciones y plataformas (x86 CI + ARM cuando exista runner) y falla el
   merge ante cualquier desviación.
2. **Golden hashes por gate:** cada fase (F1, F2…) congela hashes de secuencia canónicos
   (`tests/fixtures/golden_hashes.json`) que el CI re-verifica.
3. **Mapas de calor de combate:** al final de partida, la capa de presentación emite
   telemetría estructural silenciosa basada en hashes de decisión (nunca PII), para
   rebalancear matemáticamente mecánicas críticas (coste de oportunidad de Resonancia,
   penalización de llenado EX).
4. La telemetría es opt-in, agregada por hashes, y el juego funciona idéntico sin ella.

## Consecuencias

- La suite xUnit cubre unidades; el harness certifica la propiedad emergente (determinismo)
  end-to-end.
- Los datos de balance dejan de ser opiniones: el impacto táctico se mide y se ajusta con
  evidencia.
