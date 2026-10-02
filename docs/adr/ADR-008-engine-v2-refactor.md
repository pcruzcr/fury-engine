# ADR-008 — Fury Engine v2: build structure, netcode, replays and data-driven simulation

Fecha: 2026-10-02 · Estado: aceptado · Sustituye las afirmaciones no verificadas de `ENGINE-ROADMAP.md`

## Contexto

Una auditoría completa encontró que el proyecto **nunca había podido ejecutarse en Godot** (el ensamblado se emitía
fuera de `res://.godot/mono/temp/bin`) y que varias piezas descritas como terminadas eran stubs o tenían bugs de fondo:
rollback con los jugadores invertidos en el segundo peer, replays que no se podían guardar, UDP sin redundancia,
sistemas de género (armas, arcanas, tag...) sin conexión a la simulación, frame data "data-driven" con stuns fijos en
código, caminata del jugador 2 invertida, y una presentación sin escala ni eventos.

## Decisiones

### Estructura
- `Fury.Game.csproj` vive en la **raíz** (convención de Godot: `<assembly_name>.csproj` junto a `project.godot`); compila
  `src/Fury.Game/**`. Las librerías `src/Fury.Core.*` son puras (sin Godot), con analizadores estrictos y AOT.
- `Directory.Build.props` centraliza la política; `.gdignore` evita que Godot escanee las librerías Core.
- `Fury.Core.Authoring` (puro) contiene los documentos de edición (tablas de movimientos, stages); la UI de edición vive en
  `src/Fury.Game/Tools` y está accesible desde el menú en builds de debug. `Fury.Editor` desaparece.

### Simulación (Sim v2)
- **FMT2** (`BinaryMoveTable`): 23 enteros por movimiento. Hitstun/blockstun, nivel de golpe (mid/high/low/overhead), rasgos
  (aéreo, launcher, knockdown, throw, proyectil, imbloqueable), comando (botones + motion + postura), categoría y máscara de
  cancels, lanzamiento, coste de medidor, prioridad y parámetros de proyectil. Todo campo nuevo vale 0 por defecto y
  reproduce la fórmula anterior; FMT1 sigue siendo legible.
- `InputBuffer`: 32 frames, SOCD (izq+der = neutral, arriba gana), `PressAge` para buffering de 4 frames con consumo de
  pulsaciones. `MotionRecognizer`: QCF/QCB/DP/RDP/HCF/HCB/360, charge, doble toque, adelante/atrás mantenido.
- `CommandResolver` recorre la tabla del personaje y elige por prioridad: el motor ya no conoce "Glen" ni los 7 slots.
- FSM: ataques aéreos, cancels tras conectar, dash/backdash (invulnerabilidad), lanzadores y juggles, knockdown con
  wake-up invulnerable, proyectiles (pool de 4 en el estado), throws con tech, pushback con fricción y regla de esquina.
  Los combos persisten mientras el rival está en hitstun (antes se reiniciaban en cada ataque: no había escalado real).
- Corregido: caminata y salto relativos al facing (P2 caminaba al revés), pushback que solo movía a rivales en el aire,
  auto-gasto de EX en el especial, hurtbox agachada en el aire, constantes de movimiento desproporcionadas
  (caminar 3 u/frame, saltar 54 u con un luchador de 4 u).
- **Módulos** (`ISimModule` / `SimModule`): estado serializado dentro del snapshot de rollback y hooks de gameplay
  (daño, velocidad, alcance, suelo, rebote, personaje activo, sustitución de KO). Integrados: `WeaponModule`,
  `ArcanaModule`, `TransformationModule`, `StageModule` (plataformas, móviles, rebote, cinta, peligros; sin estado),
  `TagTeamModule`, `FinisherModule` y `HoldModule`. Macro3 activa poderes, Macro4 hace tag.

### Netcode
- `RollbackRunner` recibe `localSlot` (ambos peers simulan en el mismo orden P1/P2), aplica `inputDelay` de verdad,
  barrera de predicción y estadísticas de rollback. Los eventos de la sim se emiten **una sola vez por frame**
  (`SimEventSink.BeginFrame`), así la resimulación no duplica sonidos ni efectos.
- Protocolo v2: ventana redundante de hasta 32 inputs + ack + huella de estado confirmado cada 30 frames (detección de
  desync). `RemoteInputBuffer` acotado; `RttEstimator` mide envío→ack.
- Seguridad: claves **direccionales** (HKDF con transcript y token opcional del matchmaker) — un paquete reflejado falla;
  HMAC sin estado y thread-safe; filtro de origen; recepción sin alocaciones; handshake con timeout y verificación de
  versión de reglas/personajes. El handshake sin token es anónimo (cifrado, no autenticado).
- `NetplaySession` (Core, sin Godot) une transporte y rollback; `OnlineMatch` lo usa en Godot.

### Replays
- Formato FRY4 real: cabecera con huellas de reglas/personajes, inputs, checkpoints cada 60 frames, hash final y trailer
  SHA-256 o HMAC-SHA256 (con clave se rechaza el downgrade). Parser estricto; `Verify` resimula y compara.

### Presentación (Godot)
- `WorldProjection` es el único punto Fp→float. `FightPresentation` abstrae la vista: 2D plana, 2.5D con cámara en
  perspectiva y 3D con cámara orbital; la **simulación sigue siendo 2D** (no hay eje de profundidad jugable).
  Los personajes pueden aportar `res://characters/<id>/visual_2d.tscn` / `visual_3d.tscn` (con `AnimationPlayer`).
- Eventos → VFX (partículas CPU), SFX polifónicos (sonidos procedurales de respaldo), shake y flash con opciones de
  accesibilidad; HUD responsivo; menús funcionales (arcade, versus, training, online directo, replays, opciones).

### Verificación
- 299 tests + 13 de authoring; certificación headless contra hashes versionados (`cert/expected_hashes.txt`),
  rollback de dos peers, replay y restauración; `tools/godot-smoke.sh` carga todas las escenas y
  `tools/godot-online-smoke.sh` juega una partida real entre dos procesos Godot por UDP y compara hashes.
- El golden F1 y los hashes de certificación se **regrabaron** (cambia el comportamiento de la simulación a propósito).

## Límites conocidos (no resueltos)
- La simulación es de dos luchadores activos en un plano 2D: no hay combate 3D con esquiva lateral ni 4 jugadores.
- Las plataformas son superficies de aterrizaje unidireccionales (sin colisión lateral).
- No hay arte, animación ni audio final: las vistas usan formas primitivas hasta que se aporten escenas por personaje.
- El online es conexión directa (IP:puerto) sin matchmaking, relay/NAT traversal ni reconexión.
- Rondas (mejor de 3) se gestionan en el director, no dentro de la simulación; online juega una sola ronda.
- C# en Godot no exporta a Web; los presets Android/iOS requieren sus toolchains y no se han probado aquí.
- Los jobs de CI de AOT y de Godot (`continue-on-error`) no se han podido ejecutar en un runner real.
