# Guía de arte y contenido

Todo lo que un artista, diseñador de sonido o modder puede añadir **sin tocar C#**: personajes, stages, sonidos, música, efectos
y el aspecto de los menús. El motor ya viene con una figura procedural, sonidos sintéticos y un stage liso, así que el juego
funciona sin ningún asset; cada archivo que aportes reemplaza una pieza concreta.

## 1. Qué es arte y qué es simulación

| Es **simulación** (determinista, lo comparan online y los replays) | Es **presentación** (libre: cada jugador puede tener la suya) |
|---|---|
| estadísticas del personaje, tabla de movimientos (`moves.fmt2`), reglas del stage (plataformas, peligros, ancho, gravedad) | nombres, colores, sprites, escenas, retratos, sonidos, música, capas del stage, VFX, tema de UI |

El arte **nunca** cambia el resultado de un combate: lo lee la simulación → el arte, jamás al revés. Dos jugadores online
pueden ver sprites distintos del mismo personaje, pero deben tener el mismo `id` y las mismas estadísticas y movimientos (el
handshake compara una huella y rechaza la partida si difieren, en lugar de desincronizarse).

## 2. Flujo de trabajo recomendado

```bash
# 1. Genera un paquete listo para repintar (tiras de guía con nombres, tamaños y pivote correctos + retrato + manifiesto).
godot --path . res://scenes/tools/pose_baker.tscn -- --out=C:/arte/ryu --id=ryu --name=Ryu --color=#ff8800 --base=glen
# 2. Cópialo a characters/ryu (se distribuye con el juego) o a user://characters/ryu (mod). Repinta las tiras.
# 3. Míralo en el visor (Herramientas → ART VIEWER, o):  godot --path . res://scenes/tools/character_viewer.tscn
# 4. Valida los archivos:
GODOT=/ruta/a/godot tools/validate-content.sh
```

El **visor** carga el mismo código que un combate: eliges personaje, stage y cualquier estado o movimiento, recorres los
frames, y ves hurtbox (azul/verde), hitbox (rojo) y el pivote (cruz cian) encima de tu arte. `R` relee los archivos del disco
sin reiniciar. Teclas: `Espacio` reproducir, `←/→` avanzar frame, `B` cajas, `F` voltear, `Tab` cambiar vista (2D / 2.5D / 3D).

El **validador** (`tools/validate-content.sh`, también en el smoke de CI) lee todos los paquetes y comprueba el manifiesto y los
archivos a los que apunta. Sale con código 1 si hay errores; los avisos no fallan.

## 3. Personajes

### Carpeta

```
characters/ryu/                      (o user://characters/ryu/ para mods; las carpetas que empiezan por '_' son plantillas)
  character.json                     manifiesto (obligatorio)
  moves.fmt2                         tabla de movimientos (la guarda el editor de frame data) — o usa "baseOn"
  portrait.png                       selección de personaje y HUD (recomendado 256×320)
  sprites/idle.png, walk_forward.png, stand_light.png ...     tiras de sprites
  visual_2d.tscn / visual_3d.tscn    alternativa: escenas con AnimationPlayer
  sfx/hit_light.ogg, voice_hurt.ogg  sonidos propios
```

### `character.json`

Admite comentarios `//` y comas finales. Todos los campos tienen valor por defecto salvo `id` y `moves`/`baseOn`.

```jsonc
{
  "schema": 1,
  "id": "ryu",                      // 2-24 caracteres: minúsculas, dígitos y _, empieza por letra
  "displayName": "Ryu",
  "baseOn": "glen",                 // copia los movimientos de otro personaje... (se ignora si hay "moves")
  "moves": "moves.fmt2",            // ...o usa tu propia tabla (mínimo 7 movimientos: las ranuras canónicas)
  "stats": { "maxHealth": 1000, "walkForward": 0.11, "walkBack": 0, "dashSpeed": 0, "dashFrames": 0,
             "backdashSpeed": 0, "backdashFrames": 0, "backdashInvulnFrames": 0 },   // 0 = valor derivado del motor
  "moveNames": ["jab", "", "fierce"],                // nombre por índice de movimiento; "" conserva el de la ranura
  "portrait": "portrait.png",
  "colors": { "primary": "#ff8800", "accent": "#ffd0a0" },
  "visual": {
    "scale": 1.0, "pixelArt": false,
    "sprites": { "frameWidth": 448, "frameHeight": 256, "pivotX": 160, "pivotY": 236,
                 "animations": { "idle": { "file": "sprites/idle.png", "frames": 8, "fps": 10 } } },
    "scene2D": "visual_2d.tscn", "scene3D": "visual_3d.tscn"
  },
  "audio": { "sfx": { "hit_light": "sfx/hit_light.ogg", "voice_hurt": "sfx/hurt.ogg" } }
}
```

Los personajes incluidos (`glen`, `kira`) también aceptan un `character.json` **solo de presentación** en
`characters/glen/`: nombre, colores, arte y sonidos se aplican; sus estadísticas y movimientos siguen en el motor.

Reglas de elección de arte: en la vista 2D gana `scene2D`, luego `sprites`, luego la figura procedural. En 2.5D/3D gana
`scene3D`, luego `sprites` (como una tarjeta en el plano de combate), luego la figura procedural.

### Tamaños y escala

- 1 unidad de simulación = **48 px** a la escala de referencia. Un luchador mide **4 unidades = 192 px** de alto y su hurtbox
  1 unidad (48 px) de ancho; en 3D, 1 unidad = 0,5 m (2 m de alto).
- Dibuja siempre al personaje **mirando a la derecha** (hacia el rival); el motor lo voltea.
- `pivotX/pivotY` es el píxel de **cada frame** (desde su esquina superior izquierda) que se coloca en los pies del luchador. El
  visor dibuja una cruz donde cae. Deja margen: los golpes largos llegan a 5-6 unidades (240-290 px) delante del pivote.
- `scale` multiplica todo el arte (útil si pintas a 2× o 4×: `0.5` / `0.25`). `pixelArt` usa filtrado nearest.

### Sprites: tiras

Una imagen **por animación**, con los frames de izquierda a derecha, todos del mismo tamaño y PNG con transparencia. Tamaño de
la imagen = `frames × frameWidth` por `frameHeight` (el validador lo exige y sugiere el `frames` correcto si no cuadra).

Nombres de animación y qué estado las pide (si falta una, se usa la siguiente de la cadena; **solo `idle` es obligatoria**):

| Estado | Cadena de respaldo |
|---|---|
| parado | `idle` |
| caminar adelante / atrás | `walk_forward` / `walk_back` → `walk_forward` → `idle` |
| agachado | `crouch` → `idle` |
| saltando | `jump_up` (subiendo) o `jump_down` (bajando) → `jump` → `idle` |
| dash / backdash | `dash` / `backdash` → `walk_forward` / `walk_back` → `idle` |
| atacando | **nombre del movimiento** → `move_<índice>` → `attack_air` (si salta) → `attack_crouch` (si agachado) → `attack` → `idle` |
| golpeado | `hit_air` / `hit_crouch` → `hit` → `idle` |
| bloqueando | `block_crouch` → `block` → `idle` |
| derribado / KO | `knockdown` → `ko` → `hit` → `idle` (y al revés para KO) |
| antes de empezar la ronda | `intro` → `idle` (se reproduce una vez) |
| el ganador, al acabar la ronda y la partida | `win` → `idle` (cicla) |

Nombres de movimiento: los de `moveNames`, y si faltan, los de las 7 ranuras canónicas (`stand_light`, `stand_medium`,
`stand_heavy`, `crouch_light`, `crouch_heavy`, `special`, `super`), y si no `move_<índice>`. Una animación con un nombre que el
motor nunca pedirá (una errata) se avisa en el validador.

### Sincronía con la frame data

Todo se calcula con contadores de la simulación, nunca con el reloj, así que el arte queda clavado a los frames aunque haya
hitstop, pausa, replay o rollback:

- **Ataques**: la tira se estira sobre **toda la duración del movimiento** (startup + active + recovery). El frame `i` de una
  tira de `N` frames se muestra durante los ticks `[i·total/N, (i+1)·total/N)`. Los ticks activos son `startup+1 … startup+active`:
  pinta el frame de contacto en el índice `floor((startup+1)·N/total)`. El visor muestra el frame de estado y el hitbox para
  alinearlo.
- **idle, walk\*, win** (`loop` por defecto): ciclan con el reloj global a su `fps`.
- **El resto** (`hit`, `block`, `crouch`, `jump`, `knockdown`...): se reproducen una vez a su `fps` desde que empieza el estado y
  se quedan en el último frame (la duración del aturdimiento depende del golpe). Se puede forzar con `"loop": true/false`.

### Escenas (`AnimationPlayer`)

Alternativa a las tiras, en 2D (`Node2D`) o 3D (`Node3D`, p. ej. un glTF importado). El motor busca un `AnimationPlayer`, usa los
mismos nombres de clip y los **controla por búsqueda de tiempo** (`Seek`), no los reproduce: el `loop mode` del clip decide si
cicla, los ataques se estiran sobre el movimiento. Por eso no uses pistas de audio ni llamadas a métodos con efectos
secundarios en esos clips (usa `audio.sfx` del manifiesto). Origen de la escena = los pies; +X = hacia el rival.

### Sonidos del personaje

`audio.sfx` asocia una clave a un archivo del paquete. Claves: las globales de la sección 6, más voces
(`voice_hurt`, `voice_ko`, `voice_win`), `whoosh_<movimiento>` y `voice_<movimiento>` (suenan al empezar ese movimiento).

## 4. Stages

Un stage es un `.stage` (JSON que escribe el editor de stages) junto a su arte. Se busca en este orden:
`user://stages/<n>.stage`, `user://stages/<n>/stage.stage`, `res://stages/<n>.stage`, `res://stages/<n>/stage.stage`. Las rutas de
imagen y música son **relativas a la carpeta del archivo**. Se elige en Opciones → Stage (solo modos offline).

```jsonc
{
  "Name": "Dojo", "Width": 2400, "Height": 900, "Gravity": 1,
  "BackgroundColor": { "R": 0.08, "G": 0.06, "B": 0.16, "A": 1 },
  "Platforms": [], "Hazards": [],                  // geometría de juego: la edita el editor de stages
  "DefaultFloor": false,                           // false si una capa pinta el suelo
  "PixelArt": false,
  "Music": "music/dojo.ogg",                       // opcional, ogg o mp3
  "Layers": [
    { "Image": "sky.png",       "Depth": 1,    "Repeat": true },
    { "Image": "mountains.png", "Depth": 0.75, "Repeat": true, "OffsetY": 60 },
    { "Image": "floor.png",     "Depth": 0,    "Repeat": true, "OffsetY": -110 },
    { "Image": "posts.png",     "Depth": 0,    "Repeat": true, "OffsetY": -60, "Foreground": true }
  ]
}
```

- Cada capa se ancla por el **centro de su borde inferior** en `(OffsetX, OffsetY)` píxeles respecto al centro del suelo (Y hacia
  arriba). `Scale` la escala.
- `Depth` dice lo lejos que está: `0` se mueve con los luchadores, `1` queda fija en pantalla como el cielo, y los valores
  intermedios se deslizan más despacio que la cámara. `Repeat` la repite en horizontal sin fin (haz el borde derecho
  continuo con el izquierdo). `Foreground` la dibuja **delante** de los luchadores.
- La cámara 2D se aleja hasta ~2,2× cuando los luchadores se separan: las capas fijas y no repetidas deben ser anchas
  (≥ 2800 px; el validador avisa si son estrechas). Máximo 12 capas.
- En 2.5D/3D las capas son tarjetas a distintas profundidades y la perspectiva da el paralaje (aproximación: no hay suelo 3D
  real; un suelo pintado con `Depth` 0 queda en el plano de combate).
- Plataformas y peligros siguen siendo del editor de stages: las capas son solo arte (el arte no cambia la colisión).

Para empezar: `godot --headless --path . res://scenes/tools/stage_baker.tscn -- --out=C:/arte/dojo --name=Dojo` genera un stage
completo con capas de guía (cielo, montañas, colinas, suelo y primer plano); está también en `stages/_template`.

## 5. Música

El `Jukebox` persiste entre escenas, hace crossfade y repite en bucle. Pistas por clave, en `res://audio/music/` o
`user://audio/music/` (`.ogg` / `.mp3`): `menu` (menús), `fight`, `training`, `victory` (fin de partida). Un stage con `Music`
usa la suya en el combate. Sin archivo = silencio (en el menú y el combate) o se mantiene la música actual (`victory`). Volumen en
Opciones (Música).

## 6. Sonidos

Búsqueda por clave: paquete del personaje que actúa → `res://audio/sfx/<clave>.ogg|wav|mp3` → `user://audio/sfx/` → tono
sintético del motor. Los sonidos de voz no tienen tono de respaldo.

| Clave | Cuándo suena |
|---|---|
| `hit_light`, `hit_heavy` | golpe conectado (según el daño; con la voz del atacante si su paquete la tiene) |
| `block` | golpe bloqueado |
| `whoosh`, `whoosh_<mov>` | un movimiento empieza |
| `jump`, `dash`, `projectile` | saltar, dash, lanzar un proyectil |
| `knockdown`, `ko`, `rage`, `win`, `round_start` | derribo, KO, poder/rage, victoria, inicio de ronda |
| `ui_move`, `ui_confirm` | menús |
| `voice_hurt`, `voice_<mov>`, `voice_ko`, `voice_win` | voces del personaje |

`.ogg` y `.mp3` se leen desde cualquier sitio; `.wav` solo de `res://` (importado por el editor). Volúmenes: Maestro / Música /
Efectos en Opciones.

## 7. Efectos de impacto (VFX)

Una escena `impact_hit`, `impact_block`, `impact_ko` o `impact_rage` (`.tscn`; sufijo `_3d` para 2.5D/3D) en `res://vfx/` o
`user://vfx/` reemplaza la ráfaga de partículas de ese impacto. La raíz puede ser cualquier `Node2D` / `Node3D`; en cada impacto
se reinician sus emisores de partículas (`CPUParticles*`, `GPUParticles*`) y arrancan sus `AnimatedSprite2D` y su
`AnimationPlayer` (clip `play`, o el primero). El arte animado se oculta solo al terminar.

## 8. Interfaz

En `res://ui/` (o `user://ui/`):

- `theme.tres`: un `Theme` de Godot aplicado a todos los controles. Define las variaciones de tipo `HudHealthBar` y
  `HudMeterBar` (base `ProgressBar`, estilos `fill` y `background`) para dar aspecto a las barras del HUD.
- `font.ttf` / `font.otf` / `font.woff2`: fuente por defecto.
- `menu_background.png` (fondo de todos los menús) y `logo.png` (sustituye al título del menú principal).
- `hud/health_fill.png`, `health_back.png`, `meter_fill.png`, `meter_back.png`: skins sencillos de barras (imagen estirada),
  para cuando un tema completo es demasiado.
- Los retratos de los personajes aparecen en la selección y junto a las barras del HUD.

## 9. Dónde viven los archivos y cómo se distribuyen

- **Con el juego**: `res://characters`, `res://stages`, `res://audio`, `res://vfx`, `res://ui`. Los importa el editor y van al
  `.pck` al exportar (las carpetas `characters/_*` y los stages `_*` son plantillas y se excluyen).
- **Mods**: las mismas carpetas bajo `user://` (en Windows `%APPDATA%\Godot\app_userdata\Dual Fury\`). Se leen directamente, sin
  importar; pensado para trabajo en curso y contenido de la comunidad. Un `id` repetido se ignora con un aviso.
- **Online y replays**: los paquetes de personaje deben estar instalados en ambos lados (si no, el mensaje lo dice). Los stages
  propios solo se usan offline.

## 10. Límites conocidos

- La simulación es de dos luchadores en un plano 2D; el 3D es una presentación de ese plano (no hay esquiva lateral).
- Las capas de stage en 3D son tarjetas, no geometría; no hay un suelo 3D pintable.
- Una escena con `AnimationPlayer` no puede reproducir audio ni ejecutar métodos desde sus pistas (se controla por `Seek`).
- Un relay o un servidor no distribuye arte: cada jugador necesita sus propios archivos.
