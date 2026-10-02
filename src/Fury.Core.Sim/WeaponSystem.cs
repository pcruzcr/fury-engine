using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Weapon system for weapon-based fighters (Samurai Shodown, Soulcalibur 2D).
/// Weapons extend hitboxes, have durability, and can be disarmed.
/// All state is deterministic and serializable.
/// </summary>
public sealed class WeaponSystem
{
    public const int MaxWeapons = 4;

    public enum WeaponType : byte
    {
        None = 0,
        Sword = 1,
        Spear = 2,
        Staff = 3,
        Dagger = 4,
        Axe = 5,
        Bow = 6
    }

    public enum WeaponState : byte
    {
        Sheathed = 0,
        Active = 1,
        Disarmed = 2,
        Broken = 3
    }

    private readonly WeaponData[] _weapons;

    public WeaponSystem(FightConfig cfg)
    {
        _ = cfg;
        _weapons = new WeaponData[MaxWeapons];
        for (int i = 0; i < MaxWeapons; i++)
        {
            _weapons[i] = new WeaponData();
        }
    }

    public int Count { get; private set; }

    /// <summary>Dynamic words per configured weapon in a snapshot.</summary>
    public const int WordsPerSlot = 3;

    /// <summary>Number of snapshot words for the weapons configured so far.</summary>
    public int StateWords => Count * WordsPerSlot;

    /// <summary>Writes the mutable state of every configured weapon (rollback savestate).</summary>
    public void ExportState(Span<int> dest)
    {
        for (int i = 0; i < Count; i++)
        {
            int o = i * WordsPerSlot;
            dest[o] = (int)_weapons[i].State;
            dest[o + 1] = _weapons[i].Durability;
            dest[o + 2] = _weapons[i].RecoveryTimer;
        }
    }

    /// <summary>Restores state written by <see cref="ExportState"/>.</summary>
    public void ImportState(ReadOnlySpan<int> source)
    {
        for (int i = 0; i < Count; i++)
        {
            int o = i * WordsPerSlot;
            ref var item = ref _weapons[i];
            item.State = (WeaponState)source[o];
            item.Durability = source[o + 1];
            item.RecoveryTimer = source[o + 2];
        }
    }

    public void AddWeapon(in WeaponData data)
    {
        if (Count >= MaxWeapons) return;
        _weapons[Count] = data;
        Count++;
    }

    public void Clear()
    {
        Count = 0;
        for (int i = 0; i < MaxWeapons; i++)
        {
            _weapons[i] = new WeaponData();
        }
    }

    public void Update()
    {
        for (int i = 0; i < Count; i++)
        {
            ref var w = ref _weapons[i];
            if (w.State == WeaponState.Disarmed)
            {
                w.RecoveryTimer--;
                if (w.RecoveryTimer <= 0)
                {
                    w.State = WeaponState.Active;
                }
            }
        }
    }

    /// <summary>
    /// Gets the active weapon hitbox extension for a fighter.
    /// </summary>
    public Fp GetHitboxExtension(int weaponIndex)
    {
        if (weaponIndex < 0 || weaponIndex >= Count) return Fp.Zero;
        ref var w = ref _weapons[weaponIndex];
        if (w.State != WeaponState.Active) return Fp.Zero;
        return w.Range;
    }

    /// <summary>
    /// Gets the active weapon damage bonus.
    /// </summary>
    public int GetDamageBonus(int weaponIndex)
    {
        if (weaponIndex < 0 || weaponIndex >= Count) return 0;
        ref var w = ref _weapons[weaponIndex];
        if (w.State != WeaponState.Active) return 0;
        return w.DamageBonus;
    }

    /// <summary>
    /// Attempts to disarm a weapon.
    /// </summary>
    public bool TryDisarm(int weaponIndex)
    {
        if (weaponIndex < 0 || weaponIndex >= Count) return false;
        ref var w = ref _weapons[weaponIndex];
        if (w.State != WeaponState.Active) return false;
        w.State = WeaponState.Disarmed;
        w.RecoveryTimer = 180; // 3 seconds
        return true;
    }

    /// <summary>
    /// Repairs a broken/disarmed weapon.
    /// </summary>
    public void Repair(int weaponIndex)
    {
        if (weaponIndex < 0 || weaponIndex >= Count) return;
        _weapons[weaponIndex].State = WeaponState.Active;
        _weapons[weaponIndex].Durability = _weapons[weaponIndex].MaxDurability;
    }

    /// <summary>
    /// Reduces weapon durability.
    /// </summary>
    public void ReduceDurability(int weaponIndex, int amount)
    {
        if (weaponIndex < 0 || weaponIndex >= Count) return;
        ref var w = ref _weapons[weaponIndex];
        w.Durability -= amount;
        if (w.Durability <= 0)
        {
            w.State = WeaponState.Broken;
        }
    }

    public WeaponData GetWeapon(int index)
    {
        if (index < 0 || index >= Count) return new WeaponData();
        return _weapons[index];
    }

    /// <summary>
    /// Serializable weapon data.
    /// </summary>
    public record struct WeaponData
    {
        public WeaponType Type { get; init; }
        public WeaponState State { get; set; }
        public Fp Range { get; init; }
        public int DamageBonus { get; init; }
        public int Durability { get; set; }
        public int MaxDurability { get; init; }
        public int RecoveryTimer { get; set; }
        public Fp Weight { get; init; }

        public WeaponData()
        {
            Type = WeaponType.Sword;
            State = WeaponState.Active;
            Range = Fp.FromInt(40);
            DamageBonus = 50;
            Durability = 100;
            MaxDurability = 100;
            Weight = Fp.FromInt(10);
        }
    }
}
