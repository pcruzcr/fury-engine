// Fighter.Core — fight simulation: the §15 tick pipeline over two fighters.
// Order (ADR, never reorder without ADR): InputRead → Buffer → CommandResolution →
// FSM Commit → Movement → HitDetection → Resolution+Energy → Snapshot (external) → Events.
// F1 scope notes: grounded attacks only (no air normals yet); holding away always blocks
// (no proximity-block); walk-back does not exist (away = block stance); throws-tech as specified.
namespace Fighter.Core;

/// <summary>Fight simulation: the §15 tick pipeline over two fighters (Façade over
/// input, FSM, movement, collision, resolution, energy). Deterministic: same seed +
/// same inputs ⇒ same states, hashes, events. Presentation reads; never writes.</summary>
public sealed class FightSimulation
{
    /// <summary>Live fighter states (index 0 = P1).</summary>
    public readonly Fighter[] Fighters = { new Fighter(), new Fighter() };
    /// <summary>Character data (index 0 = P1).</summary>
    public readonly FighterData[] Datas = new FighterData[2];
    /// <summary>Stage definition (modifiers; id travels in snapshots).</summary>
    public readonly StageData Stage;
    private readonly RingBuffer<InputState>[] _hist =
        { new(SimulationUnits.RingCapacity), new(SimulationUnits.RingCapacity) };
    private readonly InputState[] _prev = { default, default };
    private readonly DeterministicRng _rng;

    /// <summary>Current frame (monotonic within the round).</summary>
    public int Frame { get; private set; }
    /// <summary>Round number (presentation-tracked; reset per round by the caller).</summary>
    public int RoundNumber = 1;
    /// <summary>Round timer, frames remaining (timeout resolves by HP).</summary>
    public int RoundTimer;
    /// <summary>Round decided (KO or timeout).</summary>
    public bool Over;
    /// <summary>Winner index (0/1), 2 = draw, −1 = undecided.</summary>
    public int Winner = -1;

    /// <summary>This step's sim events (cleared and refilled every step;
    /// the reference is stable — contents are valid only until the next Step).</summary>
    public System.Collections.Generic.List<SimEvent> LastSimEvents = new();
    /// <summary>This step's presentation triggers (cleared and refilled every step;
    /// the reference is stable — contents are valid only until the next Step).</summary>
    public System.Collections.Generic.List<PresEvent> LastPresEvents = new();

    /// <summary>Optional deterministic observer (metrics/tracing/debugging). Null = zero overhead
    /// beyond one branch. Never part of snapshots, hashes or replays.</summary>
    public IFightObserver? Observer;

    /// <summary>Creates a fight: seed, two character definitions, stage (default Arena),
    /// round time (default 80s ranked).</summary>
    public FightSimulation(ulong seed, FighterData a, FighterData b, int roundTimer = 80 * 60, StageData? stage = null)
    {
        _rng = new DeterministicRng(seed);
        Datas[0] = a; Datas[1] = b;
        Stage = stage ?? StageData.Arena();
        StageData.Validate(Stage);
        RoundTimer = roundTimer;
        Spawn(0, 300000, 1);
        Spawn(1, 660000, -1);
        _prev[0] = new InputState(Buttons.None, 0, 0, -1);
        _prev[1] = new InputState(Buttons.None, 0, 0, -1);
    }

    private void Spawn(int i, int x, int facing)
    {
        var f = Fighters[i];
        f.Pos = new Vector2I(x, Calibration.GroundY);
        f.Vel = Vector2I.Zero;
        f.Facing = facing;
        f.MaxHp = Datas[i].MaxHp;
        f.Hp = f.MaxHp;
        f.Affinity = Datas[i].Affinity;
        f.Gimmick = Datas[i].Gimmick;
        f.JuggleMax = Datas[i].JuggleMax;
        f.Enter(FighterState.Neutral);
    }

    /// <summary>Manual Resonance activation entry point (presenter/AI calls this;
    /// the auto-trigger lifeline is evaluated inside resolution).</summary>
    public bool TryActivateResonance(int idx, bool manual) =>
        EnergySystem.TryActivateResonance(Fighters[idx], manual, Frame,
            LastSimEvents, LastPresEvents, idx);

    /// <summary>Starts the next round after <see cref="Over"/>: winner keeps EX, loser gets
    /// the 30% grace (GDD §5.3), positions/facing/states reset, timers cleared, combo state
    /// zeroed, round timer restored. Deterministic: same call order ⇒ same state.</summary>
    public void NextRound()
    {
        if (!Over) throw new InvalidOperationException("NextRound requires a decided round.");
        for (int i = 0; i < 2; i++)
            EnergySystem.ResetForRound(Fighters[i], isLoser: i != Winner);
        Fighters[0].Pos = new Vector2I(300000, Calibration.GroundY);
        Fighters[1].Pos = new Vector2I(660000, Calibration.GroundY);
        Fighters[0].Vel = Vector2I.Zero;
        Fighters[1].Vel = Vector2I.Zero;
        Fighters[0].Facing = 1;
        Fighters[1].Facing = -1;
        RoundTimer = 80 * 60;
        RoundNumber++;
        Over = false;
        Winner = -1;
        _prev[0] = new InputState(Buttons.None, 0, 0, Frame);
        _prev[1] = new InputState(Buttons.None, 0, 0, Frame);
        _hist[0].Clear();
        _hist[1].Clear();
        LastSimEvents.Clear();
        LastPresEvents.Clear();
    }

    /// <summary>Read-only input history copy (oldest→newest) for training HUD/debugging.
    /// Presentation-only accessor: never feeds the simulation.</summary>
    public InputState[] GetHistory(int idx)
    {
        var tmp = new InputState[_hist[idx].Count];
        _hist[idx].CopyOldestFirst(tmp);
        return tmp;
    }

    /// <summary>Advances exactly one fixed 60 Hz frame through the §15 pipeline
    /// (buffer → actions → movement → interaction → timers → observer → frame++).
    /// Single-threaded by design (zero heap allocation on the hot path after
    /// warmup: event lists are cleared and reused, inputs stay in locals).</summary>
    [SingleThreaded]
    public void Step(InputState p1, InputState p2)
    {
        LastSimEvents.Clear();
        LastPresEvents.Clear();
        if (Over)
        {
            Observer?.OnFrame(this, Frame);
            return;
        }

        // Stage zones (positions from last frame — deterministic): EX ×1.05 / speed ×0.9 inside.
        for (int i = 0; i < 2; i++) UpdateZone(Fighters[i]);

        // 1-2. Buffer (always pushed, even while frozen — inputs are never lost to history).
        _hist[0].Push(p1);
        _hist[1].Push(p2);
        InputState edgePrev0 = _prev[0], edgePrev1 = _prev[1]; // edges pre-overwrite (Move needs them)
        _prev[0] = p1; _cur[0] = p1;
        _prev[1] = p2; _cur[1] = p2;

        // 0. Hitstop gate: movement/timers/RNG frozen — BUT cancel inputs are buffered
        // (genre-standard: specials chained during freeze execute on the first live frame;
        // here they start immediately with MoveFrame frozen, which is equivalent).
        // Fresh actions are NOT admitted while frozen (no whiff starts out of freeze).
        if (Fighters[0].Hitstop > 0 || Fighters[1].Hitstop > 0)
        {
            StartBufferedCancel(0, edgePrev0, p1);
            StartBufferedCancel(1, edgePrev1, p2);
            Fighters[0].LastInput = p1;
            Fighters[1].LastInput = p2;
            RecordCommandNormal(); // Phase 4 intent is recorded even while frozen.
            if (Fighters[0].Hitstop > 0) Fighters[0].Hitstop--;
            if (Fighters[1].Hitstop > 0) Fighters[1].Hitstop--;
            NotifyObserver();
            Frame++;
            return;
        }

        // 3. Facing (only while both actionable — no free turns mid-attack).
        if (FighterStateMeta.IsActionable(Fighters[0].State) &&
            FighterStateMeta.IsActionable(Fighters[1].State))
        {
            int dx = Fighters[1].Pos.X - Fighters[0].Pos.X;
            if (dx != 0)
            {
                Fighters[0].Facing = dx > 0 ? 1 : -1;
                Fighters[1].Facing = dx > 0 ? -1 : 1;
            }
        }

        // 4. Fresh actions (edges computed against pre-step inputs).
        StartActions(0, edgePrev0, p1);
        StartActions(1, edgePrev1, p2);
        Fighters[0].LastInput = p1;
        Fighters[1].LastInput = p2;
        RecordCommandNormal(); // Phase 4: intent recording (MoveResolver stays the authority).

        // 5. Movement (needs the same pre-step edges for jump).
        Move(0, p1, edgePrev0);
        Move(1, p2, edgePrev1);

        // 6. Interactions.
        Interact();

        // 7. Timers, states, energy, round.
        for (int i = 0; i < 2; i++) TickFighter(i);
        for (int i = 0; i < 2; i++) EnergySystem.TickRage(Fighters[i], Frame, LastSimEvents, i);
        for (int i = 0; i < 2; i++) EnergySystem.TickResonance(Fighters[i], Frame, LastSimEvents, i);
        for (int i = 0; i < 2; i++) EnergySystem.MaybeAutoRage(Fighters[i], Frame, LastSimEvents, LastPresEvents, i);
        ResetCombosWhereStanding();
        if (RoundTimer > 0 && --RoundTimer == 0 && !Over) Timeout();
        _ = _rng.NextULong(); // one draw per frame: stage-future stream stays frame-locked
        NotifyObserver();
        Frame++;
        CheckRoundEnd();
    }

    /// <summary>Observer epilogue (R1): this step's sim events in emission order, then OnFrame.
    /// Runs on live, frozen AND over steps — exactly once per Step call.</summary>
    private void NotifyObserver()
    {
        var obs = Observer;
        if (obs is null) return;
        foreach (var e in LastSimEvents) obs.OnSimEvent(this, e);
        obs.OnFrame(this, Frame);
    }

    /// <summary>Freeze-buffered cancel: only an in-progress connected attack (and not
    /// a parried one) may start its chained move while hitstop is frozen.</summary>
    /// <summary>Recomputes a fighter's zone multipliers from feet X (neutral outside).</summary>
    private void UpdateZone(Fighter f)
    {
        bool inside = false;
        foreach (var z in Stage.Zones)
            if (z.Contains(f.Pos.X)) { inside = true; break; }
        f.ExZonePerMille = inside ? StageData.ExZonePerMille : 1000;
        f.SpeedZonePerMille = inside ? StageData.SpeedZonePerMille : 1000;
    }

    /// <summary>Phase-4 command-normal INTENT recording (informational only).
    /// Resolves <see cref="CommandNormalResolver"/> over each fighter's ring
    /// history (which already includes the current frame) and stores the
    /// result in <see cref="Fighter.LastCommandNormal"/> (schema 5). The fight
    /// authority stays <see cref="MoveResolver"/>: this never starts a move.
    /// Zero heap allocation (stack-only span over the ≤8 history entries).</summary>
    private void RecordCommandNormal()
    {
        System.Span<InputState> tmp = stackalloc InputState[SimulationUnits.RingCapacity];
        for (int i = 0; i < 2; i++)
        {
            _hist[i].CopyTo(tmp);
            Fighters[i].LastCommandNormal = (int)CommandNormalResolver.Resolve(
                tmp.Slice(0, _hist[i].Count), Fighters[i].State, Fighters[i].Facing);
        }
    }

    private void StartBufferedCancel(int i, InputState prev, InputState cur)
    {
        var f = Fighters[i];
        if (f.State != FighterState.Attack || !f.DidHit || f.NoCancel) return;
        bool behind = IsBehind(i);
        var ctx = new ResolveContext(Datas[i], f.Facing, prev, cur, _hist[i],
            f.State, f.DidHit, ActiveKindOf(i), f.MoveId, f.AirAttackUsed, f.Airborne, f.Ex, behind);
        int moveId = MoveResolver.Resolve(ctx);
        if (moveId > 0) StartMove(i, Datas[i].Get(moveId));
        // moveId -1 (gated) or 0: edge consumed by the freeze, no action. Standard behavior.
    }

    // ---- 4. Action starts ----
    private void StartActions(int i, InputState prev, InputState cur)
    {
        var f = Fighters[i];
        // BUGFIX F1: the gate must admit the cancel window (Attack + hit landed),
        // otherwise MoveResolver's cancel legality is dead code and chains can never start.
        // F2a: airborne Jump with a fresh air action also passes (one per jump).
        bool inCancelWindow = f.State == FighterState.Attack && f.DidHit;
        bool airStart = f.Airborne && f.State == FighterState.Jump && !f.AirAttackUsed;
        if (!FighterStateMeta.IsActionable(f.State) && !inCancelWindow && !airStart) return;
        bool actionable = FighterStateMeta.IsActionable(f.State);

        // Backdash: back-tap edge with an older neutral-origin back edge (grounded only).
        if (actionable && cur.DirX == -f.Facing && prev.DirX != -f.Facing && HasOlderBackEdge(i))
        {
            f.Enter(FighterState.Backdash, Calibration.BackdashDuration);
            f.InvulnT = 6; // GDD §4.6 frames 1-6
            f.Vel = new Vector2I(-f.Facing * Calibration.BackdashSpeed * (1 + f.AccelStacks), 0);
            return;
        }

        // Parry: LP+MP chord edge (OPEN-007). Checked before move resolution.
        if (actionable && IsChordEdge(prev, cur, Buttons.LP | Buttons.MP))
        {
            bool dantRage = f.RageActive && f.Affinity == RageAffinity.Dant;
            f.Enter(FighterState.ParryStance, dantRage ? Calibration.ParryWindowDantRage : Calibration.ParryWindow);
            f.ParryStanceT = f.StateDuration;
            if (dantRage && f.FirstParryAvailable)
            {
                f.FirstParryAvailable = false;
                f.ParryGraceArmed = true; // OPEN-004: grace granted at expiry
            }
            return;
        }

        bool behind = IsBehind(i);
        var ctx = new ResolveContext(Datas[i], f.Facing, prev, cur, _hist[i],
            f.State, f.DidHit, ActiveKindOf(i), f.MoveId, f.AirAttackUsed, f.Airborne, f.Ex, behind);
        int moveId = MoveResolver.Resolve(ctx);
        if (moveId == -1) return; // motion consumed by a gated attempt (e.g. meterless super): no fallback
        if (moveId != 0)
        {
            var started = Datas[i].Get(moveId);
            StartMove(i, started);
            // Motion specials eat jump inputs (standard input priority): a 360 ending in U+HP
            // performs the grab instead of jumping. Flag consumed by Move().
            if (started.MotionGated && cur.DirY == 1) f.SuppressJump = true;
        }
    }

    private MoveKind ActiveKindOf(int i)
    {
        var f = Fighters[i];
        if (f.State == FighterState.Attack && f.MoveId != 0) return Datas[i].Get(f.MoveId).Kind;
        return MoveKind.Normal;
    }

    private static bool IsChordEdge(InputState prev, InputState cur, Buttons chord) =>
        (cur.Buttons & chord) == chord && (prev.Buttons & chord) != chord;

    /// <summary>Double-tap-back from NEUTRAL stick (F1 input rule, documented):
    /// the older back-press must itself be an edge out of neutral (DirX==0 before it).
    /// This disambiguates deliberate taps from block-hold resumes (which originate from
    /// walk-toward or a previous hold). Cost: no instant backdash straight out of forward
    /// walk — release to neutral first. Flagged for feel review, NOT an OPEN (fully specified).</summary>
    private bool HasOlderBackEdge(int i)
    {
        int n = System.Math.Min(_hist[i].Count, Calibration.DoubleTapWindow);
        var f = Fighters[i];
        for (int o = 1; o < n; o++) // o=0 is current frame; need a STRICTLY older press
        {
            var s = _hist[i].GetLatest(o);
            if (s.DirX != -f.Facing) continue;
            if (curFrameDelta(i, o) > Calibration.DoubleTapWindow) break;
            // The older press must come from neutral (edge), not from a hold/walk.
            if (o + 1 >= _hist[i].Count) return true; // history edge = spawn neutral
            if (_hist[i].GetLatest(o + 1).DirX == 0) return true;
        }
        return false;
    }

    private int curFrameDelta(int i, int offset) => Frame - _hist[i].GetLatest(offset).Frame;

    private bool IsBehind(int i)
    {
        var me = Fighters[i].Pos.X;
        var op = Fighters[1 - i].Pos.X;
        int oppFacing = Fighters[1 - i].Facing;
        return (me - op) * oppFacing < 0;
    }

    private void StartMove(int i, MoveData move)
    {
        var f = Fighters[i];
        if (move.ExCost > 0)
        {
            if (f.Ex < move.ExCost) return; // insufficient: no silent fallback (resolver pre-gates)
            EnergySystem.Spend(f, move.ExCost);
        }
        // Danimal attack speed +20% (documented F1 rule).
        int divS = 1, divR = 1;
        if (f.RageActive && f.Affinity == RageAffinity.Danimal) { divS = 6; divR = 6; }
        f.StartupEff = divS == 6 ? CombatMath.DanimalHaste(move.Startup) : System.Math.Max(1, move.Startup);
        f.ActiveEff = move.Active;
        f.RecoveryEff = divR == 6 ? CombatMath.DanimalHaste(move.Recovery) : System.Math.Max(1, move.Recovery);
        f.DashDist = move.Id is GlenData.Sp1Short or GlenData.Sp1Mid or GlenData.Sp1Far
            ? move.Id == GlenData.Sp1Short ? GlenData.Sp1ShortDist
              : move.Id == GlenData.Sp1Mid ? GlenData.Sp1MidDist : GlenData.Sp1FarDist
            : 0;
        f.MoveId = move.Id;
        f.MoveFrame = 0;
        f.DidHit = false;
        f.NoCancel = false;
        f.ArmorUsed = false;
        // Grounded attacks root the fighter; air attacks preserve jump momentum
        // (zeroing it mid-air hangs the fighter AND insta-lands fresh jumps — both wrong).
        if (!f.Airborne) f.Vel = Vector2I.Zero;
        f.CounterStanceT = (move.Flags & MoveFlags.Counter) != 0 ? 30 : 0; // GDD §3.2 SP.3
        f.Enter(FighterState.Attack, f.StartupEff + f.ActiveEff + f.RecoveryEff);
        if (move.Kind == MoveKind.Super)
        {
            f.InvulnT = f.StartupEff; // GDD §3.2: 10f inv
            LastPresEvents.Add(new PresEvent(Frame, PresKind.SuperFlash, f.Pos.X, f.Pos.Y, AttackLevel.Super));
        }
    }

    // ---- 5. Movement ----
    private void Move(int i, InputState cur, InputState prevEdge)
    {
        var f = Fighters[i];
        bool suppressJump = f.SuppressJump; // set by StartActions same frame; single-use
        f.SuppressJump = false;
        if (f.Airborne)
        {
            f.Vel = new Vector2I(f.Vel.X, f.Vel.Y + Calibration.Gravity);
            f.Pos += f.Vel;
            if (f.Pos.Y >= Calibration.GroundY)
            {
                f.Pos = new Vector2I(f.Pos.X, Calibration.GroundY);
                f.Airborne = false;
                f.Vel = Vector2I.Zero;
                f.AirAttackUsed = false;
                if (f.State == FighterState.Hitstun)
                    f.Enter(FighterState.Knockdown, Calibration.KnockdownDuration); // juggle ender
                else if (f.State is FighterState.Jump or FighterState.Attack)
                {
                    // Landing cancels air attacks (F1 rule): clear the move too, no stale MoveId.
                    f.MoveId = 0;
                    f.DidHit = false;
                    f.Enter(FighterState.Neutral);
                }
            }
        }
        else if (f.State == FighterState.Backdash)
        {
            int bspeed = Calibration.BackdashSpeed * (1 + f.AccelStacks); // Kira gimmick
            bspeed = bspeed * f.SpeedZonePerMille / 1000; // Dojo meditation zone
            f.Vel = f.StateFrame < 6
                ? new Vector2I(-f.Facing * bspeed, 0)
                : Vector2I.Zero;
            f.Pos += f.Vel;
        }
        else if (f.State == FighterState.Attack && f.DashDist > 0 && f.MoveFrame < f.StartupEff)
        {
            f.Pos += new Vector2I(f.Facing * (f.DashDist / f.StartupEff), 0); // SP.1 dash
        }
        else if (FighterStateMeta.IsActionable(f.State))
        {
            if (cur.DirX == f.Facing) // toward = walk (away = block stance, no walk-back)
            {
                // Order: base → accel → zone → rage (documented; all integer, all floored).
                int speed = Datas[i].WalkSpeed * (1 + f.AccelStacks); // Kira gimmick: up to ×3
                speed = speed * f.SpeedZonePerMille / 1000; // Dojo meditation zone
                if (f.RageActive && f.Affinity == RageAffinity.Dant) speed = CombatMath.DantSlow(speed);
                if (f.RageActive && f.Affinity == RageAffinity.Danimal) speed = CombatMath.DanimalSwift(speed);
                f.Vel = new Vector2I(f.Facing * speed, 0);
                f.Pos += f.Vel;
                if (f.State == FighterState.Neutral) f.Enter(FighterState.Walk);
            }
            else
            {
                f.Vel = Vector2I.Zero;
                if (f.State == FighterState.Walk) f.Enter(FighterState.Neutral);
                // Jump: up edge while grounded (F1: grounded jumps only), unless a motion
                // special consumed this frame's input (input priority, documented F1 rule).
                // Liftoff integrates IMMEDIATELY (no 1-frame ground delay): an air action
                // started next frame must see Y<0, or StartMove's rooting would insta-land it.
                if (cur.DirY == 1 && prevEdge.DirY != 1 && !suppressJump)
                {
                    f.Vel = new Vector2I(cur.DirX * Calibration.JumpVelX, Calibration.JumpVelY);
                    f.Pos += f.Vel;
                    f.Airborne = true;
                    f.Enter(FighterState.Jump);
                }
            }
        }
        f.Pos = BodyCollision.ClampStage(f.Pos);
        var a = Fighters[0].Pos;
        var b = Fighters[1].Pos;
        BodyCollision.Separate(ref a, ref b);
        Fighters[0].Pos = BodyCollision.ClampStage(a);
        Fighters[1].Pos = BodyCollision.ClampStage(b);
    }

    // ---- 6. Interactions ----
    private void Interact()
    {
        bool a0 = IsActive(0, out MoveData? m0);
        bool a1 = IsActive(1, out MoveData? m1);

        if (a0 && a1)
        {
            int r0 = Rank(m0!), r1 = Rank(m1!);
            if (r0 == r1)
            {
                bool superDuel = m0!.Kind == MoveKind.Super && m1!.Kind == MoveKind.Super;
                CombatResolution.ApplyClash(Fighters[0], Fighters[1],
                    superDuel ? Calibration.SuperVsSuperStun : Calibration.ClashStun,
                    Frame, LastSimEvents, LastPresEvents);
            }
            else if (r0 > r1)
            {
                ResolveAttacker(0, m0!);
            }
            else
            {
                ResolveAttacker(1, m1!);
            }
            return;
        }
        if (a0) ResolveAttacker(0, m0!);
        else if (a1) ResolveAttacker(1, m1!);
    }

    private static int Rank(MoveData m) => m.Kind == MoveKind.Throw ? -1 : (int)m.Level;

    private bool IsActive(int i, out MoveData? move)
    {
        move = null;
        var f = Fighters[i];
        if (f.State != FighterState.Attack || f.DidHit || f.MoveId == 0) return false;
        move = Datas[i].Get(f.MoveId);
        // Counter stances (SP.3) occupy the Attack state but never strike; they are
        // resolved as defenders, never as attackers.
        if ((move.Flags & MoveFlags.Counter) != 0) { move = null; return false; }
        return f.MoveFrame >= f.StartupEff && f.MoveFrame < f.StartupEff + f.ActiveEff;
    }

    private void ResolveAttacker(int attIdx, MoveData move)
    {
        var att = Fighters[attIdx];
        var def = Fighters[1 - attIdx];
        // F1 rule (genre-standard, flagged for review): knocked-down fighters are
        // unhittable until wakeup completes. No OTG in F1.
        if (def.State is FighterState.Knockdown or FighterState.Wakeup)
        {
            LastSimEvents.Add(new SimEvent(Frame, SimEventType.Miss, attIdx, 1 - attIdx));
            return;
        }
        InputState defInput = _cur[1 - attIdx]; // defender's CURRENT input (block held this frame)
        // Throws carry their own range check (ResolveThrow); the strike hitbox does not apply.
        if (move.Kind != MoveKind.Throw
            && !BodyCollision.HitboxOf(att.Pos, att.Facing, move.Level)
                .Overlaps(BodyCollision.BodyOf(def.Pos)))
            return; // whiff this frame; active window may still connect later

        // Auto-resonance intercept (OPEN-003): lethal-to-<15% unblocked hit pre-activates.
        if (!def.ResActive && !def.ResUsedRound && def.ResCd == 0 && move.Kind != MoveKind.Throw)
        {
            int prelim = PreviewDamage(att, def, move);
            if (CombatMath.IsLifeline(def.Hp - prelim, def.MaxHp))
                EnergySystem.TryActivateResonance(def, false, Frame, LastSimEvents, LastPresEvents, 1 - attIdx);
        }

        if (move.Kind == MoveKind.Throw)
            CombatResolution.ResolveThrow(att, def, move, attIdx, _hist[1 - attIdx],
                move.Id == GlenData.Sp2, Frame, LastSimEvents, LastPresEvents);
        else
            CombatResolution.ResolveStrike(att, def, move, attIdx, defInput,
                Frame, LastSimEvents, LastPresEvents);
    }

    private readonly InputState[] _cur = { default, default };

    private static int PreviewDamage(Fighter att, Fighter def, MoveData move)
    {
        int v = DamageScaler.Apply(move.Damage, att.ComboGiven, move.Kind == MoveKind.Super);
        if ((move.Flags & MoveFlags.Physical) != 0)
            v = v * EnergySystem.PhysicalMult(att) / EnergySystem.PhysicalDen(att);
        else if ((move.Flags & MoveFlags.Energy) != 0)
            v = v * EnergySystem.EnergyMultNum(att) / EnergySystem.EnergyMultDen(att);
        bool counter = def.State == FighterState.Attack && def.MoveFrame < def.StartupEff;
        if (counter) v = v * 6 / 5;
        return v; // NOTE: pre-resonance preview (resonance applies after auto-trigger)
    }

    // ---- 7. Timers ----
    private void TickFighter(int i)
    {
        var f = Fighters[i];
        if (f.Hitstop > 0) return; // frozen (gate already handled, safety)
        f.StateFrame++;
        if (f.InvulnT > 0) f.InvulnT--;
        if (f.CounterStanceT > 0) f.CounterStanceT--;
        if (f.ParryGraceT > 0) f.ParryGraceT--;

        switch (f.State)
        {
            case FighterState.Attack:
                f.MoveFrame++;
                if (f.MoveFrame >= f.StartupEff + f.ActiveEff + f.RecoveryEff)
                {
                    f.MoveId = 0;
                    // Grounded → neutral; mid-air expiry → back to Jump (falls, no 2nd air action).
                    f.Enter(f.Airborne ? FighterState.Jump : FighterState.Neutral);
                }
                break;
            case FighterState.Hitstun:
            case FighterState.Blockstun:
            case FighterState.ClashStun:
            case FighterState.ParryStun:
                if (f.StateFrame >= f.StateDuration) ExpireStun(i);
                break;
            case FighterState.Knockdown:
                if (f.StateFrame >= f.StateDuration) f.Enter(FighterState.Wakeup, Calibration.WakeupDuration);
                break;
            case FighterState.Wakeup:
                if (f.StateFrame >= f.StateDuration) f.Enter(FighterState.Neutral);
                break;
            case FighterState.Backdash:
                if (f.StateFrame >= f.StateDuration) f.Enter(FighterState.Neutral);
                break;
            case FighterState.ParryStance:
                if (--f.ParryStanceT <= 0)
                {
                    if (f.ParryGraceArmed && !f.ParryGraceUsed)
                    {
                        f.ParryGraceArmed = false;
                        f.ParryGraceT = Calibration.ParryGraceFirstDant;
                    }
                    f.Enter(FighterState.ParryWhiff, Calibration.ParryWhiffRecovery);
                    f.ParryWhiffT = f.StateDuration;
                }
                break;
            case FighterState.ParryWhiff:
                if (--f.ParryWhiffT <= 0) f.Enter(FighterState.Neutral);
                break;
        }
    }

    private void ExpireStun(int i)
    {
        var f = Fighters[i];
        if (f.State == FighterState.Hitstun && f.PendingKnockdown)
        {
            f.PendingKnockdown = false;
            f.Airborne = false;
            f.Vel = Vector2I.Zero;
            f.Enter(FighterState.Knockdown, Calibration.KnockdownDuration);
        }
        else f.Enter(FighterState.Neutral);
    }

    private void ResetCombosWhereStanding()
    {
        for (int v = 0; v < 2; v++)
        {
            if (Fighters[v].Airborne) continue; // juggle continuity: air never resets the combo
            var vs = Fighters[v].State;
            if (vs is FighterState.Neutral or FighterState.Walk or FighterState.Wakeup)
            {
                Fighters[1 - v].ComboGiven = 0;
                Fighters[v].ComboTaken = 0;
            }
        }
    }

    private void Timeout()
    {
        Over = true;
        Winner = Fighters[0].Hp == Fighters[1].Hp ? 2 : (Fighters[0].Hp > Fighters[1].Hp ? 0 : 1);
        LastSimEvents.Add(new SimEvent(Frame, SimEventType.RoundEnd, Winner, 1 - Winner));
    }

    private void CheckRoundEnd()
    {
        if (Over) return;
        bool ko0 = Fighters[0].State == FighterState.KO;
        bool ko1 = Fighters[1].State == FighterState.KO;
        if (!ko0 && !ko1) return;
        Over = true;
        Winner = ko0 && ko1 ? 2 : (ko1 ? 0 : 1);
        LastSimEvents.Add(new SimEvent(Frame, SimEventType.RoundEnd, Winner, 1 - Winner));
    }

    // ---- Snapshot v5 (§33 full field set + gimmick blob + command-normal intent) ----
    /// <summary>Captures an immutable snapshot (schema 5) with hash.</summary>
    public FightSnapshot Capture() => FightSnapshot.Capture(this);

    /// <summary>Restores a snapshot (schema-checked, length-capped, bounds-checked).</summary>
    public void Restore(FightSnapshot snap)
    {
        if (snap.SchemaVersion != FightSnapshot.Schema)
            throw new SnapshotSchemaException("FightSnapshot schema mismatch.");
        SnapshotReader.CheckLength(snap.Payload);
        var r = new SnapshotReader(snap.Payload);
        if (r.ReadInt() != FightSnapshot.Schema) throw new SnapshotSchemaException("Payload schema mismatch.");
        Frame = r.ReadInt();
        RoundNumber = r.ReadInt();
        RoundTimer = r.ReadInt();
        Over = r.ReadInt() != 0;
        Winner = r.ReadInt();
        int stageId = r.ReadInt();
        if (stageId != Stage.Id)
            throw new SnapshotSchemaException($"Stage mismatch: snapshot stage {stageId}, sim stage {Stage.Id}.");
        _rng.Restore(r.ReadULong());
        for (int i = 0; i < 2; i++) ReadFighter(r, Fighters[i]);
        for (int i = 0; i < 2; i++)
        {
            int n = r.ReadHistoryCount();
            _hist[i].Clear();
            for (int k = 0; k < n; k++) _hist[i].Push(r.ReadInput());
            _prev[i] = r.ReadInput();
            _cur[i] = r.ReadInput();
        }
        if (!r.End) throw new SnapshotSchemaException("FightSnapshot trailing bytes.");
    }

    private static void ReadFighter(SnapshotReader r, Fighter f)
    {
        f.Pos = new Vector2I(r.ReadInt(), r.ReadInt());
        f.Vel = new Vector2I(r.ReadInt(), r.ReadInt());
        f.Facing = r.ReadInt();
        f.State = (FighterState)r.ReadInt();
        f.StateFrame = r.ReadInt(); f.StateDuration = r.ReadInt();
        f.MoveId = r.ReadInt(); f.MoveFrame = r.ReadInt();
        f.StartupEff = r.ReadInt(); f.ActiveEff = r.ReadInt(); f.RecoveryEff = r.ReadInt();
        f.DashDist = r.ReadInt();
        f.DidHit = r.ReadInt() != 0; f.NoCancel = r.ReadInt() != 0;
        f.ArmorUsed = r.ReadInt() != 0; f.PendingKnockdown = r.ReadInt() != 0;
        f.Hp = r.ReadInt(); f.MaxHp = r.ReadInt(); f.Ex = r.ReadInt(); f.Res = r.ReadInt();
        f.Affinity = (RageAffinity)r.ReadInt();
        f.RageActive = r.ReadInt() != 0; f.RageTimer = r.ReadInt(); f.RageCd = r.ReadInt();
        f.ResActive = r.ReadInt() != 0; f.ResTimer = r.ReadInt(); f.ResCd = r.ReadInt();
        f.ResUsedRound = r.ReadInt() != 0;
        f.Hitstop = r.ReadInt();
        f.Airborne = r.ReadInt() != 0; f.JuggleCount = r.ReadInt(); f.JuggleMax = r.ReadInt();
        f.ComboGiven = r.ReadInt(); f.ComboTaken = r.ReadInt(); f.ConsecBlocked = r.ReadInt();
        f.ParryStanceT = r.ReadInt(); f.ParryWhiffT = r.ReadInt(); f.ParryGraceT = r.ReadInt();
        f.ParryGraceArmed = r.ReadInt() != 0;
        f.FirstParryAvailable = r.ReadInt() != 0; f.ParryGraceUsed = r.ReadInt() != 0;
        f.CounterStanceT = r.ReadInt(); f.InvulnT = r.ReadInt(); f.RageStage = r.ReadInt();
        f.SuppressJump = r.ReadInt() != 0;
        f.AccelStacks = r.ReadInt(); f.AirAttackUsed = r.ReadInt() != 0;
        f.Gimmick = (GimmickId)r.ReadInt();
        f.ExZonePerMille = r.ReadInt(); f.SpeedZonePerMille = r.ReadInt();
        f.LastInput = r.ReadInput();
        f.GimmickState = ReadGimmickData(r);
        f.LastCommandNormal = r.ReadInt();
    }

    /// <summary>Reads a <see cref="GimmickData"/> blob: tag int, then each
    /// payload word as little-endian lo/hi int pairs (keeps the fighter block
    /// int-based so <see cref="FightSnapshot.FighterBlockInts"/> stays exact).</summary>
    private static GimmickData ReadGimmickData(SnapshotReader r)
    {
        var g = default(GimmickData);
        g.Tag = (byte)r.ReadInt();
        for (int k = 0; k < GimmickData.PayloadWords; k++)
        {
            uint lo = (uint)r.ReadInt();
            ulong hi = (uint)r.ReadInt();
            g.SetWord(k, lo | (hi << 32));
        }
        return g;
    }

    internal void WriteTo(SnapshotWriter w)
    {
        w.Write(FightSnapshot.Schema);
        w.Write(Frame); w.Write(RoundNumber); w.Write(RoundTimer);
        w.Write(Over ? 1 : 0); w.Write(Winner);
        w.Write(Stage.Id);
        w.Write(_rng.State);
        for (int i = 0; i < 2; i++) WriteFighter(w, Fighters[i]);
        for (int i = 0; i < 2; i++)
        {
            w.Write(_hist[i].Count);
            for (int k = 0; k < _hist[i].Count; k++) w.Write(_hist[i].PeekOldest(k));
            w.Write(_prev[i]); w.Write(_cur[i]);
        }
    }

    private static void WriteFighter(SnapshotWriter w, Fighter f)
    {
        w.Write(f.Pos.X); w.Write(f.Pos.Y);
        w.Write(f.Vel.X); w.Write(f.Vel.Y);
        w.Write(f.Facing);
        w.Write((int)f.State);
        w.Write(f.StateFrame); w.Write(f.StateDuration);
        w.Write(f.MoveId); w.Write(f.MoveFrame);
        w.Write(f.StartupEff); w.Write(f.ActiveEff); w.Write(f.RecoveryEff);
        w.Write(f.DashDist);
        w.Write(f.DidHit ? 1 : 0); w.Write(f.NoCancel ? 1 : 0);
        w.Write(f.ArmorUsed ? 1 : 0); w.Write(f.PendingKnockdown ? 1 : 0);
        w.Write(f.Hp); w.Write(f.MaxHp); w.Write(f.Ex); w.Write(f.Res);
        w.Write((int)f.Affinity);
        w.Write(f.RageActive ? 1 : 0); w.Write(f.RageTimer); w.Write(f.RageCd);
        w.Write(f.ResActive ? 1 : 0); w.Write(f.ResTimer); w.Write(f.ResCd);
        w.Write(f.ResUsedRound ? 1 : 0);
        w.Write(f.Hitstop);
        w.Write(f.Airborne ? 1 : 0); w.Write(f.JuggleCount); w.Write(f.JuggleMax);
        w.Write(f.ComboGiven); w.Write(f.ComboTaken); w.Write(f.ConsecBlocked);
        w.Write(f.ParryStanceT); w.Write(f.ParryWhiffT); w.Write(f.ParryGraceT);
        w.Write(f.ParryGraceArmed ? 1 : 0);
        w.Write(f.FirstParryAvailable ? 1 : 0); w.Write(f.ParryGraceUsed ? 1 : 0);
        w.Write(f.CounterStanceT); w.Write(f.InvulnT); w.Write(f.RageStage);
        w.Write(f.SuppressJump ? 1 : 0);
        w.Write(f.AccelStacks); w.Write(f.AirAttackUsed ? 1 : 0);
        w.Write((int)f.Gimmick);
        w.Write(f.ExZonePerMille); w.Write(f.SpeedZonePerMille);
        w.Write(f.LastInput);
        WriteGimmickData(w, f.GimmickState);
        w.Write(f.LastCommandNormal);
    }

    /// <summary>Writes a <see cref="GimmickData"/> blob: tag int, then each
    /// payload word as little-endian lo/hi int pairs (symmetric with
    /// <see cref="ReadGimmickData"/>).</summary>
    private static void WriteGimmickData(SnapshotWriter w, GimmickData g)
    {
        w.Write((int)g.Tag);
        for (int k = 0; k < GimmickData.PayloadWords; k++)
        {
            ulong v = g.GetWord(k);
            w.Write((int)(v & 0xFFFFFFFFUL));
            w.Write((int)(v >> 32));
        }
    }
}

/// <summary>Immutable fight snapshot, schema 5 (F5: +GimmickState blob and
/// +LastCommandNormal per fighter, appended as int tail — 88 ints/fighter).
/// Older payloads are rejected loudly (pre-release).</summary>
public sealed class FightSnapshot
{
    /// <summary>Wire schema version (mismatch rejected loudly).</summary>
    public const int Schema = 5;
    /// <summary>Fighter block size in ints (WriteFighter/ReadFighter must stay symmetric with this).
    /// White-box tests use it to locate fields; a mismatch fails loudly there, never silently.</summary>
    internal const int FighterBlockInts = 88;
    /// <summary>Schema this payload was captured with.</summary>
    public int SchemaVersion { get; }
    /// <summary>Raw little-endian payload.</summary>
    public byte[] Payload { get; }
    /// <summary>FNV-1a hash of the payload (divergence detection).</summary>
    public ulong Hash { get; }

    private FightSnapshot(byte[] payload)
    {
        SchemaVersion = Schema;
        Payload = payload;
        Hash = StateHasher.Hash(payload);
    }

    /// <summary>Internal for white-box hostile-payload tests (bounds/cap validation).
    /// Production path is <see cref="Capture(FightSimulation)"/> only.</summary>
    internal FightSnapshot(byte[] payload, bool bypassValidation) : this(payload) { }

    /// <summary>Captures a snapshot of <paramref name="sim"/>.</summary>
    public static FightSnapshot Capture(FightSimulation sim)
    {
        var w = new SnapshotWriter();
        sim.WriteTo(w);
        return new FightSnapshot(w.ToArray());
    }
}
