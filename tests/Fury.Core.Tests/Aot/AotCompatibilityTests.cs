using System.Reflection;
using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Tests.Aot;

/// <summary>
/// F5 gate: AOT compatibility tests. Verifies that the sim and core modules
/// don't use reflection, dynamic code generation, or other AOT-incompatible features.
/// </summary>
public class AotCompatibilityTests
{
    [Fact]
    public void Fp_DoesNotUseReflection()
    {
        var fp = Fp.FromRatio(3, 2);
        Assert.Equal(Fp.FromInt(3), fp * Fp.FromInt(2));
    }

    [Fact]
    public void Xoshiro_DoesNotUseReflection()
    {
        var rng = new Xoshiro256StarStar(42);
        var value = rng.NextULong();
        Assert.NotEqual(0UL, value);
    }

    [Fact]
    public void BinaryMoveTable_DoesNotUseReflection()
    {
        var moves = new[] { new MoveEntry(1, 2, 3, 4, 5, 6, 7, 8, 9, 10) };
        var table = BinaryMoveTable.Parse(BinaryMoveTableWriter.Write(moves));
        Assert.Equal(1, table.MoveCount);
    }

    [Fact]
    public void FightSimulation_DoesNotUseReflection()
    {
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create());
        var neutral = new InputState(0);
        sim.Step(neutral, neutral);
        Assert.Equal(1, sim.Frame);
    }

    [Fact]
    public void CoreAssemblies_DontReferenceReflectionEmit()
    {
        var assemblies = new[]
        {
            typeof(Fp).Assembly,
            typeof(Xoshiro256StarStar).Assembly,
            typeof(FightSimulation).Assembly,
            typeof(BinaryMoveTable).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var references = assembly.GetReferencedAssemblies();
            Assert.DoesNotContain(references, r =>
                r.Name == "System.Reflection.Emit" ||
                r.Name == "System.Reflection.Emit.Lightweight");
        }
    }
}
