// Fighter.Core — frame-data JSON exporter (F4 tools).
// Single source of truth flows DATA → runtime AND tools/HUD: this exporter serializes the
// exact FighterData structures the simulation runs on (§49: no manual duplicates).
// Uses System.Text.Json (BCL only, no packages, no Godot dependency).
namespace Fighter.Core;

/// <summary>Frame-data JSON exporter: serializes the exact runtime structures.</summary>
public static class FrameDataExport
{
    /// <summary>Byte-stable JSON of a fighter (fixed key order): moves, costs, chains, flags.</summary>
    public static string ToJson(FighterData d)
    {
        var sb = new System.Text.StringBuilder(8 * 1024);
        sb.Append("{\"name\":").Append(JsonStr(d.Name));
        sb.Append(",\"maxHp\":").Append(d.MaxHp);
        sb.Append(",\"affinity\":").Append(JsonStr(d.Affinity.ToString()));
        sb.Append(",\"gimmick\":").Append(JsonStr(d.Gimmick.ToString()));
        sb.Append(",\"walkSpeed\":").Append(d.WalkSpeed);
        sb.Append(",\"juggleMax\":").Append(d.JuggleMax);
        sb.Append(",\"qcf\":[").Append(d.QcfShort).Append(',').Append(d.QcfMid).Append(',').Append(d.QcfFar).Append(']');
        sb.Append(",\"dpKick\":").Append(d.DpKickMove);
        sb.Append(",\"spin\":").Append(d.SpinMove);
        sb.Append(",\"doubleQcf\":").Append(d.DoubleQcfMove);
        sb.Append(",\"moves\":[");
        bool first = true;
        foreach (var m in d.Moves)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"id\":").Append(m.Id);
            sb.Append(",\"name\":").Append(JsonStr(m.Name));
            sb.Append(",\"kind\":").Append(JsonStr(m.Kind.ToString()));
            sb.Append(",\"level\":").Append(JsonStr(m.Level.ToString()));
            sb.Append(",\"buttons\":").Append((int)m.Buttons);
            sb.Append(",\"dir\":").Append(JsonStr(m.Dir.ToString()));
            sb.Append(",\"startup\":").Append(m.Startup);
            sb.Append(",\"active\":").Append(m.Active);
            sb.Append(",\"recovery\":").Append(m.Recovery);
            sb.Append(",\"onBlock\":").Append(m.OnBlock);
            sb.Append(",\"damage\":").Append(m.Damage);
            sb.Append(",\"flags\":").Append((uint)m.Flags);
            sb.Append(",\"exCost\":").Append(m.ExCost);
            sb.Append(",\"isEx\":").Append(m.IsEx ? "true" : "false");
            sb.Append(",\"motionGated\":").Append(m.MotionGated ? "true" : "false");
            sb.Append(",\"airOK\":").Append(m.AirOK ? "true" : "false");
            sb.Append(",\"chainTo\":[");
            for (int i = 0; i < m.ChainTo.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(m.ChainTo[i]);
            }
            sb.Append("]}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string JsonStr(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
