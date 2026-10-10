# Trazabilidad GDD v8.0 → código

Cada requisito del **GDD v8.0 Definitiva** (`gdd/Dual_Fury_GDD_v8.0_Definitiva.docx`) contra lo que existe hoy. Es la medida de
"producto terminado". Estados:

- **HECHO**: existe y tiene evidencia (test, certificación o smoke).
- **PARCIAL**: existe una versión distinta o incompleta (se explica).
- **FALTA**: no existe.
- **DESVIACIÓN**: existe algo distinto a lo que dice el GDD; hay que decidir si se cambia el código o se enmienda el GDD
  (ver `docs/ROADMAP.md`, sección "Decisiones del director").

Fuente para portar lo que falta: el proyecto anterior (v0.2.0) **sí implementaba el ruleset del GDD** (energía, parry, clash,
counter hit, escalado, Command Normals, Glen con 27 movimientos y Kira) y sus decisiones de diseño resueltas están en
`docs/archive/legacy-v0.2-design/` (`10-open-decisions.md`: OPEN-001…009). Su código y tests están en
`archive-reference/legacy-core-v0.2/` como referencia. Lo marcan las filas con "**portar de**".

## 1. Visión y producto (GDD §1)

| Requisito | Estado | Evidencia / nota |
|---|---|---|
| Fighting game 2.5D en Godot 4.x / C# | **HECHO** | Vistas 2D, 2.5D y 3D sobre una sim 2D (`FightPresentation2D/3D`) |
| Núcleo C# + UI/IA/tools en GDScript | **DESVIACIÓN** | Todo es C# (cero GDScript). Decisión consciente: un solo lenguaje, tests unitarios sobre la IA y las tools |
| Plataformas: PC (Steam), PS5, Xbox, Switch | **PARCIAL** | Solo Windows exportado y probado. Sin Steamworks. Consolas: ver ADR-011 (riesgo de C# en consolas) |
| Recurso dual con coste de oportunidad (EX vs Resonancia) | **FALTA** | La Resonancia solo es un medidor que sube; no hay estado activo ni coste (§5) |
| Command Normals como sistema | **PARCIAL** | El motor soporta direcciones mantenidas + botón (`MotionId.HoldForward`), pero cada personaje tiene 1 (F+Heavy), no el set del GDD |
| Parry como nodo económico | **FALTA** | `InputButton.Parry` existe y no hace nada. **Portar de** `CombatResolution.cs` (parry, +15 % EX / +10 % Resonancia) |
| Modelo premium $14.99 + DLC | n/a | Negocio, no código |

## 2. Lore y narrativa (GDD §2)

| Requisito | Estado | Nota |
|---|---|---|
| Cosmología, Clan Gris, Familia Ares, 9 capítulos, 5 finales | **FALTA** | Solo texto en el GDD y `docs/archive/legacy-v0.2-design/08-lore-bible.md`. Sin sistema de diálogo, cinemáticas ni capítulos |
| Preguntas abiertas del director (Apéndice D) | **FALTA** | Sin resolver; bloquean escribir diálogos y arte conceptual de varios personajes |

## 3. Roster (GDD §3)

| Requisito | Estado | Nota |
|---|---|---|
| 16 personajes (3 iniciales, 5 desbloqueables, 6 ocultos, Num Ba, Nad'ian) | **FALTA** | 2 personajes: Glen y Kira |
| Glen según ficha (950 HP, ~27 movimientos con la frame data del GDD) | **DESVIACIÓN** | Glen actual: 1000 HP, 14 movimientos, daños distintos (LP 40 vs 28 del GDD). **Portar de** `GlenData.cs` |
| Kira (900 HP, gimmick Aceleración) | **DESVIACIÓN** | Kira actual: 1000 HP, sin gimmick. En el GDD solo hay HP y gimmick: su frame data es una decisión abierta (OPEN-008) |
| Gimmicks por personaje | **PARCIAL** | Hay módulos genéricos (armas, arcana, transformación, tag, finisher, hold) con tests; ningún gimmick del GDD |
| Pipeline "frame data primero, arte después" | **HECHO** | `FrameDataEditor` → validador → visor → arte (`docs/ART_PIPELINE.md`) |
| Añadir un personaje sin tocar código | **HECHO** | Paquetes `character.json` (`ContentLibrary`), con plantillas |

## 4. Sistema de combate (GDD §4)

| Requisito | Estado | Evidencia / nota |
|---|---|---|
| 1v1, mejor de 3, 60 FPS fijos, nunca delta time | **HECHO** | `FightDirector` (RoundsToWin = 2); sim a paso fijo |
| Tiempo de ronda 80 s ranked / 90 s casual | **DESVIACIÓN** | 99 s (`FightConfig.Default`) |
| Vida base 1000 | **HECHO** | `FightConfig.MaxHealth` |
| **6 botones base** (LP/MP/HP/LK/MK/HK) | **DESVIACIÓN** | 3 fuerzas (Light/Medium/Heavy) + Special; `InputState` es de 16 bits y ya usa 14. Decisión final del GDD: 6 botones → ver ADR-011 |
| Hitstop por tipo: L4 / M6 / H10 / EX12 / Super20 | **HECHO** (sin test que fije la tabla) | Está en los datos de movimientos de Glen y Kira; el Super no tiene zoom ni oscurecimiento de cámara |
| Counter Hit (+2f hitstop, ×1.2) | **FALTA** | Sin concepto. **Portar de** `CombatMath.cs` / `CombatResolution.cs` |
| Parry (ventana 6f, stun 15f, fallo ×1.2) | **FALTA** | Ver §1. Input resuelto en el diseño anterior: acorde LP+MP (OPEN-007) |
| KO final con slow-mo ×0.25 (12f) | **FALTA** | |
| Máquina de estados: neutral, hitstun, blockstun, knockdown, wakeup, aéreo | **HECHO** | `FighterStateMachine`, tests de reglas v2 |
| Estados Counter Hit, Parry, Rage, Resonancia, Agotamiento | **FALTA** | `Rage` es un flag; Agotamiento sin disparador definido en el GDD (OPEN-009) |
| Jerarquía de prioridad (Light vs Medium, Clash, Super vs Super, proyectil vs proyectil) | **FALTA** | Sin tabla de prioridad ni Clash. **Portar de** `CombatResolution.cs` (Clash) |
| Escalado de daño 100/92/84/77/70/−8 %, mínimo 10 %, Super −20 % | **DESVIACIÓN** | Lineal: −10 % por golpe, mínimo 60 % (`ComboScale*`). **Portar de** `DamageScaler.cs` |
| Chip 10 %; bloqueo alto/bajo; overheads y lows | **HECHO** | `HitLevel`, `ChipDamagePercent`; falta el −5 % EX por bloquear 3+ seguidos |
| Backdash con 6f de invulnerabilidad | **HECHO** | `BackdashInvulnFrames` (6 por defecto) |
| Tech de agarre (ventana 10f, LP+LK) | **PARCIAL** | Dos agarres simultáneos se anulan (`ThrowTeched`); no hay ventana de 10f ni input propio |
| Juggle (2-3 golpes según personaje) | **PARCIAL** | Launchers y rebote de juggle; sin tope de golpes por personaje |
| Pushblock, Alpha Counter | n/a | Excluidos del MVP por el GDD |

## 5. Energía dual (GDD §5)

| Requisito | Estado | Nota |
|---|---|---|
| Barra EX de 3 segmentos | **HECHO** | 0..300 puntos |
| Llenado EX: +1 %/5 HP dados, +1 %/3 HP recibidos, parry +15 %, combo 5+ +10 %, tech +5 % | **DESVIACIÓN** | Ganancia plana por golpe (`ExGainPerHit`). **Portar de** `EnergySystem.cs` |
| Rage al llenar la barra (3 segmentos), por afinidad: 5/6/7 s, cooldown 8/10/15 s | **DESVIACIÓN** | El Rage actual es una remontada al bajar del 30 % de vida (+15 % de daño). **Portar de** `EnergySystem.cs` |
| Resonancia activa: −20 % daño, EX ×0,5, 3 s, cooldown 8 s, auto-activación al <15 % HP | **FALTA** | Es el coste de oportunidad central del juego. Reglas finas ya resueltas: OPEN-003 |
| Anti-false-choice: tope EX tras combos >40 % HP, bonus Dant +15 %, gracia post-KO 30 % EX | **FALTA** | OPEN-005 resuelto solo en la parte de generación |
| Rage Burst manual | n/a | Excluido del MVP |

## 6. Modos de juego (GDD §6)

| Requisito | Estado | Nota |
|---|---|---|
| Hub (Teatro de las Sombras) | **FALTA** | |
| Arcade por dificultad con sub-jefes de Glen (Kamy, DOB, cucaracha), Mirror Match, Test Your Might | **PARCIAL** | Hay "Arcade" = rondas contra la CPU sin escalera, dificultades ni sub-jefes |
| Historia (9 capítulos) | **FALTA** | |
| Torres de Ares (4 + Torre Negra, 10 pisos, roguelite) | **FALTA** | Los hazards de stage existen; no la estructura de torres |
| Boss Rush (12 combates), cadena ultra-secreta | **FALTA** | |
| Dojo: tutorial de 10 lecciones, desafíos, combo trials | **FALTA** | Sí hay modo Entrenamiento con dummy configurable, historial de inputs, frame data y ventaja medida |
| Online ranked (ligas), casual, sala de hasta 8 + espectadores | **PARCIAL** | Partida 1v1 directa o por relay con código de sala; sin matchmaking, ligas, salas ni espectadores |
| Sistema de repeticiones determinista (LZ4) | **PARCIAL** | Repeticiones deterministas y verificadas (FRY4); sin compresión (3,9 KB por 900 frames, no hace falta) |

## 7. Stages y arquitectura técnica (GDD §7)

| Requisito | Estado | Nota |
|---|---|---|
| 13 stages | **FALTA** | 1 arena por defecto; el editor y las capas de arte permiten crear más como datos |
| Hazards y plataformas móviles deterministas | **HECHO** | `StageModule` (sin estado, derivado del frame), con tests |
| Modificadores de stage (Zonas de Meditación, campos de datos, Sala del Espejo, gravedad alterna) | **FALTA** | |
| Posiciones enteras sin float drift | **DESVIACIÓN** (equivalente) | Punto fijo Q16.16 (`Fp`) en lugar de `Vector2I` en milipíxeles |
| InputBuffer de 8 | **DESVIACIÓN** | 32 frames con SOCD |
| Bucle de 9 fases | **HECHO** | `FightSimulation`; el orden no es idéntico al del GDD |
| `StateSnapshot` blittable, < 0,5 ms | **HECHO** | SoA de enteros; MessagePack **no** se adoptó (OPEN-006) |
| IA genera inputs por el mismo buffer (replays contra IA reproducibles) | **HECHO** | `IInputProvider` / `CpuBrain` |
| IA en GDScript con Behavior Trees | **DESVIACIÓN** | `CpuBrain` en C# |
| Rollback fases 0-3 | **HECHO** | `RollbackRunner`, `NetplaySession` |
| Rollback fases 4-5 (corrección visual, predicción adaptativa) | **PARCIAL** | Hay interpolación física de Godot y deltas de snapshot (no usados en el protocolo); sin predicción adaptativa |
| 10 suites GUT en CI | **DESVIACIÓN** | xUnit (386 tests). Mapa: AABB, Replay, Snapshot, FSM (`FightRulesV2Tests`) → sí; Hitstop, CommandNormal, Damage, Priority, Energy y Gimmick **contra los valores exactos del GDD → faltan** |

## 8. Progresión, arte, audio, IA, accesibilidad (GDD §8)

| Requisito | Estado | Nota |
|---|---|---|
| Esencias azul/roja/dorada, desbloqueos, skins, perfil de cuenta | **FALTA** | Sin perfil ni guardado de progreso (solo ajustes) |
| Arte retro-modernizado 2.5D (sprites 512², stages 3D low-poly) | **FALTA** | Pipeline listo (`docs/ART_PIPELINE.md`); sin arte. Los stages 3D reales (geometría) no están soportados: capas/tarjetas |
| IA asistida con política de licencias | n/a | Política de producción |
| Música dinámica en capas (base/tensión/clímax/Rage/Resonancia) | **FALTA** | Música por pista con crossfade (`Jukebox`) |
| SFX por evento y voces | **PARCIAL** | Claves de sonido por personaje y tonos sintéticos de respaldo; sin audio real. Sin ping de parry, clang de counter, etc. |
| IA por niveles (Fácil → Maestro: reacción, parry %, adaptación) | **PARCIAL** | `CpuBrain` con parámetro de agresividad; sin parry ni adaptación |
| Remapeo completo | **HECHO** | `ControlsMenu` / `InputBindings` |
| Modo Simple (Command Normals a 1 botón), mapa de Command Normals en pausa | **FALTA** | |
| 3 perfiles de daltonismo | **FALTA** | |
| Desactivar screen shake y flashes | **HECHO** | `ReduceMotion`, `ReduceFlashes` |
| Volumen independiente (música/SFX/voces/UI) | **PARCIAL** | Maestro, música y efectos; sin voces ni UI por separado |
| Velocidad ajustable en el Dojo 50-150 %, tamaño de HUD, subtítulos | **FALTA** | |
| Localización | **HECHO** (marco) | `i18n/*.json` con español; el resto del GDD (diálogos) no existe aún |

## 9. MVP y producción (GDD §9)

| Hito del MVP | Estado | Nota |
|---|---|---|
| Motor determinista + replay (semanas 1-2) | **HECHO** | |
| Glen con normales, Command Normals, FSM, hitstop (semanas 3-4) | **PARCIAL** | Glen genérico, no el del GDD |
| Barra EX + Rage + Resonancia + coste de oportunidad (semanas 5-6) | **FALTA** | |
| Kira + Parry + Clash + juggle aéreo (semana 7) | **PARCIAL** | Kira sin gimmick ni parry/clash |
| VS local + Training con frame data (semana 8) | **HECHO** | Más: online, replays, editor y pipeline de arte |
| Polish + playtests formales 10-20 personas (semanas 9-10) | **FALTA** | Cero playtests |
| Hito Go/No-Go de la semana 4 ("¿el combate se siente bien?") | **FALTA** | No se ha podido evaluar: nadie lo ha jugado y falta el ruleset que el GDD usa para decidirlo |
| Pregunta única del MVP: "¿Rage/Resonancia con Command Normals crea tensión táctica?" | **FALTA** | El motor aún no contiene esas mecánicas |

## 10. Checklist de QA (GDD Apéndice C)

| Ítem | Estado |
|---|---|
| Replay 2 min ×10 con el mismo build = 100 % | **HECHO** (`cert`, `Replay` tests) |
| Replay en ARM y x86 = 100 % | **FALTA** (sin ARM) |
| Command Normals / hitbox visual / cancelaciones / hitstop L4-M6-H10-EX12-S20 | **PARCIAL** (hitbox visual y cancelaciones sí; los valores del GDD no están fijados por tests) |
| Parry, Clash, EX, Resonancia ×0,5, cooldown 8 s, gimmicks de Glen y Kira | **FALTA** |
| La IA genera inputs hacia el buffer, no accede a la FSM | **HECHO** |
| Switch 60 FPS / carga ≤ 3 s | **FALTA** |
| SFX sincronizado con hitframes (< 2f) | **PARCIAL** (se dispara en el tick del evento; sin medir) |
| Remapeo en todas las plataformas | **PARCIAL** (Windows) |
| 10 suites GUT sin fallos | **DESVIACIÓN** (xUnit, ver §7) |

## 11. Lo que el proyecto tiene y el GDD no pedía

Rollback con relay y detección de desync, validación de contenido, pipeline de arte como datos, localización, visor y generadores de
plantillas, mods como datos con regla de confianza, certificación determinista en CI (Windows/Linux/macOS), análisis Native AOT.
Ver ADR-008, ADR-009 y ADR-010.
