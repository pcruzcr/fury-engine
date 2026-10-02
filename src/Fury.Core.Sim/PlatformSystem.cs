using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Platform system for stage interactions (TMNT, Smash Bros, platform fighters).
/// Platforms can be solid, pass-through, moving, or hazardous.
/// All platform state is deterministic and serializable.
/// </summary>
public sealed class PlatformSystem
{
    public const int MaxPlatforms = 8;

    public enum PlatformType : byte
    {
        Solid = 0,
        PassThrough = 1,
        Moving = 2,
        Hazard = 3,
        Bounce = 4,
        Conveyor = 5
    }

    public enum PlatformState : byte
    {
        Inactive = 0,
        Active = 1,
        Triggered = 2
    }

    private readonly PlatformData[] _platforms;

    public PlatformSystem(FightConfig cfg)
    {
        _ = cfg;
        _platforms = new PlatformData[MaxPlatforms];
        for (int i = 0; i < MaxPlatforms; i++)
        {
            _platforms[i] = new PlatformData();
        }
    }

    public int Count { get; private set; }

    public void AddPlatform(in PlatformData data)
    {
        if (Count >= MaxPlatforms) return;
        _platforms[Count] = data with 
        { 
            State = PlatformState.Active,
            CurrentX = data.StartX,
            CurrentY = data.StartY
        };
        Count++;
    }

    public void Clear()
    {
        Count = 0;
        for (int i = 0; i < MaxPlatforms; i++)
        {
            _platforms[i] = new PlatformData();
        }
    }

    public void Update()
    {
        for (int i = 0; i < Count; i++)
        {
            ref var p = ref _platforms[i];
            if (p.State == PlatformState.Inactive) continue;

            p.FrameCounter++;

            // Update moving platforms
            if (p.Type == PlatformType.Moving && p.MoveDuration > 0)
            {
                int t = p.FrameCounter % p.MoveDuration;
                Fp progress = Fp.FromRatio(t, p.MoveDuration);
                p.CurrentX = p.StartX + (p.EndX - p.StartX) * progress;
                p.CurrentY = p.StartY + (p.EndY - p.StartY) * progress;
            }

            // Update conveyor
            if (p.Type == PlatformType.Conveyor)
            {
                p.CurrentX += p.ConveyorSpeed;
                if (p.CurrentX > p.EndX || p.CurrentX < p.StartX)
                {
                    p.ConveyorSpeed = -p.ConveyorSpeed;
                }
            }
        }
    }

    /// <summary>
    /// Checks if a fighter at (x, y) with velocity (vx, vy) lands on any platform.
    /// Returns the platform index or -1 if none.
    /// </summary>
    public int CheckLanding(Fp x, Fp y, Fp vx, Fp vy, Fp width, Fp height)
    {
        for (int i = 0; i < Count; i++)
        {
            ref var p = ref _platforms[i];
            if (p.State == PlatformState.Inactive) continue;
            if (p.Type == PlatformType.PassThrough && vy < Fp.Zero) continue;

            Fp fighterBottom = y + height / Fp.FromInt(2);
            Fp fighterLeft = x - width / Fp.FromInt(2);
            Fp fighterRight = x + width / Fp.FromInt(2);

            Fp platLeft = p.CurrentX - p.Width / Fp.FromInt(2);
            Fp platRight = p.CurrentX + p.Width / Fp.FromInt(2);
            Fp platTop = p.CurrentY - p.Height / Fp.FromInt(2);

            if (fighterRight > platLeft && fighterLeft < platRight)
            {
                if (fighterBottom >= platTop && fighterBottom <= platTop + Fp.FromInt(8) && vy >= Fp.Zero)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    /// <summary>
    /// Checks if a fighter collides with any solid platform from the side.
    /// </summary>
    public bool CheckSideCollision(Fp x, Fp y, Fp width, Fp height)
    {
        for (int i = 0; i < Count; i++)
        {
            ref var p = ref _platforms[i];
            if (p.State == PlatformState.Inactive) continue;
            if (p.Type != PlatformType.Solid) continue;

            Fp fighterLeft = x - width / Fp.FromInt(2);
            Fp fighterRight = x + width / Fp.FromInt(2);
            Fp fighterTop = y - height / Fp.FromInt(2);
            Fp fighterBottom = y + height / Fp.FromInt(2);

            Fp platLeft = p.CurrentX - p.Width / Fp.FromInt(2);
            Fp platRight = p.CurrentX + p.Width / Fp.FromInt(2);
            Fp platTop = p.CurrentY - p.Height / Fp.FromInt(2);
            Fp platBottom = p.CurrentY + p.Height / Fp.FromInt(2);

            if (fighterRight > platLeft && fighterLeft < platRight &&
                fighterBottom > platTop && fighterTop < platBottom)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if a fighter touches a hazard platform.
    /// </summary>
    public bool CheckHazard(Fp x, Fp y, Fp width, Fp height)
    {
        for (int i = 0; i < Count; i++)
        {
            ref var p = ref _platforms[i];
            if (p.State == PlatformState.Inactive) continue;
            if (p.Type != PlatformType.Hazard) continue;

            Fp fighterLeft = x - width / Fp.FromInt(2);
            Fp fighterRight = x + width / Fp.FromInt(2);
            Fp fighterTop = y - height / Fp.FromInt(2);
            Fp fighterBottom = y + height / Fp.FromInt(2);

            Fp platLeft = p.CurrentX - p.Width / Fp.FromInt(2);
            Fp platRight = p.CurrentX + p.Width / Fp.FromInt(2);
            Fp platTop = p.CurrentY - p.Height / Fp.FromInt(2);
            Fp platBottom = p.CurrentY + p.Height / Fp.FromInt(2);

            if (fighterRight > platLeft && fighterLeft < platRight &&
                fighterBottom > platTop && fighterTop < platBottom)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets bounce velocity from a bounce platform.
    /// </summary>
    public Fp GetBounceVelocity(int platformIndex)
    {
        if (platformIndex < 0 || platformIndex >= Count) return Fp.Zero;
        return _platforms[platformIndex].BounceForce;
    }

    /// <summary>
    /// Gets conveyor push speed.
    /// </summary>
    public Fp GetConveyorSpeed(int platformIndex)
    {
        if (platformIndex < 0 || platformIndex >= Count) return Fp.Zero;
        return _platforms[platformIndex].ConveyorSpeed;
    }

    public PlatformData GetPlatform(int index)
    {
        if (index < 0 || index >= Count) return new PlatformData();
        return _platforms[index];
    }

    /// <summary>
    /// Serializable platform data.
    /// </summary>
    public record struct PlatformData
    {
        public PlatformType Type { get; init; }
        public PlatformState State { get; init; }
        public Fp StartX { get; init; }
        public Fp StartY { get; init; }
        public Fp EndX { get; init; }
        public Fp EndY { get; init; }
        public Fp CurrentX { get; set; }
        public Fp CurrentY { get; set; }
        public Fp Width { get; init; }
        public Fp Height { get; init; }
        public int MoveDuration { get; init; }
        public int FrameCounter { get; set; }
        public Fp ConveyorSpeed { get; set; }
        public Fp BounceForce { get; init; }
        public int Damage { get; init; }

        public PlatformData()
        {
            Type = PlatformType.Solid;
            State = PlatformState.Inactive;
            Width = Fp.FromInt(100);
            Height = Fp.FromInt(20);
            MoveDuration = 0;
            BounceForce = Fp.FromInt(12);
            Damage = 0;
        }
    }
}
