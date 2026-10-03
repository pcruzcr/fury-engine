using Godot;

namespace Fury.Game.Presentation;

/// <summary>Draws the procedural fighter (see <see cref="FighterPoser"/>) with outlined, round-capped limbs. Local space is pixels, Y down.</summary>
public sealed partial class PuppetBody2D : Node2D
{
    private static readonly Color Outline = new(0.04f, 0.04f, 0.07f);
    private static readonly Color Skin = new(0.96f, 0.78f, 0.62f);

    private readonly Vector2[] _pose = new Vector2[FighterPoser.JointCount];
    private readonly Color _body;
    private readonly Color _shade;
    private readonly Color _accent;

    public PuppetBody2D(Color body)
    {
        _body = body;
        _shade = body.Darkened(0.35f);
        _accent = body.Lightened(0.55f);
    }

    /// <summary>Copies a pose (fighter space: units, Y up) and schedules a redraw.</summary>
    public void SetPose(Vector2[] pose)
    {
        Array.Copy(pose, _pose, _pose.Length);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawFigure(outline: true);
        DrawFigure(outline: false);
    }

    private Vector2 P(Joint joint)
    {
        var p = _pose[(int)joint];
        return new Vector2(p.X * WorldProjection.PixelsPerUnit, -p.Y * WorldProjection.PixelsPerUnit);
    }

    private void DrawFigure(bool outline)
    {
        float ppu = WorldProjection.PixelsPerUnit;
        float grow = outline ? 0.16f * ppu : 0f;
        float limb = (FighterPoser.LimbWidth * ppu) + grow;
        float torso = (0.66f * ppu) + grow;
        Color Pick(Color c) => outline ? Outline : c;

        // Far side first, then the trunk and head, then the near side on top.
        Bone(P(Joint.ShoulderFar), P(Joint.ElbowFar), P(Joint.HandFar), limb, Pick(_shade));
        Bone(P(Joint.Hip), P(Joint.KneeFar), P(Joint.FootFar), limb * 1.05f, Pick(_shade));
        Cap(P(Joint.FootFar), (0.22f * ppu) + (grow * 0.5f), Pick(_shade.Darkened(0.3f)));

        Segment(P(Joint.Hip), P(Joint.Neck), torso, Pick(_body));
        float headRadius = (FighterPoser.HeadRadius * ppu) + (grow * 0.5f);
        Cap(P(Joint.Head), headRadius, Pick(Skin));

        Bone(P(Joint.Hip), P(Joint.KneeNear), P(Joint.FootNear), limb * 1.05f, Pick(_body));
        Cap(P(Joint.FootNear), (0.24f * ppu) + (grow * 0.5f), Pick(_shade.Darkened(0.3f)));
        Bone(P(Joint.ShoulderNear), P(Joint.ElbowNear), P(Joint.HandNear), limb, Pick(_body));
        Cap(P(Joint.HandNear), (0.2f * ppu) + (grow * 0.5f), Pick(Skin));
        Cap(P(Joint.HandFar), (0.18f * ppu) + (grow * 0.5f), Pick(_shade.Lightened(0.2f)));

        if (outline) return;

        // Face and headband: they show where the fighter is looking even in a crowded frame.
        var head = P(Joint.Head);
        var neck = P(Joint.Neck);
        var up = (head - neck).Normalized();
        var right = new Vector2(-up.Y, up.X); // toward the opponent
        DrawLine(head + (up * headRadius * 0.35f) - (right * headRadius), head + (up * headRadius * 0.35f) + (right * headRadius), _accent, 0.1f * ppu);
        DrawCircle(head + (right * headRadius * 0.45f) + (up * headRadius * -0.05f), 0.06f * ppu, Colors.Black);
        DrawLine(P(Joint.Hip) - new Vector2(torso * 0.45f, 0), P(Joint.Hip) + new Vector2(torso * 0.45f, 0), _accent, 0.1f * ppu);
    }

    private void Bone(Vector2 a, Vector2 mid, Vector2 b, float width, Color color)
    {
        Segment(a, mid, width, color);
        Segment(mid, b, width * 0.92f, color);
    }

    private void Segment(Vector2 a, Vector2 b, float width, Color color)
    {
        DrawLine(a, b, color, width);
        DrawCircle(a, width * 0.5f, color);
        DrawCircle(b, width * 0.5f, color);
    }

    private void Cap(Vector2 at, float radius, Color color) => DrawCircle(at, radius, color);
}
