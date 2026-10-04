using Godot;

namespace Fury.Game.UI;

/// <summary>Small helpers to build responsive menus from containers (no hard-coded pixel positions).</summary>
internal static class UiKit
{
    public static readonly Color Background = new(0.06f, 0.06f, 0.12f);

    /// <summary>Full-screen background plus a centered vertical column; returns the column.</summary>
    public static VBoxContainer CenterColumn(Control host, int separation = 12)
    {
        Control background;
        var picture = Content.UiTheme.Texture("menu_background.png");
        if (picture != null)
        {
            background = new TextureRect { Texture = picture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore };
        }
        else
        {
            background = new ColorRect { Color = Background, MouseFilter = Control.MouseFilterEnum.Ignore };
        }
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        host.AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        host.AddChild(center);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        column.AddThemeConstantOverride("separation", separation);
        center.AddChild(column);
        return column;
    }

    public static Label Title(Container parent, string text, int size = 56)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        parent.AddChild(label);
        return label;
    }

    public static Label Text(Container parent, string text, int size = 20, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var label = new Label { Text = text, HorizontalAlignment = align, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        parent.AddChild(label);
        return label;
    }

    public static Button Button(Container parent, string text, Action onPressed, int size = 24)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 46) };
        button.AddThemeFontSizeOverride("font_size", size);
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }

    public static HBoxContainer Row(Container parent, string label, Control control)
    {
        var row = new HBoxContainer();
        var text = new Label { Text = label, CustomMinimumSize = new Vector2(190, 0), VerticalAlignment = VerticalAlignment.Center };
        row.AddChild(text);
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(control);
        parent.AddChild(row);
        return row;
    }

    public static void GoTo(Node from, string scene) => from.GetTree().ChangeSceneToFile(scene);
}
