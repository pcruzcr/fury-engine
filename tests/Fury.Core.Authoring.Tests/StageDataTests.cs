using Fury.Core.Authoring;
using Xunit;

namespace Fury.Core.Authoring.Tests;

public class StageEditorTests
{
    [Fact]
    public void StageData_DefaultValues_AreValid()
    {
        var stage = new StageData
        {
            Name = "Test Stage",
            Width = 1920,
            Height = 1080,
            Gravity = 1.0f,
            BackgroundColor = new ColorRgba(0.1f, 0.1f, 0.2f)
        };

        Assert.Equal("Test Stage", stage.Name);
        Assert.Equal(1920, stage.Width);
        Assert.Equal(1080, stage.Height);
        Assert.Equal(1.0f, stage.Gravity);
        Assert.Empty(stage.Platforms);
        Assert.Empty(stage.Hazards);
    }

    [Fact]
    public void StageData_AddPlatform_IncreasesCount()
    {
        var stage = new StageData();
        stage.Platforms.Add(new PlatformData { X = 0, Y = 0, Width = 200, Height = 50 });

        Assert.Single(stage.Platforms);
        Assert.Equal(200, stage.Platforms[0].Width);
    }

    [Fact]
    public void StageData_AddHazard_IncreasesCount()
    {
        var stage = new StageData();
        stage.Hazards.Add(new HazardData { X = 100, Y = 100, Width = 50, Height = 50, Damage = 10 });

        Assert.Single(stage.Hazards);
        Assert.Equal(10, stage.Hazards[0].Damage);
    }

    [Fact]
    public void PlatformData_Properties_AreSettable()
    {
        var platform = new PlatformData
        {
            X = 10,
            Y = 20,
            Width = 300,
            Height = 60
        };

        Assert.Equal(10, platform.X);
        Assert.Equal(20, platform.Y);
        Assert.Equal(300, platform.Width);
        Assert.Equal(60, platform.Height);
    }

    [Fact]
    public void HazardData_Properties_AreSettable()
    {
        var hazard = new HazardData
        {
            X = 50,
            Y = 60,
            Width = 100,
            Height = 100,
            Damage = 25
        };

        Assert.Equal(50, hazard.X);
        Assert.Equal(60, hazard.Y);
        Assert.Equal(100, hazard.Width);
        Assert.Equal(100, hazard.Height);
        Assert.Equal(25, hazard.Damage);
    }
}
