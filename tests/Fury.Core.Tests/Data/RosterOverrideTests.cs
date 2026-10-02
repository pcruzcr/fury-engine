using Fury.Core.Data;
using Xunit;

namespace Fury.Core.Tests.Data;

public class RosterOverrideTests
{
    [Fact]
    public void Override_ReplacesTheTable_ChangesTheFingerprint_AndCanBeRemoved()
    {
        ulong builtIn = CharacterRoster.Create("glen").Fingerprint();
        var moves = new List<MoveEntry>();
        var table = GlenData.Create().Moves;
        for (int i = 0; i < table.MoveCount; i++) moves.Add(table.GetMove(i));
        moves[0] = moves[0] with { Damage = 999 };

        try
        {
            CharacterRoster.SetMoveTableOverride("glen", BinaryMoveTable.Parse(BinaryMoveTableWriter.Write([.. moves])));
            Assert.True(CharacterRoster.HasOverride("GLEN"));
            var modded = CharacterRoster.Create("glen");
            Assert.Equal(999, modded.Moves.GetMove(0).Damage);
            Assert.NotEqual(builtIn, modded.Fingerprint()); // peers / replays with other data are refused
        }
        finally
        {
            CharacterRoster.SetMoveTableOverride("glen", null);
        }

        Assert.False(CharacterRoster.HasOverride("glen"));
        Assert.Equal(builtIn, CharacterRoster.Create("glen").Fingerprint());
    }

    [Fact]
    public void Override_MustKeepTheCanonicalSlots()
    {
        var tooSmall = BinaryMoveTable.Parse(BinaryMoveTableWriter.Write(new MoveEntry[MoveSlot.Count - 1]));
        Assert.Throws<InvalidDataException>(() => CharacterRoster.SetMoveTableOverride("kira", tooSmall));
        Assert.False(CharacterRoster.HasOverride("kira"));
    }
}
