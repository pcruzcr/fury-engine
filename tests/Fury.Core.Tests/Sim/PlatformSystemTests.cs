using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>
/// Tests for the platform system (TMNT, Smash Bros, platform fighters).
/// </summary>
public class PlatformSystemTests
{
    private static FightConfig CreateConfig() => FightConfig.Default();

    [Fact]
    public void Platform_Add_IncreasesCount()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        var data = new PlatformSystem.PlatformData
        {
            Type = PlatformSystem.PlatformType.Solid,
            StartX = Fp.FromInt(100),
            StartY = Fp.FromInt(200),
            Width = Fp.FromInt(100),
            Height = Fp.FromInt(20)
        };

        ps.AddPlatform(data);

        Assert.Equal(1, ps.Count);
    }

    [Fact]
    public void Platform_Clear_ResetsCount()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        ps.AddPlatform(new PlatformSystem.PlatformData { Type = PlatformSystem.PlatformType.Solid });
        ps.Clear();

        Assert.Equal(0, ps.Count);
    }

    [Fact]
    public void Platform_CheckLanding_ReturnsCorrectIndex()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        ps.AddPlatform(new PlatformSystem.PlatformData
        {
            Type = PlatformSystem.PlatformType.Solid,
            StartX = Fp.FromInt(100),
            StartY = Fp.FromInt(200),
            Width = Fp.FromInt(100),
            Height = Fp.FromInt(20)
        });

        int result = ps.CheckLanding(Fp.FromInt(100), Fp.FromInt(165), Fp.Zero, Fp.FromInt(5), Fp.FromInt(30), Fp.FromInt(60));

        Assert.Equal(0, result);
    }

    [Fact]
    public void Platform_CheckLanding_NoPlatform_ReturnsMinusOne()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        int result = ps.CheckLanding(Fp.FromInt(100), Fp.FromInt(190), Fp.Zero, Fp.FromInt(5), Fp.FromInt(30), Fp.FromInt(60));

        Assert.Equal(-1, result);
    }

    [Fact]
    public void Platform_CheckSideCollision_TrueWhenColliding()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        ps.AddPlatform(new PlatformSystem.PlatformData
        {
            Type = PlatformSystem.PlatformType.Solid,
            StartX = Fp.FromInt(100),
            StartY = Fp.FromInt(200),
            Width = Fp.FromInt(100),
            Height = Fp.FromInt(20)
        });

        bool result = ps.CheckSideCollision(Fp.FromInt(100), Fp.FromInt(200), Fp.FromInt(30), Fp.FromInt(60));

        Assert.True(result);
    }

    [Fact]
    public void Platform_CheckSideCollision_FalseWhenNotColliding()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        ps.AddPlatform(new PlatformSystem.PlatformData
        {
            Type = PlatformSystem.PlatformType.Solid,
            StartX = Fp.FromInt(100),
            StartY = Fp.FromInt(200),
            Width = Fp.FromInt(100),
            Height = Fp.FromInt(20)
        });

        bool result = ps.CheckSideCollision(Fp.FromInt(500), Fp.FromInt(500), Fp.FromInt(30), Fp.FromInt(60));

        Assert.False(result);
    }

    [Fact]
    public void Platform_CheckHazard_TrueWhenTouchingHazard()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        ps.AddPlatform(new PlatformSystem.PlatformData
        {
            Type = PlatformSystem.PlatformType.Hazard,
            StartX = Fp.FromInt(100),
            StartY = Fp.FromInt(200),
            Width = Fp.FromInt(100),
            Height = Fp.FromInt(20)
        });

        bool result = ps.CheckHazard(Fp.FromInt(100), Fp.FromInt(200), Fp.FromInt(30), Fp.FromInt(60));

        Assert.True(result);
    }

    [Fact]
    public void Platform_CheckHazard_FalseWhenNotTouching()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        ps.AddPlatform(new PlatformSystem.PlatformData
        {
            Type = PlatformSystem.PlatformType.Hazard,
            StartX = Fp.FromInt(100),
            StartY = Fp.FromInt(200),
            Width = Fp.FromInt(100),
            Height = Fp.FromInt(20)
        });

        bool result = ps.CheckHazard(Fp.FromInt(500), Fp.FromInt(500), Fp.FromInt(30), Fp.FromInt(60));

        Assert.False(result);
    }

    [Fact]
    public void Platform_MovingPlatform_UpdatesPosition()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        ps.AddPlatform(new PlatformSystem.PlatformData
        {
            Type = PlatformSystem.PlatformType.Moving,
            StartX = Fp.FromInt(0),
            StartY = Fp.FromInt(200),
            EndX = Fp.FromInt(200),
            EndY = Fp.FromInt(200),
            MoveDuration = 120,
            Width = Fp.FromInt(100),
            Height = Fp.FromInt(20)
        });

        var initial = ps.GetPlatform(0);
        Assert.Equal(Fp.FromInt(0), initial.CurrentX);

        for (int i = 0; i < 60; i++) ps.Update();

        var updated = ps.GetPlatform(0);
        Assert.True(updated.CurrentX > Fp.FromInt(0));
    }

    [Fact]
    public void Platform_PassThrough_AllowsJumpThrough()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        ps.AddPlatform(new PlatformSystem.PlatformData
        {
            Type = PlatformSystem.PlatformType.PassThrough,
            StartX = Fp.FromInt(100),
            StartY = Fp.FromInt(200),
            Width = Fp.FromInt(100),
            Height = Fp.FromInt(20)
        });

        int result = ps.CheckLanding(Fp.FromInt(100), Fp.FromInt(210), Fp.Zero, Fp.FromInt(-5), Fp.FromInt(30), Fp.FromInt(60));
        Assert.Equal(-1, result);

        result = ps.CheckLanding(Fp.FromInt(100), Fp.FromInt(160), Fp.Zero, Fp.FromInt(5), Fp.FromInt(30), Fp.FromInt(60));
        Assert.Equal(0, result);
    }

    [Fact]
    public void Platform_GetBounceVelocity_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        ps.AddPlatform(new PlatformSystem.PlatformData
        {
            Type = PlatformSystem.PlatformType.Bounce,
            BounceForce = Fp.FromInt(15)
        });

        Fp result = ps.GetBounceVelocity(0);

        Assert.Equal(Fp.FromInt(15), result);
    }

    [Fact]
    public void Platform_GetConveyorSpeed_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var ps = new PlatformSystem(cfg);

        ps.AddPlatform(new PlatformSystem.PlatformData
        {
            Type = PlatformSystem.PlatformType.Conveyor,
            ConveyorSpeed = Fp.FromInt(2)
        });

        Fp result = ps.GetConveyorSpeed(0);

        Assert.Equal(Fp.FromInt(2), result);
    }
}
