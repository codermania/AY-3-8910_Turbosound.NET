using Yamaha.Psg.Formats.Psg;

namespace Yamaha.Psg.Formats.Tests;

public class PsgFileReaderTests
{
    private static byte[] Header(byte version = 0, byte frequency = 0)
        => [(byte)'P', (byte)'S', (byte)'G', 0x1A, version, frequency, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    private static Stream ToStream(params byte[][] parts)
    {
        var all = parts.SelectMany(p => p).ToArray();
        return new MemoryStream(all);
    }

    [Fact]
    public void Load_MinimalFile_NoRegisterWrites_ProducesOneEmptyFrame()
    {
        using var stream = ToStream(Header(), [0xFF]);

        var player = PsgFileReader.Load(stream);

        Assert.Single(player.Frames);
        for (int r = 0; r < 14; r++)
        {
            Assert.Equal(0, player.Frames[0][r]);
        }
    }

    [Fact]
    public void Load_InvalidMagic_ThrowsFormatException()
    {
        using var stream = ToStream([(byte)'X', (byte)'X', (byte)'X', 0x1A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        Assert.Throws<FormatException>(() => PsgFileReader.Load(stream));
    }

    [Fact]
    public void Load_TruncatedHeader_ThrowsFormatException()
    {
        using var stream = new MemoryStream([(byte)'P', (byte)'S', (byte)'G']);

        Assert.Throws<FormatException>(() => PsgFileReader.Load(stream));
    }

    [Fact]
    public void Load_SingleFrameWithRegisterWrite_AppliesValue()
    {
        using var stream = ToStream(Header(), [0xFF, 8, 0x0F]);

        var player = PsgFileReader.Load(stream);

        Assert.Single(player.Frames);
        Assert.Equal(0x0F, player.Frames[0][8]);
    }

    [Fact]
    public void Load_RegisterValues_PersistAcrossFramesUnlessOverwritten()
    {
        using var stream = ToStream(Header(), [0xFF, 8, 0x0F, 0xFF, 9, 0x05]);

        var player = PsgFileReader.Load(stream);

        Assert.Equal(2, player.Frames.Count);
        Assert.Equal(0x0F, player.Frames[0][8]);
        Assert.Equal(0, player.Frames[0][9]);

        // The second frame did not rewrite R8 — the value should carry over.
        Assert.Equal(0x0F, player.Frames[1][8]);
        Assert.Equal(0x05, player.Frames[1][9]);
    }

    [Fact]
    public void Load_RepeatMarker_InsertsFourTimesNRepeatedFrames()
    {
        using var stream = ToStream(Header(), [0xFF, 8, 0x0A, 0xFE, 0x02]);

        var player = PsgFileReader.Load(stream);

        Assert.Equal(1 + (2 * 4), player.Frames.Count);
        foreach (var frame in player.Frames)
        {
            Assert.Equal(0x0A, frame[8]);
        }
    }

    [Fact]
    public void Load_Fe01Ff_IsEquivalentToFiveInterruptMarkers()
    {
        // The documented example of equivalence: "FE 01 FF" == "FF FF FF FF FF".
        using var stream = ToStream(Header(), [0xFE, 0x01, 0xFF]);

        var player = PsgFileReader.Load(stream);

        Assert.Equal(5, player.Frames.Count);
    }

    [Fact]
    public void Load_RegisterNumberOutsideAyRange_IsSkippedWithoutDesyncingStream()
    {
        // Register 200 (a non-AY device extension) must be skipped along with its value byte,
        // without breaking the parse of the valid (8, 0x0F) pair that follows it.
        using var stream = ToStream(Header(), [0xFF, 200, 0xAB, 8, 0x0F]);

        var player = PsgFileReader.Load(stream);

        Assert.Single(player.Frames);
        Assert.Equal(0x0F, player.Frames[0][8]);
    }

    [Fact]
    public void Load_IoPortRegisters14And15_AreSkipped()
    {
        using var stream = ToStream(Header(), [0xFF, 14, 0xAA, 15, 0xBB, 8, 0x0F]);

        var player = PsgFileReader.Load(stream);

        Assert.Single(player.Frames);
        Assert.Equal(0x0F, player.Frames[0][8]); // parsing didn't desync from skipping R14/R15
    }

    [Fact]
    public void Load_FrameRateHz_DefaultsTo50_WhenVersionBelow10()
    {
        using var stream = ToStream(Header(version: 5, frequency: 25), [0xFF]);

        var player = PsgFileReader.Load(stream);

        Assert.Equal(50, player.Metadata.FrameRateHz);
    }

    [Fact]
    public void Load_FrameRateHz_UsesHeaderByte_WhenVersion10OrAbove()
    {
        using var stream = ToStream(Header(version: 10, frequency: 25), [0xFF]);

        var player = PsgFileReader.Load(stream);

        Assert.Equal(25, player.Metadata.FrameRateHz);
    }

    [Fact]
    public void Load_TruncatedMidPair_StopsGracefullyWithoutThrowing()
    {
        using var stream = ToStream(Header(), [0xFF, 8]); // missing the value byte

        var player = PsgFileReader.Load(stream);

        Assert.Single(player.Frames);
        Assert.Equal(0, player.Frames[0][8]); // value wasn't applied, but parsing didn't crash
    }
}
