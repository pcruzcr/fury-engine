using Fury.Core.Math;
using Fury.Core.Sim;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>Debug / training overlay: hurtboxes (green), active hitboxes (red) and pushboxes (blue).</summary>
public sealed partial class BoxOverlay2D : Node2D
{
    private FightSimulation? _sim;

    public void Bind(FightSimulation sim) => _sim = sim;

    public override void _Draw()
    {
        if (_sim == null) return;
        float ppu = WorldProjection.PixelsPerUnit;

        for (int f = 0; f < FightState.FighterCount; f++)
        {
            _sim.GetHurtbox(f, out var hMinX, out var hMinY, out var hMaxX, out var hMaxY);
            DrawBox(hMinX, hMinY, hMaxX, hMaxY, new Color(0.2f, 1f, 0.3f), filled: false);

            // Pushbox: full fighter height, pushbox width.
            Fp x = _sim.State.GetX(f);
            Fp y = _sim.State.GetY(f);
            Fp half = _sim.Config.PushboxHalfWidth;
            DrawBox(x - half, y - _sim.Config.HurtboxHeight, x + half, y, new Color(0.3f, 0.5f, 1f, 0.7f), filled: false);

            if (_sim.TryGetHitbox(f, out var aMinX, out var aMinY, out var aMaxX, out var aMaxY))
            {
                DrawBox(aMinX, aMinY, aMaxX, aMaxY, new Color(1f, 0.15f, 0.15f, 0.45f), filled: true);
            }
        }

        for (int slot = 0; slot < FightState.ProjectileSlots; slot++)
        {
            if (!_sim.State.ProjectileActive(slot)) continue;
            _sim.GetProjectileBox(slot, out var pMinX, out var pMinY, out var pMaxX, out var pMaxY);
            DrawBox(pMinX, pMinY, pMaxX, pMaxY, new Color(1f, 0.15f, 0.15f, 0.45f), filled: true);
        }

        _ = ppu;
    }

    private void DrawBox(Fp minX, Fp minY, Fp maxX, Fp maxY, Color color, bool filled)
    {
        var topLeft = WorldProjection.ToScreen(minX, minY);
        var bottomRight = WorldProjection.ToScreen(maxX, maxY);
        DrawRect(new Rect2(topLeft, bottomRight - topLeft), color, filled, filled ? -1f : 2f);
    }
}
