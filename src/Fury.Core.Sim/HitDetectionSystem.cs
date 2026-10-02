using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Phase 6 hit detection: active-window melee hitboxes and live projectiles against the opponent's hurtbox.
/// All candidate pairs (2 melee + <see cref="FightState.ProjectileSlots"/> projectiles) are batched through the
/// integer SIMD kernel (ADR-002) as a conservative gate, then confirmed with the exact test: identical integer
/// semantics on both paths, pure defense-in-depth. Defenders that are invulnerable (wake-up, backdash) or
/// lying down cannot be hit. Zero heap allocations per frame (stackalloc only).
/// </summary>
public sealed class HitDetectionSystem
{
    private const int Pairs = FightState.FighterCount + FightState.ProjectileSlots;

    private readonly FighterStateMachine _fsm;

    public HitDetectionSystem(FighterStateMachine fsm) => _fsm = fsm;

    /// <summary>
    /// <paramref name="connects"/>[attacker] = true when the active melee attack of that fighter reaches its
    /// opponent; <paramref name="projectileHits"/>[slot] = true when that projectile reaches the opponent of its owner.
    /// </summary>
    public void Detect(FightState s, Span<bool> connects, Span<bool> projectileHits)
    {
        Span<int> aMinX = stackalloc int[Pairs];
        Span<int> aMinY = stackalloc int[Pairs];
        Span<int> aMaxX = stackalloc int[Pairs];
        Span<int> aMaxY = stackalloc int[Pairs];
        Span<int> bMinX = stackalloc int[Pairs];
        Span<int> bMinY = stackalloc int[Pairs];
        Span<int> bMaxX = stackalloc int[Pairs];
        Span<int> bMaxY = stackalloc int[Pairs];
        Span<bool> gate = stackalloc bool[Pairs];
        Span<bool> candidate = stackalloc bool[Pairs];

        for (int attacker = 0; attacker < FightState.FighterCount; attacker++)
        {
            connects[attacker] = false;
            int defender = 1 - attacker;

            Fp hMinX = Fp.Zero, hMinY = Fp.Zero, hMaxX = Fp.Zero, hMaxY = Fp.Zero;
            candidate[attacker] = Vulnerable(s, defender)
                && _fsm.TryGetHitbox(s, attacker, out hMinX, out hMinY, out hMaxX, out hMaxY);
            if (candidate[attacker])
            {
                aMinX[attacker] = hMinX.Raw;
                aMinY[attacker] = hMinY.Raw;
                aMaxX[attacker] = hMaxX.Raw;
                aMaxY[attacker] = hMaxY.Raw;
            }
            else
            {
                // Degenerate point box at the origin. It is NOT guaranteed empty under strict overlap, so
                // the pair is also excluded explicitly through the candidate flag below.
                aMinX[attacker] = aMinY[attacker] = aMaxX[attacker] = aMaxY[attacker] = 0;
            }
            StoreHurtbox(s, defender, attacker, bMinX, bMinY, bMaxX, bMaxY);
        }

        for (int slot = 0; slot < FightState.ProjectileSlots; slot++)
        {
            int i = FightState.FighterCount + slot;
            projectileHits[slot] = false;
            int defender = 1 - (s.ProjectileActive(slot) ? s.ProjectileOwner(slot) : 0);

            candidate[i] = s.ProjectileActive(slot) && Vulnerable(s, defender);
            if (candidate[i])
            {
                _fsm.GetProjectileBox(s, slot, out var pMinX, out var pMinY, out var pMaxX, out var pMaxY);
                aMinX[i] = pMinX.Raw;
                aMinY[i] = pMinY.Raw;
                aMaxX[i] = pMaxX.Raw;
                aMaxY[i] = pMaxY.Raw;
            }
            else
            {
                aMinX[i] = aMinY[i] = aMaxX[i] = aMaxY[i] = 0;
            }
            StoreHurtbox(s, defender, i, bMinX, bMinY, bMaxX, bMaxY);
        }

        AabbSimd.OverlapMask(aMinX, aMinY, aMaxX, aMaxY, bMinX, bMinY, bMaxX, bMaxY, gate);

        for (int i = 0; i < Pairs; i++)
        {
            if (!candidate[i] || !gate[i]) continue;

            // Exact confirmation (half-open intervals).
            bool exact = aMinX[i] < bMaxX[i] && aMinY[i] < bMaxY[i] && bMinX[i] < aMaxX[i] && bMinY[i] < aMaxY[i];
            if (i < FightState.FighterCount) connects[i] = exact;
            else projectileHits[i - FightState.FighterCount] = exact;
        }
    }

    private void StoreHurtbox(FightState s, int defender, int index, Span<int> minX, Span<int> minY, Span<int> maxX, Span<int> maxY)
    {
        _fsm.GetHurtbox(s, defender, out var uMinX, out var uMinY, out var uMaxX, out var uMaxY);
        minX[index] = uMinX.Raw;
        minY[index] = uMinY.Raw;
        maxX[index] = uMaxX.Raw;
        maxY[index] = uMaxY.Raw;
    }

    /// <summary>Invulnerable, knocked-down or KO'd fighters have no active hurtbox.</summary>
    private static bool Vulnerable(FightState s, int defender) =>
        s.GetInvuln(defender) == 0 && s.GetStateId(defender) is not (FighterStateId.Knockdown or FighterStateId.Ko);
}
