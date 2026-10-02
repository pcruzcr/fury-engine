using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>
/// Tests for the arcana system (Arcana Heart).
/// </summary>
public class ArcanaSystemTests
{
    private static FightConfig CreateConfig() => FightConfig.Default();

    [Fact]
    public void Arcana_Add_IncreasesCount()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData { Type = ArcanaSystem.ArcanaType.Fire });

        Assert.Equal(1, arcanaSystem.Count);
    }

    [Fact]
    public void Arcana_Clear_ResetsCount()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData { Type = ArcanaSystem.ArcanaType.Fire });
        arcanaSystem.Clear();

        Assert.Equal(0, arcanaSystem.Count);
    }

    [Fact]
    public void Arcana_TryActivate_Success()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            MeterCost = 300
        });

        bool result = arcanaSystem.TryActivate(0, 400);

        Assert.True(result);
        Assert.Equal(ArcanaSystem.ArcanaState.Charging, arcanaSystem.GetArcana(0).State);
    }

    [Fact]
    public void Arcana_TryActivate_NotEnoughMeter_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            MeterCost = 300
        });

        bool result = arcanaSystem.TryActivate(0, 200);

        Assert.False(result);
    }

    [Fact]
    public void Arcana_TryActivate_AlreadyActive_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            MeterCost = 300
        });

        arcanaSystem.TryActivate(0, 400);
        bool result = arcanaSystem.TryActivate(0, 400);

        Assert.False(result);
    }

    [Fact]
    public void Arcana_Update_ChargingToActive()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            ChargeTime = 30
        });

        arcanaSystem.TryActivate(0, 400);
        Assert.Equal(ArcanaSystem.ArcanaState.Charging, arcanaSystem.GetArcana(0).State);

        for (int i = 0; i < 30; i++) arcanaSystem.Update();

        Assert.Equal(ArcanaSystem.ArcanaState.Active, arcanaSystem.GetArcana(0).State);
    }

    [Fact]
    public void Arcana_Update_ActiveToCooldown()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            ChargeTime = 10,
            Duration = 60
        });

        arcanaSystem.TryActivate(0, 400);
        for (int i = 0; i < 10; i++) arcanaSystem.Update();
        Assert.Equal(ArcanaSystem.ArcanaState.Active, arcanaSystem.GetArcana(0).State);

        for (int i = 0; i < 60; i++) arcanaSystem.Update();

        Assert.Equal(ArcanaSystem.ArcanaState.Cooldown, arcanaSystem.GetArcana(0).State);
    }

    [Fact]
    public void Arcana_GetStatMultiplier_Inactive_ReturnsOne()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            SpeedMultiplier = Fp.FromInt(2)
        });

        Fp result = arcanaSystem.GetStatMultiplier(0, ArcanaSystem.StatType.Speed);

        Assert.Equal(Fp.One, result);
    }

    [Fact]
    public void Arcana_GetStatMultiplier_Active_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            ChargeTime = 10,
            SpeedMultiplier = Fp.FromInt(2)
        });

        arcanaSystem.TryActivate(0, 400);
        for (int i = 0; i < 10; i++) arcanaSystem.Update();

        Fp result = arcanaSystem.GetStatMultiplier(0, ArcanaSystem.StatType.Speed);

        Assert.Equal(Fp.FromInt(2), result);
    }

    [Fact]
    public void Arcana_GetDamageMultiplier_Active_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            ChargeTime = 10,
            DamageMultiplier = Fp.FromInt(3)
        });

        arcanaSystem.TryActivate(0, 400);
        for (int i = 0; i < 10; i++) arcanaSystem.Update();

        Fp result = arcanaSystem.GetDamageMultiplier(0);

        Assert.Equal(Fp.FromInt(3), result);
    }

    [Fact]
    public void Arcana_GetSpecialMove_Active_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            ChargeTime = 10,
            SpecialMoveIndex = 5
        });

        arcanaSystem.TryActivate(0, 400);
        for (int i = 0; i < 10; i++) arcanaSystem.Update();

        int result = arcanaSystem.GetSpecialMove(0);

        Assert.Equal(5, result);
    }

    [Fact]
    public void Arcana_GetSpecialMove_Inactive_ReturnsMinusOne()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        arcanaSystem.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            SpecialMoveIndex = 5
        });

        int result = arcanaSystem.GetSpecialMove(0);

        Assert.Equal(-1, result);
    }

    [Fact]
    public void Arcana_GetArcana_InvalidIndex_ReturnsDefault()
    {
        var cfg = CreateConfig();
        var arcanaSystem = new ArcanaSystem(cfg);

        var result = arcanaSystem.GetArcana(-1);

        Assert.Equal(ArcanaSystem.ArcanaType.None, result.Type);
    }
}
