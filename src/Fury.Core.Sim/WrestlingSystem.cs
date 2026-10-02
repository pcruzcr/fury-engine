using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Wrestling/throw system for grappling fighters (Dead or Alive 2D, Rumble Roses).
/// Manages holds, throws, counters, and chain throws.
/// All state is deterministic and serializable.
/// </summary>
public sealed class WrestlingSystem
{
    public const int MaxHolds = 8;

    public enum HoldType : byte
    {
        None = 0,
        FrontHold = 1,
        BackHold = 2,
        SideHold = 3,
        AerialHold = 4,
        GroundHold = 5
    }

    public enum HoldState : byte
    {
        Inactive = 0,
        Active = 1,
        Escaping = 2,
        Countering = 3
    }

    public enum ThrowDirection : byte
    {
        Forward = 0,
        Back = 1,
        Left = 2,
        Right = 3,
        Up = 4,
        Down = 5
    }

    private readonly HoldData[] _holds;

    public WrestlingSystem(FightConfig cfg)
    {
        _ = cfg;
        _holds = new HoldData[MaxHolds];
        for (int i = 0; i < MaxHolds; i++)
        {
            _holds[i] = new HoldData();
        }
    }

    public int Count { get; private set; }

    /// <summary>Dynamic words per configured hold in a snapshot.</summary>
    public const int WordsPerSlot = 5;

    /// <summary>Number of snapshot words for the holds configured so far.</summary>
    public int StateWords => Count * WordsPerSlot;

    /// <summary>Writes the mutable state of every configured hold (rollback savestate).</summary>
    public void ExportState(Span<int> dest)
    {
        for (int i = 0; i < Count; i++)
        {
            int o = i * WordsPerSlot;
            dest[o] = (int)_holds[i].State;
            dest[o + 1] = _holds[i].Attacker;
            dest[o + 2] = _holds[i].Defender;
            dest[o + 3] = _holds[i].EscapeTimer;
            dest[o + 4] = _holds[i].EscapeProgress;
        }
    }

    /// <summary>Restores state written by <see cref="ExportState"/>.</summary>
    public void ImportState(ReadOnlySpan<int> source)
    {
        for (int i = 0; i < Count; i++)
        {
            int o = i * WordsPerSlot;
            ref var item = ref _holds[i];
            item.State = (HoldState)source[o];
            item.Attacker = source[o + 1];
            item.Defender = source[o + 2];
            item.EscapeTimer = source[o + 3];
            item.EscapeProgress = source[o + 4];
        }
    }

    public void AddHold(in HoldData data)
    {
        if (Count >= MaxHolds) return;
        _holds[Count] = data with { State = HoldState.Inactive };
        Count++;
    }

    public void Clear()
    {
        Count = 0;
        for (int i = 0; i < MaxHolds; i++)
        {
            _holds[i] = new HoldData();
        }
    }

    public void Update()
    {
        for (int i = 0; i < Count; i++)
        {
            ref var h = ref _holds[i];
            if (h.State == HoldState.Active)
            {
                h.EscapeTimer--;
                if (h.EscapeTimer <= 0)
                {
                    h.State = HoldState.Inactive;
                }
            }
            else if (h.State == HoldState.Escaping)
            {
                h.EscapeProgress++;
                if (h.EscapeProgress >= h.EscapeThreshold)
                {
                    h.State = HoldState.Inactive;
                }
            }
            else if (h.State == HoldState.Countering)
            {
                // Counter window expires back to neutral (was previously stuck forever).
                h.EscapeTimer--;
                if (h.EscapeTimer <= 0)
                {
                    h.State = HoldState.Inactive;
                }
            }
        }
    }

    /// <summary>
    /// Attempts to initiate a hold on an opponent.
    /// </summary>
    public bool TryInitiateHold(int holdIndex, int attacker, int defender)
    {
        if (holdIndex < 0 || holdIndex >= Count) return false;
        ref var h = ref _holds[holdIndex];
        if (h.State != HoldState.Inactive) return false;

        h.State = HoldState.Active;
        h.Attacker = attacker;
        h.Defender = defender;
        h.EscapeTimer = h.MaxEscapeTime;
        h.EscapeProgress = 0;
        return true;
    }

    /// <summary>
    /// Attempts to escape an active hold.
    /// </summary>
    public bool TryEscape(int holdIndex)
    {
        if (holdIndex < 0 || holdIndex >= Count) return false;
        ref var h = ref _holds[holdIndex];
        if (h.State != HoldState.Active) return false;

        h.State = HoldState.Escaping;
        h.EscapeProgress = 0;
        return true;
    }

    /// <summary>
    /// Attempts to counter a hold.
    /// </summary>
    public bool TryCounter(int holdIndex)
    {
        if (holdIndex < 0 || holdIndex >= Count) return false;
        ref var h = ref _holds[holdIndex];
        if (h.State != HoldState.Active) return false;

        h.State = HoldState.Countering;
        h.EscapeTimer = h.ChainWindow > 0 ? h.ChainWindow : 30;
        return true;
    }

    /// <summary>Counts one escape attempt (a button mash) against an active hold.</summary>
    public void Mash(int holdIndex)
    {
        if (holdIndex < 0 || holdIndex >= Count) return;
        ref var h = ref _holds[holdIndex];
        if (h.State == HoldState.Active) h.EscapeProgress++;
    }

    /// <summary>Ends a hold immediately (escaped, finished or cancelled).</summary>
    public void Release(int holdIndex)
    {
        if (holdIndex < 0 || holdIndex >= Count) return;
        _holds[holdIndex].State = HoldState.Inactive;
    }

    /// <summary>
    /// Gets throw damage for a hold.
    /// </summary>
    public int GetThrowDamage(int holdIndex)
    {
        if (holdIndex < 0 || holdIndex >= Count) return 0;
        ref var h = ref _holds[holdIndex];
        if (h.State != HoldState.Active) return 0;
        return h.Damage;
    }

    /// <summary>
    /// Gets throw direction.
    /// </summary>
    public ThrowDirection GetThrowDirection(int holdIndex)
    {
        if (holdIndex < 0 || holdIndex >= Count) return ThrowDirection.Forward;
        return _holds[holdIndex].Direction;
    }

    public HoldData GetHold(int index)
    {
        if (index < 0 || index >= Count) return new HoldData();
        return _holds[index];
    }

    /// <summary>
    /// Serializable hold data.
    /// </summary>
    public record struct HoldData
    {
        public HoldType Type { get; init; }
        public HoldState State { get; set; }
        public ThrowDirection Direction { get; init; }
        public int Damage { get; init; }
        public int MaxEscapeTime { get; init; }
        public int EscapeThreshold { get; init; }
        public int Attacker { get; set; }
        public int Defender { get; set; }
        public int EscapeTimer { get; set; }
        public int EscapeProgress { get; set; }
        public bool IsChainThrow { get; init; }
        public int ChainWindow { get; init; }

        public HoldData()
        {
            Type = HoldType.FrontHold;
            State = HoldState.Inactive;
            Direction = ThrowDirection.Forward;
            Damage = 100;
            MaxEscapeTime = 60;
            EscapeThreshold = 30;
            ChainWindow = 30;
        }
    }
}
