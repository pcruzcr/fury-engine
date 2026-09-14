using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Phase 6 hit detection: active-window hitboxes vs hurtboxes. Candidate pairs are batched
/// through the integer SIMD kernel (ADR-002) as a conservative gate, then confirmed with
/// the exact Fp test — identical integer semantics on both paths, pure defense-in-depth.
/// Zero heap allocations per frame (stackalloc only).
/// </summary>
public sealed class HitDetectionSystem
{
    private readonly FighterStateMachine _fsm;

    public HitDetectionSystem(FighterStateMachine fsm) => _fsm = fsm;

    /// <summary>connects[attacker] = true when the active attack of that fighter reaches its opponent.</summary>
    public void Detect(FightState s, Span<bool> connects)
    {
        Span<int> aMinX = stackalloc int[FightState.FighterCount];
        Span<int> aMinY = stackalloc int[FightState.FighterCount];
        Span<int> aMaxX = stackalloc int[FightState.FighterCount];
        Span<int> aMaxY = stackalloc int[FightState.FighterCount];
        Span<int> bMinX = stackalloc int[FightState.FighterCount];
        Span<int> bMinY = stackalloc int[FightState.FighterCount];
        Span<int> bMaxX = stackalloc int[FightState.FighterCount];
        Span<int> bMaxY = stackalloc int[FightState.FighterCount];
        Span<bool> simd = stackalloc bool[FightState.FighterCount];

        for (int attacker = 0; attacker < FightState.FighterCount; attacker++)
        {
            connects[attacker] = false;

            if (_fsm.TryGetHitbox(s, attacker, out var hMinX, out var hMinY, out var hMaxX, out var hMaxY))
            {
                aMinX[attacker] = hMinX.Raw;
                aMinY[attacker] = hMinY.Raw;
                aMaxX[attacker] = hMaxX.Raw;
                aMaxY[attacker] = hMaxY.Raw;
            }
            else
            {
                // Degenerate empty box: strict-inequality overlap can never fire.
                aMinX[attacker] = aMinY[attacker] = aMaxX[attacker] = aMaxY[attacker] = 0;
            }

            int defender = 1 - attacker;
            _fsm.GetHurtbox(s, defender, out var uMinX, out var uMinY, out var uMaxX, out var uMaxY);
            bMinX[attacker] = uMinX.Raw;
            bMinY[attacker] = uMinY.Raw;
            bMaxX[attacker] = uMaxX.Raw;
            bMaxY[attacker] = uMaxY.Raw;
        }

        AabbSimd.OverlapMask(aMinX, aMinY, aMaxX, aMaxY, bMinX, bMinY, bMaxX, bMaxY, simd);

        for (int attacker = 0; attacker < FightState.FighterCount; attacker++)
        {
            if (!simd[attacker]) continue;

            // Exact Fp confirmation (half-open intervals).
            bool exact = aMinX[attacker] < bMaxX[attacker] && aMinY[attacker] < bMaxY[attacker]
                      && bMinX[attacker] < aMaxX[attacker] && bMinY[attacker] < aMaxY[attacker];
            connects[attacker] = exact;
        }
    }
}
