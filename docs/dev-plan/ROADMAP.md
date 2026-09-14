# Fury Engine — Roadmap canónico

Fuente: GDD Dual Fury v8.0 (respaldo externo) + ADR-001..006. El motor es un esclavo de
renderizado; la sim es una caja negra determinista, paralela y criptográficamente segura.

## Fases

### F0 — Fundación ✅ (commit `a5d2594`)
Solución hexagonal, Fp Q16.16, xoshiro256**, xxHash64, CI multi-OS, ADR-001.

### F0.5 — Pilares DOD ✅ (ADRs 002–006)
- `Fury.Core.Memory.Arena`: bump allocator zero-GC (ADR-002).
- `AabbSimd`: broadphase SIMD entero Vector128<int>, bit-exacto x86/ARM (ADR-002).
- `SnapshotDiffer`: compresión delta de estado (ADR-004).
- `PacketAuthenticator` + `PacketReplayGuard`: HMAC-SHA256 + anti-replay (ADR-004).
- `BinaryMoveTable`: tablas binarias FMT1 LE con validación estructural (ADR-003).

### F1 — Sim de combate DOD
Pipeline de 9 fases sobre arena: estado SoA en arena (frame 0), FSM data-driven leyendo
`BinaryMoveTable`, hitboxes `FpVec2`/AABB con filtro SIMD + resolución exacta Fp,
Glen/Kira portados desde el backup, DamageScaler, HitstopTable, EX/Resonancia/Rage.
**Gate:** golden hashes nuevos; 0 asignaciones tras frame 0 (budget en CI).

### F2 — Rollback AAA
Save/restore O(1) del estado en arena, prediction, resimulación ≤8 frames, delay hiding,
`SnapshotDiffer` en tránsito, handshake con intercambio de claves (HKDF), loopback harness
multi-instancia. **Gate:** jitter inyectado produce estado final idéntico al lockstep.

### F3 — Presentación extrema (ADR-005)
`FightDirector` + RenderingServer bypass, CameraRig, VFX pooling GPUParticles2D, audio con
crossfades anti-corrección, HUD, compute shaders de escenario, input Platform/ gamepads.
**Gate:** demo jugable headless `--auto-test` + visual; cero GC por frame de pelea.

### F4 — Observabilidad y seguridad
`IFightTracer` en vivo (rollbacks/frame, ping, sim ms), eventos estructurados, replays
firmados con cadena de hashes, mapas de calor de combate opt-in (ADR-006), fuzzing de
parsers de red/datos. **Gate:** suite de tamper (mutar 1 byte → rechazo) en verde.

### F5 — Certificación headless + multiplataforma
Harness de combates ciegos IA-vs-IA en CI (ADR-006), AOT probe, presets Linux/macOS/Android/
iOS, Steam Deck. **Gate:** Windows + Linux exportados pasando el auto-test embebido.

### F6 — Estabilización y release
Perfilado de presupuestos (16.67 ms), documentación de API del framework, tag v1.0-fury.
