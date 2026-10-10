# 12 — Mapa de patrones (R1)

Cada patrón con su ubicación, su porqué y su test. Nada decorativo: todo patrón aquí
tiene una prueba que lo demuestra.

| Patrón (GoF/lenguaje) | Dónde | Por qué | Test que lo prueba |
|---|---|---|---|
| Memento | `StateSnapshot` / `FightSnapshot` (+ `Capture`/`Restore`) | Rollback y replay exigen estado externalizado versionado | `SnapshotTests`, `FightSnapshotTests`, fuzz multi-punto |
| Observer | `IFightObserver` + `FightSimulation.Observer` | Métricas/traza/debug sin tocar la sim (§61) | `ObserverTests` (traza exacta, orden, no-interferencia) |
| Memento (intent) | `CommandNormalResolver` → `Fighter.LastCommandNormal` (schema 5) | Intent informativo separado de la autoridad (`MoveResolver`); divergencia ventana-vs-frame documentada (DF-CON-005) | `IntentAuthorityParityTests`, `CommandNormalTests` |
| State | `FighterState` + `FighterStateMeta` + `Enter` | Transiciones explícitas, juggle/knockdown incluidos | `StateMachineTests` |
| Command | `InputState` + `RingBuffer` + `MoveResolver.Resolve` | Inputs como datos: buffer, replay, rollback | `CommandNormalTests`, `ReplayTests` |
| Strategy (cerrado) | `GimmickId` + `Affiliation` Rage profiles | Identidad por personaje sin excepciones arbitrarias (§46) | `GimmickTests`, `EnergyTests` (Rage) |
| Flyweight | `MoveData`/`FighterData` compartidos (nunca clonados por frame) | Cero allocs de datos en hot path | `MeasurementTests` (0.45 µs/step) |
| Façade | `FightSimulation` (pipeline §15 en un `Step`) | Un punto de entrada determinista | Todas las suites fight-level |
| Policy/Seam | `IExPolicy`/`IResonancePolicy` (histórico) → `CombatMath` + `EnergySystem` | Fórmulas con nombre, centralizadas, testeables | `DamageTests`, `EnergyTests` |
| Builder (datos) | `GlenData.Build` / `KiraData.Build` + `DataValidator` | Datos inválidos fallan ruidosamente (§52) | `DataValidationTests` |
| Template Method | `SnapshotWriter`/`SnapshotReader` (layout manual LE) | Wire format byte-estable y versionado | `FrameDataExportTests` (byte-stable), robustez |
| Null Object | Observador nulo (una rama) | Cero overhead sin observador | `NullObserver_ZeroBehavioralDifference` |

Reglas transversales (compilador como guardián):
- `Fighter.Core` sin `Godot.dll`: target MSBuild `ForbidGodotReference` + `CorePurityTests`.
- Superficie pública 100% documentada: `GenerateDocumentationFile` + `TreatWarningsAsErrors`
  (CS1591 es error). Tests con la misma higiene (sin doc-file: los nombres SON la doc).
- Enteros deterministas: `CorePurityTests` prohíbe `float`/`double` en estado de sim.
- Payloads hostiles: `SnapshotReader.Require` + `SnapshotLimits` + `ReadHistoryCount`
  (nunca `IndexOutOfRange`, nunca allocs sin cota) — probado en `RobustnessTests`.
