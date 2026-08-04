using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;
using Yamaha.Psg.Formats.Common;
using Yamaha.Psg.Formats.Vtx;

namespace Yamaha.Psg.Formats.Tests;

public class VtxFileReaderTests
{
    private static byte[] BuildVtxFile(
        string chipId,
        byte stereoModeByte,
        int loopVbl,
        int clockHz,
        byte frameRateHz,
        int year,
        string title,
        string author,
        string tracker,
        string editor,
        string comment,
        byte[][] frames) // frames[f][r] = value of register r in frame f (14 registers per frame)
    {
        int frameCount = frames.Length;
        var interleaved = new byte[frameCount * RegisterFrame.RegisterCount];
        for (int r = 0; r < RegisterFrame.RegisterCount; r++)
        {
            for (int f = 0; f < frameCount; f++)
            {
                interleaved[(r * frameCount) + f] = frames[f][r];
            }
        }

        var commands = interleaved
            .Select(b => (TestLh5Encoder.Command)new TestLh5Encoder.Literal(b))
            .ToArray();
        byte[] compressed = TestLh5Encoder.Encode(commands);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((byte)chipId[0]);
        writer.Write((byte)chipId[1]);
        writer.Write(stereoModeByte);
        writer.Write((ushort)loopVbl);
        writer.Write((uint)clockHz);
        writer.Write(frameRateHz);
        writer.Write((ushort)year);
        writer.Write((uint)interleaved.Length);

        WriteNullTerminated(writer, title);
        WriteNullTerminated(writer, author);
        WriteNullTerminated(writer, tracker);
        WriteNullTerminated(writer, editor);
        WriteNullTerminated(writer, comment);

        writer.Write(compressed);

        return ms.ToArray();
    }

    private static void WriteNullTerminated(BinaryWriter writer, string s)
    {
        foreach (char c in s)
        {
            writer.Write((byte)c);
        }

        writer.Write((byte)0);
    }

    [Fact]
    public void Load_ParsesHeaderMetadata_ForAyChip()
    {
        var frame = new byte[RegisterFrame.RegisterCount];
        frame[13] = 0x0A;
        var data = BuildVtxFile(
            "ay", stereoModeByte: 1 /* Abc */, loopVbl: 5, clockHz: 1_773_400, frameRateHz: 50,
            year: 2024, title: "Test Song", author: "Test Author", tracker: "Vortex Tracker II",
            editor: "Editor", comment: "A comment", frames: [frame]);

        var player = VtxFileReader.Load(new MemoryStream(data));

        Assert.Equal(ChipVariant.Ay_3_8910, player.Metadata.ChipVariant);
        Assert.Equal(PanningPreset.Abc, player.Metadata.Panning);
        Assert.Equal(1_773_400, player.Metadata.ClockHz);
        Assert.Equal(50, player.Metadata.FrameRateHz);
        Assert.Equal(5, player.Metadata.LoopFrame);
        Assert.Equal(2024, player.Metadata.Year);
        Assert.Equal("Test Song", player.Metadata.Title);
        Assert.Equal("Test Author", player.Metadata.Author);
        Assert.Equal("Vortex Tracker II", player.Metadata.TrackerName);
        Assert.Equal("A comment", player.Metadata.Comment);
    }

    [Fact]
    public void Load_ParsesYmChipIdentifier()
    {
        var frame = new byte[RegisterFrame.RegisterCount];
        var data = BuildVtxFile(
            "ym", stereoModeByte: 0, loopVbl: 0, clockHz: 2_000_000, frameRateHz: 50,
            year: 0, title: "", author: "", tracker: "", editor: "", comment: "", frames: [frame]);

        var player = VtxFileReader.Load(new MemoryStream(data));

        Assert.Equal(ChipVariant.Ym2149, player.Metadata.ChipVariant);
        Assert.Equal(2_000_000, player.Metadata.ClockHz);
    }

    [Theory]
    [InlineData(0, PanningPreset.Mono)]
    [InlineData(1, PanningPreset.Abc)]
    [InlineData(2, PanningPreset.Acb)]
    [InlineData(3, PanningPreset.Bac)]
    [InlineData(4, PanningPreset.Bca)]
    [InlineData(5, PanningPreset.Cab)]
    [InlineData(6, PanningPreset.Cba)]
    public void Load_MapsStereoModeByte_ToPanningPreset(byte stereoMode, PanningPreset expected)
    {
        var frame = new byte[RegisterFrame.RegisterCount];
        var data = BuildVtxFile(
            "ay", stereoMode, 0, 1_773_400, 50, 0, "", "", "", "", "", [frame]);

        var player = VtxFileReader.Load(new MemoryStream(data));

        Assert.Equal(expected, player.Metadata.Panning);
    }

    [Fact]
    public void Load_DeinterleavesFrames_InColumnMajorOrder()
    {
        var frame0 = new byte[RegisterFrame.RegisterCount];
        frame0[0] = 0x12;
        frame0[8] = 0x0F;

        var frame1 = new byte[RegisterFrame.RegisterCount];
        frame1[0] = 0x34;
        frame1[8] = 0x05;

        var data = BuildVtxFile("ay", 1, 0, 1_773_400, 50, 0, "", "", "", "", "", [frame0, frame1]);

        var player = VtxFileReader.Load(new MemoryStream(data));

        Assert.Equal(2, player.Frames.Count);
        Assert.Equal(0x12, player.Frames[0][0]);
        Assert.Equal(0x0F, player.Frames[0][8]);
        Assert.Equal(0x34, player.Frames[1][0]);
        Assert.Equal(0x05, player.Frames[1][8]);
    }

    [Fact]
    public void Load_R13SentinelFF_MeansNotWritten_AndCarriesForwardPreviousValue()
    {
        var frame0 = new byte[RegisterFrame.RegisterCount];
        frame0[13] = 0x0A; // an explicit write of the envelope shape

        var frame1 = new byte[RegisterFrame.RegisterCount];
        frame1[13] = 0xFF; // the "unchanged" sentinel

        var data = BuildVtxFile("ay", 1, 0, 1_773_400, 50, 0, "", "", "", "", "", [frame0, frame1]);

        var player = VtxFileReader.Load(new MemoryStream(data));

        Assert.True(player.Frames[0].EnvelopeShapeWritten);
        Assert.Equal(0x0A, player.Frames[0][13]);

        Assert.False(player.Frames[1].EnvelopeShapeWritten);
        Assert.Equal(0x0A, player.Frames[1][13]); // carried forward from the previous frame, not 0xFF
    }

    [Fact]
    public void Load_R13ExplicitValue_MarksEnvelopeShapeWritten()
    {
        var frame0 = new byte[RegisterFrame.RegisterCount];
        frame0[13] = 0x08;

        var frame1 = new byte[RegisterFrame.RegisterCount];
        frame1[13] = 0x08; // same value, but explicitly rewritten

        var data = BuildVtxFile("ay", 1, 0, 1_773_400, 50, 0, "", "", "", "", "", [frame0, frame1]);

        var player = VtxFileReader.Load(new MemoryStream(data));

        Assert.True(player.Frames[0].EnvelopeShapeWritten);
        Assert.True(player.Frames[1].EnvelopeShapeWritten); // not 0xFF -> a real write, even with the same value
    }

    [Fact]
    public void Load_InvalidChipIdentifier_ThrowsFormatException()
    {
        var frame = new byte[RegisterFrame.RegisterCount];
        var data = BuildVtxFile("xx", 1, 0, 1_773_400, 50, 0, "", "", "", "", "", [frame]);

        Assert.Throws<FormatException>(() => VtxFileReader.Load(new MemoryStream(data)));
    }

    [Fact]
    public void Load_TooShortForHeader_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => VtxFileReader.Load(new MemoryStream(new byte[5])));
    }
}
