using Fury.Core.Math;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>
/// The single place where deterministic simulation units become Godot floats (ADR-005).
/// Simulation: X grows right, Y grows DOWN with the ground at Y = 0 (so a fighter in the air has
/// negative Y). One simulation unit is <see cref="PixelsPerUnit"/> pixels in 2D and
/// <see cref="MetersPerUnit"/> meters in 3D (a 4-unit fighter is 2 m tall).
/// </summary>
public static class WorldProjection
{
    public const float PixelsPerUnit = 48f;

    /// <summary>Screen Y of the ground line in the 960x540 reference viewport.</summary>
    public const float GroundScreenY = 440f;

    public const float MetersPerUnit = 0.5f;

    public static Vector2 ToScreen(Fp x, Fp y) => new(x.ToFloat() * PixelsPerUnit, GroundScreenY + (y.ToFloat() * PixelsPerUnit));

    public static Vector2 ToScreen(float x, float y) => new(x * PixelsPerUnit, GroundScreenY + (y * PixelsPerUnit));

    /// <summary>Simulation (x, y) to a 3D point on the fight plane; <paramref name="z"/> is depth for effects/camera.</summary>
    public static Vector3 ToWorld3D(Fp x, Fp y, float z = 0f) => new(x.ToFloat() * MetersPerUnit, -y.ToFloat() * MetersPerUnit, z);

    public static Vector3 ToWorld3D(float x, float y, float z = 0f) => new(x * MetersPerUnit, -y * MetersPerUnit, z);
}
