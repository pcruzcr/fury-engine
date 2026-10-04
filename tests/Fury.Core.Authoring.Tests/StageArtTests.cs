using System.IO;
using Fury.Core.Authoring;
using Xunit;

namespace Fury.Core.Authoring.Tests;

/// <summary>Stage art: layers, music and floor options in the stage document.</summary>
public class StageArtTests
{
    private static StageData Valid() => new()
    {
        Name = "Dojo",
        Width = 2000,
        Height = 900,
        Gravity = 1f,
        BackgroundColor = new ColorRgba(0.1f, 0.1f, 0.2f),
    };

    [Fact]
    public void Layers_MusicAndFloor_RoundTripThroughJson()
    {
        var stage = Valid();
        stage.Music = "music/dojo.ogg";
        stage.DefaultFloor = false;
        stage.PixelArt = true;
        stage.Layers.Add(new BackgroundLayer { Image = "sky.png", Depth = 1, Scale = 2, Repeat = true });
        stage.Layers.Add(new BackgroundLayer { Image = "rail.png", Depth = 0, OffsetY = -10, Foreground = true });

        var back = StageSerializer.FromJson(StageSerializer.ToJson(stage));

        Assert.Equal("music/dojo.ogg", back.Music);
        Assert.False(back.DefaultFloor);
        Assert.True(back.PixelArt);
        Assert.Equal(2, back.Layers.Count);
        Assert.Equal("sky.png", back.Layers[0].Image);
        Assert.True(back.Layers[0].Repeat);
        Assert.Equal(2, back.Layers[0].Scale);
        Assert.True(back.Layers[1].Foreground);
        Assert.Equal(-10, back.Layers[1].OffsetY);
    }

    [Fact]
    public void StagesSavedBeforeArtExisted_LoadWithTheOldLook()
    {
        // The first stage format: no layers, music, floor or pixel-art fields at all.
        const string old = """
            { "Name": "Old", "Width": 2000, "Height": 900, "Gravity": 1,
              "BackgroundColor": { "R": 0.1, "G": 0.1, "B": 0.2, "A": 1 }, "Platforms": [], "Hazards": [] }
            """;
        var stage = StageSerializer.FromJson(old);

        Assert.Empty(stage.Layers);
        Assert.Equal("", stage.Music);
        Assert.True(stage.DefaultFloor);
        Assert.False(stage.PixelArt);
    }

    [Fact]
    public void NullEntriesInHandWrittenLayers_AreDropped()
    {
        const string json = """
            { "Name": "N", "Width": 2000, "Height": 900, "Gravity": 1,
              "BackgroundColor": { "R": 0, "G": 0, "B": 0, "A": 1 },
              "Layers": [ null, { "Image": "a.png" }, { "Image": null } ] }
            """;
        var stage = StageSerializer.FromJson(json.Replace("{ \"Image\": null }", "{ \"Image\": \"b.png\" }"));
        Assert.Equal(["a.png", "b.png"], stage.Layers.Select(l => l.Image));
    }

    [Fact]
    public void Validation_RejectsUnsafeOrOutOfRangeArt()
    {
        void Rejects(Action<StageData> mutate)
        {
            var stage = Valid();
            mutate(stage);
            Assert.Throws<InvalidDataException>(stage.Validate);
        }

        BackgroundLayer Layer() => new() { Image = "ok.png" };

        Rejects(s => s.Layers.Add(new BackgroundLayer { Image = "" }));
        Rejects(s => s.Layers.Add(new BackgroundLayer { Image = "../escape.png" }));
        Rejects(s => s.Layers.Add(new BackgroundLayer { Image = "/abs/path.png" }));
        Rejects(s => s.Layers.Add(new BackgroundLayer { Image = "C:/x.png" }));
        Rejects(s => s.Layers.Add(new BackgroundLayer { Image = "sky.exe" }));
        Rejects(s => s.Layers.Add(new BackgroundLayer { Image = "a.png", Depth = 1.5 }));
        Rejects(s => s.Layers.Add(new BackgroundLayer { Image = "a.png", Depth = double.NaN }));
        Rejects(s => s.Layers.Add(new BackgroundLayer { Image = "a.png", Scale = 0 }));
        Rejects(s => s.Layers.Add(new BackgroundLayer { Image = "a.png", OffsetX = 1e9 }));
        Rejects(s =>
        {
            for (int i = 0; i <= StageData.MaxLayers; i++) s.Layers.Add(Layer());
        });
        Rejects(s => s.Music = "../song.ogg");
        Rejects(s => s.Music = "song.wav");

        var good = Valid();
        good.Music = "audio/song.mp3";
        good.Layers.Add(new BackgroundLayer { Image = "bg/sky.webp", Depth = 0.5, Scale = 1.5 });
        good.Validate();
    }
}
