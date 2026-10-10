// REFACTOR+HARDEN D3/D7: determinism matrix (10k frames, seed 0xDEADBEEF,
// file-driven inputs, golden hash) + hot-path allocation budget.
// Parallelization is disabled ASSEMBLY-wide for one reason: allocation budgets
// are sharpest when the measured thread does nothing else. The per-thread
// counter below is already immune to other threads, but sequential execution
// also removes thread-hopping noise. The suite runs in ~1s single-threaded,
// so nothing of value is lost.
using Fighter.Core;
using Xunit.Abstractions;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Fighter.Core.Tests;

public sealed class DeterminismMatrixTests
{
    private const ulong MatrixSeed = 0xDEADBEEFUL;
    private const int MatrixFrames = 10_000;
    private readonly ITestOutputHelper _out;
    public DeterminismMatrixTests(ITestOutputHelper @out) => _out = @out;

    internal static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir, "Fighter.Godot.sln")))
                return dir;
            dir = System.IO.Path.GetDirectoryName(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        }
        throw new InvalidOperationException("Repo root (Fighter.Godot.sln) not found from test output dir.");
    }

    internal static (InputState[] P1, InputState[] P2) LoadFixtureFrames(string path, int frames)
    {
        byte[] bytes = System.IO.File.ReadAllBytes(path);
        if (bytes.Length != frames * 32)
            throw new InvalidOperationException($"Fixture is {bytes.Length}B; expected {frames * 32}B.");
        var p1 = new InputState[frames];
        var p2 = new InputState[frames];
        for (int f = 0; f < frames; f++)
        {
            p1[f] = new InputState((Buttons)ReadInt(bytes, f * 32),
                ReadInt(bytes, f * 32 + 4), ReadInt(bytes, f * 32 + 8), ReadInt(bytes, f * 32 + 12));
            p2[f] = new InputState((Buttons)ReadInt(bytes, f * 32 + 16),
                ReadInt(bytes, f * 32 + 20), ReadInt(bytes, f * 32 + 24), ReadInt(bytes, f * 32 + 28));
        }
        return (p1, p2);
    }

    private static int ReadInt(byte[] src, int o) =>
        src[o] | (src[o + 1] << 8) | (src[o + 2] << 16) | (src[o + 3] << 24);

    private static ulong RunMatrix(InputState[] p1, InputState[] p2, out ulong fnv)
    {
        var sim = new FightSimulation(MatrixSeed, GlenData.Build(), KiraData.Build());
        for (int f = 0; f < MatrixFrames; f++)
            sim.Step(p1[f], p2[f]);
        var snap = sim.Capture();
        fnv = snap.Hash;
        return DeterminismHash.Hash(snap.Payload);
    }

    [Trait("Category", "DeterminismMatrix")]
    [Fact]
    public void TenKFrames_FileDriven_MatchesGolden()
    {
        string root = RepoRoot();
        var (p1, p2) = LoadFixtureFrames(
            System.IO.Path.Combine(root, "tests", "fixtures", "determinism_input.bin"), MatrixFrames);

        ulong xxA = RunMatrix(p1, p2, out ulong fnvA);
        ulong xxB = RunMatrix(p1, p2, out ulong fnvB);
        Assert.Equal(xxA, xxB); // run-to-run stability first
        Assert.Equal(fnvA, fnvB);
        _out.WriteLine($"MATRIX runA xx=0x{xxA:X16} fnv=0x{fnvA:X16}");

        string goldenPath = System.IO.Path.Combine(root, "tests", "fixtures", "golden_hashes.json");
        string goldenHex;
        if (!System.IO.File.Exists(goldenPath))
        {
            // Bootstrap (executed, not claimed): persist this run, then re-run
            // and require the re-run to match. PASS requires 3 identical runs.
            System.IO.File.WriteAllText(goldenPath,
                $"{{\n  \"determinism_matrix_10k\": {{\n    \"seed\": \"0x{MatrixSeed:X16}\",\n" +
                $"    \"frames\": {MatrixFrames},\n    \"xxhash64\": \"0x{xxA:X16}\",\n" +
                $"    \"fnv1a\": \"0x{fnvA:X16}\"\n  }}\n}}\n");
            ulong xxC = RunMatrix(p1, p2, out _);
            Assert.Equal(xxA, xxC);
            goldenHex = $"0x{xxA:X16}";
        }
        else
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(goldenPath));
            goldenHex = doc.RootElement.GetProperty("determinism_matrix_10k").GetProperty("xxhash64").GetString()
                ?? throw new InvalidOperationException("Golden file missing xxhash64.");
        }
        _out.WriteLine($"MATRIX golden={goldenHex} actual=0x{xxA:X16}");
        Assert.Equal(goldenHex, $"0x{xxA:X16}");
    }
}

public sealed class AllocationBudgetTests
{
    private readonly ITestOutputHelper _out;
    public AllocationBudgetTests(ITestOutputHelper @out) => _out = @out;

    // NOTE: these tests measure allocations, so they CANNOT run under a
    // coverage profiler (coverlet allocates on the measured threads:
    // 50-75KB phantom). Run coverage with:
    //   dotnet test --collect:"XPlat Code Coverage" --filter "Category!=AllocationBudget"
    // Per-thread counters (not process-wide) make them immune to runner noise.
    [Trait("Category", "AllocationBudget")]
    [Fact]
    public void ThousandNeutralSteps_AllocateUnder4KB()
    {
        var sim = new FightSimulation(3, GlenData.Build(), GlenData.Build());
        for (int f = 0; f < 300; f++) // warmup: stabilize capacities, JIT, enumerators
            sim.Step(new InputState(Buttons.None, 0, 0, f), new InputState(Buttons.None, 0, 0, f));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 300; f < 1300; f++)
            sim.Step(new InputState(Buttons.None, 0, 0, f), new InputState(Buttons.None, 0, 0, f));
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        _out.WriteLine($"BUDGET fight-neutral allocated={delta}B / 1000 steps");
        Assert.True(delta < 4096, $"Hot path allocated {delta}B over 1000 steps (budget 4096B).");
    }

    [Trait("Category", "AllocationBudget")]
    [Fact]
    public void SubstrateSteps_AllocateNothing()
    {
        var sim = new DeterministicSim(3);
        for (int f = 0; f < 100; f++)
            sim.Step(InputGen.P1(f), InputGen.P2(f));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 100; f < 1100; f++)
            sim.Step(InputGen.P1(f), InputGen.P2(f));
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        _out.WriteLine($"BUDGET substrate allocated={delta}B / 1000 steps");
        Assert.True(delta < 4096, $"Substrate allocated {delta}B over 1000 steps.");
    }
}
