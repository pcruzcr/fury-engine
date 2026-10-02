using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Combat;

/// <summary>
/// F1 gate (ADR-006): the canonical 600-frame fight must produce the exact per-frame hash
/// chain recorded in tests/fixtures/golden_hashes_f1.json. Any change that alters sim
/// behavior — even by one ulp of state — breaks this test and must ship with a consciously
/// re-recorded fixture. Re-record with: FURY_RECORD_GOLDEN=1 dotnet test --filter Golden.
/// </summary>
public class GoldenHashTests
{
    private const int Frames = 600;
    private const string FixturePath = "fixtures/golden_hashes_f1.json";

    private static ulong[] RunCanonicalFight()
    {
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create());
        var rng = new Xoshiro256StarStar(0xD0A1F00DUL);

        var hashes = new ulong[Frames];
        for (int f = 0; f < Frames; f++)
        {
            sim.Step(new InputState((ushort)rng.NextULong()), new InputState((ushort)rng.NextULong()));
            hashes[f] = sim.SnapshotHash;
        }
        return hashes;
    }

    private static string FixturePathResolved
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                if (File.Exists(Path.Combine(dir, "FuryEngine.sln")))
                {
                    return Path.Combine(dir, "tests", "fixtures", "golden_hashes_f1.json");
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent is null) break;
                dir = parent;
            }
            return Path.Combine(AppContext.BaseDirectory, "tests", "fixtures", "golden_hashes_f1.json");
        }
    }

    [Fact]
    public void CanonicalFight_MatchesGoldenHashes()
    {
        var hashes = RunCanonicalFight();

        if (Environment.GetEnvironmentVariable("FURY_RECORD_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FixturePathResolved)!);
            File.WriteAllLines(FixturePathResolved, hashes.Select(h => h.ToString("x16")));
            return;
        }

        Assert.True(File.Exists(FixturePathResolved), $"Golden fixture missing at {FixturePathResolved}");
        var golden = File.ReadAllLines(FixturePathResolved)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => Convert.ToUInt64(l, 16))
            .ToArray();

        Assert.Equal(Frames, golden.Length);
        for (int f = 0; f < Frames; f++)
        {
            if (golden[f] != hashes[f])
            {
                Assert.Fail($"Golden hash mismatch at frame {f}: expected {golden[f]:x16}, got {hashes[f]:x16}. " +
                            "If this change is intentional, re-record with FURY_RECORD_GOLDEN=1 and note it in the ADR.");
            }
        }
    }
}
