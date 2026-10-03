using Godot;

namespace Fury.Game.Presentation;

/// <summary>
/// The procedural fighter as 3D meshes: a cylinder per bone and spheres for the head, hands and feet, posed from
/// <see cref="FighterPoser"/> joints (fighter space, simulation units). Near limbs sit in front of the fight plane, far limbs behind.
/// </summary>
public sealed partial class PuppetBody3D : Node3D
{
    private const float NearZ = 0.1f;
    private const float FarZ = -0.1f;

    private readonly record struct BoneDef(Joint From, Joint To, float Radius, bool Near, int Material);

    private static readonly BoneDef[] Bones =
    [
        new(Joint.ShoulderFar, Joint.ElbowFar, 0.075f, false, Shade),
        new(Joint.ElbowFar, Joint.HandFar, 0.07f, false, Shade),
        new(Joint.Hip, Joint.KneeFar, 0.09f, false, Shade),
        new(Joint.KneeFar, Joint.FootFar, 0.08f, false, Shade),
        new(Joint.Hip, Joint.Neck, 0.15f, true, Body),
        new(Joint.Hip, Joint.KneeNear, 0.09f, true, Body),
        new(Joint.KneeNear, Joint.FootNear, 0.08f, true, Body),
        new(Joint.ShoulderNear, Joint.ElbowNear, 0.075f, true, Body),
        new(Joint.ElbowNear, Joint.HandNear, 0.07f, true, Body),
    ];

    private const int Body = 0;
    private const int Shade = 1;
    private const int Skin = 2;

    private readonly MeshInstance3D[] _boneMeshes = new MeshInstance3D[Bones.Length];
    private readonly MeshInstance3D _head;
    private readonly MeshInstance3D[] _ends = new MeshInstance3D[4]; // hand near, hand far, foot near, foot far
    private readonly StandardMaterial3D[] _materials = new StandardMaterial3D[3];

    public PuppetBody3D(Color body)
    {
        _materials[Body] = Material(body);
        _materials[Shade] = Material(body.Darkened(0.35f));
        _materials[Skin] = Material(new Color(0.96f, 0.78f, 0.62f));

        var unitCylinder = new CylinderMesh { Height = 1f, TopRadius = 1f, BottomRadius = 1f, RadialSegments = 12, Rings = 1 };
        for (int i = 0; i < Bones.Length; i++)
        {
            _boneMeshes[i] = new MeshInstance3D { Mesh = unitCylinder, MaterialOverride = _materials[Bones[i].Material] };
            AddChild(_boneMeshes[i]);
        }

        var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 14, Rings = 8 };
        _head = new MeshInstance3D { Mesh = sphere, MaterialOverride = _materials[Skin], Scale = Vector3.One * (FighterPoser.HeadRadius * WorldProjection.MetersPerUnit) };
        AddChild(_head);
        for (int i = 0; i < _ends.Length; i++)
        {
            _ends[i] = new MeshInstance3D { Mesh = sphere, MaterialOverride = _materials[i == 0 ? Skin : Shade] };
            AddChild(_ends[i]);
        }
    }

    /// <summary>The body material, so the view can drive emission from the fighter state.</summary>
    public StandardMaterial3D BodyMaterial => _materials[Body];

    /// <summary>Poses every bone from the joints (fighter space: units, Y up).</summary>
    public void SetPose(Vector2[] pose)
    {
        for (int i = 0; i < Bones.Length; i++)
        {
            var def = Bones[i];
            var a = To3D(pose[(int)def.From], def.Near);
            var b = To3D(pose[(int)def.To], def.Near);
            var axis = b - a;
            float length = axis.Length();
            var mesh = _boneMeshes[i];
            if (length < 1e-4f)
            {
                mesh.Visible = false;
                continue;
            }

            mesh.Visible = true;
            // Scale in the mesh's own space: a unit cylinder becomes a bone of the right radius and length.
            var rotation = new Basis(new Quaternion(Vector3.Up, axis / length));
            var basis = new Basis(rotation.Column0 * def.Radius, rotation.Column1 * length, rotation.Column2 * def.Radius);
            mesh.Transform = new Transform3D(basis, (a + b) * 0.5f);
        }

        _head.Position = To3D(pose[(int)Joint.Head], near: true);
        SetEnd(0, pose[(int)Joint.HandNear], near: true, 0.11f);
        SetEnd(1, pose[(int)Joint.HandFar], near: false, 0.1f);
        SetEnd(2, pose[(int)Joint.FootNear], near: true, 0.12f);
        SetEnd(3, pose[(int)Joint.FootFar], near: false, 0.11f);
    }

    private void SetEnd(int index, Vector2 at, bool near, float radius)
    {
        _ends[index].Position = To3D(at, near);
        _ends[index].Scale = Vector3.One * radius;
    }

    private static Vector3 To3D(Vector2 p, bool near) =>
        new(p.X * WorldProjection.MetersPerUnit, p.Y * WorldProjection.MetersPerUnit, near ? NearZ : FarZ);

    private static StandardMaterial3D Material(Color color) => new() { AlbedoColor = color, Roughness = 0.65f };
}
