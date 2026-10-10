# 00 — Visión, Stack y Reglas Fijas

## Elevator pitch
Fighting 2.5D donde **una sola Barra EX financia Rage ofensivo O Resonancia defensiva, nunca ambas**. La cosmología (Vacío vs Luz) justifica la mecánica. El jefe final construyó el torneo. El NPC del Hub era el protagonista enmascarado.

## Ficha producto
- **Título:** DUAL FURY · **Género:** Fighting 2.5D competitivo + single-player profundo
- **Launch:** 8 activos (3 inicio + 5 desbloqueables) + 6 ocultos + Num Ba (100%) + Nad'ian (101%) = 16
- **Plataformas objetivo:** PC Steam → PS5 / Xbox Series / Switch vía porteo (Godot no exporta nativo a consola)
- **Modelo:** Premium $14.99 + DLC cosmético/narrativo. **Regla fija:** DLC jugable gratis en ranked si el oponente lo tiene
- **DLC:** DLC1 $4.99 Mes19 (2 pj + stage + narrativa) · DLC2 $7.99 Mes23+ (Torres Secretas como modo + 2 pj)
- **Presupuesto:** $80–150k USD / 15–18 meses equipo LATAM 6–8 personas

## Stack fijado (análisis changelog 4.7.2)
- **Godot 4.7.2-stable Mono** (commit `ed1daf0bf`, 18-ago-2026) + **.NET SDK 9**, TFM `net9.0` (obligatorio Android 16k Play Store, `net8.0` resto)
- **C#:** FSM, hit detection, AABB, RingBuffer[8], CommandNormalResolver, Energy, Snapshot, rollback
- **GDScript:** UI/HUD/progresión/audio, Behavior Trees IA (solo emiten inputs al RingBuffer, nunca tocan FSM), tools editor
- Claves 4.7 a usar: `VirtualJoystick` (mobile), HDR + `AreaLight3D` (look PS5-like solo tier PC), `DrawableTexture2D`, `Perfetto` tracing Android, downloader de templates por plataforma, `Tween.tween_await()` + `Control offset_transform` (cinemáticas/UI)
- Límites: C# experimental en Android/iOS, **sin Web con C#**, upgrades solo `4.7.x` mantenimiento durante MVP

## Reglas fijas de diseño
1. 60 FPS fijo, fixed timestep, **nunca delta en sim**. AABB propio `Vector2I` (sin `CharacterBody2D`, sin float drift ARM↔x86)
2. Hitstop es **simulación**, no presentación. Sin hitstop no hay MVP
3. Parry es el único nodo que alimenta EX **y** Resonancia a la vez
4. Ares **exclusivos Modo Torre** (no Arcade/Historia jugables, no Boss Rush normal)
5. Frame data del programador **antes** que arte. Sin datos aprobados no se anima
6. GDD congelado Mes 3. Go/No-Go Semana 4 del MVP
