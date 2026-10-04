using Fury.Game.Content;
using Fury.Core.Math;
using Fury.Core.Sim;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>
/// Flat 2D view: orthographic side view with a smoothed, zooming, limit-aware camera. Fighters are the procedural
/// puppet unless the character's pack ships art (sprite strips or an authored scene, see <see cref="CharacterVisuals"/>).
/// Positions are assigned on the physics tick so Godot's physics interpolation renders smoothly at any refresh rate.
/// </summary>
public sealed partial class FightPresentation2D : FightPresentation
{
    private const float MinZoom = 0.45f;
    private const float MaxZoom = 1.0f;
    private const float MarginPixels = 260f;

    private FightSimulation? _sim;
    private Node2D _world = null!;
    private Camera2D _camera = null!;
    private BoxOverlay2D? _overlay;
    private ParticlePool2D _particles = null!;
    private readonly FighterShape2D[] _fighters = new FighterShape2D[2];
    private readonly Polygon2D[] _projectiles = new Polygon2D[FightState.ProjectileSlots];
    private readonly List<Polygon2D> _stageShapes = [];
    private StageModule? _stageModule;
    private ShaderMaterial _backdropMaterial = null!;
    private StageArtLayers2D? _artLayers;
    private float _shake;
    private float _zoom = 1f;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _world = new Node2D { Name = "World2D" };
        AddChild(_world);

        BuildBackdrop();
        _artLayers = new StageArtLayers2D(_world, StageArt);
        for (int f = 0; f < 2; f++) _fighters[f] = new FighterShape2D(_world, f);
        for (int p = 0; p < _projectiles.Length; p++)
        {
            _projectiles[p] = new Polygon2D
            {
                Polygon = [new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f)],
                Color = new Color(1f, 0.7f, 0.2f),
                ZIndex = 8,
                Visible = false,
            };
            _world.AddChild(_projectiles[p]);
        }

        _overlay = new BoxOverlay2D { ZIndex = 30, Visible = false };
        _world.AddChild(_overlay);
        _particles = new ParticlePool2D(_world);

        _camera = new Camera2D
        {
            ProcessCallback = Camera2D.Camera2DProcessCallback.Physics,
            PositionSmoothingEnabled = false,
            LimitSmoothed = true,
        };
        AddChild(_camera);
        _camera.MakeCurrent();
        _rng.Randomize();
    }

    public override void Bind(FightSimulation sim)
    {
        _sim = sim;
        _overlay?.Bind(sim);
        BuildStageShapes(sim);
        int limit = (int)(sim.Config.StageHalfWidth.ToFloat() * WorldProjection.PixelsPerUnit) + 80;
        if (_camera != null)
        {
            _camera.LimitLeft = -limit;
            _camera.LimitRight = limit;
            _camera.LimitTop = -200;
            _camera.LimitBottom = (int)WorldProjection.GroundScreenY + 160;
        }
        for (int f = 0; f < 2; f++)
        {
            _fighters[f].SetCharacter(sim.Data(f).Id);
            _fighters[f].Reset();
        }
        Sync();
        _camera?.ResetSmoothing();
        if (_camera != null) _camera.Position = new Vector2(0, 270);
    }

    public override void Sync()
    {
        if (_sim == null) return;
        var state = _sim.State;
        for (int f = 0; f < 2; f++) _fighters[f].Apply(_sim, f, SituationOf(_sim, f));
        SyncProjectiles();
        SyncStage();

        if (_overlay != null)
        {
            _overlay.Visible = ShowBoxes;
            if (ShowBoxes) _overlay.QueueRedraw();
        }

        UpdateCamera(state.GetX(0), state.GetX(1));
    }

    private void BuildStageShapes(FightSimulation sim)
    {
        foreach (var shape in _stageShapes) shape.QueueFree();
        _stageShapes.Clear();
        _stageModule = sim.GetModule<StageModule>();
        if (_stageModule == null || _world == null) return;

        for (int i = 0; i < _stageModule.Count; i++)
        {
            var shape = new Polygon2D
            {
                Polygon = [new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f)],
                Color = PlatformColor(_stageModule.TypeOf(i)),
                ZIndex = -3,
            };
            _world.AddChild(shape);
            _stageShapes.Add(shape);
        }
    }

    private void SyncStage()
    {
        if (_stageModule == null) return;
        int frame = System.Math.Max(0, _sim!.Frame - 1); // the frame the fighters were just carried with
        for (int i = 0; i < _stageShapes.Count; i++)
        {
            var (left, right, top, bottom) = _stageModule.BoundsAt(i, frame);
            var topLeft = WorldProjection.ToScreen(left, top);
            var bottomRight = WorldProjection.ToScreen(right, bottom);
            _stageShapes[i].Position = (topLeft + bottomRight) * 0.5f;
            _stageShapes[i].Scale = bottomRight - topLeft;
        }
    }

    private void SyncProjectiles()
    {
        for (int slot = 0; slot < _projectiles.Length; slot++)
        {
            bool live = _sim!.State.ProjectileActive(slot);
            _projectiles[slot].Visible = live;
            if (!live) continue;
            _sim.GetProjectileBox(slot, out var minX, out var minY, out var maxX, out var maxY);
            var topLeft = WorldProjection.ToScreen(minX, minY);
            var bottomRight = WorldProjection.ToScreen(maxX, maxY);
            _projectiles[slot].Position = (topLeft + bottomRight) * 0.5f;
            _projectiles[slot].Scale = bottomRight - topLeft;
        }
    }

    public override void SpawnImpact(int fighter, ImpactKind kind)
    {
        if (_sim == null) return;
        var pos = WorldProjection.ToScreen(_sim.State.GetX(fighter), _sim.State.GetY(fighter));
        _particles.Burst(pos + new Vector2(0, -_sim.Config.HurtboxHeight.ToFloat() * WorldProjection.PixelsPerUnit * 0.6f), kind);
    }

    public override string? CurrentAnimation(int fighter) => _fighters[fighter].CurrentAnimation;

    public override void Shake(float strength)
    {
        if (GameSettings.ReduceMotion) return;
        _shake = Mathf.Max(_shake, Mathf.Clamp(strength, 0f, 1f));
    }

    public override void _Process(double delta)
    {
        // Backdrop animation runs on render time: purely cosmetic, never read by the simulation.
        _backdropMaterial?.SetShaderParameter("time", (float)(Time.GetTicksMsec() * 0.001));
    }

    private void UpdateCamera(Fp x0, Fp x1)
    {
        float mid = (x0.ToFloat() + x1.ToFloat()) * 0.5f * WorldProjection.PixelsPerUnit;
        float distance = Mathf.Abs(x1.ToFloat() - x0.ToFloat()) * WorldProjection.PixelsPerUnit;
        float visibleWidth = GetViewport().GetVisibleRect().Size.X;

        float targetZoom = Mathf.Clamp(visibleWidth / (distance + MarginPixels), MinZoom, MaxZoom);
        _zoom = Mathf.Lerp(_zoom, targetZoom, 0.12f);
        _camera.Zoom = new Vector2(_zoom, _zoom);

        var target = new Vector2(mid, 270f);
        _camera.Position = _camera.Position.Lerp(target, 0.2f);
        _artLayers?.Update(_camera.Position.X);

        if (_shake > 0.001f)
        {
            float amplitude = _shake * 14f;
            _camera.Offset = new Vector2(_rng.RandfRange(-amplitude, amplitude), _rng.RandfRange(-amplitude, amplitude));
            _shake *= 0.82f;
        }
        else
        {
            _shake = 0f;
            _camera.Offset = Vector2.Zero;
        }
    }

    private void BuildBackdrop()
    {
        var shader = new Shader
        {
            Code = """
                shader_type canvas_item;
                uniform float time;
                uniform vec4 top_color : source_color = vec4(0.07, 0.08, 0.18, 1.0);
                uniform vec4 bottom_color : source_color = vec4(0.28, 0.14, 0.22, 1.0);
                void fragment() {
                    float glow = 0.04 * sin(UV.x * 18.0 + time * 0.6) * sin(UV.y * 9.0 - time * 0.4);
                    vec4 c = mix(top_color, bottom_color, UV.y);
                    COLOR = vec4(c.rgb + vec3(glow), 1.0);
                }
                """,
        };
        _backdropMaterial = new ShaderMaterial { Shader = shader };
        if (StageBackground is Color stageColor)
        {
            _backdropMaterial.SetShaderParameter("top_color", stageColor);
            _backdropMaterial.SetShaderParameter("bottom_color", stageColor.Lerp(new Color(0.35f, 0.2f, 0.3f), 0.5f));
        }
        var backdrop = new ColorRect
        {
            Name = "Backdrop",
            Material = _backdropMaterial,
            Position = new Vector2(-2600, -400),
            Size = new Vector2(5200, WorldProjection.GroundScreenY + 400),
            ZIndex = -100,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _world.AddChild(backdrop);

        var ground = new ColorRect
        {
            Name = "Ground",
            Color = new Color(0.12f, 0.1f, 0.14f),
            Position = new Vector2(-2600, WorldProjection.GroundScreenY),
            Size = new Vector2(5200, 400),
            ZIndex = -5,
            Visible = StageArt.DefaultFloor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _world.AddChild(ground);

        var line = new ColorRect
        {
            Name = "GroundLine",
            Color = new Color(0.85f, 0.75f, 0.5f, 0.8f),
            Position = new Vector2(-2600, WorldProjection.GroundScreenY - 2),
            Size = new Vector2(5200, 3),
            ZIndex = -4,
            Visible = StageArt.DefaultFloor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _world.AddChild(line);
    }

    /// <summary>Default fighter: the procedural puppet (see <see cref="FighterPoser"/>), replaced by the character's own scene when it ships one.</summary>
    private sealed class FighterShape2D
    {
        private readonly Node2D _root = new();
        private readonly PuppetBody2D _puppet;
        private readonly PoseSmoother _smoother = new();
        private readonly Vector2[] _target = new Vector2[FighterPoser.JointCount];
        private readonly Color _slotColor;
        private ICharacterVisual? _visual;
        private string _characterId = "";

        public FighterShape2D(Node parent, int index)
        {
            _root.Name = $"Fighter{index + 1}";
            _root.ZIndex = 5 + index;
            _slotColor = index == 0 ? new Color(0.25f, 0.5f, 1f) : new Color(1f, 0.3f, 0.3f);
            _puppet = new PuppetBody2D(_slotColor);
            _root.AddChild(_puppet);
            parent.AddChild(_root);
        }

        public void SetCharacter(string id)
        {
            if (id == _characterId) return;
            _characterId = id;
            _visual?.Root.QueueFree();

            var entry = ContentLibrary.Find(id);
            _visual = CharacterVisuals.Create2D(entry);
            if (_visual != null) _root.AddChild(_visual.Root);
            _puppet.SetColors(entry?.Primary ?? _slotColor, entry?.Accent);
            _puppet.Visible = _visual == null;
            _smoother.Reset();
        }

        /// <summary>The next pose snaps instead of blending (new round or rematch).</summary>
        public void Reset() => _smoother.Reset();

        public string? CurrentAnimation => _visual?.CurrentAnimation;

        public void Apply(FightSimulation sim, int f, in FighterSituation situation)
        {
            var s = sim.State;
            int facing = s.GetFacing(f);

            _root.Position = WorldProjection.ToScreen(s.GetX(f), s.GetY(f));
            _root.Scale = new Vector2(facing, 1f);

            if (_visual != null)
            {
                _visual.Apply(situation); // authored art decides its own look: no tint
                return;
            }

            int state = s.GetStateId(f);
            FighterPoser.Compute(sim, f, _target);
            // Attacks are drawn exactly on their frame data; everything else eases between poses.
            _smoother.Blend(_target, state == FighterStateId.Attack ? 1f : 0.55f);
            _puppet.SetPose(_smoother.Pose);
            _puppet.Modulate = state switch
            {
                FighterStateId.Attack => new Color(1f, 0.95f, 0.5f),
                FighterStateId.Hitstun or FighterStateId.Knockdown => new Color(1f, 0.6f, 0.35f),
                FighterStateId.Blockstun => new Color(0.5f, 0.9f, 1f),
                FighterStateId.Ko => new Color(0.5f, 0.5f, 0.5f),
                _ => Colors.White,
            };
        }
    }
}
