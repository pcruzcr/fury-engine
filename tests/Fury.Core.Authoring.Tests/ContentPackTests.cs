using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Authoring.Tests;

/// <summary>Character packs as data: manifest parsing and validation, loading into a config, roster registration.</summary>
[Collection("roster")]
public class ContentPackTests
{
    private const string MinimalJson = """
        {
          // hand-written files may carry comments and trailing commas
          "schema": 1,
          "id": "testchar",
          "displayName": "Test Char",
          "baseOn": "glen",
          "stats": { "maxHealth": 900, "walkForward": 0.12, "dashSpeed": 0.3, },
          "moveNames": ["jab", "", "fierce"],
          "colors": { "primary": "#ff8800" },
        }
        """;

    private static byte[]? NoFiles(string path) => null;

    private static CharacterPackResult Load(string json, Func<string, byte[]?>? files = null) =>
        CharacterPackLoader.Load(json, files ?? NoFiles, "test");

    private static string WithVisual(string spritesJson) => $$"""
        { "schema": 1, "id": "artchar", "baseOn": "glen", "visual": { "sprites": {{spritesJson}} } }
        """;

    [Fact]
    public void Manifest_ParsesCommentsAndTrailingCommas_AndRoundTrips()
    {
        var manifest = CharacterManifestSerializer.FromJson(MinimalJson);
        Assert.Equal("testchar", manifest.Id);
        Assert.Equal(900, manifest.Stats.MaxHealth);
        Assert.Equal(new[] { "jab", "", "fierce" }, manifest.MoveNames);
        Assert.False(manifest.Validate().HasErrors);

        var again = CharacterManifestSerializer.FromJson(CharacterManifestSerializer.ToJson(manifest));
        Assert.Equal(manifest.Id, again.Id);
        Assert.Equal(manifest.Stats.WalkForward, again.Stats.WalkForward);
        Assert.Equal(manifest.MoveNames, again.MoveNames);
        Assert.Equal(manifest.Colors.Primary, again.Colors.Primary);
    }

    [Fact]
    public void Manifest_NullSectionsInHandWrittenJson_AreNormalised()
    {
        var manifest = CharacterManifestSerializer.FromJson("""{ "id": "nulls", "baseOn": "glen", "stats": null, "visual": null, "audio": null, "colors": null, "moveNames": null }""");
        Assert.NotNull(manifest.Stats);
        Assert.NotNull(manifest.Visual);
        Assert.NotNull(manifest.Audio.Sfx);
        Assert.False(manifest.Validate().HasErrors);
    }

    [Theory]
    [InlineData("""{ "id": "Bad Id", "baseOn": "glen" }""", "id must be")]
    [InlineData("""{ "id": "x", "baseOn": "glen" }""", "id must be")]
    [InlineData("""{ "id": "okid" }""", "either 'moves'")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "schema": 2 }""", "schema must be")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "stats": { "maxHealth": 5 } }""", "maxHealth")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "stats": { "walkForward": 7 } }""", "walkForward")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "stats": { "dashFrames": 500 } }""", "dashFrames")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "colors": { "primary": "blue" } }""", "colors.primary")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "moveNames": ["Jab"] }""", "lowercase")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "moveNames": ["jab", "jab"] }""", "used twice")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "moveNames": ["idle"] }""", "clashes")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "portrait": "../secret.png" }""", "relative path")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "portrait": "face.exe" }""", "must end with")]
    [InlineData("""{ "id": "okid", "moves": "/etc/passwd.fmt2" }""", "relative path")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "visual": { "scale": 0 } }""", "visual.scale")]
    [InlineData("""{ "id": "okid", "baseOn": "glen", "audio": { "sfx": { "hit_light": "C:/x.wav" } } }""", "relative path")]
    public void Manifest_RejectsInvalidValues(string json, string expected)
    {
        var report = CharacterManifestSerializer.FromJson(json).Validate();
        Assert.True(report.HasErrors);
        Assert.Contains(report.Issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_NotJson_IsReportedNotThrown()
    {
        Assert.Throws<InvalidDataException>(() => CharacterManifestSerializer.FromJson("not json"));
        var result = Load("{ broken");
        Assert.Null(result.Pack);
        Assert.True(result.Report.HasErrors);
    }

    [Fact]
    public void Sprites_Validation_RequiresIdle_AndSaneNumbers_AndWarnsAboutMissingAnimations()
    {
        var ok = CharacterManifestSerializer.FromJson(WithVisual("""{ "frameWidth": 128, "frameHeight": 128, "pivotX": 64, "pivotY": 120, "animations": { "idle": { "file": "idle.png", "frames": 6, "fps": 10 } } }"""));
        var report = ok.Validate();
        Assert.False(report.HasErrors);
        Assert.Contains(report.Issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("'walk_forward'", StringComparison.Ordinal));

        string[] broken =
        [
            """{ "animations": { "walk_forward": { "file": "w.png", "frames": 4 } } }""",                              // no idle
            """{ "frameWidth": 4, "animations": { "idle": { "file": "i.png" } } }""",                                    // frame too small
            """{ "pivotX": 999, "animations": { "idle": { "file": "i.png" } } }""",                                       // pivot outside
            """{ "animations": { "idle": { "file": "i.png", "frames": 0 } } }""",                                        // no frames
            """{ "animations": { "idle": { "file": "i.png", "fps": 500 } } }""",                                         // fps
            """{ "animations": { "idle": { "file": "../i.png" } } }""",                                                  // escapes the pack
            """{ "animations": { "Idle": { "file": "i.png" } } }""",                                                     // bad name
            """{ "animations": {} }""",                                                                                   // empty
        ];
        foreach (string sprites in broken)
        {
            Assert.True(CharacterManifestSerializer.FromJson(WithVisual(sprites)).Validate().HasErrors, sprites);
        }
    }

    [Fact]
    public void Sprites_UnknownAnimationName_WarnsThatItWillNeverPlay_ButMoveNamesAreKnown()
    {
        string json = """
            { "schema": 1, "id": "artchar", "baseOn": "glen", "moveNames": ["jab"],
              "visual": { "sprites": { "animations": {
                 "idle": { "file": "i.png" }, "dance": { "file": "d.png" }, "jab": { "file": "j.png" }, "move_9": { "file": "m.png" } } } } }
            """;
        var report = CharacterManifestSerializer.FromJson(json).Validate();
        Assert.False(report.HasErrors);
        Assert.Contains(report.Issues, i => i.Where.Contains("'dance'", StringComparison.Ordinal) && i.Severity == IssueSeverity.Warning);
        Assert.DoesNotContain(report.Issues, i => i.Where.Contains("'jab'", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Issues, i => i.Where.Contains("'move_9'", StringComparison.Ordinal));
    }

    [Fact]
    public void Loader_BaseOn_CopiesMoves_AndConvertsStatsToExactFixedPoint()
    {
        var result = Load(MinimalJson);
        Assert.NotNull(result.Pack);
        var config = result.Pack!.Config;
        var glen = CharacterRoster.Create("glen");

        Assert.Equal("testchar", config.Id);
        Assert.Equal(900, config.MaxHealth);
        Assert.Equal(Fp.FromRatio(120, 1000), config.WalkForwardRaw);
        Assert.Equal(Fp.FromRatio(300, 1000), config.DashSpeed);
        Assert.Equal(glen.Moves.ContentHash, config.Moves.ContentHash);
        Assert.NotEqual(glen.Fingerprint(), config.Fingerprint()); // id and stats differ
    }

    [Fact]
    public void Loader_MovesFile_WinsOverBaseOn()
    {
        var kira = CharacterRoster.Create("kira");
        byte[] bytes = MoveTableDocument.FromTable(kira.Moves).ToBytes();
        string json = """{ "schema": 1, "id": "kirafan", "moves": "moves.fmt2", "baseOn": "glen" }""";

        var result = Load(json, path => path == "moves.fmt2" ? bytes : null);
        Assert.NotNull(result.Pack);
        Assert.Equal(kira.Moves.ContentHash, result.Pack!.Config.Moves.ContentHash);
    }

    [Fact]
    public void Loader_ReportsEveryWayTheMovesCanBeWrong()
    {
        Assert.Contains("not found", Load("""{ "id": "mvfile", "moves": "moves.fmt2" }""").Report.Issues[0].Message, StringComparison.Ordinal);
        Assert.Contains("invalid", Load("""{ "id": "mvfile", "moves": "moves.fmt2" }""", _ => [1, 2, 3]).Report.Issues[0].Message, StringComparison.Ordinal);
        Assert.Contains("not installed", Load("""{ "id": "mvfile", "baseOn": "nobody" }""").Report.Issues[0].Message, StringComparison.Ordinal);
        Assert.Contains("itself", Load("""{ "id": "mvfile", "baseOn": "mvfile" }""").Report.Issues[0].Message, StringComparison.Ordinal);

        // A table with fewer than the canonical slots is refused.
        var glen = CharacterRoster.Create("glen");
        var small = new MoveTableDocument(Enumerable.Range(0, 3).Select(glen.Moves.GetMove));
        var tooSmall = Load("""{ "id": "mvfile", "moves": "moves.fmt2" }""", _ => small.ToBytes());
        Assert.Null(tooSmall.Pack);
        Assert.Contains("canonical", tooSmall.Report.Issues[0].Message, StringComparison.Ordinal);

        // A move that breaks the rules is refused too.
        var bad = MoveTableDocument.FromTable(glen.Moves).ToBytes();
        var doc = MoveTableDocument.FromTable(glen.Moves);
        var wrecked = new MoveTableDocument(Enumerable.Range(0, doc.Count).Select(i => i == 2 ? doc.Get(i) with { ActiveFrames = 0 } : doc.Get(i)));
        var rejected = Load("""{ "id": "mvfile", "moves": "moves.fmt2" }""", _ => wrecked.ToBytes());
        Assert.Null(rejected.Pack);
        Assert.NotEmpty(bad);
    }

    [Fact]
    public void VisualFields_DoNotChangeTheFingerprint_ButStatsDo()
    {
        ulong Fingerprint(string extra) => Load($$"""{ "schema": 1, "id": "fp", "baseOn": "glen" {{extra}} }""").Pack!.Config.Fingerprint();

        ulong plain = Fingerprint("");
        Assert.Equal(plain, Fingerprint(""","displayName": "Other", "colors": { "primary": "#112233" }, "visual": { "scale": 2 }, "moveNames": ["a"]"""));
        Assert.NotEqual(plain, Fingerprint(""","stats": { "maxHealth": 1100 }"""));
        Assert.NotEqual(plain, Fingerprint(""","stats": { "walkForward": 0.2 }"""));
    }

    [Fact]
    public void Roster_RegisterCreateOverrideAndUnregister()
    {
        var pack = Load(MinimalJson).Pack!;
        try
        {
            CharacterRoster.Register(pack.Config);
            Assert.Contains("testchar", CharacterRoster.Ids);
            Assert.True(CharacterRoster.Contains("TESTCHAR"));
            Assert.Equal("testchar", CharacterRoster.Create("TestChar").Id);
            Assert.Equal(new[] { "glen", "kira" }, CharacterRoster.Ids.Take(2));

            // Editing the move table through the override works for registered characters as well.
            var glen = CharacterRoster.Create("glen");
            CharacterRoster.SetMoveTableOverride("testchar", glen.Moves);
            Assert.Equal(glen.Moves.ContentHash, CharacterRoster.Create("testchar").Moves.ContentHash);
            CharacterRoster.SetMoveTableOverride("testchar", null);

            // Registering again replaces instead of duplicating.
            CharacterRoster.Register(pack.Config with { MaxHealth = 1234 });
            Assert.Single(CharacterRoster.Ids, "testchar");
            Assert.Equal(1234, CharacterRoster.Create("testchar").MaxHealth);
        }
        finally
        {
            Assert.True(CharacterRoster.Unregister("testchar"));
        }

        Assert.False(CharacterRoster.Contains("testchar"));
        Assert.Throws<ArgumentException>(() => CharacterRoster.Create("testchar"));
        Assert.False(CharacterRoster.Unregister("testchar"));
    }

    [Fact]
    public void Roster_RefusesToReplaceBuiltIns_AndTablesWithoutTheCanonicalSlots()
    {
        var glen = CharacterRoster.Create("glen");
        Assert.Throws<ArgumentException>(() => CharacterRoster.Register(glen));
        Assert.Throws<ArgumentException>(() => CharacterRoster.Register(glen with { Id = "KIRA" }));
        Assert.True(CharacterRoster.IsBuiltIn("Glen"));
        Assert.False(CharacterRoster.IsBuiltIn("testchar"));
    }

    // ------------------------------------------------------------------ animation vocabulary

    [Fact]
    public void AnimationStates_MirrorTheSimulationStateIds()
    {
        Assert.Equal(FighterStateId.Idle, AnimationStates.Idle);
        Assert.Equal(FighterStateId.WalkForward, AnimationStates.WalkForward);
        Assert.Equal(FighterStateId.WalkBack, AnimationStates.WalkBack);
        Assert.Equal(FighterStateId.Crouch, AnimationStates.Crouch);
        Assert.Equal(FighterStateId.Jump, AnimationStates.Jump);
        Assert.Equal(FighterStateId.Attack, AnimationStates.Attack);
        Assert.Equal(FighterStateId.Hitstun, AnimationStates.Hitstun);
        Assert.Equal(FighterStateId.Blockstun, AnimationStates.Blockstun);
        Assert.Equal(FighterStateId.Knockdown, AnimationStates.Knockdown);
        Assert.Equal(FighterStateId.Ko, AnimationStates.Ko);
        Assert.Equal(FighterStateId.Dash, AnimationStates.Dash);
        Assert.Equal(FighterStateId.Backdash, AnimationStates.Backdash);
    }

    [Fact]
    public void AnimationNames_MoveNaming_FollowsAuthorThenSlotThenIndex()
    {
        string[] names = ["jab", "", "fierce"];
        Assert.Equal("jab", AnimationNames.MoveName(names, 0));
        Assert.Equal("stand_medium", AnimationNames.MoveName(names, 1)); // empty keeps the slot name
        Assert.Equal("fierce", AnimationNames.MoveName(names, 2));
        Assert.Equal("crouch_light", AnimationNames.MoveName(names, 3));
        Assert.Equal("move_9", AnimationNames.MoveName(names, 9));
        Assert.Equal("super", AnimationNames.MoveName(null, MoveSlot.Super));

        Assert.True(AnimationNames.IsValidName("walk_forward"));
        Assert.False(AnimationNames.IsValidName("Walk"));
        Assert.False(AnimationNames.IsValidName("a b"));
        Assert.False(AnimationNames.IsValidName("1up"));
        Assert.False(AnimationNames.IsValidName(""));
    }

    [Fact]
    public void Resolver_FollowsTheFallbackChains_AndCaches()
    {
        var available = new HashSet<string> { "idle", "walk_forward", "attack", "jab", "hit", "attack_crouch", "move_5" };
        int lookups = 0;
        var resolver = new AnimationResolver(["jab"], name =>
        {
            lookups++;
            return available.Contains(name);
        });

        Assert.Equal("idle", resolver.Resolve(AnimationStates.Idle, -1, false, false, false));
        Assert.Equal("walk_forward", resolver.Resolve(AnimationStates.WalkForward, -1, false, false, false));
        Assert.Equal("walk_forward", resolver.Resolve(AnimationStates.WalkBack, -1, false, false, false));   // no walk_back: reuse forward
        Assert.Equal("idle", resolver.Resolve(AnimationStates.Crouch, -1, false, false, false));              // nothing better
        Assert.Equal("idle", resolver.Resolve(AnimationStates.Jump, -1, false, true, true));
        Assert.Equal("jab", resolver.Resolve(AnimationStates.Attack, 0, false, false, false));                // the move's own name
        Assert.Equal("move_5", resolver.Resolve(AnimationStates.Attack, 5, false, false, false));             // by index
        Assert.Equal("attack_crouch", resolver.Resolve(AnimationStates.Attack, 3, true, false, false));
        Assert.Equal("attack", resolver.Resolve(AnimationStates.Attack, 4, false, true, false));              // air falls to the generic one
        Assert.Equal("hit", resolver.Resolve(AnimationStates.Hitstun, -1, false, true, false));
        Assert.Equal("hit", resolver.Resolve(AnimationStates.Knockdown, -1, false, false, false));            // knockdown -> ko -> hit
        Assert.Equal("idle", resolver.Resolve(AnimationStates.Blockstun, -1, false, false, false));

        int before = lookups;
        Assert.Equal("jab", resolver.Resolve(AnimationStates.Attack, 0, false, false, false));
        Assert.Equal(before, lookups); // cached
    }

    [Fact]
    public void Resolver_ReturnsNullWhenTheCharacterHasNothing()
    {
        var resolver = new AnimationResolver(null, _ => false);
        Assert.Null(resolver.Resolve(AnimationStates.Idle, -1, false, false, false));
    }

    [Fact]
    public void Resolver_PrefersSpecificVariants()
    {
        var all = new AnimationResolver(null, _ => true);
        Assert.Equal("jump_up", all.Resolve(AnimationStates.Jump, -1, false, true, true));
        Assert.Equal("jump_down", all.Resolve(AnimationStates.Jump, -1, false, true, false));
        Assert.Equal("hit_air", all.Resolve(AnimationStates.Hitstun, -1, false, true, false));
        Assert.Equal("hit_crouch", all.Resolve(AnimationStates.Hitstun, -1, true, false, false));
        Assert.Equal("block_crouch", all.Resolve(AnimationStates.Blockstun, -1, true, false, false));
        Assert.Equal("backdash", all.Resolve(AnimationStates.Backdash, -1, false, false, false));
        Assert.Equal("stand_light", all.Resolve(AnimationStates.Attack, MoveSlot.StandLight, false, false, false));
    }

    [Fact]
    public void Timing_LoopsWithTheGlobalClock_PlaysOnceFromStateStart_AndStretchesAttacks()
    {
        // 6 frames at 12 fps = a new frame every 5 ticks.
        Assert.Equal(0, AnimationTiming.FrameIndex(6, 12, loop: true, stateFrame: 0, clock: 0, spanTicks: 0));
        Assert.Equal(1, AnimationTiming.FrameIndex(6, 12, loop: true, stateFrame: 99, clock: 5, spanTicks: 0));
        Assert.Equal(0, AnimationTiming.FrameIndex(6, 12, loop: true, stateFrame: 0, clock: 30, spanTicks: 0));   // wraps
        Assert.Equal(1, AnimationTiming.FrameIndex(6, 12, loop: false, stateFrame: 5, clock: 999, spanTicks: 0));
        Assert.Equal(5, AnimationTiming.FrameIndex(6, 12, loop: false, stateFrame: 500, clock: 0, spanTicks: 0)); // holds the last frame

        // A 20-tick move over 5 frames: one frame every 4 ticks, the last frame reached at the end.
        Assert.Equal(0, AnimationTiming.FrameIndex(5, 12, true, stateFrame: 0, clock: 0, spanTicks: 20));
        Assert.Equal(2, AnimationTiming.FrameIndex(5, 12, true, stateFrame: 8, clock: 0, spanTicks: 20));
        Assert.Equal(4, AnimationTiming.FrameIndex(5, 12, true, stateFrame: 20, clock: 0, spanTicks: 20));
        Assert.Equal(4, AnimationTiming.FrameIndex(5, 12, true, stateFrame: 99, clock: 0, spanTicks: 20));

        Assert.Equal(0, AnimationTiming.FrameIndex(1, 12, true, 77, 77, 0));
        Assert.Equal(0, AnimationTiming.FrameIndex(8, 12, true, -5, 0, 0));
        Assert.Equal(0.5, AnimationTiming.Time(1.0, loop: false, stateFrame: 30, clock: 0, spanTicks: 0), 6);
        Assert.Equal(0.25, AnimationTiming.Time(1.0, loop: true, stateFrame: 0, clock: 75, spanTicks: 0), 6);
        Assert.Equal(0.5, AnimationTiming.Time(2.0, loop: false, stateFrame: 10, clock: 0, spanTicks: 40), 6);
        Assert.Equal(1.0, AnimationTiming.Time(1.0, loop: false, stateFrame: 600, clock: 0, spanTicks: 0), 6);
    }

    [Fact]
    public void DefaultLoop_OnlyIdleLikeCyclesLoop()
    {
        Assert.True(AnimationNames.DefaultLoop("idle"));
        Assert.True(AnimationNames.DefaultLoop("walk_forward"));
        Assert.False(AnimationNames.DefaultLoop("attack"));
        Assert.False(AnimationNames.DefaultLoop("hit"));
        Assert.False(AnimationNames.DefaultLoop("stand_light"));
    }
}

/// <summary>Sound keys a pack may override.</summary>
public class AudioKeyTests
{
    [Fact]
    public void KnownKeys_AreGlobalVoicesAndPerMoveNames()
    {
        var manifest = CharacterManifestSerializer.FromJson("""{ "id": "voiced", "baseOn": "glen", "moveNames": ["jab"] }""");

        Assert.True(AudioKeys.IsKnown("hit_light", manifest));
        Assert.True(AudioKeys.IsKnown("voice_hurt", manifest));
        Assert.True(AudioKeys.IsKnown("voice_jab", manifest));
        Assert.True(AudioKeys.IsKnown("whoosh_jab", manifest));
        Assert.True(AudioKeys.IsKnown("voice_special", manifest));   // a canonical slot name
        Assert.True(AudioKeys.IsKnown("whoosh_move_9", manifest));

        Assert.False(AudioKeys.IsKnown("hit_ligth", manifest));      // typo
        Assert.False(AudioKeys.IsKnown("voice_nonsense", manifest));
        Assert.False(AudioKeys.IsKnown("", manifest));
    }
}

public class PresentationPoseTests
{
    [Fact]
    public void IntroAndWin_ArePlayedWhenTheCharacterHasThem_AndFallBackToIdle()
    {
        var all = new AnimationResolver(null, _ => true);
        Assert.Equal("intro", all.Resolve(AnimationStates.Intro, -1, false, false, false));
        Assert.Equal("win", all.Resolve(AnimationStates.Win, -1, false, false, false));

        var onlyIdle = new AnimationResolver(null, name => name == "idle");
        Assert.Equal("idle", onlyIdle.Resolve(AnimationStates.Intro, -1, false, false, false));
        Assert.Equal("idle", onlyIdle.Resolve(AnimationStates.Win, -1, false, false, false));
        Assert.True(AnimationNames.DefaultLoop("win"));
        Assert.False(AnimationNames.DefaultLoop("intro"));
    }
}

/// <summary>Interface languages as flat JSON files keyed by the English text.</summary>
public class LocalizationTests
{
    [Fact]
    public void Parse_ReadsNameAndMessages_AllowingCommentsAndEmptyValues()
    {
        const string json = """
            {
              // Spanish
              "_name": "Español",
              "PLAY": "JUGAR",
              "ROUND {0}": "RONDA {0}",
              "Untranslated yet": "",
            }
            """;
        var (table, report) = LocalizationTable.Parse("es", json);

        Assert.NotNull(table);
        Assert.False(report.HasErrors);
        Assert.Equal("Español", table!.Name);
        Assert.Equal("JUGAR", table.Messages["PLAY"]);
        Assert.Equal("RONDA {0}", table.Messages["ROUND {0}"]);
        Assert.DoesNotContain("Untranslated yet", table.Messages.Keys);
        Assert.DoesNotContain("_name", table.Messages.Keys);
    }

    [Fact]
    public void Parse_WarnsAboutLostPlaceholders_AndAMissingName()
    {
        var (table, report) = LocalizationTable.Parse("fr", """{ "ROUND {0}": "MANCHE", "{1} of {0}": "{0} de {1}" }""");

        Assert.NotNull(table);
        Assert.Contains(report.Issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("'ROUND {0}'", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Issues, i => i.Message.Contains("'{1} of {0}'", StringComparison.Ordinal)); // reordered is fine
        Assert.Contains(report.Issues, i => i.Message.Contains("_name", StringComparison.Ordinal));
        Assert.Equal("fr", table!.Name);
    }

    [Theory]
    [InlineData("es", "not json")]
    [InlineData("es", """{ "PLAY": 3 }""")]
    [InlineData("es", "null")]
    [InlineData("Spanish", "{}")]
    [InlineData("", "{}")]
    public void Parse_RejectsUnusableFiles(string code, string json)
    {
        var (table, report) = LocalizationTable.Parse(code, json);
        Assert.Null(table);
        Assert.True(report.HasErrors);
    }

    [Fact]
    public void LocaleCodes_AreLanguageAndOptionalRegion()
    {
        Assert.True(LocalizationTable.IsValidCode("es"));
        Assert.True(LocalizationTable.IsValidCode("pt_BR"));
        Assert.True(LocalizationTable.IsValidCode("fil"));
        Assert.False(LocalizationTable.IsValidCode("pt-br"));
        Assert.False(LocalizationTable.IsValidCode("E"));
        Assert.False(LocalizationTable.IsValidCode(null));
        Assert.Equal(new[] { 0, 1 }, LocalizationTable.PlaceholdersOf("{1} of {0} and {0}"));
    }
}
