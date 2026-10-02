using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>
/// Tests for the wrestling/throw system (Dead or Alive 2D, Rumble Roses).
/// </summary>
public class WrestlingSystemTests
{
    private static FightConfig CreateConfig() => FightConfig.Default();

    [Fact]
    public void Wrestling_AddHold_IncreasesCount()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData { Type = WrestlingSystem.HoldType.FrontHold });

        Assert.Equal(1, ws.Count);
    }

    [Fact]
    public void Wrestling_Clear_ResetsCount()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData { Type = WrestlingSystem.HoldType.FrontHold });
        ws.Clear();

        Assert.Equal(0, ws.Count);
    }

    [Fact]
    public void Wrestling_TryInitiateHold_Success()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData { Type = WrestlingSystem.HoldType.FrontHold });

        bool result = ws.TryInitiateHold(0, 0, 1);

        Assert.True(result);
        Assert.Equal(WrestlingSystem.HoldState.Active, ws.GetHold(0).State);
    }

    [Fact]
    public void Wrestling_TryInitiateHold_AlreadyActive_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData { Type = WrestlingSystem.HoldType.FrontHold });

        ws.TryInitiateHold(0, 0, 1);
        bool result = ws.TryInitiateHold(0, 0, 1);

        Assert.False(result);
    }

    [Fact]
    public void Wrestling_TryEscape_Success()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData { Type = WrestlingSystem.HoldType.FrontHold });
        ws.TryInitiateHold(0, 0, 1);

        bool result = ws.TryEscape(0);

        Assert.True(result);
        Assert.Equal(WrestlingSystem.HoldState.Escaping, ws.GetHold(0).State);
    }

    [Fact]
    public void Wrestling_TryEscape_NotActive_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData { Type = WrestlingSystem.HoldType.FrontHold });

        bool result = ws.TryEscape(0);

        Assert.False(result);
    }

    [Fact]
    public void Wrestling_TryCounter_Success()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData { Type = WrestlingSystem.HoldType.FrontHold });
        ws.TryInitiateHold(0, 0, 1);

        bool result = ws.TryCounter(0);

        Assert.True(result);
        Assert.Equal(WrestlingSystem.HoldState.Countering, ws.GetHold(0).State);
    }

    [Fact]
    public void Wrestling_GetThrowDamage_ActiveHold_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData
        {
            Type = WrestlingSystem.HoldType.FrontHold,
            Damage = 150
        });
        ws.TryInitiateHold(0, 0, 1);

        int result = ws.GetThrowDamage(0);

        Assert.Equal(150, result);
    }

    [Fact]
    public void Wrestling_GetThrowDamage_InactiveHold_ReturnsZero()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData
        {
            Type = WrestlingSystem.HoldType.FrontHold,
            Damage = 150
        });

        int result = ws.GetThrowDamage(0);

        Assert.Equal(0, result);
    }

    [Fact]
    public void Wrestling_GetThrowDirection_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData
        {
            Type = WrestlingSystem.HoldType.FrontHold,
            Direction = WrestlingSystem.ThrowDirection.Back
        });

        var result = ws.GetThrowDirection(0);

        Assert.Equal(WrestlingSystem.ThrowDirection.Back, result);
    }

    [Fact]
    public void Wrestling_Update_EscapeProgresses()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        ws.AddHold(new WrestlingSystem.HoldData
        {
            Type = WrestlingSystem.HoldType.FrontHold,
            EscapeThreshold = 30
        });
        ws.TryInitiateHold(0, 0, 1);
        ws.TryEscape(0);

        for (int i = 0; i < 15; i++) ws.Update();

        Assert.True(ws.GetHold(0).EscapeProgress > 0);
    }

    [Fact]
    public void Wrestling_GetHold_InvalidIndex_ReturnsDefault()
    {
        var cfg = CreateConfig();
        var ws = new WrestlingSystem(cfg);

        var result = ws.GetHold(-1);

        Assert.Equal(WrestlingSystem.HoldType.FrontHold, result.Type);
    }
}
