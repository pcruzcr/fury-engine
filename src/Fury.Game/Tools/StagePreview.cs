using Fury.Core.Authoring;
using Godot;

namespace Fury.Game.Tools;

/// <summary>Scaled side view of a stage document: arena, floor, platforms and hazards, with the selected element highlighted.</summary>
public sealed partial class StagePreview : Control
{
    private StageData? _stage;
    private int _selected = -1;

    public StagePreview() => CustomMinimumSize = new Vector2(0, 220);

    public void Show(StageData stage, int selectedElement)
    {
        _stage = stage;
        _selected = selectedElement;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var area = new Rect2(Vector2.Zero, Size);
        DrawRect(area, new Color(0.05f, 0.05f, 0.09f));
        if (_stage == null) return;

        var bg = _stage.BackgroundColor;
        DrawRect(area, new Color(bg.R, bg.G, bg.B, 1f));

        // Fit the arena (width x a 6-unit tall view) in the control, floor 12 % above the bottom.
        float scale = Mathf.Max(0.01f, (Size.X - 20f) / Mathf.Max(1, _stage.Width));
        var floor = new Vector2(Size.X * 0.5f, Size.Y * 0.88f);
        DrawLine(new Vector2(10f, floor.Y), new Vector2(Size.X - 10f, floor.Y), new Color(0.9f, 0.8f, 0.5f), 2f);

        // Two fighters at their start positions (3 units = 144 px each side), 4 units tall, for scale.
        foreach (float side in new[] { -1f, 1f })
        {
            float x = floor.X + (side * 144f * scale);
            DrawRect(new Rect2(x - (24f * scale), floor.Y - (192f * scale), 48f * scale, 192f * scale), new Color(1f, 1f, 1f, 0.35f));
        }

        int index = 0;
        foreach (var p in _stage.Platforms)
        {
            var color = p.Kind switch
            {
                PlatformKind.Bounce => new Color(0.3f, 0.85f, 0.4f),
                PlatformKind.Conveyor => new Color(0.95f, 0.8f, 0.25f),
                PlatformKind.PassThrough => new Color(0.45f, 0.65f, 0.95f),
                _ => new Color(0.6f, 0.52f, 0.45f),
            };
            DrawElement(floor, scale, p.X, p.Y, p.Width, p.Height, p.MoveFrames > 0 ? new Color(0.75f, 0.55f, 0.95f) : color, index == _selected);
            if (p.MoveFrames > 0)
            {
                DrawElement(floor, scale, p.MoveToX, p.MoveToY, p.Width, p.Height, new Color(0.75f, 0.55f, 0.95f, 0.3f), false);
            }
            index++;
        }
        foreach (var h in _stage.Hazards)
        {
            DrawElement(floor, scale, h.X, h.Y, h.Width, h.Height, new Color(0.95f, 0.2f, 0.15f, 0.6f), index == _selected);
            index++;
        }
    }

    private void DrawElement(Vector2 floor, float scale, int x, int yUp, int width, int height, Color color, bool selected)
    {
        var rect = new Rect2(floor.X + ((x - (width * 0.5f)) * scale), floor.Y - ((yUp + (height * 0.5f)) * scale), width * scale, height * scale);
        DrawRect(rect, color);
        if (selected) DrawRect(rect.Grow(2f), Colors.White, filled: false, width: 2f);
    }
}
