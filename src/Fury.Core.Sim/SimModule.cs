using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Base class of gameplay modules: an <see cref="ISimModule"/> that can also bend the rules through hooks
/// (damage, speed, reach, floors, who is on screen). Every hook defaults to "no effect", so a module overrides only
/// what it needs. Hooks are called from inside the deterministic pipeline: they must be pure functions of the
/// module state and the arguments (integers / <see cref="Fp"/> only) and must not allocate.
/// </summary>
public abstract class SimModule : ISimModule
{
    /// <summary>The simulation this module is attached to (valid after it was passed to the simulation constructor).</summary>
    protected FightSimulation Sim { get; private set; } = null!;

    internal void Attach(FightSimulation sim) => Sim = sim;

    public abstract int StateWords { get; }

    public virtual void Tick(FightState state)
    {
    }

    public abstract void Serialize(Span<int> dest);

    public abstract void Deserialize(ReadOnlySpan<int> source);

    /// <summary>Damage dealt by <paramref name="attacker"/> to <paramref name="defender"/> is multiplied by this percentage (100 = unchanged).</summary>
    public virtual int DamagePercent(int attacker, int defender) => 100;

    /// <summary>Walk / dash speed percentage of a fighter (100 = unchanged).</summary>
    public virtual int WalkSpeedPercent(int fighter) => 100;

    /// <summary>Extra melee reach in Q16.16 raw units (weapons).</summary>
    public virtual int ReachBonusRaw(int fighter) => 0;

    /// <summary>
    /// Optional solid surface under a fighter (platforms). A falling fighter moving from <paramref name="previousY"/> to
    /// <paramref name="y"/> that crosses a surface lands on it; a grounded one standing on a surface keeps it.
    /// </summary>
    public virtual bool TryGetSupport(int fighter, Fp x, Fp previousY, Fp y, bool falling, out Fp surfaceY)
    {
        surfaceY = Fp.Zero;
        return false;
    }

    /// <summary>Upward launch velocity (Q16.16 raw, 0 = none) when a fighter lands on a surface (trampolines).</summary>
    public virtual int BounceVyRaw(int fighter, Fp x, Fp surfaceY) => 0;

    /// <summary>Flat damage added after all percentage modifiers (weapon bonuses).</summary>
    public virtual int FlatDamage(int attacker, int defender) => 0;

    /// <summary>A throw connected (called in addition to <see cref="OnHit"/>).</summary>
    public virtual void OnThrow(int attacker, int defender)
    {
    }

    /// <summary>Horizontal push per frame applied to a grounded fighter (conveyors, wind).</summary>
    public virtual Fp GroundPush(int fighter, Fp x, Fp y) => Fp.Zero;

    /// <summary>The character currently controlled in fighter slot <paramref name="fighter"/> (tag teams), or null for the default.</summary>
    public virtual CharacterConfig? ActiveCharacter(int fighter) => null;

    /// <summary>
    /// A fighter reached 0 health. Return true when a substitute took over (tag partner) so the round continues; the
    /// module is responsible for resetting the fighter's state.
    /// </summary>
    public virtual bool TrySubstituteKo(int fighter, FightState state) => false;

    /// <summary>A strike, projectile or throw resolved (after damage was applied).</summary>
    public virtual void OnHit(int attacker, int defender, int damage, bool blocked)
    {
    }
}

/// <summary>Aggregates the hooks of every attached <see cref="SimModule"/> for the core systems.</summary>
internal sealed class ModuleHooks
{
    private readonly SimModule[] _modules;

    public ModuleHooks(IEnumerable<ISimModule> modules) =>
        _modules = modules.OfType<SimModule>().ToArray();

    public bool Any => _modules.Length > 0;

    public int DamagePercent(int attacker, int defender)
    {
        long percent = 100;
        foreach (var module in _modules) percent = percent * module.DamagePercent(attacker, defender) / 100;
        return (int)percent;
    }

    public int WalkSpeedPercent(int fighter)
    {
        long percent = 100;
        foreach (var module in _modules) percent = percent * module.WalkSpeedPercent(fighter) / 100;
        return (int)percent;
    }

    public int ReachBonusRaw(int fighter)
    {
        int total = 0;
        foreach (var module in _modules) total += module.ReachBonusRaw(fighter);
        return total;
    }

    public bool TryGetSupport(int fighter, Fp x, Fp previousY, Fp y, bool falling, out Fp surfaceY)
    {
        bool found = false;
        surfaceY = Fp.Zero;
        foreach (var module in _modules)
        {
            if (!module.TryGetSupport(fighter, x, previousY, y, falling, out var candidate)) continue;
            // The highest surface wins (smallest Y: Y grows downward).
            if (!found || candidate < surfaceY) surfaceY = candidate;
            found = true;
        }
        return found;
    }

    public int BounceVyRaw(int fighter, Fp x, Fp surfaceY)
    {
        int bounce = 0;
        foreach (var module in _modules) bounce = System.Math.Max(bounce, module.BounceVyRaw(fighter, x, surfaceY));
        return bounce;
    }

    public int FlatDamage(int attacker, int defender)
    {
        int total = 0;
        foreach (var module in _modules) total += module.FlatDamage(attacker, defender);
        return total;
    }

    public void OnThrow(int attacker, int defender)
    {
        foreach (var module in _modules) module.OnThrow(attacker, defender);
    }

    public Fp GroundPush(int fighter, Fp x, Fp y)
    {
        Fp total = Fp.Zero;
        foreach (var module in _modules) total += module.GroundPush(fighter, x, y);
        return total;
    }

    public CharacterConfig? ActiveCharacter(int fighter)
    {
        foreach (var module in _modules)
        {
            var character = module.ActiveCharacter(fighter);
            if (character != null) return character;
        }
        return null;
    }

    public bool TrySubstituteKo(int fighter, FightState state)
    {
        foreach (var module in _modules)
        {
            if (module.TrySubstituteKo(fighter, state)) return true;
        }
        return false;
    }

    public void OnHit(int attacker, int defender, int damage, bool blocked)
    {
        foreach (var module in _modules) module.OnHit(attacker, defender, damage, blocked);
    }
}

/// <summary>Resolves "who is fighting in this slot": the default character unless a module (tag team) replaced it.</summary>
internal sealed class FighterRoster
{
    private readonly CharacterConfig[] _base;
    private readonly ModuleHooks _hooks;

    public FighterRoster(CharacterConfig p1, CharacterConfig p2, ModuleHooks hooks)
    {
        _base = [p1, p2];
        _hooks = hooks;
    }

    public CharacterConfig Get(int fighter) => (_hooks.Any ? _hooks.ActiveCharacter(fighter) : null) ?? _base[fighter];
}
