# Fury Engine · Dual Fury

Motor y framework para juegos de pelea sobre **Godot 4.7 (C# / .NET 9)** y, encima, el juego **Dual Fury**.
El núcleo es una simulación determinista de punto fijo (rollback, sin floats ni RNG en el tick) separada por completo de la
presentación, que puede mostrarse en 2D, 2.5D o 3D.

> **Estado: alpha 0.1.0 (alpha1).** El motor, el netcode y las herramientas están hechos y verificados; el juego es una **demo técnica
> jugable** con dos luchadores. **El ruleset del GDD (Parry, Resonancia, Rage por EX, Command Normals de 6 botones) aún no está en el
> motor** y no hay arte ni audio finales. Qué hay, qué falta y en qué orden: [`docs/STATUS.md`](docs/STATUS.md),
> [`docs/GDD-TRACEABILITY.md`](docs/GDD-TRACEABILITY.md) y [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Documentación

| Para… | Léelo |
|---|---|
| Saber el estado real del proyecto y cómo se verificó | [`docs/STATUS.md`](docs/STATUS.md) |
| Ver cada requisito del GDD v8.0 contra el código | [`docs/GDD-TRACEABILITY.md`](docs/GDD-TRACEABILITY.md) |
| Saber qué falta para el producto terminado y qué hay que decidir | [`docs/ROADMAP.md`](docs/ROADMAP.md) |
| Entender las decisiones de arquitectura | [`docs/adr/`](docs/adr) (ADR-001…011) |
| Programar contra el motor | [`docs/API.md`](docs/API.md) |
| Crear arte, personajes, stages, sonidos o idiomas | [`docs/ART_PIPELINE.md`](docs/ART_PIPELINE.md) |
| Probar el alpha | [`docs/ALPHA_TESTER_GUIDE.txt`](docs/ALPHA_TESTER_GUIDE.txt) (viaja dentro del zip) |
| Definición del producto | `gdd/` (el GDD v8.0 manda) |
| Material histórico y referencia del proyecto anterior | [`docs/archive/`](docs/archive), [`archive-reference/`](archive-reference) |

## Estructura

| Carpeta | Contenido |
|---|---|
| `Fury.Game.csproj` | Proyecto Godot (raíz, como exige Godot). Compila `src/Fury.Game/**` |
| `src/Fury.Core.Math` | `Fp` Q16.16, vectores, xoshiro256**, xxHash64 |
| `src/Fury.Core.Memory` | Arena sin GC |
| `src/Fury.Core.Data` | Tablas de movimientos FMT2/FMT1, `CharacterConfig`, roster (integrado + paquetes) |
| `src/Fury.Core.Sim` | Simulación: input, motions, FSM, combate, proyectiles, throws, módulos, herramientas de entrenamiento |
| `src/Fury.Core.Rollback` | `RollbackSession`, `RollbackRunner`, `StateStore` |
| `src/Fury.Core.Net` | Canales UDP directo y relay, transporte autenticado, handshake, `NetplaySession`, servidor relay |
| `src/Fury.Core.Replay` | Formato de repetición FRY4, grabador y verificador |
| `src/Fury.Core.Authoring` | Contenido como datos sin Godot: manifiestos, packs, animaciones, stages, idiomas, validación |
| `src/Fury.Core.Observability` | Trazas y mapas de calor |
| `src/Fury.Game` | Director, vistas 2D/2.5D/3D, HUD, audio, menús, contenido, herramientas |
| `characters/`, `stages/`, `i18n/` | Contenido que viaja con el juego (hoy: plantillas `_template*` e `i18n/es.json`) |
| `scenes/` | Escenas de Godot (menús, combate, herramientas) |
| `tests/`, `cert/` | Tests xUnit y certificación determinista headless |
| `tools/` | Scripts de smoke, validación y empaquetado; `tools/relay` (servidor relay) |
| `docs/`, `gdd/` | Documentación y GDDs |
| `archive-reference/` | Código del proyecto anterior que sí tenía el ruleset del GDD (no se compila) |

## Requisitos

.NET SDK 9.0.x y Godot 4.7.2 **.NET** (mono). Windows, Linux y macOS (la sim se ha verificado en Windows y Linux).

## Comandos

```bash
dotnet build FuryEngine.sln -c Release
dotnet test FuryEngine.sln -c Release
dotnet run --project cert -c Release                      # certificación (--record regraba los hashes)
GODOT=/ruta/a/godot tools/godot-smoke.sh                  # carga todas las escenas, plantillas y el validador en headless
GODOT=/ruta/a/godot tools/godot-online-smoke.sh           # dos instancias reales por UDP
RELAY=1 GODOT=/ruta/a/godot tools/godot-online-smoke.sh   # lo mismo, a través de un relay local
GODOT=/ruta/a/godot tools/validate-content.sh             # valida personajes, stages e idiomas (sale con 1 si hay errores)
GODOT=/ruta/a/godot tools/package-alpha.sh alpha1         # zip para testers: juego + relay (Windows/Linux) + guía
dotnet run --project tools/relay -c Release -- 7777       # servidor relay (puerto y dirección opcional)
```

Abrir el proyecto en Godot: `godot --editor --path .` (la primera vez construye el C# desde la raíz). Si se mezclan `dotnet build` y
`godot --build-solutions`, define `MSBUILDDISABLENODEREUSE=1`: los nodos de MSBuild que quedan vivos pueden impedir que Godot termine.

### Opciones de línea de comandos del juego (tras `--`)

| Opción | Efecto |
|---|---|
| `--fury-demo` | IA contra IA |
| `--fury-training`, `--fury-dummy=stand\|crouch\|jump\|block\|blocklow\|cpu` | Modo entrenamiento y modo del muñeco |
| `--fury-view=0\|1\|2` | Vista 2D / 2.5D / 3D |
| `--fury-character=id`, `--fury-opponent=id`, `--fury-stage=nombre` | Personajes y stage (de `user://stages` o `res://stages`) |
| `--fury-templates` | Carga también las plantillas `_*` (personajes y stages) |
| `--fury-language=es` | Fuerza el idioma |
| `--fury-round-seconds=N`, `--fury-seed=N`, `--fury-quit=N` | Duración de ronda, semilla de la IA, salir tras N ticks |
| `--fury-frames=N --fury-capture=img.png` | Captura de pantalla tras N frames y salir (combate, visor y cualquier menú) |
| `--fury-pose=texto`, `--fury-frame=N` | Visor de arte: estado o movimiento y frame |
| `--fury-online=host\|guest`, `--fury-remote=IP`, `--fury-port=N` | Partida online directa |
| `--fury-relay=host:puerto`, `--fury-room=CODIGO` | Partida online por relay (en lugar de `--fury-remote/--fury-port`) |
| `--fury-report` | Imprime qué contenido encontró el build (sirve para comprobar un export) y sale |
| `--fury-touch` | Muestra el pad táctil |

## Controles por defecto

- P1: WASD + J K L (ligero/medio/pesado), I especial, U agarre, O super, Y poder, H tag. P2: flechas + 1 2 3, 4, 5, 6, 7, 8.
- Mando: cruceta/stick izquierdo, A B X Y, hombros, Back (poder), clic del stick derecho (tag).
- En combate: F1 hitboxes · F2 cambiar vista · F3 depuración · F11 pantalla completa · Esc pausa en los modos offline (reanudar,
  reiniciar, cambiar de luchador, menú principal).
- Todas las teclas y botones de mando se reasignan en Opciones → CONTROLS y se guardan en `user://`.
- Entrenamiento: historial de tus inputs (notación numpad), frame data del último movimiento, ventaja en frames medida en vivo
  (al golpear y al ser bloqueado) y muñeco configurable con F5 o desde la pausa.

> Los controles actuales son 3 fuerzas + especial. El GDD fija **6 botones** (LP/MP/HP/LK/MK/HK): ver `docs/adr/ADR-011-dual-fury-ruleset.md`.

En móvil o con `--fury-touch` aparece un pad táctil. El editor de frame data (menú, builds de debug) guarda
`user://characters/<id>.fmt2`, que el juego aplica al arrancar.

## Stages

El editor de stages (menú, builds de debug) guarda `user://stages/<nombre>.stage` (JSON validado al cargar): ancho de arena,
gravedad, color de fondo, plataformas (sólidas, atravesables, de rebote, cinta, móviles entre dos puntos), peligros con daño, capas
de arte y música. Se elige en Opciones → Stage y se ve igual en 2D, 2.5D y 3D. Los stages son solo para los modos offline: online
ambos peers usan el stage por defecto, y las repeticiones no guardan la geometría, así que no se graban sobre un stage propio.

## Online

- **Directo**: el anfitrión abre el puerto UDP en su router; ambos introducen la dirección del otro y el mismo puerto.
- **Relay** (sin abrir puertos, útil tras NAT): ambos se conectan a un servidor relay (`tools/relay`) con un código de sala que
  comparten en privado (el botón NEW genera uno de 50 bits). El relay solo reenvía datagramas ya autenticados de extremo a extremo:
  puede perderlos o retrasarlos, no leerlos ni falsificarlos. El código deriva (PBKDF2) el id de sala que ve el relay y un secreto
  que entra en el intercambio de claves, así que un relay que no conoce el código no puede hacer man-in-the-middle. No uses
  códigos cortos o inventados: un relay hostil podría adivinarlos offline. Detalle y límites: `docs/adr/ADR-010-online-transport-relay.md`.
- El relay limita salas, salas por dirección, inactividad y paquetes por segundo, y nunca envía más bytes de los que recibe. Cualquiera
  puede alojar uno; el juego no depende de ninguno concreto.
- Verificado: partidas de 3 rondas entre dos procesos, Windows↔Windows, Linux↔Linux y **Windows↔Linux** (en un solo equipo). **No** se ha
  probado en Internet real.

## Visuales por defecto

Sin arte, los luchadores se dibujan como una figura articulada procedural (`FighterPoser`): esqueleto con IK de dos huesos que se pone
en pose según el estado de la simulación y, en los ataques, extiende el miembro hasta la hitbox real del movimiento (así se lee lo
mismo que pasa en la frame data). Funciona en 2D y como mallas en 2.5D/3D, y se sustituye por el arte del personaje cuando lo hay.

## Arte y contenido (sin tocar código)

Personajes, stages, sonidos, música, efectos, idiomas y el aspecto de los menús son **datos**: carpetas con un manifiesto JSON que el
motor lee, valida y registra al arrancar. Guía completa: [`docs/ART_PIPELINE.md`](docs/ART_PIPELINE.md). Resumen:

```bash
# Personaje listo para repintar (tiras de guía con los nombres, tamaños y pivote correctos + retrato + manifiesto)
godot --path . res://scenes/tools/pose_baker.tscn -- --out=C:/arte/ryu --id=ryu --name=Ryu --color=#ff8800 --base=glen
# Stage con capas de guía (cielo, montañas, colinas, suelo, primer plano)
godot --headless --path . res://scenes/tools/stage_baker.tscn -- --out=C:/arte/dojo --name=Dojo
# Visor de arte: cualquier estado o movimiento frame a frame con hurtbox/hitbox/pivote encima (R recarga del disco)
godot --path . res://scenes/tools/character_viewer.tscn
```

- **Personaje**: `characters/<id>/character.json` (+ `moves.fmt2` o `baseOn`, retrato, tiras de sprites o escenas con
  `AnimationPlayer`, sonidos). Las estadísticas y los movimientos entran en la huella que comparan online y repeticiones; el arte no,
  así que cada jugador puede tener el suyo. Las animaciones se eligen con cadenas de respaldo (solo `idle` es obligatoria) y se
  sincronizan con la frame data: los ataques se estiran sobre startup + active + recovery.
- **Stage**: `.stage` con capas de parallax (`Depth`, `Repeat`, `Foreground`), música y suelo propio; plantilla en `stages/_template`.
- **Sonido y música**: claves (`hit_light`, `whoosh_<movimiento>`, `voice_hurt`…) buscadas en el paquete del personaje, en `res://audio/sfx`
  y en `user://audio/sfx`, con un tono sintético de respaldo; música por pista (`menu`, `fight`, `training`, `victory`) o por stage, con
  crossfade. Volúmenes maestro / música / efectos en Opciones.
- **VFX**: escenas `res://vfx/impact_hit|block|ko|rage(.tscn)` reemplazan las partículas de impacto.
- **UI**: `res://ui/theme.tres` (con variaciones `HudHealthBar` / `HudMeterBar`), fuente, fondo de menú, logo y skins del HUD.
- **Idiomas**: `i18n/<código>.json` (texto en inglés → traducción); `i18n/es.json` incluido, selector en Opciones → Idioma.
- **Mods**: las mismas carpetas bajo `user://` se leen sin importar. **Un mod es solo datos**: las escenas, los temas y los VFX, que pueden
  llevar scripts, solo se cargan de `res://` (ADR-009). Las carpetas que empiezan por `_` son plantillas: no se cargan salvo con
  `--fury-templates` y no se exportan.

## Crear un personaje

Un personaje son estadísticas + una tabla de movimientos (`MoveEntry`, ver `GlenData`): cada movimiento declara su comando (botones +
motion + postura), fotogramas, daño, stuns, nivel de golpe, rasgos (aéreo, launcher, throw, proyectil…), cancels y coste de medidor. Para
añadir uno sin compilar: `character.json` con `"baseOn": "glen"` (copia los movimientos), edita la tabla con el editor de frame data y
añade el arte. Un gimmick nuevo (una mecánica propia) sí requiere un módulo de simulación en C#.

Decisiones de arquitectura y lo que **no** está resuelto: [`docs/adr/`](docs/adr).
