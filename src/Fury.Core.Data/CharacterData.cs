using Fury.Core.Math;

namespace Fury.Core.Data;

/// <summary>
/// Shared authoring helpers for the sample roster. Distances are simulation units (a fighter is 4 units tall
/// and 1 wide), times are frames at 60 Hz, speeds are units per frame.
/// </summary>
internal static class MoveBuilder
{
    // Command bit layout mirrors Fury.Core.Sim.MoveCommand (Data cannot reference Sim): buttons 0-15,
    // motion 16-23, stance 24-25. Buttons use the InputButton bits.
    public const int Light = 1 << 4;
    public const int Medium = 1 << 5;
    public const int Heavy = 1 << 6;
    public const int Special = 1 << 7;
    public const int Macro1 = 1 << 10;
    public const int Macro2 = 1 << 11;

    public const int MotionQcf = 1;
    public const int MotionDp = 3;
    public const int MotionHoldForward = 12;

    public const int StanceStand = 1;
    public const int StanceCrouch = 2;
    public const int StanceAir = 3;

    public static int SpecialAndSuper => MoveCategory.Bit(MoveCategory.Special) | MoveCategory.Bit(MoveCategory.Super);

    public static int Command(int buttons, int motion = 0, int stance = 0) => buttons | (motion << 16) | (stance << 24);

    public static MoveEntry Move(string name, int startup, int active, int recovery, int damage, int hitstop, Fp pushback, Fp reach, Fp halfHeight, Fp centerY) =>
        new(FpHash.HashName(name), startup, active, recovery, damage, hitstop, pushback.Raw, reach.Raw, halfHeight.Raw, centerY.Raw);

    public static Fp R(int n, int d) => Fp.FromRatio(n, d);
}

/// <summary>Glen: pressure heavyweight. Frame data per the canonical hitstop table (L4/M6/H10/EX12/Super20).</summary>
public static class GlenData
{
    public static CharacterConfig Create()
    {
        Span<MoveEntry> moves =
        [
            // ---- canonical slots (indexes are fixed: see MoveSlot) ----
            MoveBuilder.Move("GL_SL", 4, 3, 7, 40, 4, MoveBuilder.R(1, 8), MoveBuilder.R(2, 1), MoveBuilder.R(1, 2), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Light, stance: MoveBuilder.StanceStand),
                Category = MoveCategory.Normal,
                CancelMask = MoveBuilder.SpecialAndSuper | MoveCategory.Bit(MoveCategory.Normal),
            },
            MoveBuilder.Move("GL_SM", 6, 4, 12, 70, 6, MoveBuilder.R(1, 4), MoveBuilder.R(3, 1), MoveBuilder.R(1, 2), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Medium, stance: MoveBuilder.StanceStand),
                Category = MoveCategory.Normal,
                CancelMask = MoveBuilder.SpecialAndSuper,
            },
            MoveBuilder.Move("GL_SH", 9, 5, 16, 110, 10, MoveBuilder.R(3, 8), MoveBuilder.R(7, 2), MoveBuilder.R(1, 2), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Heavy, stance: MoveBuilder.StanceStand),
                Category = MoveCategory.Normal,
                CancelMask = MoveBuilder.SpecialAndSuper,
            },
            MoveBuilder.Move("GL_CL", 4, 3, 8, 35, 4, MoveBuilder.R(1, 8), MoveBuilder.R(2, 1), MoveBuilder.R(1, 2), MoveBuilder.R(1, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Light | MoveBuilder.Medium, stance: MoveBuilder.StanceCrouch),
                Category = MoveCategory.Normal,
                Level = HitLevel.Low,
                CancelMask = MoveBuilder.SpecialAndSuper,
            },
            MoveBuilder.Move("GL_CH", 8, 4, 15, 95, 10, MoveBuilder.R(3, 8), MoveBuilder.R(3, 1), MoveBuilder.R(1, 2), MoveBuilder.R(1, 1)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Heavy, stance: MoveBuilder.StanceCrouch),
                Category = MoveCategory.Normal,
                Level = HitLevel.Low,
                Traits = MoveTraits.Knockdown,
                CancelMask = MoveCategory.Bit(MoveCategory.Super),
            },
            MoveBuilder.Move("GL_ORD", 8, 6, 20, 90, 12, MoveBuilder.R(1, 2), MoveBuilder.R(5, 1), MoveBuilder.R(3, 4), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Special),
                Category = MoveCategory.Special,
                CancelMask = MoveCategory.Bit(MoveCategory.Super),
            },
            MoveBuilder.Move("GL_ARE", 10, 8, 28, 220, 20, MoveBuilder.R(5, 8), MoveBuilder.R(7, 1), MoveBuilder.R(1, 1), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Macro2),
                Category = MoveCategory.Super,
                MeterCost = 300,
                Priority = 20,
                Traits = MoveTraits.Knockdown,
            },

            // ---- extended moves ----
            // 7: fireball, quarter-circle forward + special.
            MoveBuilder.Move("GL_FIRE", 14, 1, 26, 60, 4, MoveBuilder.R(1, 4), MoveBuilder.R(6, 5), MoveBuilder.R(3, 5), MoveBuilder.R(9, 5)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Special, MoveBuilder.MotionQcf),
                Category = MoveCategory.Special,
                Priority = 5,
                Traits = MoveTraits.Projectile,
                ProjectileSpawnFrame = 14,
                ProjectileSpeedRaw = MoveBuilder.R(7, 20).Raw,
                ProjectileLifetime = 90,
                HitstunFrames = 20,
                BlockstunFrames = 12,
                CancelMask = MoveCategory.Bit(MoveCategory.Super),
            },
            // 8: rising uppercut, dragon-punch motion + heavy. Launches for a juggle.
            MoveBuilder.Move("GL_RISE", 5, 5, 30, 90, 10, MoveBuilder.R(1, 8), MoveBuilder.R(11, 5), MoveBuilder.R(3, 2), MoveBuilder.R(2, 1)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Heavy, MoveBuilder.MotionDp),
                Category = MoveCategory.Special,
                Priority = 6,
                Traits = MoveTraits.Launcher,
                LaunchVyRaw = MoveBuilder.R(9, 10).Raw,
                CancelMask = MoveCategory.Bit(MoveCategory.Super),
            },
            // 9-11: jump normals.
            MoveBuilder.Move("GL_JL", 4, 6, 6, 40, 4, MoveBuilder.R(1, 8), MoveBuilder.R(2, 1), MoveBuilder.R(1, 2), MoveBuilder.R(2, 1)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Light, stance: MoveBuilder.StanceAir),
                Traits = MoveTraits.Air,
                HitstunFrames = 16,
                BlockstunFrames = 10,
            },
            MoveBuilder.Move("GL_JM", 6, 6, 8, 65, 6, MoveBuilder.R(1, 4), MoveBuilder.R(5, 2), MoveBuilder.R(1, 2), MoveBuilder.R(2, 1)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Medium, stance: MoveBuilder.StanceAir),
                Traits = MoveTraits.Air,
                Level = HitLevel.Overhead,
                HitstunFrames = 20,
                BlockstunFrames = 12,
            },
            MoveBuilder.Move("GL_JH", 8, 6, 10, 95, 10, MoveBuilder.R(3, 8), MoveBuilder.R(3, 1), MoveBuilder.R(3, 4), MoveBuilder.R(2, 1)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Heavy, stance: MoveBuilder.StanceAir),
                Traits = MoveTraits.Air | MoveTraits.Knockdown,
                Level = HitLevel.Overhead,
                HitstunFrames = 24,
                BlockstunFrames = 14,
            },
            // 12: grab. Cannot be blocked; two simultaneous grabs tech.
            MoveBuilder.Move("GL_GRAB", 5, 2, 20, 120, 12, MoveBuilder.R(1, 8), MoveBuilder.R(8, 5), MoveBuilder.R(1, 1), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Macro1, stance: MoveBuilder.StanceStand),
                Category = MoveCategory.Throw,
                Priority = 8,
                Traits = MoveTraits.Throw | MoveTraits.Knockdown,
                HitstunFrames = 40,
            },
            // 13: forward + heavy overhead command normal.
            MoveBuilder.Move("GL_OVH", 16, 3, 16, 80, 8, MoveBuilder.R(1, 4), MoveBuilder.R(5, 2), MoveBuilder.R(1, 2), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Heavy, MoveBuilder.MotionHoldForward, MoveBuilder.StanceStand),
                Category = MoveCategory.CommandNormal,
                Priority = 3,
                Level = HitLevel.Overhead,
                HitstunFrames = 22,
                BlockstunFrames = 12,
                CancelMask = MoveBuilder.SpecialAndSuper,
            },
        ];

        return CharacterConfig.Create("glen", 1000, MoveBuilder.R(11, 100), moves) with
        {
            WalkBackSpeed = MoveBuilder.R(9, 100),
            DashSpeed = MoveBuilder.R(22, 100),
            DashFrames = 16,
            BackdashSpeed = MoveBuilder.R(20, 100),
            BackdashFrames = 18,
        };
    }

    internal static int NameHash(string name) => FpHash.HashName(name);
}

/// <summary>Kira: speed footnote of the roster: faster walk and dashes, lighter hits, the same tables.</summary>
public static class KiraData
{
    public static CharacterConfig Create()
    {
        Span<MoveEntry> moves =
        [
            MoveBuilder.Move("KI_SL", 3, 3, 6, 35, 4, MoveBuilder.R(1, 8), MoveBuilder.R(7, 4), MoveBuilder.R(1, 2), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Light, stance: MoveBuilder.StanceStand),
                Category = MoveCategory.Normal,
                CancelMask = MoveBuilder.SpecialAndSuper | MoveCategory.Bit(MoveCategory.Normal),
            },
            MoveBuilder.Move("KI_SM", 5, 4, 11, 65, 6, MoveBuilder.R(1, 4), MoveBuilder.R(5, 2), MoveBuilder.R(1, 2), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Medium, stance: MoveBuilder.StanceStand),
                Category = MoveCategory.Normal,
                CancelMask = MoveBuilder.SpecialAndSuper,
            },
            MoveBuilder.Move("KI_SH", 8, 5, 15, 100, 10, MoveBuilder.R(3, 8), MoveBuilder.R(3, 1), MoveBuilder.R(1, 2), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Heavy, stance: MoveBuilder.StanceStand),
                Category = MoveCategory.Normal,
                CancelMask = MoveBuilder.SpecialAndSuper,
            },
            MoveBuilder.Move("KI_CL", 4, 3, 7, 30, 4, MoveBuilder.R(1, 8), MoveBuilder.R(7, 4), MoveBuilder.R(1, 2), MoveBuilder.R(1, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Light | MoveBuilder.Medium, stance: MoveBuilder.StanceCrouch),
                Category = MoveCategory.Normal,
                Level = HitLevel.Low,
                CancelMask = MoveBuilder.SpecialAndSuper,
            },
            MoveBuilder.Move("KI_CH", 7, 4, 14, 85, 10, MoveBuilder.R(3, 8), MoveBuilder.R(5, 2), MoveBuilder.R(1, 2), MoveBuilder.R(1, 1)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Heavy, stance: MoveBuilder.StanceCrouch),
                Category = MoveCategory.Normal,
                Level = HitLevel.Low,
                Traits = MoveTraits.Knockdown,
                CancelMask = MoveCategory.Bit(MoveCategory.Super),
            },
            MoveBuilder.Move("KI_ECO", 7, 6, 18, 85, 12, MoveBuilder.R(1, 2), MoveBuilder.R(11, 2), MoveBuilder.R(3, 4), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Special),
                Category = MoveCategory.Special,
                CancelMask = MoveCategory.Bit(MoveCategory.Super),
            },
            MoveBuilder.Move("KI_VAC", 9, 8, 26, 210, 20, MoveBuilder.R(5, 8), MoveBuilder.R(15, 2), MoveBuilder.R(1, 1), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Macro2),
                Category = MoveCategory.Super,
                MeterCost = 300,
                Priority = 20,
                Traits = MoveTraits.Knockdown,
            },

            // Extended moves (same slots as Glen so tools and the CPU can treat characters uniformly).
            MoveBuilder.Move("KI_FIRE", 12, 1, 24, 55, 4, MoveBuilder.R(1, 4), MoveBuilder.R(1, 1), MoveBuilder.R(1, 2), MoveBuilder.R(9, 5)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Special, MoveBuilder.MotionQcf),
                Category = MoveCategory.Special,
                Priority = 5,
                Traits = MoveTraits.Projectile,
                ProjectileSpawnFrame = 12,
                ProjectileSpeedRaw = MoveBuilder.R(2, 5).Raw,
                ProjectileLifetime = 80,
                HitstunFrames = 18,
                BlockstunFrames = 11,
                CancelMask = MoveCategory.Bit(MoveCategory.Super),
            },
            MoveBuilder.Move("KI_RISE", 4, 5, 28, 85, 10, MoveBuilder.R(1, 8), MoveBuilder.R(2, 1), MoveBuilder.R(3, 2), MoveBuilder.R(2, 1)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Heavy, MoveBuilder.MotionDp),
                Category = MoveCategory.Special,
                Priority = 6,
                Traits = MoveTraits.Launcher,
                LaunchVyRaw = MoveBuilder.R(85, 100).Raw,
                CancelMask = MoveCategory.Bit(MoveCategory.Super),
            },
            MoveBuilder.Move("KI_JL", 3, 6, 6, 35, 4, MoveBuilder.R(1, 8), MoveBuilder.R(7, 4), MoveBuilder.R(1, 2), MoveBuilder.R(2, 1)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Light, stance: MoveBuilder.StanceAir),
                Traits = MoveTraits.Air,
                HitstunFrames = 16,
                BlockstunFrames = 10,
            },
            MoveBuilder.Move("KI_JM", 5, 6, 8, 60, 6, MoveBuilder.R(1, 4), MoveBuilder.R(9, 4), MoveBuilder.R(1, 2), MoveBuilder.R(2, 1)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Medium, stance: MoveBuilder.StanceAir),
                Traits = MoveTraits.Air,
                Level = HitLevel.Overhead,
                HitstunFrames = 20,
                BlockstunFrames = 12,
            },
            MoveBuilder.Move("KI_JH", 7, 6, 10, 90, 10, MoveBuilder.R(3, 8), MoveBuilder.R(11, 4), MoveBuilder.R(3, 4), MoveBuilder.R(2, 1)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Heavy, stance: MoveBuilder.StanceAir),
                Traits = MoveTraits.Air | MoveTraits.Knockdown,
                Level = HitLevel.Overhead,
                HitstunFrames = 24,
                BlockstunFrames = 14,
            },
            MoveBuilder.Move("KI_GRAB", 5, 2, 22, 110, 12, MoveBuilder.R(1, 8), MoveBuilder.R(3, 2), MoveBuilder.R(1, 1), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Macro1, stance: MoveBuilder.StanceStand),
                Category = MoveCategory.Throw,
                Priority = 8,
                Traits = MoveTraits.Throw | MoveTraits.Knockdown,
                HitstunFrames = 40,
            },
            MoveBuilder.Move("KI_OVH", 14, 3, 15, 70, 8, MoveBuilder.R(1, 4), MoveBuilder.R(9, 4), MoveBuilder.R(1, 2), MoveBuilder.R(3, 2)) with
            {
                Command = MoveBuilder.Command(MoveBuilder.Heavy, MoveBuilder.MotionHoldForward, MoveBuilder.StanceStand),
                Category = MoveCategory.CommandNormal,
                Priority = 3,
                Level = HitLevel.Overhead,
                HitstunFrames = 22,
                BlockstunFrames = 12,
                CancelMask = MoveBuilder.SpecialAndSuper,
            },
        ];

        return CharacterConfig.Create("kira", 1000, MoveBuilder.R(13, 100), moves) with
        {
            WalkBackSpeed = MoveBuilder.R(11, 100),
            DashSpeed = MoveBuilder.R(28, 100),
            DashFrames = 14,
            BackdashSpeed = MoveBuilder.R(24, 100),
            BackdashFrames = 16,
        };
    }

    internal static int NameHash(string name) => FpHash.HashName(name);
}

/// <summary>
/// Single lookup from a character id ("glen", "kira") to its compiled config. Character
/// select, the fight director and tools resolve fighters through here instead of each
/// hard-coding Glen vs Kira.
/// </summary>
public static class CharacterRoster
{
    /// <summary>Ids in display order.</summary>
    public static IReadOnlyList<string> Ids { get; } = ["glen", "kira"];

    /// <summary>Builds the config for <paramref name="id"/> (case-insensitive).</summary>
    /// <exception cref="ArgumentException">Unknown id.</exception>
    public static CharacterConfig Create(string id) => id?.ToLowerInvariant() switch
    {
        "glen" => GlenData.Create(),
        "kira" => KiraData.Create(),
        _ => throw new ArgumentException($"Unknown character id '{id}'.", nameof(id)),
    };
}
