using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Stage geometry and hazards (TMNT / Smash style platforms) as a stateless module: every platform position is a pure
/// function of the frame number, so there is nothing to snapshot and rollback can never desync it.
/// Platforms are authored with <see cref="PlatformSystem.PlatformData"/> in simulation units: <c>StartX/StartY</c> is the
/// centre, <c>Width/Height</c> the size (the top surface is <c>Y - Height / 2</c>; Y grows downward, the floor is Y = 0).
/// Supported types: <see cref="PlatformSystem.PlatformType.Solid"/> and <see cref="PlatformSystem.PlatformType.PassThrough"/>
/// (land from above; PassThrough lets a fighter drop through by holding Down), Moving (ping-pong between start and end over
/// <c>MoveDuration</c> frames, carrying fighters), Bounce (trampoline), Conveyor (pushes grounded fighters) and Hazard (damage
/// every <see cref="HazardIntervalFrames"/> frames to anyone overlapping it). Side collisions are not simulated: surfaces are
/// one-way for landing.
/// </summary>
public sealed class StageModule : SimModule
{
    public const int HazardIntervalFrames = 30;
    private const int SupportToleranceRaw = 0x4000; // 0.25 units: how far a grounded fighter may lag a moving surface

    private readonly PlatformSystem.PlatformData[] _platforms;

    public StageModule(IEnumerable<PlatformSystem.PlatformData> platforms)
    {
        ArgumentNullException.ThrowIfNull(platforms);
        _platforms = [.. platforms];
        if (_platforms.Length > PlatformSystem.MaxPlatforms) throw new ArgumentException($"At most {PlatformSystem.MaxPlatforms} platforms.", nameof(platforms));
    }

    public int Count => _platforms.Length;

    public override int StateWords => 0;

    public override void Serialize(Span<int> dest)
    {
    }

    public override void Deserialize(ReadOnlySpan<int> source)
    {
    }

    /// <summary>Centre of platform <paramref name="index"/> at <paramref name="frame"/>.</summary>
    public (Fp X, Fp Y) CenterAt(int index, int frame)
    {
        var p = _platforms[index];
        if (p.Type != PlatformSystem.PlatformType.Moving || p.MoveDuration <= 1) return (p.StartX, p.StartY);

        // Triangle wave: out for half the period, back for the other half.
        int t = frame % p.MoveDuration;
        int half = p.MoveDuration / 2;
        Fp progress = t < half ? Fp.FromRatio(t, half) : Fp.FromRatio(p.MoveDuration - t, p.MoveDuration - half);
        return (p.StartX + ((p.EndX - p.StartX) * progress), p.StartY + ((p.EndY - p.StartY) * progress));
    }

    /// <summary>Top surface Y and horizontal span of platform <paramref name="index"/> at <paramref name="frame"/>.</summary>
    public (Fp Left, Fp Right, Fp Top, Fp Bottom) BoundsAt(int index, int frame)
    {
        var p = _platforms[index];
        var (cx, cy) = CenterAt(index, frame);
        Fp halfW = p.Width * Fp.Half;
        Fp halfH = p.Height * Fp.Half;
        return (cx - halfW, cx + halfW, cy - halfH, cy + halfH);
    }

    private static bool IsSurface(PlatformSystem.PlatformType type) => type != PlatformSystem.PlatformType.Hazard;

    public override bool TryGetSupport(int fighter, Fp x, Fp previousY, Fp y, bool falling, out Fp surfaceY)
    {
        surfaceY = Fp.Zero;
        bool found = false;
        int frame = Sim.Frame;
        bool dropping = Sim.Input(fighter).Held(InputButton.Down);

        for (int i = 0; i < _platforms.Length; i++)
        {
            var type = _platforms[i].Type;
            if (!IsSurface(type) || (type == PlatformSystem.PlatformType.PassThrough && dropping)) continue;

            var (left, right, top, _) = BoundsAt(i, frame);
            if (x < left || x > right) continue;

            bool lands = falling
                ? previousY <= top && y >= top
                : Fp.Abs(y - top).Raw <= SupportToleranceRaw;
            if (!lands) continue;
            if (!found || top < surfaceY) surfaceY = top;
            found = true;
        }
        return found;
    }

    public override int BounceVyRaw(int fighter, Fp x, Fp surfaceY)
    {
        int frame = Sim.Frame;
        for (int i = 0; i < _platforms.Length; i++)
        {
            if (_platforms[i].Type != PlatformSystem.PlatformType.Bounce) continue;
            var (left, right, top, _) = BoundsAt(i, frame);
            if (x >= left && x <= right && Fp.Abs(top - surfaceY).Raw <= SupportToleranceRaw) return _platforms[i].BounceForce.Raw;
        }
        return 0;
    }

    public override Fp GroundPush(int fighter, Fp x, Fp y)
    {
        int frame = Sim.Frame;
        for (int i = 0; i < _platforms.Length; i++)
        {
            if (_platforms[i].Type != PlatformSystem.PlatformType.Conveyor) continue;
            var (left, right, top, _) = BoundsAt(i, frame);
            if (x >= left && x <= right && Fp.Abs(y - top).Raw <= SupportToleranceRaw) return _platforms[i].ConveyorSpeed;
        }
        return Fp.Zero;
    }

    public override void Tick(FightState state)
    {
        if (state.RoundState != 0 || state.Frame % HazardIntervalFrames != 0) return;

        for (int f = 0; f < FightState.FighterCount; f++)
        {
            if (state.GetInvuln(f) > 0 || state.GetStateId(f) is FighterStateId.Ko) continue;
            Sim.GetHurtbox(f, out var minX, out var minY, out var maxX, out var maxY);

            for (int i = 0; i < _platforms.Length; i++)
            {
                if (_platforms[i].Type != PlatformSystem.PlatformType.Hazard || _platforms[i].Damage <= 0) continue;
                var (left, right, top, bottom) = BoundsAt(i, state.Frame);
                if (minX < right && maxX > left && minY < bottom && maxY > top)
                {
                    state.SetHealth(f, state.GetHealth(f) - _platforms[i].Damage);
                    break;
                }
            }
        }
    }
}
