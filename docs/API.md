# Fury Engine — API Reference (v2)

Resumen de las APIs públicas. Las decisiones están en `docs/adr/`; los límites conocidos en ADR-008.

## Fury.Core.Math

- `Fp` — Q16.16. `FromInt`, `FromRatio` (usa `long`: sin desbordes), `FromRaw`, `+ - * /`, `Sqrt` (raíz entera exacta),
  `Sin/Cos/Tan` (reducción de rango + polinomio), `Abs/Min/Max/Clamp`, `FloorInt`, `TruncInt`, `ToFloat()` (solo presentación).
- `FpVec2`, `FpVec3` — vectores sobre `Fp`.
- `Xoshiro256StarStar` — RNG con `GetState/SetState` para snapshots.
- `FpHash` — `HashInts` (sin alocaciones en little-endian), `HashBytes`, `Combine`, `HashName`.

## Fury.Core.Data

- `MoveEntry` (FMT2): 10 campos posicionales (timings, daño, hitstop, pushback, caja) + `HitstunFrames`, `BlockstunFrames`,
  `Traits` (`MoveTraits`), `Level` (`HitLevel`), `Command`, `Category`, `CancelMask`, `LaunchVyRaw`, `MeterCost`,
  `Priority`, `ProjectileSpeedRaw/Lifetime/SpawnFrame`. `EffectiveHitstun/Blockstun` reproducen la fórmula heredada con 0.
- `BinaryMoveTable.Parse(bytes)` lee FMT1 y FMT2; `BinaryMoveTableWriter.Write` emite FMT2; `ContentHash`.
- `CharacterConfig(Id, MaxHealth, WalkForwardRaw, Moves)` + estadísticas `WalkBackSpeed`, `DashSpeed/Frames`,
  `BackdashSpeed/Frames/InvulnFrames`; `Fingerprint()`. `CharacterRoster.Create(id)`.

## Fury.Core.Sim

```csharp
var sim = new FightSimulation(p1, p2, cfg: null, tracer: null, modules: [new WeaponModule(0, weapons), ...]);
sim.Step(input1, input2);          // un tick determinista, sin alocaciones
ulong hash = sim.SnapshotHash;     // base + módulos
sim.Restore(snapshot);             // rollback; sim.SnapshotWords = tamaño del snapshot
```

- `InputState(bits)` / `InputButton` (direcciones, L/M/H, Special, Block, Parry, Macro1-4).
- `InputBuffer` (32 frames, SOCD), `MotionRecognizer`, `MoveCommand.Pack(buttons, motion, stance)`, `CommandResolver`.
- `FightState` — layout SoA: campos por luchador, globales, pool de 4 proyectiles, anillos de input.
- `FightConfig` — reglas del escenario; `Fingerprint()`.
- `SimEventId` + `SimEventPayload.Pack/Unpack`; los eventos se emiten una vez por frame aunque se resimule.
- Módulos: `ISimModule` (estado en el snapshot) y `SimModule` (hooks: `DamagePercent`, `FlatDamage`, `WalkSpeedPercent`,
  `ReachBonusRaw`, `TryGetSupport`, `BounceVyRaw`, `GroundPush`, `ActiveCharacter`, `TrySubstituteKo`, `OnHit`, `OnThrow`).
  `WeaponModule`, `ArcanaModule`, `TransformationModule`, `StageModule`, `TagTeamModule`, `FinisherModule`, `HoldModule`.
- IA: `CpuBrain(seed, aggression)`, `DummyBrain`, `ScriptedInputProvider`; `IInputProvider.Poll(frame, in FightView)`.

## Fury.Core.Rollback

```csharp
var runner = new RollbackRunner(sim, localInput: f => ..., inputDelay: 2, localSlot: 0, maxPrediction: 8);
runner.TryAdvance();                 // respeta la barrera de predicción
runner.DeliverRemote(frame, input);  // resimula si la predicción fue errónea
runner.ConfirmedHash(frame);         // huella segura para comparar con el rival
```

## Fury.Core.Net

```csharp
var (keys, peer) = UdpHandshake.Run(localPort, host, remotePort, characterId, cfgHash, charHash, matchToken, timeout);
using var udp = new UdpTransport(localPort, host, remotePort, keys);
var net = new NetplaySession(sim, udp, localInput, localSlot, inputDelay: 2);
bool advanced = net.Tick();          // entrega, avanza, envía ventana redundante; net.Status: Running/Stalled/Desynced
```

`INetTransport` (ventanas de inputs + ack + huella), `RemoteInputBuffer` (acotado), `RttEstimator`, `InputPacket`,
`SessionCrypto/SessionKeys` (claves direccionales), `PacketAuthenticator`, `LoopbackPair` (red simulada determinista).

## Fury.Core.Replay

`ReplayRecorder(sim, ReplayHeader.ForSimulation(sim))` → `Step(p1, p2)` → `Finish()`; `ReplayFile.ToBytes(macKey?)`,
`ReplayFile.Parse(bytes, macKey?)`, `Verify(() => new FightSimulation(...))`.

## Fury.Core.Authoring / Observability

`MoveTableDocument` (edición validada de tablas), `StageData` + `StageSerializer` (JSON sin reflexión), `CombatHeatmap`.

## Fury.Game (Godot)

- `FightDirector` — flujo de la sesión (intro, rondas, modos). `GameSession`, `GameSettings`, `InputAdapter`.
- `Presentation/` — `FightPresentation` (2D / 2.5D / 3D), `WorldProjection`, `Hud`, `AudioManager`, `ParticlePool2D/3D`,
  `FightEventQueue` (tracer).
- `Net/OnlineMatch`, `UI/*` (menús), `Tools/*` (editores de frame data y stages).

## Presupuestos verificados

| Métrica | Valor | Verificación |
|---|---|---|
| Coste por frame (2 luchadores) | ~7-15 µs | `cert` |
| Alocaciones por frame | 0 B | `FightRulesV2Tests.NoHeapAllocations...`, `BudgetTests` |
| Determinismo | bit a bit entre plataformas | golden + `cert/expected_hashes.txt` en CI |
| Rollback | convergencia con jitter y pérdida | `NetplayTests`, `cert`, `godot-online-smoke.sh` |
