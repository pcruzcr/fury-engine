// Fighter.Core — snapshot field-level diff (REFACTOR+HARDEN, A4).
// Decodes both payloads field-by-field in WriteTo order and reports EVERY
// differing field as "path: expected=X actual=Y" (never just "different").
// Nested structures carry their path (e.g. P0.Hp, P1.Hist[2].Buttons).
// Diagnostics-only: allocates freely, never touches the hot path.
namespace Fighter.Core;

/// <summary>Field-level snapshot diff for diagnostics (mismatch triage).</summary>
public static class SnapshotDiff
{
    private static readonly string[] FighterIntFields =
    {
        "PosX", "PosY", "VelX", "VelY", "Facing", "State", "StateFrame", "StateDuration",
        "MoveId", "MoveFrame", "StartupEff", "ActiveEff", "RecoveryEff", "DashDist",
        "DidHit", "NoCancel", "ArmorUsed", "PendingKnockdown", "Hp", "MaxHp", "Ex", "Res",
        "Affinity", "RageActive", "RageTimer", "RageCd", "ResActive", "ResTimer", "ResCd",
        "ResUsedRound", "Hitstop", "Airborne", "JuggleCount", "JuggleMax", "ComboGiven",
        "ComboTaken", "ConsecBlocked", "ParryStanceT", "ParryWhiffT", "ParryGraceT",
        "ParryGraceArmed", "FirstParryAvailable", "ParryGraceUsed", "CounterStanceT",
        "InvulnT", "RageStage", "SuppressJump", "AccelStacks", "AirAttackUsed", "Gimmick",
        "ExZonePerMille", "SpeedZonePerMille",
    };

    private static readonly string[] InputFields = { "Buttons", "DirX", "DirY", "Frame" };

    // Schema 5 tail per fighter: GimmickState tag int + 15 payload words as
    // lo/hi int pairs + LastCommandNormal int (mirrors WriteGimmickData).

    private static void DecodeFight(byte[] payload, System.Collections.Generic.List<(string Path, string Value)> out_)
    {
        var r = new SnapshotReader(payload);
        int schema = r.ReadInt();
        out_.Add(("Schema", schema.ToString()));
        if (schema != FightSnapshot.Schema)
            throw new SnapshotSchemaException($"Cannot diff schema {schema} snapshot with schema {FightSnapshot.Schema} decoder.");
        out_.Add(("Frame", r.ReadInt().ToString()));
        out_.Add(("RoundNumber", r.ReadInt().ToString()));
        out_.Add(("RoundTimer", r.ReadInt().ToString()));
        out_.Add(("Over", r.ReadInt().ToString()));
        out_.Add(("Winner", r.ReadInt().ToString()));
        out_.Add(("StageId", r.ReadInt().ToString()));
        out_.Add(("RngState", $"0x{r.ReadULong():X16}"));
        for (int i = 0; i < 2; i++)
        {
            foreach (var name in FighterIntFields)
                out_.Add(($"P{i}.{name}", r.ReadInt().ToString()));
            foreach (var name in InputFields)
                out_.Add(($"P{i}.LastInput.{name}", r.ReadInt().ToString()));
            out_.Add(($"P{i}.Gimmick.Tag", r.ReadInt().ToString()));
            for (int k = 0; k < GimmickData.PayloadWords; k++)
            {
                uint lo = (uint)r.ReadInt();
                ulong hi = (uint)r.ReadInt();
                out_.Add(($"P{i}.Gimmick.W{k}", $"0x{(lo | (hi << 32)):X16}"));
            }
            out_.Add(($"P{i}.LastCommandNormal", r.ReadInt().ToString()));
        }
        for (int i = 0; i < 2; i++)
        {
            int n = r.ReadInt();
            out_.Add(($"P{i}.HistCount", n.ToString()));
            for (int k = 0; k < n; k++)
                foreach (var name in InputFields)
                    out_.Add(($"P{i}.Hist[{k}].{name}", r.ReadInt().ToString()));
            foreach (var name in InputFields)
                out_.Add(($"P{i}.Prev.{name}", r.ReadInt().ToString()));
            foreach (var name in InputFields)
                out_.Add(($"P{i}.Cur.{name}", r.ReadInt().ToString()));
        }
        if (!r.End) throw new SnapshotSchemaException("FightSnapshot trailing bytes.");
    }

    private static void DecodeSubstrate(byte[] payload, System.Collections.Generic.List<(string Path, string Value)> out_)
    {
        var r = new SnapshotReader(payload);
        out_.Add(("Schema", r.ReadInt().ToString()));
        out_.Add(("Frame", r.ReadInt().ToString()));
        out_.Add(("P1PosX", r.ReadInt().ToString()));
        out_.Add(("P1PosY", r.ReadInt().ToString()));
        out_.Add(("P1VelX", r.ReadInt().ToString()));
        out_.Add(("P1VelY", r.ReadInt().ToString()));
        out_.Add(("P2PosX", r.ReadInt().ToString()));
        out_.Add(("P2PosY", r.ReadInt().ToString()));
        out_.Add(("P2VelX", r.ReadInt().ToString()));
        out_.Add(("P2VelY", r.ReadInt().ToString()));
        out_.Add(("P1Timer", r.ReadInt().ToString()));
        out_.Add(("P2Timer", r.ReadInt().ToString()));
        out_.Add(("RoundTimer", r.ReadInt().ToString()));
        out_.Add(("RngState", $"0x{r.ReadULong():X16}"));
        for (int h = 0; h < 2; h++)
        {
            int n = r.ReadInt();
            out_.Add(($"P{h}.HistCount", n.ToString()));
            for (int k = 0; k < n; k++)
                foreach (var name in InputFields)
                    out_.Add(($"P{h}.Hist[{k}].{name}", r.ReadInt().ToString()));
        }
        if (!r.End) throw new SnapshotSchemaException("Snapshot payload trailing bytes.");
    }

    private static string DiffFields(
        System.Collections.Generic.List<(string Path, string Value)> a,
        System.Collections.Generic.List<(string Path, string Value)> b)
    {
        var sb = new System.Text.StringBuilder();
        int n = System.Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++)
        {
            if (a[i].Path != b[i].Path)
            {
                sb.AppendLine($"{a[i].Path} vs {b[i].Path}: layout divergence (layouts differ, cannot compare field-wise)");
                return sb.ToString();
            }
            if (a[i].Value != b[i].Value)
                sb.AppendLine($"{a[i].Path}: expected={a[i].Value} actual={b[i].Value}");
        }
        if (a.Count != b.Count)
            sb.AppendLine($"FieldCount: expected={a.Count} actual={b.Count}");
        return sb.ToString();
    }

    /// <summary>Diffs two fight snapshots field-by-field.</summary>
    /// <param name="a">Expected snapshot.</param>
    /// <param name="b">Actual snapshot.</param>
    /// <returns>Empty string when identical; otherwise one line per differing field.</returns>
    /// <exception cref="SnapshotSchemaException">Thrown when a payload is truncated or has trailing bytes.</exception>
    public static string Diff(FightSnapshot a, FightSnapshot b)
    {
        var fa = new System.Collections.Generic.List<(string Path, string Value)>();
        var fb = new System.Collections.Generic.List<(string Path, string Value)>();
        try
        {
            DecodeFight(a.Payload, fa);
            DecodeFight(b.Payload, fb);
        }
        catch (InvalidOperationException ex) when (ex is not SnapshotSchemaException)
        {
            throw new SnapshotSchemaException(ex.Message, ex);
        }
        return DiffFields(fa, fb);
    }

    /// <summary>Diffs two substrate snapshots field-by-field.</summary>
    /// <param name="a">Expected snapshot.</param>
    /// <param name="b">Actual snapshot.</param>
    /// <returns>Empty string when identical; otherwise one line per differing field.</returns>
    /// <exception cref="SnapshotSchemaException">Thrown when a payload is truncated or has trailing bytes.</exception>
    public static string Diff(StateSnapshot a, StateSnapshot b)
    {
        var fa = new System.Collections.Generic.List<(string Path, string Value)>();
        var fb = new System.Collections.Generic.List<(string Path, string Value)>();
        try
        {
            DecodeSubstrate(a.Payload, fa);
            DecodeSubstrate(b.Payload, fb);
        }
        catch (InvalidOperationException ex) when (ex is not SnapshotSchemaException)
        {
            throw new SnapshotSchemaException(ex.Message, ex);
        }
        return DiffFields(fa, fb);
    }
}
