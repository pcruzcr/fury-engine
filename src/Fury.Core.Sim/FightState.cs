using Fury.Core.Math;
using Fury.Core.Memory;

namespace Fury.Core.Sim;

/// <summary>
/// Deterministic fight state in Structure-of-Arrays layout over one arena slice (ADR-002).
/// Layout (every block is a contiguous run of int words):
/// [per-fighter fields, field-major: field f of fighter i is at f * 2 + i]
/// [globals][projectile pool][input rings]. The whole state serializes to a fixed <see cref="WordCount"/>
/// int span, the unit of snapshots, rollback saves and state hashes. Optional simulation modules (weapons,
/// transformations, tag teams...) append their own words AFTER this block through
/// <see cref="ISimModule"/> and are snapshotted by <see cref="FightSimulation"/>.
/// Screen convention: ground at Y = 0, Y grows downward (Godot 2D), facing +1 = right.
/// </summary>
public sealed class FightState
{
    public const int FighterCount = 2;
    public const int ProjectileSlots = 4;

    /// <summary>Per-fighter fields (word index = field * FighterCount + fighter).</summary>
    private enum Field
    {
        X, Y, Vx, Vy, Health, ExMeter, Resonance, Rage, StateId, StateFrame, Facing, MoveIndex,
        Hitstop, Hitstun, Blockstun, HasHit, ComboCount, ComboDamage, Airborne, Crouching,
        InputConsumed, Connect, Invuln, Count,
    }

    private enum Global
    {
        Frame, RoundTimer, RoundState, Winner, Count,
    }

    private enum ProjField
    {
        Active, X, Y, Vx, Life, MoveIndex, Owner, Count,
    }

    public const int FieldsPerFighter = (int)Field.Count;
    public const int PerFighterWords = FighterCount * FieldsPerFighter;
    public const int GlobalWords = (int)Global.Count;
    public const int ProjectileWords = ProjectileSlots * (int)ProjField.Count;
    public const int InputRingWords = FighterCount * (1 + InputBuffer.History); // head + ring per fighter
    public const int WordCount = PerFighterWords + GlobalWords + ProjectileWords + InputRingWords;

    private const int OffGlobal = PerFighterWords;
    private const int OffProjectiles = OffGlobal + GlobalWords;
    private const int OffInputRing = OffProjectiles + ProjectileWords;

    private readonly ArenaSlice<int> _data;

    public FightState(Arena arena)
    {
        _data = arena.Alloc<int>(WordCount);
        for (int f = 0; f < FighterCount; f++)
        {
            SetFacing(f, f == 0 ? 1 : -1);
            SetX(f, f == 0 ? Fp.FromRatio(-3, 1) : Fp.FromRatio(3, 1));
        }
    }

    private static int Idx(Field field, int f) => ((int)field * FighterCount) + f;

    private int Get(Field field, int f) => _data.Span[Idx(field, f)];

    private void Set(Field field, int f, int value) => _data.Span[Idx(field, f)] = value;

    // ------------------------------------------------------------------ globals

    public int Frame => _data.Span[OffGlobal + (int)Global.Frame];

    public int RoundTimerFrames => _data.Span[OffGlobal + (int)Global.RoundTimer];

    /// <summary>Sets the round clock (initialized by the simulation at construction).</summary>
    public void SetRoundTimer(int frames) => _data.Span[OffGlobal + (int)Global.RoundTimer] = System.Math.Max(0, frames);

    /// <summary>0 fighting, 1 over.</summary>
    public int RoundState => _data.Span[OffGlobal + (int)Global.RoundState];

    /// <summary>-1 none / draw, 0 / 1 = winning fighter.</summary>
    public int Winner => _data.Span[OffGlobal + (int)Global.Winner];

    public void TickFrame() => _data.Span[OffGlobal + (int)Global.Frame]++;

    /// <summary>Advances one frame of the round timer while the round is live.</summary>
    public void TickRoundTimer()
    {
        var span = _data.Span;
        if (span[OffGlobal + (int)Global.RoundState] == 0 && span[OffGlobal + (int)Global.RoundTimer] > 0) span[OffGlobal + (int)Global.RoundTimer]--;
    }

    public void EndRound(int winner)
    {
        _data.Span[OffGlobal + (int)Global.RoundState] = 1;
        _data.Span[OffGlobal + (int)Global.Winner] = winner;
    }

    // ------------------------------------------------------------------ per-fighter accessors

    public int GetHealth(int f) => Get(Field.Health, f);

    public void SetHealth(int f, int v) => Set(Field.Health, f, System.Math.Max(0, v));

    public int GetExMeter(int f) => Get(Field.ExMeter, f);

    public void SetExMeter(int f, int v) => Set(Field.ExMeter, f, System.Math.Clamp(v, 0, 300));

    public int GetResonance(int f) => Get(Field.Resonance, f);

    public void SetResonance(int f, int v) => Set(Field.Resonance, f, System.Math.Clamp(v, 0, 100));

    public bool IsRaging(int f) => Get(Field.Rage, f) != 0;

    public void SetRaging(int f, bool v) => Set(Field.Rage, f, v ? 1 : 0);

    public int GetStateId(int f) => Get(Field.StateId, f);

    public void SetStateId(int f, int id)
    {
        Set(Field.StateId, f, id);
        Set(Field.StateFrame, f, 0);
    }

    public int GetStateFrame(int f) => Get(Field.StateFrame, f);

    public void SetStateFrame(int f, int v) => Set(Field.StateFrame, f, v);

    public int GetFacing(int f) => Get(Field.Facing, f);

    public void SetFacing(int f, int v) => Set(Field.Facing, f, v >= 0 ? 1 : -1);

    public int GetMoveIndex(int f) => Get(Field.MoveIndex, f);

    public void SetMoveIndex(int f, int v) => Set(Field.MoveIndex, f, v);

    public int GetHitstop(int f) => Get(Field.Hitstop, f);

    public void SetHitstop(int f, int v) => Set(Field.Hitstop, f, v);

    public int GetHitstun(int f) => Get(Field.Hitstun, f);

    public void SetHitstun(int f, int v) => Set(Field.Hitstun, f, System.Math.Max(0, v));

    public int GetBlockstun(int f) => Get(Field.Blockstun, f);

    public void SetBlockstun(int f, int v) => Set(Field.Blockstun, f, System.Math.Max(0, v));

    public bool HasHitThisActive(int f) => Get(Field.HasHit, f) != 0;

    public void SetHasHit(int f, bool v) => Set(Field.HasHit, f, v ? 1 : 0);

    public int GetComboCount(int f) => Get(Field.ComboCount, f);

    public void SetComboCount(int f, int v) => Set(Field.ComboCount, f, v);

    public int GetComboDamage(int f) => Get(Field.ComboDamage, f);

    public void SetComboDamage(int f, int v) => Set(Field.ComboDamage, f, v);

    public bool IsAirborne(int f) => Get(Field.Airborne, f) != 0;

    public void SetAirborne(int f, bool v) => Set(Field.Airborne, f, v ? 1 : 0);

    public bool IsCrouching(int f) => Get(Field.Crouching, f) != 0;

    public void SetCrouching(int f, bool v) => Set(Field.Crouching, f, v ? 1 : 0);

    /// <summary>Frame number up to which button presses were already used by a move (input buffering).</summary>
    public int GetInputConsumed(int f) => Get(Field.InputConsumed, f);

    public void SetInputConsumed(int f, int frame) => Set(Field.InputConsumed, f, frame);

    /// <summary>What the fighter's current attack connected with: 0 nothing, 1 hit, 2 blocked (enables cancels).</summary>
    public int GetConnect(int f) => Get(Field.Connect, f);

    public void SetConnect(int f, int v) => Set(Field.Connect, f, v);

    /// <summary>Frames of remaining invulnerability (backdash, wake-up).</summary>
    public int GetInvuln(int f) => Get(Field.Invuln, f);

    public void SetInvuln(int f, int v) => Set(Field.Invuln, f, System.Math.Max(0, v));

    public Fp GetX(int f) => Fp.FromRaw(Get(Field.X, f));

    public void SetX(int f, Fp v) => Set(Field.X, f, v.Raw);

    public Fp GetY(int f) => Fp.FromRaw(Get(Field.Y, f));

    public void SetY(int f, Fp v) => Set(Field.Y, f, v.Raw);

    public Fp GetVx(int f) => Fp.FromRaw(Get(Field.Vx, f));

    public void SetVx(int f, Fp v) => Set(Field.Vx, f, v.Raw);

    public Fp GetVy(int f) => Fp.FromRaw(Get(Field.Vy, f));

    public void SetVy(int f, Fp v) => Set(Field.Vy, f, v.Raw);

    // ------------------------------------------------------------------ projectiles

    private static int ProjIdx(int slot, ProjField field) => OffProjectiles + (slot * (int)ProjField.Count) + (int)field;

    public bool ProjectileActive(int slot) => _data.Span[ProjIdx(slot, ProjField.Active)] != 0;

    public Fp ProjectileX(int slot) => Fp.FromRaw(_data.Span[ProjIdx(slot, ProjField.X)]);

    public Fp ProjectileY(int slot) => Fp.FromRaw(_data.Span[ProjIdx(slot, ProjField.Y)]);

    public Fp ProjectileVx(int slot) => Fp.FromRaw(_data.Span[ProjIdx(slot, ProjField.Vx)]);

    public int ProjectileLife(int slot) => _data.Span[ProjIdx(slot, ProjField.Life)];

    public int ProjectileMove(int slot) => _data.Span[ProjIdx(slot, ProjField.MoveIndex)];

    public int ProjectileOwner(int slot) => _data.Span[ProjIdx(slot, ProjField.Owner)];

    /// <summary>Spawns a projectile in the first free slot; returns false when the pool is full.</summary>
    public bool TrySpawnProjectile(int owner, int moveIndex, Fp x, Fp y, Fp vx, int life)
    {
        var span = _data.Span;
        for (int s = 0; s < ProjectileSlots; s++)
        {
            if (span[ProjIdx(s, ProjField.Active)] != 0) continue;
            span[ProjIdx(s, ProjField.Active)] = 1;
            span[ProjIdx(s, ProjField.X)] = x.Raw;
            span[ProjIdx(s, ProjField.Y)] = y.Raw;
            span[ProjIdx(s, ProjField.Vx)] = vx.Raw;
            span[ProjIdx(s, ProjField.Life)] = life;
            span[ProjIdx(s, ProjField.MoveIndex)] = moveIndex;
            span[ProjIdx(s, ProjField.Owner)] = owner;
            return true;
        }
        return false;
    }

    public void SetProjectileX(int slot, Fp x) => _data.Span[ProjIdx(slot, ProjField.X)] = x.Raw;

    public void SetProjectileLife(int slot, int life) => _data.Span[ProjIdx(slot, ProjField.Life)] = life;

    public void DespawnProjectile(int slot) => _data.Span[ProjIdx(slot, ProjField.Active)] = 0;

    /// <summary>Number of live projectiles owned by <paramref name="owner"/>.</summary>
    public int ProjectileCount(int owner)
    {
        int n = 0;
        for (int s = 0; s < ProjectileSlots; s++)
        {
            if (ProjectileActive(s) && ProjectileOwner(s) == owner) n++;
        }
        return n;
    }

    // ------------------------------------------------------------------ input rings / snapshots

    /// <summary>Snapshots one fighter's input ring into the state. Rollback contract: the
    /// buffer is part of the savestate, or edge detection diverges on resimulation.</summary>
    public void SetInputRing(int f, int head, ReadOnlySpan<int> ring)
    {
        int b = OffInputRing + (f * (1 + InputBuffer.History));
        _data.Span[b] = head;
        ring[..InputBuffer.History].CopyTo(_data.Span[(b + 1)..]);
    }

    /// <summary>Restores one fighter's input ring from the state.</summary>
    public void GetInputRing(int f, Span<int> ringOut, out int head)
    {
        int b = OffInputRing + (f * (1 + InputBuffer.History));
        head = _data.Span[b];
        _data.Span.Slice(b + 1, InputBuffer.History).CopyTo(ringOut);
    }

    /// <summary>Serializes the full state (including input rings) into <paramref name="dest"/>.</summary>
    public void Serialize(Span<int> dest)
    {
        if (dest.Length < WordCount) throw new ArgumentException("Snapshot destination too small.");
        _data.Span[..WordCount].CopyTo(dest);
    }

    /// <summary>Restores a full state snapshot (rollback save/restore contract).</summary>
    public void Deserialize(ReadOnlySpan<int> source)
    {
        if (source.Length < WordCount) throw new ArgumentException("Snapshot source too small.");
        source[..WordCount].CopyTo(_data.Span);
    }
}
