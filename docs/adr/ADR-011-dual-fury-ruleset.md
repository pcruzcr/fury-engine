# ADR-011: Cómo llega el ruleset de Dual Fury al motor

- **Estado:** **Propuesto**: necesita la decisión del director (ver `docs/ROADMAP.md`, sección 2)
- **Contexto:** el motor (Fury Engine v2) es genérico y está verificado, pero no contiene las mecánicas que definen Dual Fury en el GDD
  v8.0: Parry, Resonancia activa, Rage financiado por EX, Clash, Counter Hit, escalado de daño propio, 6 botones y la frame data de
  Glen/Kira. El proyecto anterior (v0.2.0) sí las tenía y sus decisiones de diseño están resueltas (`docs/archive/legacy-v0.2-design/`,
  `10-open-decisions.md`); su código y tests están en `archive-reference/legacy-core-v0.2/`. La distancia exacta está en
  `docs/GDD-TRACEABILITY.md`.

## Decisión propuesta

1. **El motor sigue siendo genérico; Dual Fury es un ruleset sobre él.** Las reglas del GDD entran como un módulo de simulación
   (`ISimModule`, estado serializado en el snapshot de rollback) más parámetros en `FightConfig` y datos de personaje. Las constantes
   del GDD (80/90 s, 100/92/84/77/70 %, 6f de ventana de Parry…) viven en configuración y entran en `FightConfig.Fingerprint()`.
2. **Seis botones, como fija el GDD.** `InputState` pasa de 16 a 32 bits (afecta `InputPacket`, `RemoteInputBuffer`, el anillo de input
   del snapshot y el formato de repetición, que sube a FRY5). Se aprovecha para decidir de una vez el input del Parry (el acorde LP+MP
   del diseño anterior ya no encaja) y los Command Normals.
3. **Los tests del GDD son criterio de aceptación**: se portan las suites del proyecto anterior (CommandNormal, Damage, Priority,
   Energy, Gimmick, Hitstop) contra los valores exactos del GDD y se recertifica (nuevos goldens, como en ADR-007).
4. **Desviaciones asumidas del GDD** (a enmendar en el GDD, no en el código): todo C# en lugar de GDScript, xUnit en lugar de GUT, `Fp`
   Q16.16 en lugar de `Vector2I`, sin MessagePack.

## Alternativas descartadas

- Reescribir el motor para encajar el GDD: se pierden rollback, netcode, herramientas y pipeline, todos verificados.
- Mantener 3 botones y cambiar el GDD: posible, pero los Command Normals del GDD (F+HP, B+MP…) y las fichas de Glen asumen 6.

## Riesgo abierto: plataformas

El GDD apunta a PS5, Xbox y Switch. Godot no incluye exportadores de consola y el soporte de C# en consolas no está confirmado; hay que
verificarlo con un socio de portado antes de comprometer esas plataformas. El determinismo en ARM (Switch, Apple Silicon, Android)
tampoco está verificado.
