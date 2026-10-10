// Fighter.Core — Glen Dantao data (GDD v8.0 §3.2). DESIGN TRUTH transcribed, not invented.
// Startup / OnBlock / Damage = GDD values. Active / Recovery = [CAL] by level (see Calibration).
// DF-CON-002: B+HP and B+MP are Heavy → hitstop 10 (system table wins over the 8f column value).
namespace Fighter.Core;

/// <summary>Rage affinity: selects the Rage profile and damage boosts (GDD §5.2).</summary>
public enum RageAffinity : byte
{
    /// <summary>Chaos/Void: physical +25%, speedster, no block, armor, 5s/8s.</summary>
    Danimal,
    /// <summary>Order/Light: energy +25%, wide parry, 6s/10s.</summary>
    Dant,
    /// <summary>Synthesis: +10% all, mid-combo mode switch, 7s/15s.</summary>
    Gray,
}

/// <summary>Character definition: stats + move list + motion map (data, never logic).</summary>
public sealed class FighterData
{
    /// <summary>Display name.</summary>
    public string Name = "";
    /// <summary>Max HP (GDD value).</summary>
    public int MaxHp;
    /// <summary>Rage affinity.</summary>
    public RageAffinity Affinity;
    /// <summary>Character gimmick (closed set).</summary>
    public GimmickId Gimmick;
    /// <summary>Walk speed, millipixels/frame.</summary>
    public int WalkSpeed = Calibration.WalkSpeed;
    /// <summary>Juggle cap (GDD: 2–3 per character).</summary>
    public int JuggleMax = 3;
    /// <summary>Motion→move map. 0 = unsupported (motion falls through to button resolution,
    /// documented: no silent mis-mapping).</summary>
    public int QcfShort, QcfMid, QcfFar, DpKickMove, SpinMove, DoubleQcfMove;
    /// <summary>Validated move list (lookup by id).</summary>
    public System.Collections.Generic.List<MoveData> Moves = new();

    /// <summary>Looks up a move by id (throws on unknown id — never return null).</summary>
    public MoveData Get(int id)
    {
        foreach (var m in Moves)
            if (m.Id == id) return m;
        throw new InvalidOperationException($"Unknown move id {id} for {Name}.");
    }
}

/// <summary>Glen Dantao data (GDD v8.0 §3.2 DESIGN TRUTH; gaps marked [CAL]).</summary>
public static class GlenData
{
    /// <summary>LP Jab ombligo.</summary>
    public const int LP = 1;
    /// <summary>MP Jab estomago.</summary>
    public const int MP = 2;
    /// <summary>HP Jab alto.</summary>
    public const int HP = 3;
    /// <summary>LK Kick muslo (low).</summary>
    public const int LK = 4;
    /// <summary>MK Sidekick.</summary>
    public const int MK = 5;
    /// <summary>HK Patada cabeza (launcher).</summary>
    public const int HK = 6;
    /// <summary>F+HP Recto cabeza (overhead).</summary>
    public const int F_HP = 11;
    /// <summary>B+HP Gancho cabeza.</summary>
    public const int B_HP = 12;
    /// <summary>F+MP Recto estomago.</summary>
    public const int F_MP = 13;
    /// <summary>B+MP Gancho higado.</summary>
    public const int B_MP = 14;
    /// <summary>B+LP Uppercut (anti-air).</summary>
    public const int B_LP = 15;
    /// <summary>F+HK Hook kick (launcher).</summary>
    public const int F_HK = 16;
    /// <summary>B+HK Crescent kick.</summary>
    public const int B_HK = 17;
    /// <summary>F+LK Hook muslo (low).</summary>
    public const int F_LK = 18;
    /// <summary>B+LK Kick rapido.</summary>
    public const int B_LK = 19;
    /// <summary>D+B+LK Barrida (low, knockdown).</summary>
    public const int Sweep = 20;
    /// <summary>HP+HK Volteo (front throw).</summary>
    public const int ThrowFwd = 21;
    /// <summary>HP+HK Estrangula (back throw).</summary>
    public const int ThrowBack = 22;
    /// <summary>Asalto del Lobo, corto (QCF+LP).</summary>
    public const int Sp1Short = 31;
    /// <summary>Asalto del Lobo, medio (QCF+MP).</summary>
    public const int Sp1Mid = 32;
    /// <summary>Asalto del Lobo, largo (QCF+HP).</summary>
    public const int Sp1Far = 33;
    /// <summary>Agarre del Vacio (360+P command grab).</summary>
    public const int Sp2 = 34;
    /// <summary>Aura Lobo (DP+K counter stance).</summary>
    public const int Sp3 = 35;
    /// <summary>Lobo Blanco (QCFx2+P super).</summary>
    public const int Super = 36;
    /// <summary>j.LP air normal.</summary>
    public const int JLp = 41;
    /// <summary>j.MP air normal.</summary>
    public const int JMp = 42;
    /// <summary>j.HP air normal.</summary>
    public const int JHp = 43;

    // Dash distances for Asalto del Lobo variants (LP corto / MP medio / HP largo) [CAL].
    /// <summary>SP1 short dash distance, millipixels [CAL].</summary>
    public const int Sp1ShortDist = 60000;
    /// <summary>SP1 medium dash distance, millipixels [CAL].</summary>
    public const int Sp1MidDist = 100000;
    /// <summary>SP1 far dash distance, millipixels [CAL].</summary>
    public const int Sp1FarDist = 140000;

    /// <summary>Builds + validates Glen's data (throws loudly on inconsistency).</summary>
    public static FighterData Build()
    {
        var d = new FighterData
        {
            Name = "Glen Dantao", MaxHp = 950, Affinity = RageAffinity.Danimal,
            QcfShort = Sp1Short, QcfMid = Sp1Mid, QcfFar = Sp1Far,
            DpKickMove = Sp3, SpinMove = Sp2, DoubleQcfMove = Super,
        };
        MoveData N(int id, string n, Buttons b, DirReq q, int st, int ob, int dmg,
            AttackLevel lv, MoveFlags f = MoveFlags.None) => new()
            {
                Id = id, Name = n, Kind = MoveKind.Normal, Level = lv, Buttons = b, Dir = q,
                Startup = st, Active = ActiveFor(lv), Recovery = RecoveryFor(lv),
                OnBlock = ob, Damage = dmg, Flags = f | MoveFlags.Physical, ExCost = 0,
            };
        MoveData C(int id, string n, Buttons b, DirReq q, int st, int ob, int dmg,
            AttackLevel lv, MoveFlags f = MoveFlags.None)
        {
            var m = N(id, n, b, q, st, ob, dmg, lv, f);
            m.Kind = MoveKind.CommandNormal;
            return m;
        }

        d.Moves.Add(N(LP, "Jab ombligo", Buttons.LP, DirReq.Neutral, 4, 2, 28, AttackLevel.Light));
        d.Moves.Add(N(MP, "Jab estomago", Buttons.MP, DirReq.Neutral, 7, -1, 55, AttackLevel.Medium));
        d.Moves.Add(N(HP, "Jab alto", Buttons.HP, DirReq.Neutral, 12, -6, 90, AttackLevel.Heavy));
        d.Moves.Add(N(LK, "Kick muslo", Buttons.LK, DirReq.Neutral, 5, 1, 30, AttackLevel.Light, MoveFlags.Low));
        d.Moves.Add(N(MK, "Sidekick", Buttons.MK, DirReq.Neutral, 8, -3, 60, AttackLevel.Medium));
        d.Moves.Add(N(HK, "Patada cabeza", Buttons.HK, DirReq.Neutral, 13, -8, 95, AttackLevel.Heavy, MoveFlags.Launcher));
        d.Get(HK).Recovery = 12; // [CAL] route link: chained launcher must recover in time to jump

        d.Moves.Add(C(F_HP, "Recto cabeza", Buttons.HP, DirReq.Forward, 10, -4, 80, AttackLevel.Heavy, MoveFlags.Overhead));
        d.Moves.Add(C(B_HP, "Gancho cabeza", Buttons.HP, DirReq.Back, 9, -2, 72, AttackLevel.Heavy));
        d.Moves.Add(C(F_MP, "Recto estomago", Buttons.MP, DirReq.Forward, 8, -2, 60, AttackLevel.Medium));
        d.Moves.Add(C(B_MP, "Gancho higado", Buttons.MP, DirReq.Back, 11, -5, 78, AttackLevel.Heavy));
        d.Moves.Add(C(B_LP, "Uppercut", Buttons.LP, DirReq.Back, 6, -1, 45, AttackLevel.Medium));
        d.Moves.Add(C(F_HK, "Hook kick", Buttons.HK, DirReq.Forward, 12, -7, 88, AttackLevel.Heavy, MoveFlags.Launcher));
        d.Moves.Add(C(B_HK, "Crescent kick", Buttons.HK, DirReq.Back, 14, -9, 100, AttackLevel.Heavy));
        d.Moves.Add(C(F_LK, "Hook muslo", Buttons.LK, DirReq.Forward, 6, 0, 32, AttackLevel.Light, MoveFlags.Low));
        d.Moves.Add(C(B_LK, "Kick rapido", Buttons.LK, DirReq.Back, 4, 1, 25, AttackLevel.Light));
        d.Moves.Add(C(Sweep, "Barrida", Buttons.LK, DirReq.DownBack, 10, -4, 35, AttackLevel.Light,
            MoveFlags.Low | MoveFlags.Knockdown));

        d.Moves.Add(new MoveData
        {
            Id = ThrowFwd, Name = "Volteo", Kind = MoveKind.Throw, Level = AttackLevel.None,
            Buttons = Buttons.HP | Buttons.HK, Dir = DirReq.Neutral,
            Startup = 6, Active = 2, Recovery = 14, OnBlock = 0, Damage = 85,
        });
        d.Moves.Add(new MoveData
        {
            Id = ThrowBack, Name = "Estrangula", Kind = MoveKind.Throw, Level = AttackLevel.None,
            Buttons = Buttons.HP | Buttons.HK, Dir = DirReq.Back,
            Startup = 5, Active = 2, Recovery = 14, OnBlock = 0, Damage = 100,
        });

        MoveData Sp(int id, string n, int st, int ob, int dmg, MoveFlags f = MoveFlags.None) => new()
        {
            Id = id, Name = n, Kind = MoveKind.Special, Level = AttackLevel.Special,
            Buttons = Buttons.None, Dir = DirReq.Neutral, // motion-gated (see MotionMatcher)
            Startup = st, Active = Calibration.ActiveSpecial, Recovery = Calibration.RecoverySpecial,
            OnBlock = ob, Damage = dmg, Flags = f | MoveFlags.Physical, ExCost = 0, MotionGated = true,
        };
        d.Moves.Add(Sp(Sp1Short, "Asalto del Lobo (corto)", 10, -4, 85));
        d.Moves.Add(Sp(Sp1Mid, "Asalto del Lobo (medio)", 10, -4, 85));
        d.Moves.Add(Sp(Sp1Far, "Asalto del Lobo (largo)", 10, -4, 85));
        d.Moves.Add(new MoveData
        {
            Id = Sp2, Name = "Agarre del Vacio", Kind = MoveKind.Throw, Level = AttackLevel.None,
            Buttons = Buttons.None, Dir = DirReq.Neutral, // 360+P motion-gated, untechable
            Startup = 6, Active = 2, Recovery = 20, OnBlock = 0, Damage = 130, MotionGated = true,
        });
        d.Moves.Add(Sp(Sp3, "Aura Lobo", 8, 4, 0, MoveFlags.Counter));
        d.Moves.Find(m => m.Id == Sp3)!.Recovery = 22; // stance 30f must fit: 8+4+22 = 34 [CAL]
        d.Moves.Add(new MoveData
        {
            Id = Super, Name = "Lobo Blanco", Kind = MoveKind.Super, Level = AttackLevel.Super,
            Buttons = Buttons.None, Dir = DirReq.Neutral, // QCFx2+P motion-gated
            Startup = 10, Active = Calibration.ActiveSuper, Recovery = Calibration.RecoverySuper,
            OnBlock = 0, Damage = 230, Flags = MoveFlags.Physical, ExCost = 2000, MotionGated = true,
        });

        // Air normals: genre-standard airs run faster than grounds [CAL]
        // (required for juggle links, DF-CON-003). GDD names j.MP/j.HP but gives no numbers.
        d.Moves.Add(Air(JLp, "j.LP", Buttons.LP, 4, 28, AttackLevel.Light));
        d.Moves.Add(Air(JMp, "j.MP", Buttons.MP, 6, 55, AttackLevel.Medium));
        d.Moves.Add(Air(JHp, "j.HP", Buttons.HP, 9, 90, AttackLevel.Heavy));

        // Target chains transcribed from GDD §3.2 combo routes (BnB LP>MP, corner F+HP>B+HP...).
        d.Get(LP).ChainTo = new[] { LP, MP };
        d.Get(MP).ChainTo = new[] { HK }; // launcher route MP>HK
        d.Get(F_HP).ChainTo = new[] { B_HP };
        d.Get(JLp).ChainTo = new[] { JMp };
        d.Get(JMp).ChainTo = new[] { JHp };

        DataValidator.ValidateFighter(d);
        return d;
    }

    private static MoveData Air(int id, string n, Buttons b, int st, int dmg, AttackLevel lv) => new()
    {
        Id = id, Name = n, Kind = MoveKind.Normal, Level = lv, Buttons = b, Dir = DirReq.Neutral,
        Startup = st, Active = 4, Recovery = 10, OnBlock = -2, Damage = dmg, // [CAL] active/recovery
        Flags = MoveFlags.Physical, ExCost = 0, AirOK = true,
    };

    private static int ActiveFor(AttackLevel lv) => lv switch
    {
        AttackLevel.Light => Calibration.ActiveLight,
        AttackLevel.Medium => Calibration.ActiveMedium,
        AttackLevel.Heavy => Calibration.ActiveHeavy,
        _ => Calibration.ActiveMedium,
    };

    private static int RecoveryFor(AttackLevel lv) => lv switch
    {
        AttackLevel.Light => Calibration.RecoveryLight,
        AttackLevel.Medium => Calibration.RecoveryMedium,
        AttackLevel.Heavy => Calibration.RecoveryHeavy,
        _ => Calibration.RecoveryMedium,
    };
}
