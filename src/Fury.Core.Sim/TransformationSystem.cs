using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Transformation system for fighters that can change form (Bloody Roar, Dragon Ball).
/// Each form has different stats, moves, and visual properties.
/// All state is deterministic and serializable.
/// </summary>
public sealed class TransformationSystem
{
    public const int MaxForms = 4;

    public enum TransformationType : byte
    {
        None = 0,
        BeastForm = 1,      // Bloody Roar: animal transformation
        PowerUp = 2,        // Dragon Ball: Super Saiyan
        Armor = 3,          // Armor change
        Possession = 4,     // Spirit possession
        Fusion = 5         // Character fusion
    }

    public enum TransformationState : byte
    {
        Inactive = 0,
        Active = 1,
        Charging = 2,
        Cooldown = 3
    }

    private readonly FormData[] _forms;

    public TransformationSystem(FightConfig cfg)
    {
        _ = cfg;
        _forms = new FormData[MaxForms];
        for (int i = 0; i < MaxForms; i++)
        {
            _forms[i] = new FormData();
        }
    }

    public int Count { get; private set; }

    /// <summary>Dynamic words per configured form in a snapshot.</summary>
    public const int WordsPerSlot = 4;

    /// <summary>Number of snapshot words for the forms configured so far.</summary>
    public int StateWords => Count * WordsPerSlot;

    /// <summary>Writes the mutable state of every configured form (rollback savestate).</summary>
    public void ExportState(Span<int> dest)
    {
        for (int i = 0; i < Count; i++)
        {
            int o = i * WordsPerSlot;
            dest[o] = (int)_forms[i].State;
            dest[o + 1] = _forms[i].ChargeTimer;
            dest[o + 2] = _forms[i].ActiveTimer;
            dest[o + 3] = _forms[i].CooldownTimer;
        }
    }

    /// <summary>Restores state written by <see cref="ExportState"/>.</summary>
    public void ImportState(ReadOnlySpan<int> source)
    {
        for (int i = 0; i < Count; i++)
        {
            int o = i * WordsPerSlot;
            ref var item = ref _forms[i];
            item.State = (TransformationState)source[o];
            item.ChargeTimer = source[o + 1];
            item.ActiveTimer = source[o + 2];
            item.CooldownTimer = source[o + 3];
        }
    }

    public void AddForm(in FormData data)
    {
        if (Count >= MaxForms) return;
        _forms[Count] = data with { State = TransformationState.Inactive };
        Count++;
    }

    public void Clear()
    {
        Count = 0;
        for (int i = 0; i < MaxForms; i++)
        {
            _forms[i] = new FormData();
        }
    }

    public void Update()
    {
        for (int i = 0; i < Count; i++)
        {
            ref var f = ref _forms[i];
            switch (f.State)
            {
                case TransformationState.Charging:
                    f.ChargeTimer--;
                    if (f.ChargeTimer <= 0)
                    {
                        f.State = TransformationState.Active;
                        f.ActiveTimer = f.Duration;
                    }
                    break;
                case TransformationState.Active:
                    f.ActiveTimer--;
                    if (f.ActiveTimer <= 0)
                    {
                        f.State = TransformationState.Cooldown;
                        f.CooldownTimer = f.Cooldown;
                    }
                    break;
                case TransformationState.Cooldown:
                    f.CooldownTimer--;
                    if (f.CooldownTimer <= 0)
                    {
                        f.State = TransformationState.Inactive;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Attempts to activate a transformation.
    /// </summary>
    public bool TryActivate(int formIndex, int currentMeter)
    {
        if (formIndex < 0 || formIndex >= Count) return false;
        ref var f = ref _forms[formIndex];
        if (f.State != TransformationState.Inactive) return false;
        if (currentMeter < f.MeterCost) return false;

        f.State = TransformationState.Charging;
        f.ChargeTimer = f.ChargeTime;
        return true;
    }

    /// <summary>
    /// Gets the stat multiplier for an active transformation.
    /// </summary>
    public Fp GetStatMultiplier(int formIndex, StatType stat)
    {
        if (formIndex < 0 || formIndex >= Count) return Fp.One;
        ref var f = ref _forms[formIndex];
        if (f.State != TransformationState.Active) return Fp.One;

        return stat switch
        {
            StatType.Speed => f.SpeedMultiplier,
            StatType.Power => f.PowerMultiplier,
            StatType.Defense => f.DefenseMultiplier,
            _ => Fp.One
        };
    }

    /// <summary>
    /// Gets the damage multiplier for an active transformation.
    /// </summary>
    public Fp GetDamageMultiplier(int formIndex)
    {
        if (formIndex < 0 || formIndex >= Count) return Fp.One;
        ref var f = ref _forms[formIndex];
        if (f.State != TransformationState.Active) return Fp.One;
        return f.DamageMultiplier;
    }

    public FormData GetForm(int index)
    {
        if (index < 0 || index >= Count) return new FormData();
        return _forms[index];
    }

    public enum StatType : byte
    {
        Speed = 0,
        Power = 1,
        Defense = 2
    }

    /// <summary>
    /// Serializable form data.
    /// </summary>
    public record struct FormData
    {
        public TransformationType Type { get; init; }
        public TransformationState State { get; set; }
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

        public FormData()
        {
            Type = TransformationType.None;
            State = TransformationState.Inactive;
            SpeedMultiplier = Fp.One;
            PowerMultiplier = Fp.One;
            DefenseMultiplier = Fp.One;
            DamageMultiplier = Fp.One;
            MeterCost = 500;
            ChargeTime = 60;
            Duration = 600;
            Cooldown = 300;
        }
    }
}
