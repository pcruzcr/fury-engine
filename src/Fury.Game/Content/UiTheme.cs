using Godot;
using FileAccess = Godot.FileAccess;

namespace Fury.Game.Content;

/// <summary>
/// Visual identity of the menus and the HUD, replaceable by art without code. Looked up in <c>res://ui/</c> then
/// <c>user://ui/</c>:
/// <list type="bullet">
/// <item><c>theme.tres</c> (<c>res://</c> only: a theme resource can reference scripts) a Godot <see cref="Theme"/> applied to every control of every scene (buttons, labels, panels...).
/// It may define the type variations <c>HudHealthBar</c> and <c>HudMeterBar</c> (base type ProgressBar) to skin the HUD bars.</item>
/// <item><c>font.ttf</c> / <c>font.otf</c> / <c>font.woff2</c> becomes the default font.</item>
/// <item><c>menu_background.png</c> behind every menu, <c>logo.png</c> instead of the title text on the main menu.</item>
/// <item><c>hud/health_fill.png</c>, <c>hud/health_back.png</c>, <c>hud/meter_fill.png</c>, <c>hud/meter_back.png</c> simple bar skins
/// (stretched images) for when a full theme is more than needed.</item>
/// </list>
/// </summary>
public static class UiTheme
{
    private static readonly string[] Roots = ["res://ui", "user://ui"];
    private static readonly string[] FontExtensions = [".ttf", ".otf", ".woff2"];
    private static bool _applied;

    /// <summary>The theme applied to the root window (null when the game uses Godot's default look).</summary>
    public static Theme? Current { get; private set; }

    /// <summary>Loads the theme and font once per run; later calls do nothing.</summary>
    public static void Apply(Node anyNode)
    {
        if (_applied) return;
        _applied = true;

        // theme.tres is a resource that may reference scripts: only the game's own (res://) theme is loaded. Fonts and images are data.
        Theme? theme = null;
        foreach (string root in Roots.Where(AssetLoader.IsTrusted))
        {
            string path = $"{root}/theme.tres";
            if (ResourceLoader.Exists(path) && ResourceLoader.Load<Theme>(path) is { } loaded)
            {
                theme = loaded;
                break;
            }
        }

        var font = LoadFont();
        if (font != null)
        {
            theme ??= new Theme();
            theme.DefaultFont = font;
        }

        if (theme == null) return;
        Current = theme;
        anyNode.GetTree().Root.Theme = theme;
    }

    /// <summary>A texture from the UI folders (e.g. <c>menu_background.png</c>), or null.</summary>
    public static Texture2D? Texture(string relative)
    {
        foreach (string root in Roots)
        {
            string path = $"{root}/{relative}";
            if (!AssetLoader.Exists(path)) continue;
            var texture = AssetLoader.LoadTexture(path);
            if (texture != null) return texture;
        }
        return null;
    }

    /// <summary>
    /// The style of a HUD bar part. A theme variation wins; otherwise a skin image from <c>ui/hud/</c>; otherwise null, which tells
    /// the caller to use the built-in flat colours.
    /// </summary>
    public static StyleBox? HudBar(string variation, string part, string skinFile)
    {
        if (Current != null && Current.HasStylebox(part, variation)) return Current.GetStylebox(part, variation);

        var texture = Texture($"hud/{skinFile}.png");
        return texture == null ? null : new StyleBoxTexture { Texture = texture, AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch, AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch };
    }

    private static Font? LoadFont()
    {
        foreach (string root in Roots)
        {
            foreach (string extension in FontExtensions)
            {
                string path = $"{root}/font{extension}";
                if (ResourceLoader.Exists(path) && ResourceLoader.Load<Font>(path) is { } resource) return resource;
                if (FileAccess.FileExists(path))
                {
                    var file = new FontFile();
                    if (file.LoadDynamicFont(path) == Error.Ok) return file;
                }
            }
        }
        return null;
    }
}
