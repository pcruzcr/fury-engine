using Godot;

namespace Fury.Game.UI;

/// <summary>
/// On-screen fight pad for touch devices (Android, iOS, touch laptops): a direction cluster on the left and the
/// attack buttons on the right. Every button is a <see cref="TouchScreenButton"/> bound to the same
/// <c>p1_*</c> input actions as the keyboard, so <see cref="InputAdapter"/> and the whole stack (rollback,
/// replays) see exactly the same inputs. Textures are generated in code (no assets) and the layout follows the
/// viewport size. Shown only when a touch screen exists or when forced with <c>--fury-touch</c>.
/// </summary>
public sealed partial class TouchControls : CanvasLayer
{
    private sealed record PadButton(string Action, string Label, float X, float Y, float Radius);

    // Offsets are in units of the cluster size (so they scale with the screen).
    private static readonly PadButton[] DirectionButtons =
    [
        new("p1_up", "^", 0f, -1f, 0.55f),
        new("p1_down", "v", 0f, 1f, 0.55f),
        new("p1_left", "<", -1f, 0f, 0.55f),
        new("p1_right", ">", 1f, 0f, 0.55f),
    ];

    private static readonly PadButton[] ActionButtons =
    [
        new("p1_light", "L", -1.05f, 0.55f, 0.55f),
        new("p1_medium", "M", 0f, 0.0f, 0.55f),
        new("p1_heavy", "H", 1.05f, -0.55f, 0.55f),
        new("p1_special", "S", 0f, -1.15f, 0.55f),
        new("p1_macro1", "G", -1.05f, -0.55f, 0.45f),
        new("p1_macro2", "!", 1.05f, 0.55f, 0.45f),
        new("p1_macro3", "P", 2.1f, -1.0f, 0.4f),
        new("p1_macro4", "T", 2.1f, 0.0f, 0.4f),
    ];

    private readonly List<(TouchScreenButton Node, PadButton Def, bool Left)> _buttons = [];
    private bool _connected;
    private Texture2D _disc = null!;
    private Texture2D _discPressed = null!;

    /// <summary>True when the pad should be shown on this device.</summary>
    public static bool ShouldShow() =>
        GameSettings.ForceTouchControls || DisplayServer.IsTouchscreenAvailable() || OS.HasFeature("mobile");

    public override void _Ready()
    {
        Layer = 20;
        if (!ShouldShow())
        {
            Visible = false;
            SetProcessInput(false);
            return;
        }

        _disc = MakeDisc(128, new Color(1f, 1f, 1f, 0.22f));
        _discPressed = MakeDisc(128, new Color(1f, 1f, 1f, 0.55f));
        foreach (var def in DirectionButtons) Add(def, left: true);
        foreach (var def in ActionButtons) Add(def, left: false);

        GetViewport().SizeChanged += Layout;
        _connected = true;
        Layout();
    }

    public override void _ExitTree()
    {
        if (_connected && GetViewport() != null) GetViewport().SizeChanged -= Layout;
    }

    private void Add(PadButton def, bool left)
    {
        var button = new TouchScreenButton
        {
            TextureNormal = _disc,
            TexturePressed = _discPressed,
            Shape = new CircleShape2D { Radius = 64f },
            ShapeCentered = true,
            Action = def.Action,
        };
        var label = new Label
        {
            Text = def.Label,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Size = new Vector2(128, 128),
            Position = Vector2.Zero, // the texture origin is its top-left corner
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 54);
        button.AddChild(label);
        AddChild(button);
        _buttons.Add((button, def, left));
    }

    private void Layout()
    {
        var size = GetViewport().GetVisibleRect().Size;
        float unit = Mathf.Clamp(size.Y * 0.11f, 40f, 90f); // cluster size follows the screen height
        var leftCenter = new Vector2((size.X * 0.02f) + (unit * 2.3f), size.Y - (unit * 1.9f));
        var rightCenter = new Vector2(size.X - (unit * 3.4f), size.Y - (unit * 1.9f));

        foreach (var (node, def, left) in _buttons)
        {
            var center = left ? leftCenter : rightCenter;
            float scale = def.Radius * unit * 2f / 128f;
            node.Scale = new Vector2(scale, scale);
            node.Position = center + new Vector2(def.X * unit, def.Y * unit) - (new Vector2(64f, 64f) * scale); // centre the disc on its slot
        }
    }

    /// <summary>Filled anti-aliased disc, generated in memory.</summary>
    private static ImageTexture MakeDisc(int size, Color color)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float radius = (size * 0.5f) - 2f;
        var center = new Vector2(size * 0.5f, size * 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(center);
                float edge = Mathf.Clamp(radius - d + 1f, 0f, 1f); // 1 px soft edge
                if (edge > 0f) image.SetPixel(x, y, new Color(color.R, color.G, color.B, color.A * edge));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }
}
