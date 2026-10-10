# Hoja de ruta hasta el producto terminado

Qué falta, en qué orden y qué hay que decidir. El producto terminado es lo que define el **GDD v8.0**; el estado actual y la
distancia a él están en `docs/STATUS.md` y `docs/GDD-TRACEABILITY.md`. Los esfuerzos son **orientativos** (personas-semana de un
programador, sin contar arte ni audio) y salen de leer el código y el plan del propio GDD (§9); sirven para ordenar, no para
comprometer fechas.

## 0. Dónde estamos en el plan del GDD

El motor y las herramientas cubren, y superan, lo que el GDD llama **Pre-producción** (meses 1-3: determinismo validado, replay,
VS local, training). Lo que **no** está hecho es lo que el GDD pone como razón de ser del MVP: el sistema Rage / Resonancia /
Parry con Command Normals, y la validación con jugadores. Hoy no se puede responder a la "pregunta única del MVP" porque las
mecánicas que la plantean no existen en el motor.

## 1. Orden de trabajo

### Hito A — Vertical slice del GDD (el que desbloquea todo lo demás)

Llevar el ruleset de Dual Fury al motor como datos y un módulo de simulación (decisión en ADR-011), usando el proyecto anterior como
referencia (`archive-reference/legacy-core-v0.2/`, `docs/archive/legacy-v0.2-design/`):

| Trabajo | Esfuerzo | Referencia |
|---|---|---|
| Decidir y aplicar los **6 botones** (ancho de `InputState`, paquete de red, repeticiones FRY5, goldens) | 1-2 sem | ADR-011 |
| Economía de energía: EX por fórmulas del GDD, Rage por afinidad, Resonancia activa, anti-snowball, gracia post-KO | 2-3 sem | `EnergySystem.cs`, OPEN-001…005 |
| Parry, Clash, Counter Hit, tabla de prioridad, escalado de daño del GDD, tech de agarre con ventana, tope de juggle, ronda de 80/90 s | 2-3 sem | `CombatResolution.cs`, `DamageScaler.cs`, `CombatMath.cs` |
| Glen (950 HP, ~27 movimientos) y Kira (900 HP, Aceleración) con la frame data del GDD y el set de Command Normals | 1-2 sem + diseño | `GlenData.cs`, `KiraData.cs`; OPEN-008 (la frame data de Kira no existe en el GDD) |
| Presentación: zoom/oscurecimiento del Super, slow-mo del KO, flash de Parry, indicador de Counter Hit | 1 sem | GDD §4.3 |
| Tests contra los valores exactos del GDD (las suites CommandNormal, Damage, Priority, Energy, Gimmick, Hitstop) | 1-2 sem | `archive-reference/legacy-core-v0.2/tests/` |
| Recertificar (cert, goldens, replays) y reponer la guía del alpha | 0,5 sem | |

**Total orientativo: 8-12 semanas.** Salida: dos personajes con el combate del GDD. Después, **playtest formal (10-20 personas) y el
Go/No-Go del GDD**: si el combate no se siente bien, no se sigue escalando.

### Hito B — Alpha de contenido (GDD meses 4-7)

4 personajes, 2 torres, Arcade completo (dificultades, sub-jefes, Mirror Match), Historia capítulos 1-4, perfil y guardado,
IA por niveles con parry. Cada personaje nuevo es: ficha de frame data → gimmick como módulo de simulación → paquete de datos
→ arte (pipeline listo, el GDD calcula 15-23 días por personaje). Esfuerzo de motor: ~1-2 sem por personaje, ~4-6 sem para el
sistema de torres/roguelite, ~3-4 sem Arcade, ~6-8 sem Historia (diálogo, cinemáticas, capítulos), ~3-4 sem perfil/economía.

### Hito C — Beta (meses 8-12)

Roster completo (16), 13 stages, todos los modos (Hub, Torres, Boss Rush, cadena ultra-secreta, Dojo con tutorial, desafíos y
combo trials), Historia completa, accesibilidad completa (Modo Simple, mapa de Command Normals en pausa, daltonismo, velocidad
del Dojo, tamaño de HUD, subtítulos, volúmenes por categoría), música dinámica en capas, voces.

### Hito D — Online como producto (puede ir en paralelo a B/C)

| Trabajo | Esfuerzo |
|---|---|
| Servidor de emparejamiento / lobby, ligas (ELO), salas de hasta 8, espectadores | 8-12 sem |
| Flota de relays (despliegue, monitorización), hole punching, reconexión | 4-6 sem |
| Puerta de versión en el handshake y detección de builds distintas | 0,5 sem |
| Cuentas, baneos, telemetría opt-in | 4-6 sem |
| Pruebas en Internet real con latencia, pérdida y NAT (hoy solo hay simulación y pruebas locales) | continuo |

### Hito E — Plataformas y lanzamiento (meses 13-16)

Steam (Steamworks, logros, nube), instalador y firma, exportes Linux y macOS probados como juego (hoy solo la sim se prueba en
Linux), **verificación de determinismo en ARM** (Apple Silicon, Android, Switch: riesgo crítico del GDD), beta cerrada de
500-1000 jugadores, certificación. **Consolas**: Godot no incluye exportadores de consola (los ofrecen socios de portado) y el soporte de C# en consolas
no está confirmado: hay que verificarlo con un socio antes de comprometerse, o replantear el stack (ADR-011). Es la decisión de
mayor riesgo del producto.

## 2. Decisiones que necesita el director (bloquean trabajo)

| # | Decisión | Opciones | Recomendación |
|---|---|---|---|
| 1 | **6 botones** (GDD: decisión final) frente a los 3+1 actuales | A: aplicar 6 botones (cambio de formato de input); B: enmendar el GDD a 3 fuerzas | A: es la decisión escrita del GDD y los Command Normals del GDD (F+HP, B+MP…) la presuponen |
| 2 | **Input del Parry**: el diseño anterior lo resolvió como acorde LP+MP, que choca con el esquema de 6 botones | Re-decidir con el esquema final | Decidir junto con el punto 1 |
| 3 | **Frame data de Kira** (el GDD solo da HP y gimmick) y de los 14 personajes restantes | Diseñador de combate | Antes de cualquier reclamo de balance |
| 4 | Disparador del **Agotamiento** (el GDD lo lista sin regla) y Rage Burst manual | Definir o retirar | Retirar del MVP |
| 5 | **Valores base**: ronda 80/90 s y vida por personaje (950/900…) frente a 99 s y 1000 | Aplicar el GDD | Aplicar el GDD en el Hito A |
| 6 | **Consolas**: sin exportador propio de Godot y con C# sin confirmar en consolas | Consultar a un socio de portado / motor alternativo / lanzar solo PC primero | Lanzar PC primero y decidir consolas con presupuesto de portado |
| 7 | Preguntas abiertas de lore (GDD Apéndice D) y Vippers | Escribir canon | Antes de guion y arte conceptual |
| 8 | ¿Se mantienen **GDScript** para UI/IA según el GDD, o todo C#? | Enmendar el GDD | Enmendar: ya es todo C# y está probado |

## 3. Riesgos principales

1. **El combate nunca se ha jugado con el ruleset real.** Mitigación: Hito A y Go/No-Go.
2. **Determinismo ARM** sin verificar. Mitigación: runner ARM en CI (Apple Silicon / Linux ARM) antes del Hito B.
3. **Consolas con C#.** Mitigación: decisión 6.
4. **Online solo probado en local.** Mitigación: relay público de pruebas y partidas reales entre casas distintas (Hito D).
5. **Alcance** (GDD: riesgo "muy alto"): congelar el GDD, mover personajes ocultos a DLC si hace falta.
6. **Un solo desarrollador/asistente conoce el motor**: mantener ADRs, tests de documentación y esta hoja de ruta al día.

## 4. Qué se puede hacer ya, sin esperar al Hito A

- Jugar el alpha (`build/DualFury-alpha-alpha1-windows.zip`) y reportar sensaciones: sirve para el feel de movimiento, saltos,
  netcode y menús, aunque no para el ruleset.
- Probar el relay en Internet real (un VPS con un puerto UDP) y partidas entre dos casas.
- Empezar el arte con el pipeline de plantillas (`docs/ART_PIPELINE.md`): la parte visual no depende del ruleset.
- Resolver las decisiones de la sección 2.
