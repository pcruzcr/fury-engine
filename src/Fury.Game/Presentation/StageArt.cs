using Fury.Core.Authoring;
using Fury.Game.Content;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>The art of a stage: layers, floor choice and music, with the folder the relative paths refer to.</summary>
/// <param name="Folder">Folder of the stage file (<c>res://stages/dojo</c>, <c>user://stages</c>...); empty for the default arena.</param>
/// <param name="DefaultFloor">Draw the engine's plain floor (off when a layer paints it).</param>
public sealed record StageArtSet(string Folder, IReadOnlyList<BackgroundLayer> Layers, bool DefaultFloor, bool PixelArt, string Music)
{
    /// <summary>The plain look every stage had before art existed.</summary>
    public static StageArtSet Default { get; } = new("", [], true, false, "");

    public string Resolve(string relative) => $"{Folder}/{relative}";

    /// <summary>Full path of the stage's music, or null for the default fight music.</summary>
    public string? MusicPath => Music.Length == 0 ? null : Resolve(Music);
}

/// <summary>
/// Draws the layers of a <see cref="StageArtSet"/> in the 2D view. Each layer is a sprite anchored by its bottom centre to
/// the floor; its horizontal position follows the camera by its depth (0 = world, 1 = screen), which is all a side-scrolling
/// parallax needs because the camera only pans sideways. Missing images are skipped with one warning.
/// </summary>
public sealed class StageArtLayers2D
{
    private const int TilesEachSide = 8;
    private const int BackgroundZ = -50;
    private const int ForegroundZ = 12;

    private readonly List<(Sprite2D Sprite, double Depth, double OffsetX)> _layers = [];

    public StageArtLayers2D(Node2D world, StageArtSet art)
    {
        for (int i = 0; i < art.Layers.Count; i++)
        {
            var layer = art.Layers[i];
            var texture = AssetLoader.LoadTexture(art.Resolve(layer.Image));
            if (texture == null) continue;

            float scale = (float)layer.Scale;
            var sprite = new Sprite2D
            {
                Name = $"Layer{i}",
                Texture = texture,
                Centered = false,
                Scale = new Vector2(scale, scale),
                ZIndex = layer.Foreground ? ForegroundZ + i : BackgroundZ + i,
                TextureFilter = art.PixelArt ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.LinearWithMipmaps,
            };

            float w = texture.GetWidth();
            float h = texture.GetHeight();
            if (layer.Repeat)
            {
                sprite.TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled;
                sprite.RegionEnabled = true;
                sprite.RegionRect = new Rect2(0, 0, w * ((TilesEachSide * 2) + 1), h);
                sprite.Offset = new Vector2(-w * ((TilesEachSide * 2) + 1) * 0.5f, -h);
            }
            else
            {
                sprite.Offset = new Vector2(-w * 0.5f, -h);
            }

            world.AddChild(sprite);
            _layers.Add((sprite, layer.Depth, layer.OffsetX));
            sprite.Position = new Vector2((float)layer.OffsetX, WorldProjection.GroundScreenY - (float)art.Layers[i].OffsetY);
        }
    }

    public int Count => _layers.Count;

    /// <summary>Slides every layer for the camera's current horizontal position (pixels).</summary>
    public void Update(float cameraX)
    {
        foreach (var (sprite, depth, offsetX) in _layers)
        {
            sprite.Position = new Vector2((cameraX * (float)depth) + (float)offsetX, sprite.Position.Y);
        }
    }
}

/// <summary>
/// The 3D counterpart: layers become cards behind (or, for foreground layers, in front of) the fight plane. The perspective
/// camera provides the parallax; cards farther away are scaled up so they look the same size as in the 2D view.
/// </summary>
public static class StageArtLayers3D
{
    private const float ReferenceDistance = 9f;

    public static int Build(Node3D world, StageArtSet art)
    {
        int built = 0;
        for (int i = 0; i < art.Layers.Count; i++)
        {
            var layer = art.Layers[i];
            var texture = AssetLoader.LoadTexture(art.Resolve(layer.Image));
            if (texture == null) continue;

            // Depth 0 lies in the fight plane (just behind the fighters, so a painted floor lines up with their feet), depth 1 is
            // far back; foreground layers hang in front of the fighters.
            float z = layer.Foreground ? 1.5f : -(0.05f + ((float)layer.Depth * 17f));
            float apparent = (ReferenceDistance + System.Math.Abs(z)) / ReferenceDistance;
            if (layer.Foreground) apparent = (ReferenceDistance - z) / ReferenceDistance;

            float pixel = WorldProjection.MetersPerUnit / WorldProjection.PixelsPerUnit * (float)layer.Scale * apparent;
            var sprite = new Sprite3D
            {
                Name = $"Layer{i}",
                Texture = texture,
                Centered = true,
                PixelSize = pixel,
                Shaded = false,
                DoubleSided = true,
                Transparent = true,
                AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
                TextureFilter = art.PixelArt ? BaseMaterial3D.TextureFilterEnum.Nearest : BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            };
            if (layer.Repeat) sprite.Scale = new Vector3(2f, 1f, 1f); // no tiling in 3D: a card wide enough for the usual camera range

            float x = (float)layer.OffsetX * WorldProjection.MetersPerUnit / WorldProjection.PixelsPerUnit * apparent;
            float bottomY = (float)layer.OffsetY * WorldProjection.MetersPerUnit / WorldProjection.PixelsPerUnit;
            sprite.Position = new Vector3(x, bottomY + (texture.GetHeight() * pixel * 0.5f), z);
            world.AddChild(sprite);
            built++;
        }
        return built;
    }
}
