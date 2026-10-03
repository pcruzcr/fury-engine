namespace Fury.Core.Authoring;

/// <summary>
/// The animation vocabulary of a character. An artist names strips / <c>AnimationPlayer</c> animations with these words; the
/// engine picks one per fighter state and falls back along a fixed chain, so a character can ship with only <c>idle</c> and
/// grow animation by animation. Pure and engine-free (the Godot layer only draws what this resolves).
/// </summary>
public static class AnimationNames
{
    public const string Idle = "idle";
    public const string WalkForward = "walk_forward";
    public const string WalkBack = "walk_back";
    public const string Crouch = "crouch";
    public const string Jump = "jump";
    public const string JumpUp = "jump_up";
    public const string JumpDown = "jump_down";
    public const string Dash = "dash";
    public const string Backdash = "backdash";
    public const string Attack = "attack";
    public const string AttackCrouch = "attack_crouch";
    public const string AttackAir = "attack_air";
    public const string Hit = "hit";
    public const string HitCrouch = "hit_crouch";
    public const string HitAir = "hit_air";
    public const string Block = "block";
    public const string BlockCrouch = "block_crouch";
    public const string Knockdown = "knockdown";
    public const string Ko = "ko";
    public const string Win = "win";
    public const string Intro = "intro";

    /// <summary>Every name the engine itself selects (move animations are named after the move, see <see cref="MoveName"/>).</summary>
    public static IReadOnlyList<string> Standard { get; } =
    [
        Idle, WalkForward, WalkBack, Crouch, Jump, JumpUp, JumpDown, Dash, Backdash, Attack, AttackCrouch, AttackAir,
        Hit, HitCrouch, HitAir, Block, BlockCrouch, Knockdown, Ko, Win, Intro,
    ];

    /// <summary>The one animation a character cannot do without.</summary>
    public static IReadOnlyList<string> Required { get; } = [Idle];

    /// <summary>Animations a finished character is expected to have (each missing one produces a warning, not an error).</summary>
    public static IReadOnlyList<string> Recommended { get; } =
        [WalkForward, WalkBack, Crouch, Jump, Attack, Hit, Block, Knockdown, Ko];

    private static readonly string[] CanonicalMoveNames =
        ["stand_light", "stand_medium", "stand_heavy", "crouch_light", "crouch_heavy", "special", "super"];

    /// <summary>Whether an animation holds a looping idle-like cycle by default (everything else plays once and holds its last frame).</summary>
    public static bool DefaultLoop(string name) => name is Idle or WalkForward or WalkBack or Win;

    /// <summary>True for names made only of lowercase letters, digits and underscores (what file and node names tolerate everywhere).</summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 40 || !char.IsAsciiLetterLower(name[0])) return false;
        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')) return false;
        }
        return true;
    }

    /// <summary>
    /// Name of move <paramref name="index"/>: the author's <c>moveNames</c> entry, else the canonical slot name (the first seven
    /// moves share the same layout in every character), else <c>move_&lt;index&gt;</c>.
    /// </summary>
    public static string MoveName(IReadOnlyList<string>? moveNames, int index)
    {
        if (moveNames != null && (uint)index < (uint)moveNames.Count && !string.IsNullOrEmpty(moveNames[index])) return moveNames[index];
        return (uint)index < (uint)CanonicalMoveNames.Length ? CanonicalMoveNames[index] : $"move_{index}";
    }
}

/// <summary>Fighter state ids as the simulation defines them (a test pins these to <c>FighterStateId</c>; Authoring does not reference the sim).</summary>
public static class AnimationStates
{
    public const int Idle = 0;
    public const int WalkForward = 1;
    public const int WalkBack = 2;
    public const int Crouch = 3;
    public const int Jump = 4;
    public const int Attack = 5;
    public const int Hitstun = 6;
    public const int Blockstun = 7;
    public const int Knockdown = 8;
    public const int Ko = 9;
    public const int Dash = 10;
    public const int Backdash = 11;
}

/// <summary>
/// Chooses the animation to show for a fighter. The result depends only on simulation values (state, move, stance, vertical
/// direction), so every machine, replay and rollback shows the same thing. Lookups are cached: after the first time a
/// situation is seen nothing is allocated.
/// </summary>
public sealed class AnimationResolver
{
    private readonly IReadOnlyList<string>? _moveNames;
    private readonly Func<string, bool> _has;
    private readonly Dictionary<int, string?> _cache = [];

    /// <param name="moveNames">The character's <c>moveNames</c> (may be null).</param>
    /// <param name="has">Whether the character provides an animation with that name.</param>
    public AnimationResolver(IReadOnlyList<string>? moveNames, Func<string, bool> has)
    {
        ArgumentNullException.ThrowIfNull(has);
        _moveNames = moveNames;
        _has = has;
    }

    /// <summary>The animation to play, or null when the character has none usable (the caller then keeps its fallback look).</summary>
    public string? Resolve(int stateId, int moveIndex, bool crouching, bool airborne, bool rising)
    {
        int key = stateId | (crouching ? 1 << 8 : 0) | (airborne ? 1 << 9 : 0) | (rising ? 1 << 10 : 0) | ((moveIndex + 1) << 12);
        if (_cache.TryGetValue(key, out string? cached)) return cached;

        string? chosen = null;
        foreach (string candidate in Candidates(stateId, moveIndex, crouching, airborne, rising))
        {
            if (_has(candidate))
            {
                chosen = candidate;
                break;
            }
        }

        _cache[key] = chosen;
        return chosen;
    }

    /// <summary>The fallback chain, most specific first (exposed for tests and for the validator's documentation).</summary>
    public IEnumerable<string> Candidates(int stateId, int moveIndex, bool crouching, bool airborne, bool rising)
    {
        switch (stateId)
        {
            case AnimationStates.WalkForward:
                yield return AnimationNames.WalkForward;
                break;
            case AnimationStates.WalkBack:
                yield return AnimationNames.WalkBack;
                yield return AnimationNames.WalkForward;
                break;
            case AnimationStates.Crouch:
                yield return AnimationNames.Crouch;
                break;
            case AnimationStates.Jump:
                yield return rising ? AnimationNames.JumpUp : AnimationNames.JumpDown;
                yield return AnimationNames.Jump;
                break;
            case AnimationStates.Attack:
                yield return AnimationNames.MoveName(_moveNames, moveIndex);
                yield return $"move_{moveIndex}";
                if (airborne) yield return AnimationNames.AttackAir;
                if (crouching) yield return AnimationNames.AttackCrouch;
                yield return AnimationNames.Attack;
                break;
            case AnimationStates.Hitstun:
                if (airborne) yield return AnimationNames.HitAir;
                if (crouching) yield return AnimationNames.HitCrouch;
                yield return AnimationNames.Hit;
                break;
            case AnimationStates.Blockstun:
                if (crouching) yield return AnimationNames.BlockCrouch;
                yield return AnimationNames.Block;
                break;
            case AnimationStates.Knockdown:
                yield return AnimationNames.Knockdown;
                yield return AnimationNames.Ko;
                yield return AnimationNames.Hit;
                break;
            case AnimationStates.Ko:
                yield return AnimationNames.Ko;
                yield return AnimationNames.Knockdown;
                yield return AnimationNames.Hit;
                break;
            case AnimationStates.Dash:
                yield return AnimationNames.Dash;
                yield return AnimationNames.WalkForward;
                break;
            case AnimationStates.Backdash:
                yield return AnimationNames.Backdash;
                yield return AnimationNames.WalkBack;
                break;
        }

        yield return AnimationNames.Idle;
    }
}

/// <summary>
/// Which animation frame (or time) to show. Everything derives from simulation counters, never from wall-clock time, so the
/// picture stays locked to the frame data through hitstop, pauses, replays and rollback.
/// </summary>
public static class AnimationTiming
{
    /// <summary>Simulation ticks per second the engine runs at.</summary>
    public const int TicksPerSecond = 60;

    /// <summary>
    /// Frame index of a strip of <paramref name="frames"/> frames.
    /// </summary>
    /// <param name="fps">Authored playback speed (frames per second).</param>
    /// <param name="loop">Loop with the global <paramref name="clock"/>; otherwise play from the start of the state and hold the last frame.</param>
    /// <param name="stateFrame">Ticks since the fighter entered its current state.</param>
    /// <param name="clock">Global simulation frame.</param>
    /// <param name="spanTicks">When positive, stretch the whole animation over exactly this many ticks (attacks span their frame data).</param>
    public static int FrameIndex(int frames, float fps, bool loop, int stateFrame, int clock, int spanTicks)
    {
        if (frames <= 1) return 0;
        stateFrame = System.Math.Max(0, stateFrame);

        if (spanTicks > 0) return System.Math.Min(frames - 1, (int)((long)stateFrame * frames / spanTicks));

        int index = (int)(((long)(loop ? clock : stateFrame) * (long)(fps * 1000f)) / (TicksPerSecond * 1000L));
        return loop ? ((index % frames) + frames) % frames : System.Math.Min(frames - 1, index);
    }

    /// <summary>Position in seconds inside an animation of <paramref name="length"/> seconds (for authored <c>AnimationPlayer</c> clips).</summary>
    public static double Time(double length, bool loop, int stateFrame, int clock, int spanTicks)
    {
        if (length <= 0) return 0;
        stateFrame = System.Math.Max(0, stateFrame);

        if (spanTicks > 0) return System.Math.Min(length, length * stateFrame / spanTicks);
        double seconds = (loop ? clock : stateFrame) / (double)TicksPerSecond;
        return loop ? seconds % length : System.Math.Min(length, seconds);
    }
}
