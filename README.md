# Fury Engine

Motor y framework para juegos de pelea sobre **Godot 4.7 (C# / .NET 9)**. El núcleo es una simulación
determinista de punto fijo (rollback-ready, sin floats ni RNG en el tick) separada por completo de la presentación,
que puede mostrarse en 2D, 2.5D o 3D. El juego de ejemplo es *Dual Fury* (Glen y Kira).

## Estructura

| Carpeta | Contenido |
|---|---|
| `Fury.Game.csproj` | Proyecto Godot (raíz, como exige Godot). Compila `src/Fury.Game/**` |
| `src/Fury.Core.Math` | `Fp` Q16.16, vectores, xoshiro256**, xxHash64 |
| `src/Fury.Core.Memory` | Arena sin GC |
| `src/Fury.Core.Data` | Tablas de movimientos FMT2/FMT1, `CharacterConfig`, roster |
| `src/Fury.Core.Sim` | Simulación: input, motions, FSM, combate, proyectiles, throws, módulos |
| `src/Fury.Core.Rollback` | `RollbackSession`, `RollbackRunner`, `StateStore` |
| `src/Fury.Core.Net` | Transporte UDP autenticado, handshake, `NetplaySession`, loopback de pruebas |
| `src/Fury.Core.Replay` | Formato de replay FRY4, grabador y verificador |
| `src/Fury.Core.Authoring` | Documentos de edición (movimientos, stages) sin Godot |
| `src/Fury.Core.Observability` | Trazas y mapas de calor |
| `src/Fury.Game` | Director, vistas 2D/2.5D/3D, HUD, audio, menús, herramientas |
| `tests/`, `cert/`, `tools/` | Tests, certificación headless y smoke tests de Godot |
| `docs/` | ADRs, API y roadmaps |

## Requisitos

- .NET SDK 9.0.x y Godot 4.7.2 **.NET** (mono). Windows, Linux y macOS.

## Comandos

```bash
dotnet build FuryEngine.sln -c Release
dotnet test FuryEngine.sln -c Release
dotnet run --project cert -c Release            # certificación (--record regraba los hashes)
GODOT=/ruta/a/godot tools/godot-smoke.sh        # carga todas las escenas en headless
GODOT=/ruta/a/godot tools/godot-online-smoke.sh # dos instancias reales por UDP
```

Abrir el proyecto en Godot: `godot --editor --path .` (la primera vez construye el C# desde la raíz).

### Opciones de línea de comandos del juego (tras `--`)

`--fury-demo` (IA vs IA), `--fury-view=0|1|2`, `--fury-frames=N --fury-capture=img.png` (captura y sale),
`--fury-quit=N`, `--fury-online=host|guest --fury-remote=IP --fury-port=N --fury-character=id --fury-seed=N`.

## Controles por defecto

- P1: WASD + J K L (ligero/medio/pesado), I especial, U agarre, O super, Y poder, H tag. P2: flechas + 1 2 3, 4, 5, 6, 7, 8.
- Mando: cruceta/stick izquierdo, A B X Y, hombros, Back (poder), clic del stick derecho (tag).
- En combate: F1 hitboxes · F2 cambiar vista · F3 depuración · Esc menú.

En móvil o con `--fury-touch` aparece un pad táctil en pantalla que dispara las mismas acciones. El editor de frame data
(menú, builds de debug) guarda `user://characters/<id>.fmt2`, que el juego carga al arrancar.

## Crear un personaje

Un personaje es una tabla de movimientos (`MoveEntry`, ver `GlenData`) más estadísticas. Cada movimiento declara su
comando (botones + motion + postura), fotogramas, daño, stuns, nivel de golpe, rasgos (aéreo, launcher, throw,
proyectil...), cancels y coste de medidor; el motor no necesita código nuevo. Para animación, añade
`res://characters/<id>/visual_2d.tscn` o `visual_3d.tscn` con un `AnimationPlayer` cuyas animaciones se llamen
`idle`, `walk_forward`, `walk_back`, `crouch`, `jump`, `attack`, `hit`, `block`, `knockdown`, `ko`.

Detalles de las decisiones y de lo que **no** está resuelto: `docs/adr/ADR-008-engine-v2-refactor.md`.
