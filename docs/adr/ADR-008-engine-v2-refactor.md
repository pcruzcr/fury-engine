# ADR-008 — Fury Engine v2: build structure, netcode, replays and data-driven simulation

Fecha: 2026-10-02 · Estado: aceptado · Sustituye las afirmaciones no verificadas de `docs/archive/dev-plan-2026-09/ENGINE-ROADMAP.md`

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
- **Canales** (`IDatagramChannel`): `DirectChannel` (UDP a una dirección conocida; descarta otros orígenes antes de parsear) y
  `RelayChannel`. Handshake y transporte comparten el mismo canal, así NAT y relay ven un único origen por jugador.
- **Relay** (`RelayServer`, `tools/relay`): reenvía entre los dos jugadores de una sala sin poder leer ni falsificar nada
  (todo va autenticado de extremo a extremo). Límites: salas, salas por dirección, inactividad, paquetes/s; sin amplificación.
  El código de sala (PBKDF2) da el id de sala y el secreto que entra en el HKDF del handshake: sin el código, un relay que
  suplanta a un jugador obtiene claves distintas y todo su tráfico se rechaza (test `Relay_WithoutTheRoomCode_...`).
  Limitación honesta: el relay ve el id de sala, así que un código corto o inventado se puede adivinar offline; el juego
  genera códigos de 50 bits.

### Replays
- Formato FRY4 real: cabecera con huellas de reglas/personajes, inputs, checkpoints cada 60 frames, hash final y trailer
  SHA-256 o HMAC-SHA256 (con clave se rechaza el downgrade). Parser estricto; `Verify` resimula y compara.

### Presentación (Godot)
- `WorldProjection` es el único punto Fp→float. `FightPresentation` abstrae la vista: 2D plana, 2.5D con cámara en
  perspectiva y 3D con cámara orbital; la **simulación sigue siendo 2D** (no hay eje de profundidad jugable).
  Los personajes pueden aportar `res://characters/<id>/visual_2d.tscn` / `visual_3d.tscn` (con `AnimationPlayer`).
- Eventos → VFX (partículas CPU), SFX polifónicos (sonidos procedurales de respaldo), shake y flash con opciones de
  accesibilidad; HUD responsivo; menús funcionales (arcade, versus, training, online directo o por relay, replays, opciones, controles reasignables, pausa).
- **Stages**: editor → `user://stages/*.stage` → `StageLoader` (píxeles Y arriba → unidades de sim Y abajo, solo enteros) →
  `StageModule` en la simulación → plataformas, peligros y fondo en las tres vistas. Solo offline: online usa el stage por
  defecto y los replays no guardan geometría.
- **Visuales por defecto**: `FighterPoser` calcula las articulaciones (IK de dos huesos con estiramiento limitado) a partir del
  estado, el frame del estado, la velocidad vertical y la frame data del movimiento; `PuppetBody2D` las dibuja con contorno y
  `PuppetBody3D` las convierte en cilindros y esferas. `PoseSmoother` suaviza las transiciones salvo en los ataques, que se
  dibujan exactos sobre su frame data. Solo lee la simulación.
- **Pipeline de arte** (`docs/ART_PIPELINE.md`): el contenido es dato. `CharacterManifest` (`character.json`) describe un personaje (estadísticas,
  movimientos vía `moves.fmt2` o `baseOn`, nombres de movimiento, colores, retrato, arte y sonidos); `CharacterPackLoader` lo convierte en un
  `CharacterConfig` y `CharacterRoster.Register` lo hace jugable (también online: la huella cubre estadísticas y movimientos, **no** el arte).
  `ContentLibrary` descubre paquetes en `res://` y `user://` (mods) y los valida; los rotos se saltan y se listan. El arte se reproduce
  desde contadores de la simulación (`AnimationResolver` + `AnimationTiming`): tiras de sprites (`Sprite2D` / `Sprite3D`) o escenas con
  `AnimationPlayer` controlado por `Seek`; los ataques se estiran sobre su frame data, así que el dibujo queda clavado a hitstop, pausa,
  replay y rollback. Stages con capas de parallax y música; sonidos por clave con tono de respaldo; escenas de VFX de impacto; tema, fuente,
  fondo, logo y skins del HUD. Herramientas: `PoseBaker` / `StageBaker` (paquetes de guía), `CharacterViewer` (frame a frame con cajas y
  pivote, recarga con R) y `ContentCheck` (validador con código de salida, en el smoke de CI).
- **Entrenamiento**: `TrainingDummy` (quieto, agachado, salto, guardia alta/baja reactiva alrededor de los frames activos, CPU) y
  `FrameAdvantageTracker` (ventaja en golpe/bloqueo medida desde el frame de contacto; `KD` si hay derribo) viven en el núcleo y
  tienen tests; `TrainingOverlay` muestra inputs en notación numpad, frame data y ventaja.
- **Controles**: `InputBindings` guarda teclado y mando por acción (`user://`), `ControlsMenu` los reasigna y `PauseMenu` (Esc)
  pausa la partida local; online no se puede pausar (la simulación sigue en el rival).

### Verificación
- 327 tests + 66 de authoring (manifiestos, cargador, roster, resolución y tiempos de animación, capas de stage, idiomas; relay: reenvío, salas, abusos, límites, handshake, partida completa); certificación headless contra hashes versionados (`cert/expected_hashes.txt`),
  rollback de dos peers, replay y restauración; `tools/godot-smoke.sh` carga todas las escenas y
  `tools/godot-online-smoke.sh` juega una partida real entre dos procesos Godot por UDP (o con `RELAY=1`, a través de un relay)
  y compara hashes. Tests, certificación (hashes idénticos) y smokes también se ejecutaron en Ubuntu 24.04 (WSL).
- El golden F1 y los hashes de certificación se **regrabaron** (cambia el comportamiento de la simulación a propósito).

## Límites conocidos (no resueltos)
- La simulación es de dos luchadores activos en un plano 2D: no hay combate 3D con esquiva lateral ni 4 jugadores.
- Las plataformas son superficies de aterrizaje unidireccionales (sin colisión lateral).
- No se incluye arte, animación ni audio final: el motor está preparado para recibirlos (ver `docs/ART_PIPELINE.md`), pero sin ellos los
  luchadores son una figura articulada procedural (poses por estado, ataques que alcanzan la hitbox real; sin poses de intro ni victoria) y el
  audio es sintético: una base legible, no un acabado. Las capas de stage en 3D son tarjetas y una escena con `AnimationPlayer` no puede
  reproducir audio ni llamar a métodos desde sus pistas (se controla por `Seek`). Los efectos de sonido `.wav` solo se leen de `res://`
  (importados); en `user://` hacen falta `.ogg` o `.mp3`.
- El online es conexión directa o por relay con código de sala compartido a mano: no hay matchmaking/lobby, hole punching
  ni reconexión. El relay no se ha probado en Internet real (solo loopback, Windows y Linux).
- Las rondas (mejor de 3) se gestionan en el director, no dentro de la simulación. Online funciona con una **línea de
  tiempo de red continua**: cada ronda es una simulación nueva cuyo frame 0 es un frame de red acordado
  (`FightState.RoundEndFrame + 1 + resultados`, derivado de estado confirmado); `NetplaySession.StartNextRound` cambia la
  simulación sin tocar el transporte y retransmite desde un historial local los frames que el rival aún no confirmó.
  Verificado con tests (jitter + pérdida) y con un mejor de 3 real entre dos procesos Godot.
- Los controles táctiles (`UI/TouchControls`) están implementados y verificados por captura, pero no probados en un
  dispositivo ni en una exportación Android/iOS real: C# en Godot no exporta a Web y esos presets necesitan sus toolchains.
- Mods de personajes: el editor guarda `user://characters/<id>.fmt2` y el juego lo carga al arrancar
  (`CharacterRoster.SetMoveTableOverride`); las huellas de personaje hacen que dos peers con tablas distintas se rechacen.
- Los jobs de CI (matriz Windows, Linux y macOS, AOT y Godot) se ejecutaron en runners reales de GitHub Actions el 2026-10-11 y pasan; el job de Godot es bloqueante.

## Ver también

Estado actual y distancia al GDD: `docs/STATUS.md`, `docs/GDD-TRACEABILITY.md`, `docs/ROADMAP.md`. Decisiones posteriores:
ADR-009 (contenido como datos y regla de confianza de los mods), ADR-010 (transporte online y relay), ADR-011 (ruleset de Dual Fury,
propuesto).
