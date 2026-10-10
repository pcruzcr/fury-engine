# Código de referencia del proyecto anterior (v0.2.0)

`legacy-core-v0.2/` es el núcleo (`Fighter.Core`) y sus tests del proyecto anterior, **rescatados del respaldo
`fighter_backup_20260914.tar.gz`**. Implementaba el ruleset del GDD v8.0 que el motor actual todavía no tiene: `EnergySystem.cs`
(EX, Rage por afinidad, Resonancia), `CombatResolution.cs` (parry, clash, counter hit), `DamageScaler.cs`, `CommandNormalResolver.cs`,
`GlenData.cs` / `KiraData.cs` y los tests contra los valores del GDD.

- **No se compila** ni forma parte de la solución (`.gdignore`, fuera de `FuryEngine.sln`). Es solo para leer y portar.
- Usa tipos propios (`Fixed`, `Vector2I`, `Fighter`) distintos de los del motor actual (`Fp`, `FightState`); portar reglas y valores, no
  copiar el código tal cual.
- Cómo y en qué orden se porta: `docs/ROADMAP.md`, Hito A, y `docs/adr/ADR-011-dual-fury-ruleset.md`.
