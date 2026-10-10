// Fighter.Core — centralized damage scaling (GDD §4.6).
// Single formula, no magic numbers dispersed. All integer (numerator/denominator).
namespace Fighter.Core;

/// <summary>Centralized damage scaling (GDD §4.6). Single formula, no dispersed magic numbers.</summary>
public static class DamageScaler
{
    /// <summary>Combo index 0 = first hit. Returns (num, den) with min 10% floor.</summary>
    public static (int num, int den) Mult(int comboIndex, bool isSuper)
    {
        (int num, int den) = comboIndex switch
        {
            0 => (1, 1),      // 100%
            1 => (23, 25),    // 92%
            2 => (21, 25),    // 84%
            3 => (77, 100),   // 77%
            4 => (7, 10),     // 70%
            _ => (System.Math.Max(10, 70 - 8 * (comboIndex - 4)), 100),
        };
        if (isSuper && comboIndex > 0) { num *= 4; den *= 5; } // extra -20% for super in combo
        // Floor: no hit ever deals 0 in a combo when base > 0 (enforced at Apply).
        return (num, den);
    }

    /// <summary>Final damage for a hit: scaling by combo index, extra super penalty,
    /// absolute 10% floor (min 1). Zero base stays zero.</summary>
    public static int Apply(int baseDamage, int comboIndex, bool isSuper)
    {
        if (baseDamage <= 0) return 0;
        var (num, den) = Mult(comboIndex, isSuper);
        int v = baseDamage * num / den;
        int floor = System.Math.Max(1, baseDamage / 10); // absolute 10% floor, min 1
        return v < floor ? floor : v;
    }
}
