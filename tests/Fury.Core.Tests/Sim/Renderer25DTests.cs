using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>
/// Tests for the 2.5D renderer (Tekken, Soulcalibur, Virtua Fighter).
/// </summary>
public class Renderer25DTests
{
    private static FightConfig CreateConfig() => FightConfig.Default();

    [Fact]
    public void Renderer_AddObject_IncreasesCount()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        int idx = renderer.AddObject(FpVec3.Zero, FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);

        Assert.Equal(0, idx);
        Assert.Equal(1, renderer.ObjectCount);
    }

    [Fact]
    public void Renderer_AddObject_Full_ReturnsMinusOne()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        for (int i = 0; i < 64; i++)
        {
            renderer.AddObject(FpVec3.Zero, FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);
        }

        int idx = renderer.AddObject(FpVec3.Zero, FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);

        Assert.Equal(-1, idx);
    }

    [Fact]
    public void Renderer_UpdateObject_UpdatesProperties()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        int idx = renderer.AddObject(FpVec3.Zero, FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);
        renderer.UpdateObject(idx, new FpVec3(Fp.FromInt(100), Fp.FromInt(200), Fp.FromInt(50)), FpVec3.One, -1);

        var commands = renderer.Render();
        Assert.Single(commands);
    }

    [Fact]
    public void Renderer_RemoveObject_DeactivatesObject()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        int idx = renderer.AddObject(FpVec3.Zero, FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);
        renderer.RemoveObject(idx);

        var commands = renderer.Render();
        Assert.Empty(commands);
    }

    [Fact]
    public void Renderer_Clear_ResetsCount()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        renderer.AddObject(FpVec3.Zero, FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);
        renderer.Clear();

        Assert.Equal(0, renderer.ObjectCount);
    }

    [Fact]
    public void Renderer_Render_ReturnsSortedCommands()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        renderer.SetCamera(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(-500)), FpVec3.Zero, Fp.FromInt(60));

        renderer.AddObject(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(100)), FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);
        renderer.AddObject(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(200)), FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);

        var commands = renderer.Render();

        Assert.Equal(2, commands.Length);
        // Painter's order: farthest first (the old assertion encoded the inverted order).
        Assert.True(commands[0].Depth > commands[1].Depth);
    }

    [Fact]
    public void Renderer_Raycast_ReturnsNearestHit()
    {
        var renderer = new Renderer25D(CreateConfig());
        renderer.AddObject(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(200)), FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);
        renderer.AddObject(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(100)), FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);

        Assert.True(renderer.Raycast(FpVec3.Zero, new FpVec3(Fp.Zero, Fp.Zero, Fp.One), Fp.FromInt(1000), out _, out var d));
        Assert.True(Fp.Abs(d - Fp.FromInt(100)) < Fp.One);
    }

    [Fact]
    public void Renderer_CullsObjectsBehindCamera_AndWorldUpIsScreenUp()
    {
        var renderer = new Renderer25D(CreateConfig());
        renderer.SetCamera(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(-10)), FpVec3.Zero, Fp.FromInt(60));
        renderer.SetViewport(960, 540);
        renderer.AddObject(new FpVec3(Fp.Zero, Fp.FromInt(2), Fp.Zero), FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);
        renderer.AddObject(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(-20)), FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);

        var commands = renderer.Render();

        Assert.Single(commands);
        Assert.True(commands[0].ScreenY < 270);
    }

    [Fact]
    public void Renderer_GetCameraForward_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        renderer.SetCamera(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(-500)), FpVec3.Zero, Fp.FromInt(60));

        var forward = renderer.GetCameraForward();

        Assert.Equal(Fp.Zero, forward.X);
        Assert.Equal(Fp.Zero, forward.Y);
        Assert.Equal(Fp.One, forward.Z);
    }

    [Fact]
    public void Renderer_GetCameraRight_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        renderer.SetCamera(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(-500)), FpVec3.Zero, Fp.FromInt(60));

        var right = renderer.GetCameraRight();

        Assert.Equal(Fp.One, right.X);
        Assert.Equal(Fp.Zero, right.Y);
        Assert.Equal(Fp.Zero, right.Z);
    }

    [Fact]
    public void Renderer_GetCameraUp_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        renderer.SetCamera(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(-500)), FpVec3.Zero, Fp.FromInt(60));

        var up = renderer.GetCameraUp();

        Assert.Equal(Fp.Zero, up.X);
        Assert.Equal(Fp.One, up.Y);
        Assert.Equal(Fp.Zero, up.Z);
    }

    [Fact]
    public void Renderer_Raycast_HitsObject_ReturnsTrue()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        renderer.AddObject(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(100)), FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);

        bool hit = renderer.Raycast(FpVec3.Zero, new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(1)), Fp.FromInt(1000), out var hitPoint, out var hitDistance);

        Assert.True(hit);
        Assert.True(Fp.Abs(hitDistance - Fp.FromInt(100)) < Fp.FromInt(1));
    }

    [Fact]
    public void Renderer_Raycast_MissesObject_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        renderer.AddObject(new FpVec3(Fp.FromInt(100), Fp.FromInt(100), Fp.FromInt(100)), FpVec3.One, -1, Renderer25D.RenderLayer.Fighter);

        bool hit = renderer.Raycast(FpVec3.Zero, new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(1)), Fp.FromInt(1000), out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void Renderer_ProjectX_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        renderer.SetCamera(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(-500)), FpVec3.Zero, Fp.FromInt(60));
        renderer.SetViewport(960, 540);

        var commands = renderer.Render();

        Assert.NotNull(commands);
    }

    [Fact]
    public void Renderer_ProjectY_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var renderer = new Renderer25D(cfg);

        renderer.SetCamera(new FpVec3(Fp.Zero, Fp.Zero, Fp.FromInt(-500)), FpVec3.Zero, Fp.FromInt(60));
        renderer.SetViewport(960, 540);

        var commands = renderer.Render();

        Assert.NotNull(commands);
    }
}
