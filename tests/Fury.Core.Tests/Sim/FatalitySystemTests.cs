using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>
/// Tests for the fatality/finisher system (Mortal Kombat).
/// </summary>
public class FatalitySystemTests
{
    private static FightConfig CreateConfig() => FightConfig.Default();

    [Fact]
    public void Fatality_Add_IncreasesCount()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData { Type = FatalitySystem.FatalityType.Fatality });

        Assert.Equal(1, fs.Count);
    }

    [Fact]
    public void Fatality_Clear_ResetsCount()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData { Type = FatalitySystem.FatalityType.Fatality });
        fs.Clear();

        Assert.Equal(0, fs.Count);
    }

    [Fact]
    public void Fatality_IsAvailable_HealthBelowThreshold_ReturnsTrue()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
            HealthThreshold = 200,
            State = FatalitySystem.FatalityState.Available
        });

        bool result = fs.IsAvailable(0, 100, 150);

        Assert.True(result);
    }

    [Fact]
    public void Fatality_IsAvailable_HealthAboveThreshold_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
            HealthThreshold = 200,
            State = FatalitySystem.FatalityState.Available
        });

        bool result = fs.IsAvailable(0, 100, 250);

        Assert.False(result);
    }

    [Fact]
    public void Fatality_IsAvailable_NotAvailable_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
            HealthThreshold = 200,
            State = FatalitySystem.FatalityState.Executing
        });

        bool result = fs.IsAvailable(0, 100, 150);

        Assert.False(result);
    }

    [Fact]
    public void Fatality_TryStartInput_Success()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
            State = FatalitySystem.FatalityState.Available
        });

        bool result = fs.TryStartInput(0);

        Assert.True(result);
        Assert.Equal(FatalitySystem.FatalityState.Inputting, fs.GetFatality(0).State);
    }

    [Fact]
    public void Fatality_TryStartInput_NotAvailable_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
            State = FatalitySystem.FatalityState.Executing
        });

        bool result = fs.TryStartInput(0);

        Assert.False(result);
    }

    [Fact]
    public void Fatality_TryExecute_Success()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
            State = FatalitySystem.FatalityState.Available
        });

        fs.TryStartInput(0);
        bool result = fs.TryExecute(0);

        Assert.True(result);
        Assert.Equal(FatalitySystem.FatalityState.Executing, fs.GetFatality(0).State);
    }

    [Fact]
    public void Fatality_TryExecute_NotInputting_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
            State = FatalitySystem.FatalityState.Available
        });

        bool result = fs.TryExecute(0);

        Assert.False(result);
    }

    [Fact]
    public void Fatality_Update_InputTimeout_ReturnsToAvailable()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
            InputWindow = 60,
            State = FatalitySystem.FatalityState.Available
        });

        fs.TryStartInput(0);
        Assert.Equal(FatalitySystem.FatalityState.Inputting, fs.GetFatality(0).State);

        for (int i = 0; i < 60; i++) fs.Update();

        Assert.Equal(FatalitySystem.FatalityState.Available, fs.GetFatality(0).State);
    }

    [Fact]
    public void Fatality_Update_ExecutionCompletes()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
            ExecutionDuration = 60,
            State = FatalitySystem.FatalityState.Available
        });

        fs.TryStartInput(0);
        fs.TryExecute(0);
        Assert.Equal(FatalitySystem.FatalityState.Executing, fs.GetFatality(0).State);

        for (int i = 0; i < 60; i++) fs.Update();

        Assert.Equal(FatalitySystem.FatalityState.Completed, fs.GetFatality(0).State);
    }

    [Fact]
    public void Fatality_GetFatalityType_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality
        });

        var result = fs.GetFatalityType(0);

        Assert.Equal(FatalitySystem.FatalityType.Fatality, result);
    }

    [Fact]
    public void Fatality_GetInputSequence_ReturnsCorrectValue()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
        });
        fs.SetInputs(0, new[] { 1, 2, 3, 4 });

        var result = fs.GetInputSequence(0);

        Assert.Equal(new[] { 1, 2, 3, 4 }, result);
    }

    [Fact]
    public void Fatality_VerifyInputs_MatchesStoredSequence()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        fs.AddFatality(new FatalitySystem.FatalityData
        {
            Type = FatalitySystem.FatalityType.Fatality,
        });
        fs.SetInputs(0, new[] { 1, 2, 3, 4 });

        Assert.True(fs.VerifyInputs(0, new[] { 1, 2, 3, 4 }));
        Assert.False(fs.VerifyInputs(0, new[] { 1, 2, 3 }));
        Assert.False(fs.VerifyInputs(0, new[] { 1, 2, 3, 5 }));
        Assert.False(fs.VerifyInputs(-1, new[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void Fatality_GetFatality_InvalidIndex_ReturnsDefault()
    {
        var cfg = CreateConfig();
        var fs = new FatalitySystem(cfg);

        var result = fs.GetFatality(-1);

        Assert.Equal(FatalitySystem.FatalityType.Fatality, result.Type);
    }
}
