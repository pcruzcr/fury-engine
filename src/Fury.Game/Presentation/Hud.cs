using Godot;

namespace Fury.Game.Presentation;

/// <summary>Everything the HUD displays for one tick.</summary>
public readonly record struct HudState(
    int Health1,
    int Health2,
    int Meter1,
    int Meter2,
    int TimerFrames,
    int Combo1,
    int Combo2,
    bool Rage1,
    bool Rage2);

/// <summary>
/// Responsive heads-up display built from containers and anchors (no hard-coded pixel positions), so it
/// scales with any window size, aspect ratio or UI stretch. Text is only rebuilt when a value changes
/// (no per-frame string allocation). Player 2's bars are mirrored. Includes banners, round pips, a hit
/// flash (disabled by the "reduce flashes" accessibility option) and a debug line.
/// </summary>
public sealed partial class Hud : CanvasLayer
{
    private const int MaxMeter = 300;

    private int _maxHealth = 1000;
    private ProgressBar _health1 = null!, _health2 = null!, _meter1 = null!, _meter2 = null!;
    private Label _name1 = null!, _name2 = null!, _timer = null!, _combo1 = null!, _combo2 = null!;
    private Label _pips1 = null!, _pips2 = null!, _banner = null!, _debug = null!;
    private ColorRect _flash = null!;
    private HudState _last = new(-1, -1, -1, -1, -1, -1, -1, false, false);

    public override void _Ready()
    {
        Layer = 10;
        var root = new Control { Name = "HudRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_flash);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        foreach (var side in new[] { "left", "right", "top" }) margin.AddThemeConstantOverride($"margin_{side}", 16);
        root.AddChild(margin);

        var bar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 16);
        margin.AddChild(bar);

        bar.AddChild(BuildSide(false, out _name1, out _health1, out _meter1, out _pips1, out _combo1));

        _timer = new Label
        {
            Text = "99",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            CustomMinimumSize = new Vector2(90, 0),
        };
        _timer.AddThemeFontSizeOverride("font_size", 44);
        bar.AddChild(_timer);

        bar.AddChild(BuildSide(true, out _name2, out _health2, out _meter2, out _pips2, out _combo2));

        _banner = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(1, 1, 1, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _banner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _banner.AddThemeFontSizeOverride("font_size", 96);
        _banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        _banner.AddThemeConstantOverride("outline_size", 12);
        root.AddChild(_banner);

        _debug = new Label { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _debug.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _debug.Position = new Vector2(12, -64);
        _debug.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(_debug);
    }

    private static VBoxContainer BuildSide(bool mirrored, out Label name, out ProgressBar health, out ProgressBar meter, out Label pips, out Label combo)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        var align = mirrored ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        name = new Label { HorizontalAlignment = align };
        name.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(name);

        health = MakeBar(1000, 26, mirrored, new Color(0.95f, 0.8f, 0.2f), new Color(0.15f, 0.05f, 0.05f));
        box.AddChild(health);
        meter = MakeBar(MaxMeter, 12, mirrored, new Color(0.3f, 0.7f, 1f), new Color(0.05f, 0.08f, 0.15f));
        box.AddChild(meter);

        pips = new Label { HorizontalAlignment = align };
        box.AddChild(pips);
        combo = new Label { HorizontalAlignment = align };
        combo.AddThemeFontSizeOverride("font_size", 28);
        combo.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        box.AddChild(combo);
        return box;
    }

    private static ProgressBar MakeBar(int max, int height, bool mirrored, Color fill, Color back)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = max,
            Value = max,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, height),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FillMode = (int)(mirrored ? ProgressBar.FillModeEnum.EndToBegin : ProgressBar.FillModeEnum.BeginToEnd),
        };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill, CornerRadiusTopLeft = 3, CornerRadiusBottomRight = 3 });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = back, BorderColor = Colors.Black, BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2 });
        return bar;
    }

    /// <summary>Sets the player names and the health scale (call when a fight starts).</summary>
    public void Setup(string p1Name, string p2Name, int maxHealth)
    {
        _maxHealth = maxHealth;
        _health1.MaxValue = maxHealth;
        _health2.MaxValue = maxHealth;
        _name1.Text = p1Name.ToUpperInvariant();
        _name2.Text = p2Name.ToUpperInvariant();
        _last = new HudState(-1, -1, -1, -1, -1, -1, -1, false, false);
        SetRoundWins(0, 0, 2);
    }

    /// <summary>Applies one tick of state; only widgets whose value changed are touched.</summary>
    public void Apply(in HudState s)
    {
        if (s.Health1 != _last.Health1) _health1.Value = Mathf.Clamp(s.Health1, 0, _maxHealth);
        if (s.Health2 != _last.Health2) _health2.Value = Mathf.Clamp(s.Health2, 0, _maxHealth);
        if (s.Meter1 != _last.Meter1) _meter1.Value = Mathf.Clamp(s.Meter1, 0, MaxMeter);
        if (s.Meter2 != _last.Meter2) _meter2.Value = Mathf.Clamp(s.Meter2, 0, MaxMeter);
        if (s.Rage1 != _last.Rage1) _name1.Modulate = s.Rage1 ? new Color(1f, 0.45f, 0.2f) : Colors.White;
        if (s.Rage2 != _last.Rage2) _name2.Modulate = s.Rage2 ? new Color(1f, 0.45f, 0.2f) : Colors.White;

        // Frames → whole seconds, rounding up like arcade clocks (the old HUD clamped raw FRAMES to 99, so it never moved).
        int seconds = (s.TimerFrames + 59) / 60;
        int lastSeconds = (_last.TimerFrames + 59) / 60;
        if (s.TimerFrames < 0 || seconds != lastSeconds || _last.TimerFrames < 0) _timer.Text = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (s.Combo1 != _last.Combo1) _combo1.Text = s.Combo1 > 1 ? $"{s.Combo1} HITS" : "";
        if (s.Combo2 != _last.Combo2) _combo2.Text = s.Combo2 > 1 ? $"{s.Combo2} HITS" : "";
        _last = s;
    }

    public void SetRoundWins(int p1, int p2, int toWin)
    {
        _pips1.Text = Pips(p1, toWin);
        _pips2.Text = Pips(p2, toWin);
    }

    private static string Pips(int wins, int toWin) => new string('●', Mathf.Clamp(wins, 0, toWin)) + new string('○', Mathf.Max(0, toWin - wins));

    /// <summary>Shows a big centered message that fades out after <paramref name="seconds"/> (0 keeps it visible).</summary>
    public void ShowBanner(string text, float seconds = 1.2f)
    {
        _banner.Text = text;
        var tween = CreateTween();
        tween.TweenProperty(_banner, "modulate:a", 1f, 0.08f);
        if (seconds > 0f)
        {
            tween.TweenInterval(seconds);
            tween.TweenProperty(_banner, "modulate:a", 0f, 0.3f);
        }
    }

    public void HideBanner() => _banner.Modulate = new Color(1, 1, 1, 0);

    /// <summary>Quick full-screen flash on strong impacts. Disabled by the photosensitivity option.</summary>
    public void Flash(Color color, float intensity = 0.35f)
    {
        if (GameSettings.ReduceFlashes) return;
        _flash.Color = new Color(color.R, color.G, color.B, intensity);
        CreateTween().TweenProperty(_flash, "color:a", 0f, 0.12f);
    }

    public void SetDebugText(string? text)
    {
        _debug.Visible = text != null && GameSettings.ShowDebugOverlay;
        if (_debug.Visible) _debug.Text = text!;
    }
}
