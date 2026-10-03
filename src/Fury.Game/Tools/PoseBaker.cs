using Fury.Core.Authoring;
using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Fury.Game.Presentation;
using Godot;

namespace Fury.Game.Tools;

/// <summary>
/// Scaffold generator for artists: renders the procedural fighter into guide sprite strips (one per animation, correct
/// names, frame counts, frame size and pivot) plus a matching <c>character.json</c> and portrait, so a new character starts as
/// a working pack that already plays in the game. The artist then repaints the strips over the guides, keeping the sizes.
/// It needs a real window (a headless Godot has no renderer):
/// <c>godot --path . res://scenes/tools/pose_baker.tscn -- --out=C:/art/ryu --id=ryu --name=Ryu --color=#ff8800 --base=glen</c>.
/// </summary>
public partial class PoseBaker : Node
{
    public const int FrameWidth = 448;
    public const int FrameHeight = 256;
    public const int PivotX = 160;
    public const int PivotY = 236;
    public const int PortraitWidth = 256;
    public const int PortraitHeight = 320;

    private readonly record struct Strip(string Name, int Frames, double Fps, Action<FightSimulation, int> Pose);

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var args = ParseArgs();
        if (!args.TryGetValue("out", out string? outDir) || string.IsNullOrWhiteSpace(outDir))
        {
            GD.PrintErr("usage: pose_baker.tscn -- --out=<folder> [--id=name] [--name=Display] [--color=#rrggbb] [--base=glen]");
            GetTree().Quit(2);
            return;
        }

        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("The pose baker draws images: run it in a window, not with --headless.");
            GetTree().Quit(3);
            return;
        }

        string id = args.GetValueOrDefault("id", "newfighter");
        string baseId = args.GetValueOrDefault("base", "glen");
        string color = args.GetValueOrDefault("color", "#4080ff");
        string display = args.GetValueOrDefault("name", id.ToUpperInvariant());
        if (!CharacterManifest.TryParseColor(color, out var parsed))
        {
            GD.PrintErr($"'{color}' is not a #rrggbb colour.");
            GetTree().Quit(2);
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(outDir, "sprites"));
            var character = CharacterRoster.Create(baseId);
            var tint = new Color(parsed.R, parsed.G, parsed.B, parsed.A);

            var viewport = new SubViewport
            {
                Size = new Vector2I(FrameWidth, FrameHeight),
                TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                Disable3D = true,
            };
            var puppet = new PuppetBody2D(tint) { Position = new Vector2(PivotX, PivotY) };
            viewport.AddChild(puppet);
            AddChild(viewport);

            var manifest = BuildManifest(id, display, baseId, color);
            foreach (var strip in Strips(character))
            {
                var sheet = new Image();
                sheet = Image.CreateEmpty(FrameWidth * strip.Frames, FrameHeight, false, Image.Format.Rgba8);
                var sim = new FightSimulation(character, character);
                var joints = new Vector2[FighterPoser.JointCount];
                for (int i = 0; i < strip.Frames; i++)
                {
                    strip.Pose(sim, i);
                    FighterPoser.Compute(sim, 0, joints);
                    puppet.SetPose(joints);
                    var frame = await Capture(viewport);
                    sheet.BlitRect(frame, new Rect2I(0, 0, FrameWidth, FrameHeight), new Vector2I(i * FrameWidth, 0));
                }

                string file = $"sprites/{strip.Name}.png";
                sheet.SavePng(Path.Combine(outDir, file));
                manifest.Visual.Sprites!.Animations[strip.Name] = new SpriteAnimationDef { File = file, Frames = strip.Frames, Fps = strip.Fps };
                GD.Print($"baked {file} ({strip.Frames} frames)");
            }

            puppet.Position = new Vector2(PortraitWidth * 0.5f, PortraitHeight + 110f);
            puppet.Scale = Vector2.One * 2.3f;
            viewport.Size = new Vector2I(PortraitWidth, PortraitHeight);
            var idleSim = new FightSimulation(character, character);
            var idleJoints = new Vector2[FighterPoser.JointCount];
            FighterPoser.Compute(idleSim, 0, idleJoints);
            puppet.SetPose(idleJoints);
            var portrait = await Capture(viewport);
            var card = Image.CreateEmpty(PortraitWidth, PortraitHeight, false, Image.Format.Rgba8);
            card.Fill(tint.Darkened(0.65f));
            card.BlendRect(portrait, new Rect2I(0, 0, PortraitWidth, PortraitHeight), Vector2I.Zero);
            card.SavePng(Path.Combine(outDir, "portrait.png"));
            manifest.Portrait = "portrait.png";

            File.WriteAllText(Path.Combine(outDir, CharacterManifest.FileName), CharacterManifestSerializer.ToJson(manifest));
            GD.Print($"Character pack written to {outDir}");
            GetTree().Quit();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            GD.PrintErr($"Could not write the pack: {e.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task<Image> Capture(SubViewport viewport)
    {
        // Two frames: the first applies the new pose, the second renders it.
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        var image = viewport.GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    private static CharacterManifest BuildManifest(string id, string display, string baseId, string color) => new()
    {
        Id = id,
        DisplayName = display,
        BaseOn = baseId,
        Colors = new CharacterColors { Primary = color },
        Visual = new CharacterVisual
        {
            Scale = 1,
            Sprites = new SpriteSheetDef { FrameWidth = FrameWidth, FrameHeight = FrameHeight, PivotX = PivotX, PivotY = PivotY },
        },
    };

    /// <summary>Every strip a finished character is expected to have, posed from the real simulation so the guides match the frame data.</summary>
    private static IEnumerable<Strip> Strips(CharacterConfig character)
    {
        var moves = character.Moves;
        int airMove = Enumerable.Range(0, moves.MoveCount).FirstOrDefault(i => moves.GetMove(i).Has(MoveTraits.Air), -1);

        yield return new Strip(AnimationNames.Idle, 8, 10, (sim, i) =>
        {
            for (int t = 0; t < 4; t++) sim.Step(new InputState(0), new InputState(0)); // advance the breathing clock
            Pose(sim, FighterStateId.Idle, 0);
        });
        yield return new Strip(AnimationNames.WalkForward, 8, 12, (sim, i) => Pose(sim, FighterStateId.WalkForward, i * 21 / 8));
        yield return new Strip(AnimationNames.WalkBack, 8, 12, (sim, i) => Pose(sim, FighterStateId.WalkBack, i * 21 / 8));
        yield return new Strip(AnimationNames.Crouch, 4, 12, (sim, i) => Pose(sim, FighterStateId.Crouch, i, crouch: true));
        yield return new Strip(AnimationNames.Jump, 6, 12, (sim, i) => Pose(sim, FighterStateId.Jump, i, air: true, vy: Fp.FromRatio(-8 + (i * 3), 10)));
        yield return new Strip(AnimationNames.Dash, 4, 14, (sim, i) => Pose(sim, FighterStateId.Dash, i * 3));
        yield return new Strip(AnimationNames.Backdash, 4, 14, (sim, i) => Pose(sim, FighterStateId.Backdash, i * 3, air: true));

        // One strip per canonical move, stretched over its frame data by the engine, plus the generic fallbacks.
        for (int move = 0; move < MoveSlot.Count; move++)
        {
            int m = move;
            yield return new Strip(AnimationNames.MoveName(null, m), 6, 12, (sim, i) => PoseMove(sim, m, i, 6, crouch: m is MoveSlot.CrouchLight or MoveSlot.CrouchHeavy, air: false));
        }
        yield return new Strip(AnimationNames.Attack, 6, 12, (sim, i) => PoseMove(sim, MoveSlot.StandLight, i, 6, false, false));
        yield return new Strip(AnimationNames.AttackCrouch, 6, 12, (sim, i) => PoseMove(sim, MoveSlot.CrouchLight, i, 6, true, false));
        if (airMove >= 0) yield return new Strip(AnimationNames.AttackAir, 6, 12, (sim, i) => PoseMove(sim, airMove, i, 6, false, true));

        yield return new Strip(AnimationNames.Hit, 3, 12, (sim, i) => Pose(sim, FighterStateId.Hitstun, i * 3));
        yield return new Strip(AnimationNames.HitCrouch, 3, 12, (sim, i) => Pose(sim, FighterStateId.Hitstun, i * 3, crouch: true));
        yield return new Strip(AnimationNames.HitAir, 3, 12, (sim, i) => Pose(sim, FighterStateId.Hitstun, i * 3, air: true));
        yield return new Strip(AnimationNames.Block, 3, 12, (sim, i) => Pose(sim, FighterStateId.Blockstun, i * 3));
        yield return new Strip(AnimationNames.BlockCrouch, 3, 12, (sim, i) => Pose(sim, FighterStateId.Blockstun, i * 3, crouch: true));
        yield return new Strip(AnimationNames.Knockdown, 6, 12, (sim, i) => Pose(sim, FighterStateId.Knockdown, i * 3, air: i < 3));
        yield return new Strip(AnimationNames.Ko, 2, 12, (sim, i) => Pose(sim, FighterStateId.Ko, i * 8));
    }

    private static void PoseMove(FightSimulation sim, int move, int frame, int frames, bool crouch, bool air)
    {
        int total = sim.Data(0).Moves.GetMove(move).TotalFrames;
        // The frame the engine will show as strip frame <frame>: the first tick of that slice of the move.
        int stateFrame = (int)System.Math.Ceiling(frame * (double)total / frames);
        Pose(sim, FighterStateId.Attack, stateFrame, crouch, air, move);
    }

    private static void Pose(FightSimulation sim, int state, int stateFrame, bool crouch = false, bool air = false, int move = -1, Fp? vy = null)
    {
        var s = sim.State;
        s.SetStateId(0, state);
        s.SetStateFrame(0, stateFrame);
        s.SetMoveIndex(0, move);
        s.SetCrouching(0, crouch);
        s.SetAirborne(0, air);
        s.SetVy(0, vy ?? Fp.Zero);
    }

    private static Dictionary<string, string> ParseArgs()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;
            int eq = arg.IndexOf('=');
            if (eq > 2) map[arg[2..eq]] = arg[(eq + 1)..];
        }
        return map;
    }
}
