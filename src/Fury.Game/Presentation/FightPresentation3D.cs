using Fury.Core.Sim;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>
/// 3D presentation of the 2D simulation (2.5D). Fighters live on the fight plane (Z = 0); the camera is a
/// real perspective <see cref="Camera3D"/>. <see cref="ViewMode.Perspective2_5D"/> keeps it side-on and locked to
/// the plane like a modern 2.5D fighter; <see cref="ViewMode.Cinematic3D"/> adds a slow orbit for replays and
/// showcases. Fighters are placeholder capsules unless <c>res://characters/{id}/visual_3d.tscn</c> exists
/// (any 3D scene, optionally with an AnimationPlayer named after <see cref="FightPresentation.StateAnimation"/>).
/// Gameplay stays 2D: the simulation has no depth axis.
/// </summary>
public sealed partial class FightPresentation3D : FightPresentation
{
    private const float VerticalFov = 32f;
    private const float MinHalfWidth = 4.2f;

    /// <summary>Orbiting cinematic camera instead of the side-locked 2.5D camera.</summary>
    public bool Cinematic { get; init; }

    private FightSimulation? _sim;
    private Node3D _world = null!;
    private Camera3D _camera = null!;
    private ParticlePool3D _particles = null!;
    private readonly FighterShape3D[] _fighters = new FighterShape3D[2];
    private readonly MeshInstance3D[] _hitboxMeshes = new MeshInstance3D[2];
    private readonly MeshInstance3D[] _projectiles = new MeshInstance3D[FightState.ProjectileSlots];
    private readonly List<MeshInstance3D> _stageMeshes = [];
    private StageModule? _stageModule;
    private float _shake;
    private float _orbit;
    private Vector3 _lookAt = new(0, 1.2f, 0);
    private float _distance = 9f;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _world = new Node3D { Name = "World3D" };
        AddChild(_world);

        _world.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = StageBackground ?? new Color(0.07f, 0.08f, 0.16f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.58f, 0.7f),
                AmbientLightEnergy = 0.8f,
            },
        });
        _world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -30f, 0f), LightEnergy = 1.1f, ShadowEnabled = false });

        BuildStage();
        for (int f = 0; f < 2; f++) _fighters[f] = new FighterShape3D(_world, f);
        for (int i = 0; i < 2; i++)
        {
            _hitboxMeshes[i] = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = Vector3.One },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.15f, 0.15f, 0.45f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
                Visible = false,
            };
            _world.AddChild(_hitboxMeshes[i]);
        }
        for (int p = 0; p < _projectiles.Length; p++)
        {
            _projectiles[p] = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.5f, Height = 1f },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.7f, 0.2f),
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.55f, 0.1f),
                },
                Visible = false,
            };
            _world.AddChild(_projectiles[p]);
        }
        _particles = new ParticlePool3D(_world);

        _camera = new Camera3D { Fov = VerticalFov, Near = 0.1f, Far = 200f };
        _world.AddChild(_camera);
        _camera.MakeCurrent();
        _rng.Randomize();
    }

    public override void Bind(FightSimulation sim)
    {
        _sim = sim;
        for (int f = 0; f < 2; f++) _fighters[f].SetCharacter(sim.Data(f).Id);
        BuildStageMeshes(sim);
        Sync();
    }

    public override void Sync()
    {
        if (_sim == null) return;
        for (int f = 0; f < 2; f++) _fighters[f].Apply(_sim, f);

        for (int f = 0; f < 2; f++)
        {
            bool has = ShowBoxes && _sim.TryGetHitbox(f, out _, out _, out _, out _);
            _hitboxMeshes[f].Visible = has;
            if (!has) continue;
            _sim.TryGetHitbox(f, out var minX, out var minY, out var maxX, out var maxY);
            var a = WorldProjection.ToWorld3D(minX, minY);
            var b = WorldProjection.ToWorld3D(maxX, maxY);
            _hitboxMeshes[f].Position = (a + b) * 0.5f;
            _hitboxMeshes[f].Scale = new Vector3(Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y), 0.6f);
        }

        for (int slot = 0; slot < _projectiles.Length; slot++)
        {
            bool live = _sim.State.ProjectileActive(slot);
            _projectiles[slot].Visible = live;
            if (!live) continue;
            _sim.GetProjectileBox(slot, out var minX, out var minY, out var maxX, out var maxY);
            var a = WorldProjection.ToWorld3D(minX, minY);
            var b = WorldProjection.ToWorld3D(maxX, maxY);
            _projectiles[slot].Position = (a + b) * 0.5f;
            _projectiles[slot].Scale = new Vector3(Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y), 0.6f);
        }

        SyncStage();
        UpdateCamera();
    }

    private void BuildStageMeshes(FightSimulation sim)
    {
        foreach (var mesh in _stageMeshes) mesh.QueueFree();
        _stageMeshes.Clear();
        _stageModule = sim.GetModule<StageModule>();
        if (_stageModule == null || _world == null) return;

        for (int i = 0; i < _stageModule.Count; i++)
        {
            var color = PlatformColor(_stageModule.TypeOf(i));
            var mesh = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = Vector3.One },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = color,
                    Transparency = color.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
                },
            };
            _world.AddChild(mesh);
            _stageMeshes.Add(mesh);
        }
    }

    private void SyncStage()
    {
        if (_stageModule == null) return;
        int frame = System.Math.Max(0, _sim!.Frame - 1);
        for (int i = 0; i < _stageMeshes.Count; i++)
        {
            var (left, right, top, bottom) = _stageModule.BoundsAt(i, frame);
            var a = WorldProjection.ToWorld3D(left, top);
            var b = WorldProjection.ToWorld3D(right, bottom);
            _stageMeshes[i].Position = (a + b) * 0.5f;
            _stageMeshes[i].Scale = new Vector3(System.Math.Abs(b.X - a.X), System.Math.Abs(b.Y - a.Y), 1.2f);
        }
    }

    public override void SpawnImpact(int fighter, ImpactKind kind)
    {
        if (_sim == null) return;
        var p = WorldProjection.ToWorld3D(_sim.State.GetX(fighter), _sim.State.GetY(fighter));
        _particles.Burst(p + new Vector3(0, _sim.Config.HurtboxHeight.ToFloat() * WorldProjection.MetersPerUnit * 0.6f, 0.3f), kind);
    }

    public override void Shake(float strength)
    {
        if (GameSettings.ReduceMotion) return;
        _shake = Mathf.Max(_shake, Mathf.Clamp(strength, 0f, 1f));
    }

    private void UpdateCamera()
    {
        if (_sim == null) return;
        float x0 = _sim.State.GetX(0).ToFloat() * WorldProjection.MetersPerUnit;
        float x1 = _sim.State.GetX(1).ToFloat() * WorldProjection.MetersPerUnit;
        float mid = (x0 + x1) * 0.5f;

        // Distance that fits both fighters (plus margin) for the current aspect ratio.
        var size = GetViewport().GetVisibleRect().Size;
        float aspect = size.Y > 0f ? size.X / size.Y : 16f / 9f;
        float halfHeightTan = Mathf.Tan(Mathf.DegToRad(VerticalFov) * 0.5f);
        float halfWidth = Mathf.Max(Mathf.Abs(x1 - x0) * 0.5f + 2.6f, MinHalfWidth);
        float targetDistance = halfWidth / (halfHeightTan * aspect);
        _distance = Mathf.Lerp(_distance, targetDistance, 0.1f);
        _lookAt = _lookAt.Lerp(new Vector3(mid, 1.2f, 0f), 0.2f);

        float yaw = 0f;
        if (Cinematic)
        {
            _orbit += 0.004f;
            yaw = Mathf.Sin(_orbit) * Mathf.DegToRad(38f);
        }

        var offset = new Vector3(Mathf.Sin(yaw) * _distance, 0.4f, Mathf.Cos(yaw) * _distance);
        if (_shake > 0.001f)
        {
            float amplitude = _shake * 0.12f;
            offset += new Vector3(_rng.RandfRange(-amplitude, amplitude), _rng.RandfRange(-amplitude, amplitude), 0f);
            _shake *= 0.82f;
        }
        else
        {
            _shake = 0f;
        }

        _camera.Position = _lookAt + offset;
        _camera.LookAt(_lookAt, Vector3.Up);
    }

    private void BuildStage()
    {
        var floorMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.13f, 0.18f), Roughness = 0.9f };
        _world.AddChild(new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new BoxMesh { Size = new Vector3(80f, 0.2f, 14f) },
            MaterialOverride = floorMaterial,
            Position = new Vector3(0, -0.1f, 0),
        });
        _world.AddChild(new MeshInstance3D
        {
            Name = "BackWall",
            Mesh = new BoxMesh { Size = new Vector3(80f, 18f, 0.4f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.16f, 0.3f), Roughness = 1f },
            Position = new Vector3(0, 8f, -5f),
        });
        _world.AddChild(new MeshInstance3D
        {
            Name = "GroundLine",
            Mesh = new BoxMesh { Size = new Vector3(80f, 0.02f, 0.08f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.8f, 0.45f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            Position = new Vector3(0, 0.01f, 0.5f),
        });
    }

    /// <summary>Placeholder fighter: a capsule scaled to the hurtbox plus a nose that shows the facing direction.</summary>
    private sealed class FighterShape3D
    {
        private readonly Node3D _root = new();
        private readonly MeshInstance3D _body;
        private readonly MeshInstance3D _nose;
        private readonly StandardMaterial3D _material;
        private Node? _visual;
        private string _characterId = "";

        public FighterShape3D(Node parent, int index)
        {
            _root.Name = $"Fighter{index + 1}";
            _material = new StandardMaterial3D { AlbedoColor = index == 0 ? new Color(0.25f, 0.5f, 1f) : new Color(1f, 0.3f, 0.3f), Roughness = 0.6f };
            _body = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1f }, MaterialOverride = _material };
            _nose = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.28f, 0.14f, 0.14f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.White },
            };
            _root.AddChild(_body);
            _root.AddChild(_nose);
            parent.AddChild(_root);
        }

        public void SetCharacter(string id)
        {
            if (id == _characterId) return;
            _characterId = id;
            _visual?.QueueFree();
            _visual = TryInstanceCharacterScene(id, "visual_3d.tscn");
            if (_visual != null) _root.AddChild(_visual);
            _body.Visible = _visual == null;
            _nose.Visible = _visual == null;
        }

        public void Apply(FightSimulation sim, int f)
        {
            var s = sim.State;
            sim.GetHurtbox(f, out _, out var minY, out _, out var maxY);
            float height = Mathf.Max((maxY - minY).ToFloat() * WorldProjection.MetersPerUnit, 0.5f);
            int facing = s.GetFacing(f);

            _root.Position = WorldProjection.ToWorld3D(s.GetX(f), s.GetY(f));
            _root.RotationDegrees = new Vector3(0, facing > 0 ? 0f : 180f, 0);

            ((CapsuleMesh)_body.Mesh).Height = height;
            _body.Position = new Vector3(0, height * 0.5f, 0);
            _nose.Position = new Vector3(0.28f, height * 0.85f, 0f);

            int state = s.GetStateId(f);
            _material.EmissionEnabled = state is FighterStateId.Attack or FighterStateId.Hitstun or FighterStateId.Blockstun;
            _material.Emission = state switch
            {
                FighterStateId.Attack => new Color(0.5f, 0.45f, 0.1f),
                FighterStateId.Hitstun => new Color(0.6f, 0.25f, 0.05f),
                _ => new Color(0.1f, 0.4f, 0.5f),
            };
            PlayAnimation(_visual, StateAnimation(state, s.IsCrouching(f)));
        }
    }
}
