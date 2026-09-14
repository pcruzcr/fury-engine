using Fury.Core.Math;
using Fury.Core.Sim;

namespace Fury.Core.Data
{
    /// <summary>Glen — pressure heavyweight. Frame data per the canonical hitstop table (L4/M6/H10/EX12/Super20).</summary>
    public static class GlenData
    {
        public static CharacterConfig Create()
        {
            Span<MoveEntry> moves =
            [
                new(NameHash("GL_SL"), 4, 3, 7, 40, 4,  Fp.FromRatio(1, 2).Raw,  Fp.FromRatio(2, 1).Raw, Fp.FromRatio(1, 2).Raw, Fp.FromRatio(3, 2).Raw),
                new(NameHash("GL_SM"), 6, 4, 12, 70, 6,  Fp.FromRatio(1, 1).Raw, Fp.FromRatio(3, 1).Raw, Fp.FromRatio(1, 2).Raw, Fp.FromRatio(3, 2).Raw),
                new(NameHash("GL_SH"), 9, 5, 16, 110, 10, Fp.FromRatio(3, 2).Raw, Fp.FromRatio(7, 2).Raw, Fp.FromRatio(1, 2).Raw, Fp.FromRatio(3, 2).Raw),
                new(NameHash("GL_CL"), 4, 3, 8, 35, 4,   Fp.FromRatio(1, 2).Raw, Fp.FromRatio(2, 1).Raw, Fp.FromRatio(1, 2).Raw, Fp.FromRatio(1, 2).Raw),
                new(NameHash("GL_CH"), 8, 4, 15, 95, 10, Fp.FromRatio(3, 2).Raw, Fp.FromRatio(3, 1).Raw, Fp.FromRatio(1, 2).Raw, Fp.FromRatio(1, 1).Raw),
                new(NameHash("GL_ORD"), 8, 6, 20, 90, 12, Fp.FromRatio(2, 1).Raw, Fp.FromRatio(5, 1).Raw, Fp.FromRatio(3, 4).Raw, Fp.FromRatio(3, 2).Raw),
                new(NameHash("GL_ARE"), 10, 8, 28, 220, 20, Fp.FromRatio(5, 2).Raw, Fp.FromRatio(7, 1).Raw, Fp.FromRatio(1, 1).Raw, Fp.FromRatio(3, 2).Raw),
            ];
            return CharacterConfig.Create("glen", 1000, Fp.FromRatio(3, 1), Fp.FromRatio(5, 2), moves);
        }

        internal static int NameHash(string name)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(name);
            var words = new int[(bytes.Length + 3) / 4];
            System.Buffer.BlockCopy(bytes, 0, words, 0, bytes.Length);
            return unchecked((int)FpHash.HashInts(words));
        }
    }

    /// <summary>Kira — speed footnote of the roster: faster walk, lighter hits, same tables.</summary>
    public static class KiraData
    {
        public static CharacterConfig Create()
        {
            Span<MoveEntry> moves =
            [
                new(NameHash("KI_SL"), 3, 3, 6, 35, 4,  Fp.FromRatio(1, 2).Raw,  Fp.FromRatio(7, 4).Raw, Fp.FromRatio(1, 2).Raw, Fp.FromRatio(3, 2).Raw),
                new(NameHash("KI_SM"), 5, 4, 11, 65, 6, Fp.FromRatio(1, 1).Raw,  Fp.FromRatio(5, 2).Raw, Fp.FromRatio(1, 2).Raw, Fp.FromRatio(3, 2).Raw),
                new(NameHash("KI_SH"), 8, 5, 15, 100, 10, Fp.FromRatio(3, 2).Raw, Fp.FromRatio(3, 1).Raw, Fp.FromRatio(1, 2).Raw, Fp.FromRatio(3, 2).Raw),
                new(NameHash("KI_CL"), 4, 3, 7, 30, 4,  Fp.FromRatio(1, 2).Raw,  Fp.FromRatio(7, 4).Raw, Fp.FromRatio(1, 2).Raw, Fp.FromRatio(1, 2).Raw),
                new(NameHash("KI_CH"), 7, 4, 14, 85, 10, Fp.FromRatio(3, 2).Raw, Fp.FromRatio(5, 2).Raw, Fp.FromRatio(1, 2).Raw, Fp.FromRatio(1, 1).Raw),
                new(NameHash("KI_ECO"), 7, 6, 18, 85, 12, Fp.FromRatio(2, 1).Raw, Fp.FromRatio(11, 2).Raw, Fp.FromRatio(3, 4).Raw, Fp.FromRatio(3, 2).Raw),
                new(NameHash("KI_VAC"), 9, 8, 26, 210, 20, Fp.FromRatio(5, 2).Raw, Fp.FromRatio(15, 2).Raw, Fp.FromRatio(1, 1).Raw, Fp.FromRatio(3, 2).Raw),
            ];
            return CharacterConfig.Create("kira", 1000, Fp.FromRatio(7, 2), Fp.FromRatio(3, 1), moves);
        }

        internal static int NameHash(string name) => GlenData.NameHash(name);
    }
}
