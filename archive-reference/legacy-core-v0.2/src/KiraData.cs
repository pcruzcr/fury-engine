// Fighter.Core — Kira data (F2a). OPEN-008: every number below is a [KIRA-CAL]
// placeholder (Glen light/medium skeleton) EXCEPT: MaxHp 900, Dant affinity, walk 2800,
// Acceleration gimmick, Energy-flagged moves (so Dant rage matters). The MECHANIC is specified
// and tested; the NUMBERS await the Combat Designer. Nothing here may feed a balance claim.
namespace Fighter.Core;

/// <summary>Kira data: SPECIFIED fields (HP/affinity/gimmick) + [KIRA-CAL] placeholders (OPEN-008).</summary>
public static class KiraData
{
    /// <summary>LP [CAL].</summary>
    public const int LP = 101;
    /// <summary>MP [CAL].</summary>
    public const int MP = 102;
    /// <summary>HP [CAL].</summary>
    public const int HP = 103;
    /// <summary>LK [CAL].</summary>
    public const int LK = 104;
    /// <summary>MK [CAL].</summary>
    public const int MK = 105;
    /// <summary>HK [CAL].</summary>
    public const int HK = 106;
    /// <summary>F+HP [CAL].</summary>
    public const int F_HP = 111;
    /// <summary>B+HP [CAL].</summary>
    public const int B_HP = 112;
    /// <summary>F+MP [CAL].</summary>
    public const int F_MP = 113;
    /// <summary>B+MP [CAL].</summary>
    public const int B_MP = 114;
    /// <summary>j.LP [CAL].</summary>
    public const int JLp = 141;
    /// <summary>j.MP [CAL].</summary>
    public const int JMp = 142;
    /// <summary>j.HP [CAL].</summary>
    public const int JHp = 143;
    /// <summary>Front throw [CAL].</summary>
    public const int ThrowFwd = 121;
    /// <summary>Back throw [CAL].</summary>
    public const int ThrowBack = 122;
    /// <summary>Ala Cortante special (QCF+P) [CAL].</summary>
    public const int Sp1 = 131;
    /// <summary>Tormenta super (QCFx2+P) [CAL].</summary>
    public const int Super = 136;

    /// <summary>Builds + validates Kira's provisional data (throws loudly on inconsistency).</summary>
    public static FighterData Build()
    {
        var d = new FighterData
        {
            Name = "Kira [KIRA-CAL provisional]",
            MaxHp = 900, // GDD §3.3 (specified)
            Affinity = RageAffinity.Dant, // GDD §3.3 (specified)
            Gimmick = GimmickId.KiraAcceleration, // GDD §3.3 (specified)
            WalkSpeed = 2800, // [KIRA-CAL]
            QcfShort = Sp1, QcfMid = Sp1, QcfFar = Sp1, // single special for all punches
            DoubleQcfMove = Super, // DpKick/Spin unsupported → fall through to buttons
        };
        MoveData N(int id, string n, Buttons b, DirReq q, int st, int ob, int dmg,
            AttackLevel lv, MoveFlags f = MoveFlags.None) => new()
            {
                Id = id, Name = n, Kind = MoveKind.Normal, Level = lv, Buttons = b, Dir = q,
                Startup = st, Active = 2, Recovery = 10, // [KIRA-CAL]
                OnBlock = ob, Damage = dmg, Flags = f | MoveFlags.Energy, ExCost = 0,
            };
        MoveData C(int id, string n, Buttons b, DirReq q, int st, int ob, int dmg, AttackLevel lv)
        {
            var m = N(id, n, b, q, st, ob, dmg, lv);
            m.Kind = MoveKind.CommandNormal;
            return m;
        }

        d.Moves.Add(N(LP, "Jab [CAL]", Buttons.LP, DirReq.Neutral, 4, 2, 26, AttackLevel.Light));
        d.Moves.Add(N(MP, "Cross [CAL]", Buttons.MP, DirReq.Neutral, 7, -1, 52, AttackLevel.Medium));
        d.Moves.Add(N(HP, "Overhand [CAL]", Buttons.HP, DirReq.Neutral, 12, -6, 86, AttackLevel.Heavy));
        d.Moves.Add(N(LK, "Low tap [CAL]", Buttons.LK, DirReq.Neutral, 5, 1, 28, AttackLevel.Light, MoveFlags.Low));
        d.Moves.Add(N(MK, "Step kick [CAL]", Buttons.MK, DirReq.Neutral, 8, -3, 56, AttackLevel.Medium));
        d.Moves.Add(N(HK, "High kick [CAL]", Buttons.HK, DirReq.Neutral, 13, -8, 90, AttackLevel.Heavy));
        d.Moves.Add(C(F_HP, "Dart [CAL]", Buttons.HP, DirReq.Forward, 10, -4, 76, AttackLevel.Heavy));
        d.Moves.Add(C(B_HP, "Hook [CAL]", Buttons.HP, DirReq.Back, 9, -2, 68, AttackLevel.Heavy));
        d.Moves.Add(C(F_MP, "Needle [CAL]", Buttons.MP, DirReq.Forward, 8, -2, 56, AttackLevel.Medium));
        d.Moves.Add(C(B_MP, "Shovel [CAL]", Buttons.MP, DirReq.Back, 11, -5, 74, AttackLevel.Heavy));
        d.Moves.Add(new MoveData
        {
            Id = JLp, Name = "j.LP [CAL]", Kind = MoveKind.Normal, Level = AttackLevel.Light,
            Buttons = Buttons.LP, Dir = DirReq.Neutral, Startup = 4, Active = 4, Recovery = 10,
            OnBlock = -2, Damage = 26, Flags = MoveFlags.Energy, AirOK = true,
        });
        d.Moves.Add(new MoveData
        {
            Id = JMp, Name = "j.MP [CAL]", Kind = MoveKind.Normal, Level = AttackLevel.Medium,
            Buttons = Buttons.MP, Dir = DirReq.Neutral, Startup = 6, Active = 4, Recovery = 10,
            OnBlock = -2, Damage = 52, Flags = MoveFlags.Energy, AirOK = true,
        });
        d.Moves.Add(new MoveData
        {
            Id = JHp, Name = "j.HP [CAL]", Kind = MoveKind.Normal, Level = AttackLevel.Heavy,
            Buttons = Buttons.HP, Dir = DirReq.Neutral, Startup = 9, Active = 4, Recovery = 10,
            OnBlock = -2, Damage = 86, Flags = MoveFlags.Energy, AirOK = true,
        });
        d.Moves.Add(new MoveData
        {
            Id = ThrowFwd, Name = "Volteo [CAL]", Kind = MoveKind.Throw, Level = AttackLevel.None,
            Buttons = Buttons.HP | Buttons.HK, Dir = DirReq.Neutral,
            Startup = 6, Active = 2, Recovery = 14, OnBlock = 0, Damage = 80,
        });
        d.Moves.Add(new MoveData
        {
            Id = ThrowBack, Name = "Estrangula [CAL]", Kind = MoveKind.Throw, Level = AttackLevel.None,
            Buttons = Buttons.HP | Buttons.HK, Dir = DirReq.Back,
            Startup = 5, Active = 2, Recovery = 14, OnBlock = 0, Damage = 95,
        });
        d.Moves.Add(new MoveData
        {
            Id = Sp1, Name = "Ala Cortante [CAL]", Kind = MoveKind.Special, Level = AttackLevel.Special,
            Buttons = Buttons.None, Dir = DirReq.Neutral, // QCF+P motion-gated
            Startup = 9, Active = Calibration.ActiveSpecial, Recovery = 15,
            OnBlock = -3, Damage = 75, Flags = MoveFlags.Energy, MotionGated = true,
        });
        d.Moves.Add(new MoveData
        {
            Id = Super, Name = "Tormenta [CAL]", Kind = MoveKind.Super, Level = AttackLevel.Super,
            Buttons = Buttons.None, Dir = DirReq.Neutral, // QCFx2+P motion-gated
            Startup = 9, Active = Calibration.ActiveSuper, Recovery = 28,
            OnBlock = 0, Damage = 220, Flags = MoveFlags.Energy, ExCost = 2000, MotionGated = true,
        });

        d.Get(LP).ChainTo = new[] { LP, MP }; // [KIRA-CAL] mirrors Glen's confirmed chains
        d.Get(JLp).ChainTo = new[] { JMp };
        d.Get(JMp).ChainTo = new[] { JHp };

        DataValidator.ValidateFighter(d);
        return d;
    }
}
