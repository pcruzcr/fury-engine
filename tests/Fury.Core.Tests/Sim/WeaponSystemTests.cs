using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>
/// Tests for the weapon system (Samurai Shodown, Soulcalibur 2D).
/// </summary>
public class WeaponSystemTests
{
    private static FightConfig CreateConfig() => FightConfig.Default();

    [Fact]
    public void Weapon_Add_IncreasesCount()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        ws.AddWeapon(new WeaponSystem.WeaponData { Type = WeaponSystem.WeaponType.Sword });

        Assert.Equal(1, ws.Count);
    }

    [Fact]
    public void Weapon_Clear_ResetsCount()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        ws.AddWeapon(new WeaponSystem.WeaponData { Type = WeaponSystem.WeaponType.Sword });
        ws.Clear();

        Assert.Equal(0, ws.Count);
    }

    [Fact]
    public void Weapon_GetHitboxExtension_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        ws.AddWeapon(new WeaponSystem.WeaponData
        {
            Type = WeaponSystem.WeaponType.Sword,
            Range = Fp.FromInt(50)
        });

        Fp result = ws.GetHitboxExtension(0);

        Assert.Equal(Fp.FromInt(50), result);
    }

    [Fact]
    public void Weapon_GetHitboxExtension_InactiveWeapon_ReturnsZero()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        ws.AddWeapon(new WeaponSystem.WeaponData
        {
            Type = WeaponSystem.WeaponType.Sword,
            Range = Fp.FromInt(50),
            State = WeaponSystem.WeaponState.Disarmed
        });

        Fp result = ws.GetHitboxExtension(0);

        Assert.Equal(Fp.Zero, result);
    }

    [Fact]
    public void Weapon_GetDamageBonus_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        ws.AddWeapon(new WeaponSystem.WeaponData
        {
            Type = WeaponSystem.WeaponType.Sword,
            DamageBonus = 75
        });

        int result = ws.GetDamageBonus(0);

        Assert.Equal(75, result);
    }

    [Fact]
    public void Weapon_TryDisarm_Success()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        ws.AddWeapon(new WeaponSystem.WeaponData { Type = WeaponSystem.WeaponType.Sword });

        bool result = ws.TryDisarm(0);

        Assert.True(result);
        Assert.Equal(WeaponSystem.WeaponState.Disarmed, ws.GetWeapon(0).State);
    }

    [Fact]
    public void Weapon_TryDisarm_AlreadyDisarmed_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        ws.AddWeapon(new WeaponSystem.WeaponData
        {
            Type = WeaponSystem.WeaponType.Sword,
            State = WeaponSystem.WeaponState.Disarmed
        });

        bool result = ws.TryDisarm(0);

        Assert.False(result);
    }

    [Fact]
    public void Weapon_Update_RecoversDisarmedWeapon()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        ws.AddWeapon(new WeaponSystem.WeaponData { Type = WeaponSystem.WeaponType.Sword });
        ws.TryDisarm(0);

        Assert.Equal(WeaponSystem.WeaponState.Disarmed, ws.GetWeapon(0).State);

        for (int i = 0; i < 180; i++) ws.Update();

        Assert.Equal(WeaponSystem.WeaponState.Active, ws.GetWeapon(0).State);
    }

    [Fact]
    public void Weapon_ReduceDurability_BreaksWeaponAtZero()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        ws.AddWeapon(new WeaponSystem.WeaponData
        {
            Type = WeaponSystem.WeaponType.Sword,
            Durability = 50,
            MaxDurability = 100
        });

        ws.ReduceDurability(0, 30);
        Assert.Equal(WeaponSystem.WeaponState.Active, ws.GetWeapon(0).State);

        ws.ReduceDurability(0, 30);
        Assert.Equal(WeaponSystem.WeaponState.Broken, ws.GetWeapon(0).State);
    }

    [Fact]
    public void Weapon_Repair_RestoresWeapon()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        ws.AddWeapon(new WeaponSystem.WeaponData
        {
            Type = WeaponSystem.WeaponType.Sword,
            State = WeaponSystem.WeaponState.Broken,
            Durability = 0
        });

        ws.Repair(0);

        Assert.Equal(WeaponSystem.WeaponState.Active, ws.GetWeapon(0).State);
        Assert.Equal(100, ws.GetWeapon(0).Durability);
    }

    [Fact]
    public void Weapon_GetWeapon_InvalidIndex_ReturnsDefault()
    {
        var cfg = CreateConfig();
        var ws = new WeaponSystem(cfg);

        var result = ws.GetWeapon(-1);

        Assert.Equal(WeaponSystem.WeaponType.Sword, result.Type);
    }
}
