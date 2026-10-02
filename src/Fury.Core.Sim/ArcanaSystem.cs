using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Arcana system for Arcana Heart-style elemental powers.
/// Each arcana grants unique abilities, stat modifiers, and special moves.
/// All state is deterministic and serializable.
/// </summary>
public sealed class ArcanaSystem
{
    public const int MaxArcanas = 12;

    public enum ArcanaType : byte
    {
        None = 0,
        Fire = 1,
        Water = 2,
        Earth = 3,
        Wind = 4,
        Light = 5,
        Dark = 6,
        Thunder = 7,
        Ice = 8,
        Metal = 9,
        Wood = 10,
        Radiant = 11,
        Shadow = 12
    }

    public enum ArcanaState : byte
    {
        Inactive = 0,
        Active = 1,
        Charging = 2,
        Cooldown = 3
    }

    private readonly ArcanaData[] _arcanas;

    public ArcanaSystem(FightConfig cfg)
    {
        _ = cfg;
        _arcanas = new ArcanaData[MaxArcanas];
        for (int i = 0; i < MaxArcanas; i++)
        {
            _arcanas[i] = new ArcanaData();
        }
    }

    public int Count { get; private set; }

    /// <summary>Dynamic words per configured arcana in a snapshot.</summary>
    public const int WordsPerSlot = 4;

    /// <summary>Number of snapshot words for the arcanas configured so far.</summary>
    public int StateWords => Count * WordsPerSlot;

    /// <summary>Writes the mutable state of every configured arcana (rollback savestate).</summary>
    public void ExportState(Span<int> dest)
    {
        for (int i = 0; i < Count; i++)
        {
            int o = i * WordsPerSlot;
            dest[o] = (int)_arcanas[i].State;
            dest[o + 1] = _arcanas[i].ChargeTimer;
            dest[o + 2] = _arcanas[i].ActiveTimer;
            dest[o + 3] = _arcanas[i].CooldownTimer;
        }
    }

    /// <summary>Restores state written by <see cref="ExportState"/>.</summary>
    public void ImportState(ReadOnlySpan<int> source)
    {
        for (int i = 0; i < Count; i++)
        {
            int o = i * WordsPerSlot;
            ref var item = ref _arcanas[i];
            item.State = (ArcanaState)source[o];
            item.ChargeTimer = source[o + 1];
            item.ActiveTimer = source[o + 2];
            item.CooldownTimer = source[o + 3];
        }
    }

    public void AddArcana(in ArcanaData data)
    {
        if (Count >= MaxArcanas) return;
        _arcanas[Count] = data with { State = ArcanaState.Inactive };
        Count++;
    }

    public void Clear()
    {
        Count = 0;
        for (int i = 0; i < MaxArcanas; i++)
        {
            _arcanas[i] = new ArcanaData();
        }
    }

    public void Update()
    {
        for (int i = 0; i < Count; i++)
        {
            ref var a = ref _arcanas[i];
            switch (a.State)
            {
                case ArcanaState.Charging:
                    a.ChargeTimer--;
                    if (a.ChargeTimer <= 0)
                    {
                        a.State = ArcanaState.Active;
                        a.ActiveTimer = a.Duration;
                    }
                    break;
                case ArcanaState.Active:
                    a.ActiveTimer--;
                    if (a.ActiveTimer <= 0)
                    {
                        a.State = ArcanaState.Cooldown;
                        a.CooldownTimer = a.Cooldown;
                    }
                    break;
                case ArcanaState.Cooldown:
                    a.CooldownTimer--;
                    if (a.CooldownTimer <= 0)
                    {
                        a.State = ArcanaState.Inactive;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Attempts to activate an arcana.
    /// </summary>
    public bool TryActivate(int arcanaIndex, int currentMeter)
    {
        if (arcanaIndex < 0 || arcanaIndex >= Count) return false;
        ref var a = ref _arcanas[arcanaIndex];
        if (a.State != ArcanaState.Inactive) return false;
        if (currentMeter < a.MeterCost) return false;

        a.State = ArcanaState.Charging;
        a.ChargeTimer = a.ChargeTime;
        return true;
    }

    /// <summary>
    /// Gets the stat multiplier for an active arcana.
    /// </summary>
    public Fp GetStatMultiplier(int arcanaIndex, StatType stat)
    {
        if (arcanaIndex < 0 || arcanaIndex >= Count) return Fp.One;
        ref var a = ref _arcanas[arcanaIndex];
        if (a.State != ArcanaState.Active) return Fp.One;

        return stat switch
        {
            StatType.Speed => a.SpeedMultiplier,
            StatType.Power => a.PowerMultiplier,
            StatType.Defense => a.DefenseMultiplier,
            _ => Fp.One
        };
    }

    /// <summary>
    /// Gets the damage multiplier for an active arcana.
    /// </summary>
    public Fp GetDamageMultiplier(int arcanaIndex)
    {
        if (arcanaIndex < 0 || arcanaIndex >= Count) return Fp.One;
        ref var a = ref _arcanas[arcanaIndex];
        if (a.State != ArcanaState.Active) return Fp.One;
        return a.DamageMultiplier;
    }

    /// <summary>
    /// Gets the special move granted by an active arcana.
    /// </summary>
    public int GetSpecialMove(int arcanaIndex)
    {
        if (arcanaIndex < 0 || arcanaIndex >= Count) return -1;
        ref var a = ref _arcanas[arcanaIndex];
        if (a.State != ArcanaState.Active) return -1;
        return a.SpecialMoveIndex;
    }

    public ArcanaData GetArcana(int index)
    {
        if (index < 0 || index >= Count) return new ArcanaData();
        return _arcanas[index];
    }

    public enum StatType : byte
    {
        Speed = 0,
        Power = 1,
        Defense = 2
    }

    /// <summary>
    /// Serializable arcana data.
    /// </summary>
    public record struct ArcanaData
    {
        public ArcanaType Type { get; init; }
        public ArcanaState State { get; set; }
        public Fp SpeedMultiplier { get; init; }
        public Fp PowerMultiplier { get; init; }
        public Fp DefenseMultiplier { get; init; }
        public Fp DamageMultiplier { get; init; }
        public int MeterCost { get; init; }
        public int ChargeTime { get; init; }
        public int Duration { get; init; }
        public int Cooldown { get; init; }
        public int ChargeTimer { get; set; }
        public int ActiveTimer { get; set; }
        public int CooldownTimer { get; set; }
        public int SpecialMoveIndex { get; init; }
        public bool GrantsProjectile { get; init; }
        public bool GrantsShield { get; init; }

        public ArcanaData()
        {
            Type = ArcanaType.None;
            State = ArcanaState.Inactive;
            SpeedMultiplier = Fp.One;
            PowerMultiplier = Fp.One;
            DefenseMultiplier = Fp.One;
            DamageMultiplier = Fp.One;
            MeterCost = 300;
            ChargeTime = 30;
            Duration = 600;
            Cooldown = 300;
            SpecialMoveIndex = -1;
        }
    }
}
