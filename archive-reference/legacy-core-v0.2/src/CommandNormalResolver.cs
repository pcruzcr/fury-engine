// Fighter.Core — command normal resolver (REFACTOR+HARDEN, A5).
// Pure static class, zero Godot dependency. Simulation Phase 4:
// directional modifier (Forward/Back, relative to facing) held within a
// 10-frame window + a fresh single-button edge on the current frame =>
// CommandNormal. Everything else falls back to the neutral normal (or None
// when no fresh button edge exists). The resolver reports INTENT only; the
// fight authority (MoveResolver data tables) still decides what starts.
// Recorded per fighter per frame into Fighter.LastCommandNormal and covered
// by FightSnapshot schema 5.
namespace Fighter.Core;

/// <summary>Resolved attack intent: neutral normals plus the ten GDD command
/// normals (F+HP, B+HP, F+MP, B+MP, B+LP, F+HK, B+HK, F+LK, B+LK, D+B+LK).</summary>
public enum ResolvedInput : byte
{
    /// <summary>No fresh button edge (or empty buffer, or non-participating state).</summary>
    None = 0,
    /// <summary>Neutral light punch.</summary>
    NeutralLP = 1,
    /// <summary>Neutral medium punch.</summary>
    NeutralMP = 2,
    /// <summary>Neutral heavy punch.</summary>
    NeutralHP = 3,
    /// <summary>Neutral light kick.</summary>
    NeutralLK = 4,
    /// <summary>Neutral medium kick.</summary>
    NeutralMK = 5,
    /// <summary>Neutral heavy kick.</summary>
    NeutralHK = 6,
    /// <summary>Forward + heavy punch.</summary>
    ForwardHP = 10,
    /// <summary>Back + heavy punch.</summary>
    BackHP = 11,
    /// <summary>Forward + medium punch.</summary>
    ForwardMP = 12,
    /// <summary>Back + medium punch.</summary>
    BackMP = 13,
    /// <summary>Back + light punch.</summary>
    BackLP = 14,
    /// <summary>Forward + heavy kick.</summary>
    ForwardHK = 15,
    /// <summary>Back + heavy kick.</summary>
    BackHK = 16,
    /// <summary>Forward + light kick.</summary>
    ForwardLK = 17,
    /// <summary>Back + light kick.</summary>
    BackLK = 18,
    /// <summary>Down-back + light kick.</summary>
    DownBackLK = 19,
}

/// <summary>Pure command-normal intent resolver (simulation Phase 4).</summary>
/// <remarks>
/// <para>PRIORITY TABLE (applied top-down, first match wins):</para>
/// <para>1. Most-specific direction wins: DownBack+LK beats Back+LK beats
/// neutral LK. Down-back counts as Back for every other button (only LK has
/// a dedicated down-back variant in the GDD).</para>
/// <para>2. Directional intent beats neutral fallback: a held Forward/Back
/// within the window promotes the matching command normal; with no
/// direction in the window the same edge yields the neutral normal.</para>
/// <para>3. Exact single-button edges only: chords (two or more fresh button
/// bits, e.g. LP+LK tech, LP+MP parry) resolve to <see cref="ResolvedInput.None"/>.
/// The resolver never guesses which chord member was "meant".</para>
/// <para>4. Buttons without a GDD command normal (F+LP, F+MK, B+MK, any
/// Down+button, any Up+button) fall back to the neutral normal of the
/// pressed button.</para>
/// <para>5. Neutral-jump vs forward-jump ambiguity is OUT OF SCOPE: jump
/// intent belongs to Move(), not to attack resolution. An airborne caller
/// still gets directional attack intent from the stick history; the jump
/// itself is never inferred here.</para>
/// <para>DEVIATION (documented, not invented): the session brief asks for
/// (buffer, state) only, but Forward/Back are relative to facing, which no
/// buffer carries. The facing parameter is therefore explicit. Callers pass
/// the fighter's current facing; mirror matches stay symmetric because both
/// fighters resolve with their own facing.</para>
/// </remarks>
public static class CommandNormalResolver
{
    /// <summary>Direction window in frames (modifier held this recently counts).</summary>
    public const int CommandWindow = 10;

    // Relative direction codes: +1 forward, -1 back, -2 down-back, 0 neutral/other.
    private const int RelForward = 1;
    private const int RelBack = -1;
    private const int RelDownBack = -2;

    /// <summary>Resolves the attack intent for the latest frame in <paramref name="buffer"/>.</summary>
    /// <param name="buffer">Input history, oldest-first, latest frame last (must include the
    /// current frame; empty resolves to <see cref="ResolvedInput.None"/>).</param>
    /// <param name="state">Fighter state. KO, Knockdown and Wakeup resolve to None
    /// (non-participating); every other state resolves intent normally — intent is
    /// recorded even when the fight authority later rejects the start.</param>
    /// <param name="facing">Fighter facing (+1 right, −1 left). Forward/Back are relative to this.</param>
    /// <returns>Resolved intent (never throws on well-formed input).</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="facing"/> is not +1/−1.</exception>
    public static ResolvedInput Resolve(
        System.ReadOnlySpan<InputState> buffer, FighterState state, int facing)
    {
        if (facing != 1 && facing != -1)
            throw new ArgumentException("Facing must be +1 or -1.", nameof(facing));
        if (buffer.IsEmpty) return ResolvedInput.None;
        if (state is FighterState.KO or FighterState.Knockdown or FighterState.Wakeup)
            return ResolvedInput.None;

        InputState latest = buffer[buffer.Length - 1];
        Buttons prev = buffer.Length >= 2 ? buffer[buffer.Length - 2].Buttons : Buttons.None;
        Buttons edge = latest.Buttons & ~prev;
        if (edge == Buttons.None || !IsSingleButton(edge)) return ResolvedInput.None;

        int rel = ScanDirection(buffer, facing);
        return Map(edge, rel);
    }

    private static bool IsSingleButton(Buttons edge) => edge switch
    {
        Buttons.LP or Buttons.MP or Buttons.HP or Buttons.LK or Buttons.MK or Buttons.HK => true,
        _ => false,
    };

    private static int ScanDirection(System.ReadOnlySpan<InputState> buffer, int facing)
    {
        int start = System.Math.Max(0, buffer.Length - CommandWindow);
        bool sawBack = false;
        for (int i = buffer.Length - 1; i >= start; i--)
        {
            InputState s = buffer[i];
            if (s.DirX == facing) return RelForward; // reverse scan: most recent decisive direction wins
            if (s.DirX == -facing)
            {
                if (s.DirY == -1) return RelDownBack; // most-specific direction wins immediately
                sawBack = true;
            }
        }
        return sawBack ? RelBack : 0;
    }

    private static ResolvedInput Map(Buttons edge, int rel)
    {
        if (rel == RelForward)
        {
            return edge switch
            {
                Buttons.HP => ResolvedInput.ForwardHP,
                Buttons.MP => ResolvedInput.ForwardMP,
                Buttons.HK => ResolvedInput.ForwardHK,
                Buttons.LK => ResolvedInput.ForwardLK,
                _ => NeutralOf(edge),
            };
        }
        if (rel == RelBack)
        {
            return edge switch
            {
                Buttons.HP => ResolvedInput.BackHP,
                Buttons.MP => ResolvedInput.BackMP,
                Buttons.LP => ResolvedInput.BackLP,
                Buttons.HK => ResolvedInput.BackHK,
                Buttons.LK => ResolvedInput.BackLK,
                _ => NeutralOf(edge),
            };
        }
        if (rel == RelDownBack)
        {
            if (edge == Buttons.LK) return ResolvedInput.DownBackLK;
            return Map(edge, RelBack); // down-back counts as back for other buttons
        }
        return NeutralOf(edge);
    }

    private static ResolvedInput NeutralOf(Buttons edge) => edge switch
    {
        Buttons.LP => ResolvedInput.NeutralLP,
        Buttons.MP => ResolvedInput.NeutralMP,
        Buttons.HP => ResolvedInput.NeutralHP,
        Buttons.LK => ResolvedInput.NeutralLK,
        Buttons.MK => ResolvedInput.NeutralMK,
        Buttons.HK => ResolvedInput.NeutralHK,
        _ => ResolvedInput.None,
    };
}
