using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>
/// Tests for the transformation system (Bloody Roar, Dragon Ball).
/// </summary>
public class TransformationSystemTests
{
    private static FightConfig CreateConfig() => FightConfig.Default();

    [Fact]
    public void Transformation_Add_IncreasesCount()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        ts.AddForm(new TransformationSystem.FormData { Type = TransformationSystem.TransformationType.BeastForm });

        Assert.Equal(1, ts.Count);
    }

    [Fact]
    public void Transformation_Clear_ResetsCount()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        ts.AddForm(new TransformationSystem.FormData { Type = TransformationSystem.TransformationType.BeastForm });
        ts.Clear();

        Assert.Equal(0, ts.Count);
    }

    [Fact]
    public void Transformation_TryActivate_Success()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        ts.AddForm(new TransformationSystem.FormData
        {
            Type = TransformationSystem.TransformationType.BeastForm,
            MeterCost = 500
        });

        bool result = ts.TryActivate(0, 600);

        Assert.True(result);
        Assert.Equal(TransformationSystem.TransformationState.Charging, ts.GetForm(0).State);
    }

    [Fact]
    public void Transformation_TryActivate_NotEnoughMeter_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        ts.AddForm(new TransformationSystem.FormData
        {
            Type = TransformationSystem.TransformationType.BeastForm,
            MeterCost = 500
        });

        bool result = ts.TryActivate(0, 400);

        Assert.False(result);
    }

    [Fact]
    public void Transformation_TryActivate_AlreadyActive_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        ts.AddForm(new TransformationSystem.FormData
        {
            Type = TransformationSystem.TransformationType.BeastForm,
            MeterCost = 500
        });

        ts.TryActivate(0, 600);
        bool result = ts.TryActivate(0, 600);

        Assert.False(result);
    }

    [Fact]
    public void Transformation_Update_ChargingToActive()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        ts.AddForm(new TransformationSystem.FormData
        {
            Type = TransformationSystem.TransformationType.BeastForm,
            ChargeTime = 60
        });

        ts.TryActivate(0, 600);
        Assert.Equal(TransformationSystem.TransformationState.Charging, ts.GetForm(0).State);

        for (int i = 0; i < 60; i++) ts.Update();

        Assert.Equal(TransformationSystem.TransformationState.Active, ts.GetForm(0).State);
    }

    [Fact]
    public void Transformation_Update_ActiveToCooldown()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        ts.AddForm(new TransformationSystem.FormData
        {
            Type = TransformationSystem.TransformationType.BeastForm,
            ChargeTime = 10,
            Duration = 60
        });

        ts.TryActivate(0, 600);
        for (int i = 0; i < 10; i++) ts.Update();
        Assert.Equal(TransformationSystem.TransformationState.Active, ts.GetForm(0).State);

        for (int i = 0; i < 60; i++) ts.Update();

        Assert.Equal(TransformationSystem.TransformationState.Cooldown, ts.GetForm(0).State);
    }

    [Fact]
    public void Transformation_GetStatMultiplier_Inactive_ReturnsOne()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        ts.AddForm(new TransformationSystem.FormData
        {
            Type = TransformationSystem.TransformationType.BeastForm,
            SpeedMultiplier = Fp.FromInt(2)
        });

        Fp result = ts.GetStatMultiplier(0, TransformationSystem.StatType.Speed);

        Assert.Equal(Fp.One, result);
    }

    [Fact]
    public void Transformation_GetStatMultiplier_Active_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        ts.AddForm(new TransformationSystem.FormData
        {
            Type = TransformationSystem.TransformationType.BeastForm,
            ChargeTime = 10,
            SpeedMultiplier = Fp.FromInt(2)
        });

        ts.TryActivate(0, 600);
        for (int i = 0; i < 10; i++) ts.Update();

        Fp result = ts.GetStatMultiplier(0, TransformationSystem.StatType.Speed);

        Assert.Equal(Fp.FromInt(2), result);
    }

    [Fact]
    public void Transformation_GetDamageMultiplier_Active_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        ts.AddForm(new TransformationSystem.FormData
        {
            Type = TransformationSystem.TransformationType.BeastForm,
            ChargeTime = 10,
            DamageMultiplier = Fp.FromInt(3)
        });

        ts.TryActivate(0, 600);
        for (int i = 0; i < 10; i++) ts.Update();

        Fp result = ts.GetDamageMultiplier(0);

        Assert.Equal(Fp.FromInt(3), result);
    }

    [Fact]
    public void Transformation_GetForm_InvalidIndex_ReturnsDefault()
    {
        var cfg = CreateConfig();
        var ts = new TransformationSystem(cfg);

        var result = ts.GetForm(-1);

        Assert.Equal(TransformationSystem.TransformationType.None, result.Type);
    }
}
