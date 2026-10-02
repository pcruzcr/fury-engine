namespace Fury.Core.Sim;

/// <summary>
/// A held weapon (Samurai Shodown / Soulcalibur style). While <see cref="WeaponSystem.WeaponState.Active"/> it extends
/// the owner's melee reach and adds flat damage; every landed hit wears it down and breaks it at 0 durability; a big
/// hit taken (at least <see cref="DisarmDamage"/>) knocks it out of the owner's hands until its recovery timer ends.
/// Durability and the disarm timer are part of the rollback snapshot.
/// </summary>
public sealed class WeaponModule : SimModule
{
    private readonly WeaponSystem _weapons;
    private readonly int _index;

    public WeaponModule(int fighter, WeaponSystem weapons, int weaponIndex = 0, int disarmDamage = 100)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        if (fighter is < 0 or >= FightState.FighterCount) throw new ArgumentOutOfRangeException(nameof(fighter));
        if ((uint)weaponIndex >= (uint)weapons.Count) throw new ArgumentOutOfRangeException(nameof(weaponIndex));
        Fighter = fighter;
        _weapons = weapons;
        _index = weaponIndex;
        DisarmDamage = disarmDamage;
    }

    public int Fighter { get; }

    /// <summary>Damage taken in a single hit that knocks the weapon away.</summary>
    public int DisarmDamage { get; }

    public WeaponSystem Weapons => _weapons;

    public override int StateWords => _weapons.StateWords;

    public override void Tick(FightState state) => _weapons.Update();

    public override int ReachBonusRaw(int fighter) => fighter == Fighter ? _weapons.GetHitboxExtension(_index).Raw : 0;

    public override int FlatDamage(int attacker, int defender) => attacker == Fighter ? _weapons.GetDamageBonus(_index) : 0;

    public override void OnHit(int attacker, int defender, int damage, bool blocked)
    {
        if (attacker == Fighter && !blocked) _weapons.ReduceDurability(_index, 1);
        if (defender == Fighter && !blocked && damage >= DisarmDamage) _weapons.TryDisarm(_index);
    }

    public override void Serialize(Span<int> dest) => _weapons.ExportState(dest);

    public override void Deserialize(ReadOnlySpan<int> source) => _weapons.ImportState(source);
}
