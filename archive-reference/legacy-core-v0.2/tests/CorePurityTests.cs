// F0 acceptance: Core must compile and run with zero Godot dependency.
using System.Reflection;

namespace Fighter.Core.Tests;

public sealed class CorePurityTests
{
    [Fact]
    public void CoreAssembly_ReferencesNoGodot()
    {
        var core = typeof(DeterministicSim).Assembly;
        var names = core.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain(names, n => n != null && n.Contains("Godot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoreTypes_UseNoFloatOrDoubleFields()
    {
        // Determinism rule (§14): simulation state must be integer-based.
        var asm = typeof(DeterministicSim).Assembly;
        var offenders = asm.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(f => f.FieldType == typeof(float) || f.FieldType == typeof(double))
                .Select(f => $"{t.Name}.{f.Name}"))
            .ToArray();
        Assert.Empty(offenders);
    }
}
