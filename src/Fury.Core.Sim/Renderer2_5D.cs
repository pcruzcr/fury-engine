using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// 2.5D renderer for 3D visuals with 2D gameplay (Tekken, Soulcalibur, Virtua Fighter).
/// Uses FpVec3 for depth sorting, perspective projection, and visual effects.
/// The simulation remains 2D (X, Y), but Z is used for depth and visual layering.
/// All rendering math is deterministic integer math.
/// </summary>
public sealed class Renderer25D
{
    private readonly RenderObject[] _objects;
    private readonly RenderObject[] _sortScratch;
    private int _objectCount;

    // Camera state
    private FpVec3 _cameraPosition;
    private FpVec3 _cameraTarget;
    private Fp _cameraFov; // vertical field of view in DEGREES

    // Viewport
    private int _viewportWidth;
    private int _viewportHeight;

    public Renderer25D(FightConfig cfg)
    {
        _ = cfg;
        _objects = new RenderObject[64];
        _sortScratch = new RenderObject[64];
        for (int i = 0; i < _objects.Length; i++)
        {
            _objects[i] = new RenderObject();
        }
        _cameraFov = Fp.FromInt(60);
        _viewportWidth = 960;
        _viewportHeight = 540;
    }

    public int ObjectCount => _objectCount;

    /// <param name="fov">Field of view in degrees (e.g. 60). Before it was fed straight to
    /// <see cref="Fp.Tan"/> as radians, so the default "60" meant 60 rad ≈ 3438°.</param>
    public void SetCamera(FpVec3 position, FpVec3 target, Fp fov)
    {
        _cameraPosition = position;
        _cameraTarget = target;
        _cameraFov = fov;
    }

    public void SetViewport(int width, int height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
    }

    /// <summary>
    /// Adds a render object to the scene.
    /// </summary>
    public int AddObject(FpVec3 position, FpVec3 scale, int color, RenderLayer layer)
    {
        if (_objectCount >= _objects.Length) return -1;
        int idx = _objectCount++;
        _objects[idx] = new RenderObject
        {
            Position = position,
            Scale = scale,
            Color = color,
            Layer = layer,
            IsActive = true
        };
        return idx;
    }

    /// <summary>
    /// Updates an existing render object.
    /// </summary>
    public void UpdateObject(int index, FpVec3 position, FpVec3 scale, int color)
    {
        if (index < 0 || index >= _objectCount) return;
        _objects[index].Position = position;
        _objects[index].Scale = scale;
        _objects[index].Color = color;
    }

    /// <summary>
    /// Removes a render object.
    /// </summary>
    public void RemoveObject(int index)
    {
        if (index < 0 || index >= _objectCount) return;
        _objects[index].IsActive = false;
    }

    /// <summary>
    /// Clears all render objects.
    /// </summary>
    public void Clear()
    {
        _objectCount = 0;
        for (int i = 0; i < _objects.Length; i++)
        {
            _objects[i] = new RenderObject();
        }
    }

    /// <summary>
    /// Renders the scene. Returns sorted render commands in back-to-front painter's order
    /// (farthest from the camera first). Objects at or behind the camera plane are culled.
    /// NOTE: presentation-side helper living in Sim only for deterministic Fp math reuse;
    /// it never feeds back into simulation state (ADR-005). One result-array alloc per call;
    /// no List / delegate / closure allocs in the hot path.
    /// </summary>
    public RenderCommand[] Render()
    {
        int n = 0;
        for (int i = 0; i < _objectCount; i++)
        {
            // Behind-camera objects used to be projected with dz forced to 1 (drawn in front).
            if (_objects[i].IsActive && _objects[i].Position.Z > _cameraPosition.Z)
            {
                _sortScratch[n++] = _objects[i];
            }
        }

        // Deterministic insertion sort, FARTHEST first (no delegate allocation, stable).
        // It used to sort nearest-first, the opposite of the painter's order it documented.
        for (int i = 1; i < n; i++)
        {
            var key = _sortScratch[i];
            int j = i - 1;
            while (j >= 0 && _sortScratch[j].Position.Z.CompareTo(key.Position.Z) < 0)
            {
                _sortScratch[j + 1] = _sortScratch[j];
                j--;
            }
            _sortScratch[j + 1] = key;
        }

        Fp fovScale = Fp.Tan(Fp.DegreesToRadians(_cameraFov) / Fp.FromInt(2));
        var commands = new RenderCommand[n];
        for (int i = 0; i < n; i++)
        {
            var obj = _sortScratch[i];
            commands[i] = new RenderCommand
            {
                ScreenX = ProjectX(obj.Position, fovScale),
                ScreenY = ProjectY(obj.Position, fovScale),
                Depth = obj.Position.Z,
                Scale = obj.Scale,
                Color = obj.Color,
                Layer = obj.Layer
            };
        }

        return commands;
    }

    private int ProjectX(FpVec3 worldPos, Fp fovScale)
    {
        Fp dx = worldPos.X - _cameraPosition.X;
        Fp dz = worldPos.Z - _cameraPosition.Z;
        if (dz <= Fp.Zero) dz = Fp.One;

        Fp screenX = (dx / (dz * fovScale)) * Fp.FromInt(_viewportWidth / 2) + Fp.FromInt(_viewportWidth / 2);
        return screenX.TruncInt;
    }

    private int ProjectY(FpVec3 worldPos, Fp fovScale)
    {
        Fp dy = worldPos.Y - _cameraPosition.Y;
        Fp dz = worldPos.Z - _cameraPosition.Z;
        if (dz <= Fp.Zero) dz = Fp.One;

        // Screen Y grows downward: world-up (+Y) must map to smaller screen Y.
        Fp screenY = Fp.FromInt(_viewportHeight / 2) - (dy / (dz * fovScale)) * Fp.FromInt(_viewportHeight / 2);
        return screenY.TruncInt;
    }

    public FpVec3 GetCameraForward()
    {
        return (_cameraTarget - _cameraPosition).Normalized();
    }

    public FpVec3 GetCameraRight()
    {
        FpVec3 forward = GetCameraForward();
        // If forward is parallel to Up, use Right as fallback
        if (forward.X == Fp.Zero && forward.Z == Fp.Zero)
        {
            return FpVec3.Right;
        }
        return FpVec3.Up.Cross(forward).Normalized();
    }

    public FpVec3 GetCameraUp()
    {
        FpVec3 forward = GetCameraForward();
        FpVec3 right = GetCameraRight();
        // If forward and right are parallel, use Up as fallback
        if (forward.X == Fp.Zero && forward.Y == Fp.Zero)
        {
            return FpVec3.Up;
        }
        return right.Cross(forward).Normalized();
    }

    /// <summary>
    /// Ray casting in 2.5D space (slab test). Returns the NEAREST hit; it used to return the
    /// first object in insertion order, even if another one was closer along the ray.
    /// </summary>
    public bool Raycast(FpVec3 origin, FpVec3 direction, Fp maxDistance, out FpVec3 hitPoint, out Fp hitDistance)
    {
        hitPoint = FpVec3.Zero;
        hitDistance = Fp.Zero;
        bool found = false;

        for (int i = 0; i < _objectCount; i++)
        {
            if (!_objects[i].IsActive) continue;

            var obj = _objects[i];
            FpVec3 halfScale = obj.Scale / Fp.FromInt(2);

            Fp tmin = Fp.Zero;
            Fp tmax = maxDistance;

            if (direction.X != Fp.Zero)
            {
                Fp t1 = (obj.Position.X - halfScale.X - origin.X) / direction.X;
                Fp t2 = (obj.Position.X + halfScale.X - origin.X) / direction.X;
                tmin = Fp.Max(tmin, Fp.Min(t1, t2));
                tmax = Fp.Min(tmax, Fp.Max(t1, t2));
            }
            else if (origin.X < obj.Position.X - halfScale.X || origin.X > obj.Position.X + halfScale.X)
            {
                continue;
            }

            if (direction.Y != Fp.Zero)
            {
                Fp t1 = (obj.Position.Y - halfScale.Y - origin.Y) / direction.Y;
                Fp t2 = (obj.Position.Y + halfScale.Y - origin.Y) / direction.Y;
                tmin = Fp.Max(tmin, Fp.Min(t1, t2));
                tmax = Fp.Min(tmax, Fp.Max(t1, t2));
            }
            else if (origin.Y < obj.Position.Y - halfScale.Y || origin.Y > obj.Position.Y + halfScale.Y)
            {
                continue;
            }

            if (direction.Z != Fp.Zero)
            {
                Fp t1 = (obj.Position.Z - halfScale.Z - origin.Z) / direction.Z;
                Fp t2 = (obj.Position.Z + halfScale.Z - origin.Z) / direction.Z;
                tmin = Fp.Max(tmin, Fp.Min(t1, t2));
                tmax = Fp.Min(tmax, Fp.Max(t1, t2));
            }
            else if (origin.Z < obj.Position.Z - halfScale.Z || origin.Z > obj.Position.Z + halfScale.Z)
            {
                continue;
            }

            if (tmax >= tmin && tmax > Fp.Zero && (!found || tmin < hitDistance))
            {
                hitPoint = origin + direction * tmin;
                hitDistance = tmin;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Serializable render object.
    /// </summary>
    public record struct RenderObject
    {
        public FpVec3 Position { get; set; }
        public FpVec3 Scale { get; set; }
        public int Color { get; set; }
        public RenderLayer Layer { get; init; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Serializable render command.
    /// </summary>
    public record struct RenderCommand
    {
        public int ScreenX { get; init; }
        public int ScreenY { get; init; }
        public Fp Depth { get; init; }
        public FpVec3 Scale { get; init; }
        public int Color { get; init; }
        public RenderLayer Layer { get; init; }
    }

    /// <summary>
    /// Render layer for depth sorting.
    /// </summary>
    public enum RenderLayer : byte
    {
        Background = 0,
        Stage = 1,
        Fighter = 2,
        Effect = 3,
        Foreground = 4,
        UI = 5
    }
}
