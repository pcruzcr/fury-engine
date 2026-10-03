namespace Fury.Core.Authoring;

/// <summary>
/// File-level checks of art that need image sizes (the engine-free half; the Godot validator supplies the pixel sizes).
/// </summary>
public static class ContentChecks
{
    /// <summary>A sprite strip must be exactly <c>frames x frameWidth</c> by <c>frameHeight</c> pixels.</summary>
    public static void CheckStrip(ContentReport report, string where, SpriteSheetDef sheet, SpriteAnimationDef animation, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(animation);

        if (imageHeight != sheet.FrameHeight)
        {
            report.Error(where, $"image height is {imageHeight} px but frameHeight is {sheet.FrameHeight}: every frame of every strip must have the same height.");
        }

        int expected = animation.Frames * sheet.FrameWidth;
        if (imageWidth == expected) return;

        string hint = sheet.FrameWidth > 0 && imageWidth % sheet.FrameWidth == 0
            ? $" ({imageWidth / sheet.FrameWidth} frames of {sheet.FrameWidth} px fit: set frames to {imageWidth / sheet.FrameWidth})"
            : $" (the width must be a multiple of the frame width, {sheet.FrameWidth} px)";
        report.Error(where, $"image width is {imageWidth} px but {animation.Frames} frames x {sheet.FrameWidth} px = {expected}{hint}.");
    }

    /// <summary>Portraits are shown in the character select and the HUD: tiny or huge images are almost certainly a mistake.</summary>
    public static void CheckPortrait(ContentReport report, string where, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (width < 64 || height < 64) report.Warn(where, $"portrait is only {width}x{height} px; it will look blurry (64 px minimum, 256x320 recommended).");
        else if (width > 2048 || height > 2048) report.Warn(where, $"portrait is {width}x{height} px; anything above 2048 only wastes memory.");
    }

    /// <summary>
    /// Compares the animations an authored <c>AnimationPlayer</c> offers with what the engine can ask for, so a typo in a clip
    /// name (which would silently never play) is reported.
    /// </summary>
    public static void CheckPlayerAnimations(ContentReport report, string where, CharacterManifest manifest, IEnumerable<string> clips)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(manifest);
        var names = clips.Where(c => c.Length > 0 && c != "RESET").ToList();

        if (!names.Contains(AnimationNames.Idle)) report.Error(where, "the AnimationPlayer has no 'idle' animation.");
        foreach (string clip in names.Where(c => !manifest.IsKnownAnimation(c)))
        {
            report.Warn(where, $"animation '{clip}' is not a standard animation nor a move name: it will never play.");
        }
        foreach (string recommended in AnimationNames.Recommended.Where(r => !names.Contains(r)))
        {
            report.Warn(where, $"no '{recommended}' animation: the fighter falls back to idle in that situation.");
        }
    }
}
