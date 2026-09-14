using Fury.Core.Memory;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Deterministic fight state in Structure-of-Arrays layout over one arena slice
/// (ADR-002). Field-major ordering: each field is a contiguous run of per-fighter words,
/// then global words. The whole state serializes to a fixed <see cref="WordCount"/> int
/// span — the unit of snapshots, rollback saves and state hashes.
/// Screen convention: ground at Y=0, Y grows downward (Godot 2D), facing +1 = right.
/// </summary>
public sealed class FightState
{
    public const int FighterCount = 2;
    public const int FieldsPerFighter = 20;
    public const int PerFighterWords = FighterCount * FieldsPerFighter;
    public const int GlobalWords = 12;
    public const int WordCount = PerFighterWords + GlobalWords;

    // Field block offsets (each block holds FighterCount words, index = block + fighter).
    private const int OffX = 0;
    private const int OffY = 2;
    private const int OffVx = 4;
    private const int OffVy = 6;
    private const int OffHealth = 8;
    private const int OffExMeter = 10;
    private const int OffResonance = 12;
    private const int OffRage = 14;
    private const int OffStateId = 16;
    private const int OffStateFrame = 18;
    private const int OffFacing = 20;
    private const int OffMoveIndex = 22;
    private const int OffHitstop = 24;
    private const int OffHitstun = 26;
    private const int OffBlockstun = 28;
    private const int OffHasHit = 30;
    private const int OffComboCount = 32;
    private const int OffComboDamage = 34;
    private const int OffAirborne = 36;
    private const int OffCrouching = 38;

    private const int OffFrame = PerFighterWords;
    private const int OffRoundTimer = PerFighterWords + 1;
    private const int OffRoundState = PerFighterWords + 2;
    private const int OffWinner = PerFighterWords + 3;
    private const int OffRng = PerFighterWords + 4; // 8 ints reserved for future sim RNG snapshotting

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

    public int Frame => _data.Span[OffFrame];
    public int RoundTimerFrames => _data.Span[OffRoundTimer];

    /// <summary>Sets the round clock (initialized by the simulation at construction).</summary>
    public void SetRoundTimer(int frames) => _data.Span[OffRoundTimer] = System.Math.Max(0, frames);
    public int RoundState => _data.Span[OffRoundState]; // 0 fighting, 1 over
    public int Winner => _data.Span[OffWinner];         // -1 none/draw pending, 0/1 fighter

    public int GetHealth(int f) => _data.Span[OffHealth + f];
    public void SetHealth(int f, int v) => _data.Span[OffHealth + f] = System.Math.Max(0, v);

    public int GetExMeter(int f) => _data.Span[OffExMeter + f];
    public void SetExMeter(int f, int v) => _data.Span[OffExMeter + f] = System.Math.Clamp(v, 0, 300);

    public int GetResonance(int f) => _data.Span[OffResonance + f];
    public void SetResonance(int f, int v) => _data.Span[OffResonance + f] = System.Math.Clamp(v, 0, 100);

    public bool IsRaging(int f) => _data.Span[OffRage + f] != 0;
    public void SetRaging(int f, bool v) => _data.Span[OffRage + f] = v ? 1 : 0;

    public int GetStateId(int f) => _data.Span[OffStateId + f];
    public void SetStateId(int f, int id) { _data.Span[OffStateId + f] = id; _data.Span[OffStateFrame + f] = 0; }

    public int GetStateFrame(int f) => _data.Span[OffStateFrame + f];
    public void SetStateFrame(int f, int v) => _data.Span[OffStateFrame + f] = v;

    public int GetFacing(int f) => _data.Span[OffFacing + f];
    public void SetFacing(int f, int v) => _data.Span[OffFacing + f] = v >= 0 ? 1 : -1;

    public int GetMoveIndex(int f) => _data.Span[OffMoveIndex + f];
    public void SetMoveIndex(int f, int v) => _data.Span[OffMoveIndex + f] = v;

    public int GetHitstop(int f) => _data.Span[OffHitstop + f];
    public void SetHitstop(int f, int v) => _data.Span[OffHitstop + f] = v;

    public int GetHitstun(int f) => _data.Span[OffHitstun + f];
    public void SetHitstun(int f, int v) => _data.Span[OffHitstun + f] = System.Math.Max(0, v);

    public int GetBlockstun(int f) => _data.Span[OffBlockstun + f];
    public void SetBlockstun(int f, int v) => _data.Span[OffBlockstun + f] = System.Math.Max(0, v);

    public bool HasHitThisActive(int f) => _data.Span[OffHasHit + f] != 0;
    public void SetHasHit(int f, bool v) => _data.Span[OffHasHit + f] = v ? 1 : 0;

    public int GetComboCount(int f) => _data.Span[OffComboCount + f];
    public void SetComboCount(int f, int v) => _data.Span[OffComboCount + f] = v;

    public int GetComboDamage(int f) => _data.Span[OffComboDamage + f];
    public void SetComboDamage(int f, int v) => _data.Span[OffComboDamage + f] = v;

    public bool IsAirborne(int f) => _data.Span[OffAirborne + f] != 0;
    public void SetAirborne(int f, bool v) => _data.Span[OffAirborne + f] = v ? 1 : 0;

    public bool IsCrouching(int f) => _data.Span[OffCrouching + f] != 0;
    public void SetCrouching(int f, bool v) => _data.Span[OffCrouching + f] = v ? 1 : 0;

    public Fp GetX(int f) => Fp.FromRaw(_data.Span[OffX + f]);
    public void SetX(int f, Fp v) => _data.Span[OffX + f] = v.Raw;

    public Fp GetY(int f) => Fp.FromRaw(_data.Span[OffY + f]);
    public void SetY(int f, Fp v) => _data.Span[OffY + f] = v.Raw;

    public Fp GetVx(int f) => Fp.FromRaw(_data.Span[OffVx + f]);
    public void SetVx(int f, Fp v) => _data.Span[OffVx + f] = v.Raw;

    public Fp GetVy(int f) => Fp.FromRaw(_data.Span[OffVy + f]);
    public void SetVy(int f, Fp v) => _data.Span[OffVy + f] = v.Raw;

    public void TickFrame() => _data.Span[OffFrame]++;

    /// <summary>Advances one frozen or live frame of the round timer.</summary>
    public void TickRoundTimer()
    {
        if (_data.Span[OffRoundState] == 0 && _data.Span[OffRoundTimer] > 0) _data.Span[OffRoundTimer]--;
    }

    public void EndRound(int winner)
    {
        _data.Span[OffRoundState] = 1;
        _data.Span[OffWinner] = winner;
    }

    /// <summary>Serializes the full state (including RNG words) into <paramref name="dest"/>.</summary>
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
