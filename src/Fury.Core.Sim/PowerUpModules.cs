using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>Stats a power-up can modify.</summary>
public enum PowerStat
{
    Damage,
    Power,
    Defense,
    Speed,
}

/// <summary>
/// A timed buff owned by one fighter (Arcana Heart elemental powers, Bloody Roar / Dragon Ball transformations).
/// Pressing <see cref="InputButton.Macro3"/> in a neutral state spends the meter cost and starts the charge →
/// active → cooldown cycle; while active it scales damage dealt (damage × power), damage taken (defense) and
/// movement speed. The cycle timers are part of the rollback snapshot.
/// </summary>
public abstract class PowerUpModule : SimModule
{
    protected PowerUpModule(int fighter)
    {
        if (fighter is < 0 or >= FightState.FighterCount) throw new ArgumentOutOfRangeException(nameof(fighter));
        Fighter = fighter;
    }

    /// <summary>The fighter slot that owns the power-up.</summary>
    public int Fighter { get; }

    /// <summary>True while the buff is in its active phase.</summary>
    public abstract bool IsActive { get; }

    protected abstract void UpdateSystem();

    protected abstract bool TryActivate(int meter, out int cost);

    protected abstract Fp Multiplier(PowerStat stat);

    public override void Tick(FightState state)
    {
        UpdateSystem();
        if (state.RoundState != 0 || !FighterStateId.IsNeutral(state.GetStateId(Fighter))) return;
        if (!Sim.Input(Fighter).Pressed(InputButton.Macro3)) return;

        if (TryActivate(state.GetExMeter(Fighter), out int cost))
        {
            state.SetExMeter(Fighter, state.GetExMeter(Fighter) - cost);
            Sim.EmitEvent(SimEventId.PowerActivated, SimEventPayload.Pack(Fighter, 0, 0));
        }
    }

    public override int DamagePercent(int attacker, int defender)
    {
        long percent = 100;
        if (attacker == Fighter) percent = percent * Percent(PowerStat.Damage) / 100 * Percent(PowerStat.Power) / 100;
        if (defender == Fighter) percent = percent * 100 / System.Math.Max(1, Percent(PowerStat.Defense));
        return (int)percent;
    }

    public override int WalkSpeedPercent(int fighter) => fighter == Fighter ? Percent(PowerStat.Speed) : 100;

    private int Percent(PowerStat stat) => (int)(((long)Multiplier(stat).Raw * 100) >> Fp.FractionBits);
}

/// <summary>Arcana power-up (Arcana Heart style) driven by an <see cref="ArcanaSystem"/> slot.</summary>
public sealed class ArcanaModule : PowerUpModule
{
    private readonly ArcanaSystem _arcana;
    private readonly int _index;

    public ArcanaModule(int fighter, ArcanaSystem arcana, int arcanaIndex = 0)
        : base(fighter)
    {
        ArgumentNullException.ThrowIfNull(arcana);
        if ((uint)arcanaIndex >= (uint)arcana.Count) throw new ArgumentOutOfRangeException(nameof(arcanaIndex));
        _arcana = arcana;
        _index = arcanaIndex;
    }

    public ArcanaSystem Arcana => _arcana;

    public override int StateWords => _arcana.StateWords;

    public override bool IsActive => _arcana.GetArcana(_index).State == ArcanaSystem.ArcanaState.Active;

    protected override void UpdateSystem() => _arcana.Update();

    protected override bool TryActivate(int meter, out int cost)
    {
        cost = _arcana.GetArcana(_index).MeterCost;
        return _arcana.TryActivate(_index, meter);
    }

    protected override Fp Multiplier(PowerStat stat) => stat switch
    {
        PowerStat.Damage => _arcana.GetDamageMultiplier(_index),
        PowerStat.Power => _arcana.GetStatMultiplier(_index, ArcanaSystem.StatType.Power),
        PowerStat.Defense => _arcana.GetStatMultiplier(_index, ArcanaSystem.StatType.Defense),
        _ => _arcana.GetStatMultiplier(_index, ArcanaSystem.StatType.Speed),
    };

    public override void Serialize(Span<int> dest) => _arcana.ExportState(dest);

    public override void Deserialize(ReadOnlySpan<int> source) => _arcana.ImportState(source);
}

/// <summary>Transformation power-up (Bloody Roar / Dragon Ball style) driven by a <see cref="TransformationSystem"/> form.</summary>
public sealed class TransformationModule : PowerUpModule
{
    private readonly TransformationSystem _forms;
    private readonly int _index;

    public TransformationModule(int fighter, TransformationSystem forms, int formIndex = 0)
        : base(fighter)
    {
        ArgumentNullException.ThrowIfNull(forms);
        if ((uint)formIndex >= (uint)forms.Count) throw new ArgumentOutOfRangeException(nameof(formIndex));
        _forms = forms;
        _index = formIndex;
    }

    public TransformationSystem Forms => _forms;

    public override int StateWords => _forms.StateWords;

    public override bool IsActive => _forms.GetForm(_index).State == TransformationSystem.TransformationState.Active;

    protected override void UpdateSystem() => _forms.Update();

    protected override bool TryActivate(int meter, out int cost)
    {
        cost = _forms.GetForm(_index).MeterCost;
        return _forms.TryActivate(_index, meter);
    }

    protected override Fp Multiplier(PowerStat stat) => stat switch
    {
        PowerStat.Damage => _forms.GetDamageMultiplier(_index),
        PowerStat.Power => _forms.GetStatMultiplier(_index, TransformationSystem.StatType.Power),
        PowerStat.Defense => _forms.GetStatMultiplier(_index, TransformationSystem.StatType.Defense),
        _ => _forms.GetStatMultiplier(_index, TransformationSystem.StatType.Speed),
    };

    public override void Serialize(Span<int> dest) => _forms.ExportState(dest);

    public override void Deserialize(ReadOnlySpan<int> source) => _forms.ImportState(source);
}
