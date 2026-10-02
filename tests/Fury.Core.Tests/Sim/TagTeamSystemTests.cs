using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>
/// Tests for the tag team system (Marvel vs Capcom, KOF, Capcom vs SNK).
/// </summary>
public class TagTeamSystemTests
{
    private static FightConfig CreateConfig() => FightConfig.Default();

    private static CharacterConfig CreateFighter(string id) => CharacterConfig.Create(
        id,
        1000,
        Fp.FromInt(3),
        new MoveEntry[MoveSlot.Count]);

    [Fact]
    public void TagTeam_Creation_InitializesCorrectly()
    {
        var cfg = CreateConfig();
        var teamA = new[] { CreateFighter("P1A"), CreateFighter("P1B"), CreateFighter("P1C") };
        var teamB = new[] { CreateFighter("P2A"), CreateFighter("P2B"), CreateFighter("P2C") };

        var tagSystem = new TagTeamSystem(cfg, teamA, teamB);

        Assert.Equal(0, tagSystem.ActiveIndex(0));
        Assert.Equal(0, tagSystem.ActiveIndex(1));
        Assert.Equal(1000, tagSystem.Health(0, 0));
        Assert.Equal(1000, tagSystem.Health(1, 0));
        Assert.True(tagSystem.IsAlive(0, 0));
        Assert.True(tagSystem.IsAlive(1, 0));
    }

    [Fact]
    public void TagTeam_TryTag_SwitchesActiveFighter()
    {
        var cfg = CreateConfig();
        var teamA = new[] { CreateFighter("P1A"), CreateFighter("P1B"), CreateFighter("P1C") };
        var teamB = new[] { CreateFighter("P2A"), CreateFighter("P2B"), CreateFighter("P2C") };

        var tagSystem = new TagTeamSystem(cfg, teamA, teamB);

        bool result = tagSystem.TryTag(0);

        Assert.True(result);
        Assert.Equal(1, tagSystem.ActiveIndex(0));
        Assert.Equal(120, tagSystem.TagCooldown(0));
    }

    [Fact]
    public void TagTeam_TryTag_BlockedByCooldown()
    {
        var cfg = CreateConfig();
        var teamA = new[] { CreateFighter("P1A"), CreateFighter("P1B"), CreateFighter("P1C") };
        var teamB = new[] { CreateFighter("P2A"), CreateFighter("P2B"), CreateFighter("P2C") };

        var tagSystem = new TagTeamSystem(cfg, teamA, teamB);

        tagSystem.TryTag(0);
        bool result = tagSystem.TryTag(0);

        Assert.False(result);
    }

    [Fact]
    public void TagTeam_Update_DecrementsCooldown()
    {
        var cfg = CreateConfig();
        var teamA = new[] { CreateFighter("P1A"), CreateFighter("P1B"), CreateFighter("P1C") };
        var teamB = new[] { CreateFighter("P2A"), CreateFighter("P2B"), CreateFighter("P2C") };

        var tagSystem = new TagTeamSystem(cfg, teamA, teamB);

        tagSystem.TryTag(0);
        Assert.Equal(120, tagSystem.TagCooldown(0));

        for (int i = 0; i < 60; i++) tagSystem.Update();
        Assert.Equal(60, tagSystem.TagCooldown(0));

        for (int i = 0; i < 60; i++) tagSystem.Update();
        Assert.Equal(0, tagSystem.TagCooldown(0));
    }

    [Fact]
    public void TagTeam_SetHealth_KillsFighterAtZero()
    {
        var cfg = CreateConfig();
        var teamA = new[] { CreateFighter("P1A"), CreateFighter("P1B"), CreateFighter("P1C") };
        var teamB = new[] { CreateFighter("P2A"), CreateFighter("P2B"), CreateFighter("P2C") };

        var tagSystem = new TagTeamSystem(cfg, teamA, teamB);

        tagSystem.SetHealth(0, 0, 0);

        Assert.False(tagSystem.IsAlive(0, 0));
    }

    [Fact]
    public void TagTeam_IsTeamWiped_TrueWhenAllDead()
    {
        var cfg = CreateConfig();
        var teamA = new[] { CreateFighter("P1A"), CreateFighter("P1B"), CreateFighter("P1C") };
        var teamB = new[] { CreateFighter("P2A"), CreateFighter("P2B"), CreateFighter("P2C") };

        var tagSystem = new TagTeamSystem(cfg, teamA, teamB);

        tagSystem.SetHealth(0, 0, 0);
        tagSystem.SetHealth(0, 1, 0);
        tagSystem.SetHealth(0, 2, 0);

        Assert.True(tagSystem.IsTeamWiped(0));
        Assert.False(tagSystem.IsTeamWiped(1));
    }

    [Fact]
    public void TagTeam_CountAlive_ReturnsCorrectCount()
    {
        var cfg = CreateConfig();
        var teamA = new[] { CreateFighter("P1A"), CreateFighter("P1B"), CreateFighter("P1C") };
        var teamB = new[] { CreateFighter("P2A"), CreateFighter("P2B"), CreateFighter("P2C") };

        var tagSystem = new TagTeamSystem(cfg, teamA, teamB);

        Assert.Equal(3, tagSystem.CountAlive(0));

        tagSystem.SetHealth(0, 0, 0);
        Assert.Equal(2, tagSystem.CountAlive(0));

        tagSystem.SetHealth(0, 1, 0);
        Assert.Equal(1, tagSystem.CountAlive(0));
    }

    [Fact]
    public void TagTeam_AddMeter_ClampsToMax()
    {
        var cfg = CreateConfig();
        var teamA = new[] { CreateFighter("P1A"), CreateFighter("P1B"), CreateFighter("P1C") };
        var teamB = new[] { CreateFighter("P2A"), CreateFighter("P2B"), CreateFighter("P2C") };

        var tagSystem = new TagTeamSystem(cfg, teamA, teamB);

        tagSystem.AddMeter(0, 500);
        Assert.Equal(500, tagSystem.Meter(0));

        tagSystem.AddMeter(0, 1000);
        Assert.Equal(1000, tagSystem.Meter(0));
    }

    [Fact]
    public void TagTeam_TryTag_NoAlivePartner_ReturnsFalse()
    {
        var cfg = CreateConfig();
        var teamA = new[] { CreateFighter("P1A"), CreateFighter("P1B"), CreateFighter("P1C") };
        var teamB = new[] { CreateFighter("P2A"), CreateFighter("P2B"), CreateFighter("P2C") };

        var tagSystem = new TagTeamSystem(cfg, teamA, teamB);

        tagSystem.SetHealth(0, 1, 0);
        tagSystem.SetHealth(0, 2, 0);

        bool result = tagSystem.TryTag(0);

        Assert.False(result);
    }
}
