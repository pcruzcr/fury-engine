# ADR-007 — Auditoría de código 2026-10 y regrabación del golden F1

Fecha: 2026-10-01 · Estado: aceptado

## Contexto

La auditoría encontró que `CombatResolutionSystem` sumaba `ExGainPerHit.Raw`,
`ResonanceGainOnHurt.Raw` y `ResonanceGainOnBlock.Raw` (valores Q16.16, p. ej. 12 << 16)
a medidores que se expresan en **puntos** (EX 0..300, Resonancia 0..100). El primer golpe
saturaba ambos medidores: un jab desbloqueaba el Super.

## Decisión

Las ganancias usan la parte entera (`FloorInt`) del valor de ajuste. Es un cambio de
comportamiento consciente de la simulación, así que el fixture
`tests/fixtures/golden_hashes_f1.json` se regrabó (`FURY_RECORD_GOLDEN=1`). Se verificó que
revirtiendo únicamente esta corrección el golden anterior vuelve a coincidir: ningún otro
cambio de la auditoría altera la simulación de la pelea canónica.

## Otros cambios sin efecto en el golden

- Doble KO en el mismo frame → empate (`Winner = -1`); antes perdía siempre P1.
- `HitDetectionSystem` excluye explícitamente atacantes sin hitbox activa.
- `RollbackSession` invalida las ranuras del anillo al reutilizarlas (entradas "confirmadas" de
  hace 64 frames se servían como actuales).
- `UdpTransport`: clave de sesión obligatoria (antes clave cero por defecto), primer paquete
  con secuencia 1, datagramas cortos ya no matan el bucle de recepción, entradas indexadas
  por frame.
