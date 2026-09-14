# ADR-005: Presentación extrema — servidores de Godot, compute y audio interpolado

- **Estado:** Aceptado (2026-09-14)

## Contexto

La capa Godot debe explotar el motor al máximo sin tocar la sim. Los nodos de alto nivel
implican coste jerárquico por frame; el rollback necesita correcciones visuales y de audio
sin estallidos perceptibles.

## Decisión

1. **Bypass del SceneTree para el render de pelea:** cuando la sim emite el evento de fin de
   frame, la presentación habla directamente con `RenderingServer` (canvas items, polígonos,
   interpolación de posiciones) en lugar de actualizar propiedades de nodos. El árbol de
   escenas queda para UI y estados de aplicación (menú, resultados).
2. **Shaders de cómputo para escenario:** zonas dinámicas (p. ej. Zonas de Orden, distorsión
   del Plano Gris) se resuelven en GPU con compute shaders; su salida es cosmética y jamás
   alimenta a la sim (pureza).
3. **Interpolación temporal de audio:** ante corrección de rollback (fase 4), los buses
   aplican crossfades instantáneos en vez de cortes — el canal de audio se gestiona junto al
   canal visual interpolado.
4. `physics_interpolation=true` + render a 60 Hz de sim: posiciones Fp → float SOLO en la
   frontera de presentación, con conversión única y centralizada.

## Consecuencias

- `FightDirector` y subsistemas (CameraRig, Render, Vfx, Audio, Hud) sustituyen al SimRunner
  monolítico del proyecto anterior.
- El audio procedural (SfxSynth) se mantiene como generador de assets en runtime; los buses
  y tweens viven en `Fury.Game/Audio`.
