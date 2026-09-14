# ADR-002: Núcleo de simulación DOD — arena zero-GC, SoA y SIMD entero

- **Estado:** Aceptado (2026-09-14)

## Contexto

El motor debe extraer el máximo de CPU/Godot sin violar el timestep fijo de 16.67 ms ni la
determinismo bit-exacto entre x86 y ARM. El GC de .NET y el acceso a memoria dispersa son
las dos fuentes principales de jitter en framewors de peleas.

## Decisión

1. **Arena de memoria (zero-GC):** toda la memoria de pelea se asigna en un bump allocator
   (`Fury.Core.Memory.Arena`) sobre un bloque contiguo reservado en el frame 0. Reset por
   frame para scratch transitorio. El estado vivo usa slices (`ArenaSlice<T>`) sobre
   `byte[]` con `where T : unmanaged` — GC-safe, AOT-safe, sin pinning.
2. **SoA (Structure of Arrays):** las colecciones calientes (hitboxes, cuerpos, partículas
   del sim) se organizan por campo, no por entidad, para maximizar aciertos L1/L2 y habilitar
   vectorización.
3. **SIMD entero, no float:** el broadphase AABB usa `System.Runtime.Intrinsics.Vector128<int>`
   sobre los bits crudos Q16.16. Las operaciones enteras SIMD son bit-idénticas en toda
   arquitectura, por lo que la detección por lotes (4 cajas por instrucción) NO rompe el
   determinismo. Regla de blindaje: el SIMD actúa como filtro conservador; cualquier caso
   limítrofe se resuelve con la prueba exacta en Fp.
4. **Prohibiciones:** `float`/`double` en hot path del sim; asignaciones dentro del tick;
   colecciones gestionadas en estado vivo del sim.

## Consecuencias

- El presupuesto de alocaciones por frame es verificable en CI (0 bytes tras el frame 0).
- Las herramientas de profiling estándar siguen funcionando; el arena reporta uso por
  `UsedBytes` para observabilidad.
