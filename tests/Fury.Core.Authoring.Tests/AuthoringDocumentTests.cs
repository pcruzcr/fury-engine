using System.IO;
using Fury.Core.Data;
using Fury.Core.Authoring;
using Xunit;

namespace Fury.Core.Authoring.Tests;

/// <summary>Persistence models behind the editors (they used to be UI stubs that saved nothing).</summary>
public class EditorDocumentTests
{
    [Fact]
    public void StageSerializer_RoundTripsAllFields()
    {
        var stage = new StageData
        {
            Name = "Dojo",
            Width = 2000,
            Height = 900,
            Gravity = 1.5f,
            BackgroundColor = new ColorRgba(0.2f, 0.3f, 0.4f, 1f),
        };
        stage.Platforms.Add(new PlatformData { X = 1, Y = 2, Width = 3, Height = 4 });
        stage.Hazards.Add(new HazardData { X = 5, Y = 6, Width = 7, Height = 8, Damage = 9 });

        var back = StageSerializer.FromJson(StageSerializer.ToJson(stage));

        Assert.Equal("Dojo", back.Name);
        Assert.Equal(2000, back.Width);
        Assert.Equal(1.5f, back.Gravity);
        Assert.Equal(0.3f, back.BackgroundColor.G, 3);
        Assert.Equal(3, back.Platforms[0].Width);
        Assert.Equal(9, back.Hazards[0].Damage);
    }

    [Fact]
    public void StageSerializer_RejectsGarbage()
    {
        Assert.Throws<InvalidDataException>(() => StageSerializer.FromJson("{not json"));
        Assert.Throws<InvalidDataException>(() => StageSerializer.FromJson("null"));
    }

    [Theory]
    [InlineData("../../evil", "evil.stage")]
    [InlineData("My Stage", "My_Stage.stage")]
    [InlineData("", "stage.stage")]
    [InlineData("C:\\Windows\\x", "C__Windows_x.stage")]
    public void StageSerializer_SafeFileName_CannotEscapeFolder(string name, string expected)
    {
        var file = StageSerializer.SafeFileName(name);
        Assert.Equal(expected, file);
        Assert.DoesNotContain("/", file);
        Assert.DoesNotContain("\\", file);
        Assert.DoesNotContain("..", file);
    }

    [Fact]
    public void MoveTableDocument_EditAndExport_RoundTripsThroughFmt1Parser()
    {
        var doc = MoveTableDocument.FromTable(GlenData.Create().Moves);
        var m = doc.Get(0);
        doc.Set(0, m with { Damage = 77, StartupFrames = 5 });
        int added = doc.Add(m with { NameHash = 42 });

        var parsed = BinaryMoveTable.Parse(doc.ToBytes());

        Assert.Equal(doc.Count, parsed.MoveCount);
        Assert.Equal(77, parsed.GetMove(0).Damage);
        Assert.Equal(5, parsed.GetMove(0).StartupFrames);
        Assert.Equal(42, parsed.GetMove(added).NameHash);
    }

    [Fact]
    public void MoveTableDocument_ProtectsCanonicalSlots_AndValidates()
    {
        var doc = MoveTableDocument.FromTable(KiraData.Create().Moves);
        Assert.False(doc.TryRemoveAt(0));
        Assert.False(doc.TryRemoveAt(MoveSlot.Count - 1));
        int extra = doc.Add(doc.Get(0));
        Assert.True(doc.TryRemoveAt(extra));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => doc.Set(0, doc.Get(0) with { ActiveFrames = 0 }));
    }
}
