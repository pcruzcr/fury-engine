# ADR-009: Contenido como datos y regla de confianza de los mods

- **Estado:** Aceptado (2026-10-03)
- **Contexto:** el motor debía quedar listo para que artistas y modders añadan personajes, stages, sonidos, música, efectos, textos
  y aspecto de menús sin tocar C#, sin comprometer el determinismo ni la seguridad del jugador. Detalle de uso: `docs/ART_PIPELINE.md`.

## Decisión

1. **Todo el contenido es datos validados.** Personaje = carpeta con `character.json` (+ `moves.fmt2` o `baseOn`); stage = `.stage`
   con capas; idioma = `i18n/<código>.json`; efectos, tema y sonidos por convención de nombres. Se descubren en `res://` y `user://`
   (`ContentLibrary`). Un paquete roto se omite y se informa; nunca detiene el juego.
2. **El arte nunca entra en la simulación.** La huella de un personaje (`CharacterConfig.Fingerprint`) cubre solo estadísticas y tabla de
   movimientos; nombres, colores, sprites, escenas, retratos y sonidos quedan fuera. Dos jugadores online pueden ver arte distinto;
   si las reglas difieren, el handshake rechaza la partida.
3. **Las animaciones se derivan de contadores de la simulación**, no del reloj (`AnimationResolver` + `AnimationTiming`): los ataques
   se estiran sobre startup + active + recovery, por lo que el dibujo queda fijo a la frame data durante hitstop, pausa, replay y
   rollback.
4. **Un mod es solo datos, nunca código.** Una escena (`.tscn`) o un recurso de tema (`.tres`) puede llevar scripts, así que las
   escenas de personaje, las escenas de VFX y `theme.tres` solo se cargan desde `res://` (contenido que se distribuye con el juego).
   Desde `user://` solo se aceptan imágenes, `.ogg`/`.mp3`, fuentes, JSON y tablas de movimientos, con rutas confinadas a su carpeta.
5. **Localización con el texto en inglés como clave.** Godot traduce solo las etiquetas y botones cuyo texto coincide con una clave;
   los textos construidos en código usan `Localization.T/F`. Un texto sin traducción se ve en inglés.
6. **Herramientas que generan paquetes válidos** (`PoseBaker`, `StageBaker`), un visor frame a frame (`CharacterViewer`) y un validador
   con código de salida (`ContentCheck`, `tools/validate-content.sh`) que corre en el smoke de CI. Las carpetas que empiezan por `_`
   son plantillas: no se cargan salvo con `--fury-templates` y no se exportan.

## Consecuencias

- Añadir un personaje cuesta datos y arte; solo un gimmick nuevo requiere un módulo de simulación (C#).
- El formato del manifiesto es público: cambiarlo exige subir `schema`.
- Limitaciones aceptadas: capas de stage en 3D como tarjetas; una escena con `AnimationPlayer` se controla por `Seek` (sin pistas de
  audio ni de llamadas a métodos); `.wav` solo desde `res://`.
