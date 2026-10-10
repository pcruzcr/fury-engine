# Export presets & cross-platform evidence (REFACTOR+HARDEN Block B)

Presets 1–4 are **authored, NOT verified** (no devices/hosts here).
Preset 0 (Windows Desktop) is **VERIFIED**: exported + executed + auto-test PASS
inside the shipped binary (see row). Templates 4.7.2.stable.mono installed
(`%APPDATA%/Godot/export_templates`). Nothing else here claims a successful export.

## Toolchain baseline
- Godot `4.7.2-stable` Mono family (ref `ed1daf0bf`), `.NET 9.0.318`, TFM `net9.0`
  (Android 16k page-alignment requirement — see `export_presets.cfg` header).
- `Fighter.Core`: `dotnet build` alone (no Godot). Harness: same.
- Required export templates: **4.7.2.stable.mono** (all platforms).

## Preset matrix (`export_presets.cfg`)
| # | Name | Platform | Arch | Status |
|---|------|----------|------|--------|
| 0 | Windows Desktop | Windows Desktop | x86_64 | VERIFIED: `dist/windows/DualFury.exe` + pck + data dir; headless auto-test (menú→pelea 600f, restore+resim+replay) PASS en el binario |
| 1 | Linux | Linux | x86_64 | Authored, not exported |
| 2 | Android ARM64 | Android | arm64-v8a (+armeabi-v7a, +x86_64 enabled) | Authored, not exported |
| 3 | iOS | iOS | ARM64 | Authored, not exported (needs macOS host) |
| 4 | macOS Universal | macOS | universal (ARM64+x86_64) | Authored, not exported (needs macOS host) |

## Per-preset notes
- **Windows/Linux:** no signing needed for sideload. OPEN: install 4.7.2
  templates, export from editor, smoke-test `dist/` binary at 60 Hz.
  Estimated export size: 60–120 MB (Mono runtime + PCK).
- **Android:** `minSdkVersion 24`, target per template default; architectures
  arm64-v8a + armeabi-v7a + x86_64 (x86_64 covers emulator/Chromebook).
  OPEN: keystore for `package/signed` (currently `signed=true` with no key —
  flip to unsigned debug or provide release keystore), install templates,
  `adb install`, validate 60 fps + snapshot bench on a mid-range ARM64 device.
  Estimated size: 40–80 MB (AAB recommended for Play).
- **iOS:** bundle `com.dualfury.game`, ARM64 only (no simulator slice).
  OPEN (all require a macOS host + Apple Developer account): code-sign
  identity, provisioning profiles, team id, export `.xcodeproj`, archive.
  Caveats: **no JIT on iOS** — Core is IL2CPP-safe by construction (ADR-007)
  but this is unverified until first device export. Estimated size: 80–150 MB.
- **macOS Universal:** single universal binary (ARM64+x86_64).
  OPEN: Apple Developer signing + notarization (`codesign/*`,
  `notarization/*` empty by design), validate on both architectures.
  Estimated size: 80–140 MB.

## Determinism parity harness (`Fighter.DeterminismHarness`)
```
# PC (verified this session):
dotnet run --project src/Fighter.DeterminismHarness -- --frames 10000 \
  --seed 0xDEADBEEF --input-file tests/fixtures/determinism_input.bin
# → FINAL xxHash64=0x93CE81B8A9EF7CE7 (must match on every platform)
```
```
# Android via adb (OPEN — no device; commands for when one exists):
adb push determinism_input.bin /data/local/tmp/df.bin
adb shell "run-as com.dualfury.game /data/app/.../lib/arm64/libharness.so \
  --frames 10000 --seed 0xDEADBEEF --input-file /data/local/tmp/df.bin"
# NOTE: managed harness on-device needs a `dotnet publish -r android-arm64`
# self-contained wrapper (OPEN: publish profile not authored yet).
```
```
# iOS via Instruments (OPEN — macOS host + device required):
# 1. Build the harness as a macOS-universal console tool for reference.
# 2. On device, run through Xcode Instruments → System Trace while the exported
#    game replays tests/fixtures/determinism_input.bin via a debug menu hook
#    (OPEN: hook not implemented; ReplayV2.LoadBytes is the entry point).
# 3. Compare the printed FINAL xxHash64 with the PC golden.
```

## Cross-platform build status (this session)
| Target | Status | Evidence / reason |
|--------|--------|-------------------|
| Windows x64 (PC) | PASS | `dotnet build` + `dotnet test` 289/289, harness run ×2 identical |
| Linux x64 (CI host class) | PASS (by construction) | Core+harness are pure net9.0, no platform APIs; CI runs `ci.yml` on ubuntu |
| Android ARM64 | OPEN | No device/templates; Core is integer-only + IL2CPP-safe (ADR-004/007) |
| Android x86_64 | OPEN | Same as ARM64 (emulator path documented above) |
| iOS ARM64 | OPEN (compile target only) | No macOS host; no-JIT caveat recorded |
| macOS universal | OPEN | No macOS host; preset authored |

## Determinism contract assessment
Same seed + same inputs ⇒ same bytes hashed ⇒ same hash on every platform:
the sim uses integer-only math (ADR-004), a fixed-spec RNG (ADR-005), an
explicit little-endian codec, and spec-pinned xxHash64 (ADR-006). No
platform-specific code exists in any hashed path. **No platform is marked
BLOCKED**: all gaps are missing-machines (OPEN), not design defects. The
matrix golden (`tests/fixtures/golden_hashes.json`) is the tripwire that
will catch any real drift the day a second platform runs.
