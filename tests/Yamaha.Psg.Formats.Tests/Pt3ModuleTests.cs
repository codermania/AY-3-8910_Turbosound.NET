using Yamaha.Psg.Formats.Pt3;

namespace Yamaha.Psg.Formats.Tests;

/// <summary>
/// Milestone 11.2 (raw parser) tests, against the real user-provided PT3.7 TurboSound file (see
/// fixtures/SOURCES.md). A real file is used rather than only a synthetic one because the parser's
/// header offsets were themselves resolved by cross-checking README_pt3.txt against this exact
/// file's bytes (see Pt3HeaderParser remarks) - these assertions are what pin that resolution down
/// as a regression check, not just a description of the code.
/// </summary>
public class Pt3ModuleTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "user_provided", "EA - Proudly Loneliness (2018) (DiHalt 2018, 4).pt3");

    [Fact]
    public void Load_ParsesHeaderFields()
    {
        if (!File.Exists(FixturePath)) return;

        Pt3Module module = Pt3Module.Load(FixturePath);

        Assert.Equal(7, module.Version); // "ProTracker 3.7"
        Assert.Equal("Proudly Loneliness", module.Name);
        Assert.Equal("EA@may18", module.Author);
        Assert.Equal(2, module.FrequencyTable); // ASM_34_35, per docs/PT3_TABLES.md
        Assert.Equal(5, module.Speed);
        Assert.Equal(0, module.LoopPosition);
    }

    [Fact]
    public void Load_ParsesPatternOrder_AsPatternNumbersNotRawTimes3Values()
    {
        if (!File.Exists(FixturePath)) return;

        Pt3Module module = Pt3Module.Load(FixturePath);

        int[] expected = [0, 4, 0, 1, 3, 2, 3, 7, 5, 6, 5, 8, 9, 10, 9, 11, 5, 6, 5, 8, 9, 10, 9, 11, 12, 13];
        Assert.Equal(expected, module.PatternOrder);
    }

    [Fact]
    public void Load_ResolvesExactlyThePatternsReferencedByPatternOrder()
    {
        if (!File.Exists(FixturePath)) return;

        Pt3Module module = Pt3Module.Load(FixturePath);

        Assert.Equal(14, module.Patterns.Count); // distinct values 0..13 in the pattern order
        Assert.True(module.Patterns.ContainsKey(0));
        Assert.True(module.Patterns.ContainsKey(13));
        Assert.False(module.Patterns.ContainsKey(14));
    }

    [Fact]
    public void Load_ResolvesPatternZeroChannelAddresses()
    {
        if (!File.Exists(FixturePath)) return;

        Pt3Module module = Pt3Module.Load(FixturePath);

        Pt3Pattern pattern0 = module.Patterns[0];
        Assert.Equal(312, pattern0.ChannelAAddress);
        Assert.Equal(316, pattern0.ChannelBAddress);
        Assert.Equal(312, pattern0.ChannelCAddress); // A and C share an address: both are trivially empty
    }

    [Fact]
    public void Load_LeavesUnusedSampleSlotsNull_AndParsesAUsedOne()
    {
        if (!File.Exists(FixturePath)) return;

        Pt3Module module = Pt3Module.Load(FixturePath);

        Assert.Equal(32, module.Samples.Count);
        Assert.Null(module.Samples[0]);
        Assert.Null(module.Samples[3]);
        Assert.NotNull(module.Samples[4]);

        Pt3Sample sample4 = module.Samples[4]!;
        Assert.Equal(6, sample4.Loop);
        Assert.Equal(7, sample4.Steps.Count);

        Pt3SampleStep step0 = sample4.Steps[0];
        Assert.False(step0.AmplitudeSliding);
        Assert.True(step0.EnvelopeSlideUp);
        Assert.Equal(0, step0.EnvelopeSlideValue);
        Assert.False(step0.HasEnvelope); // byte0 bit0 = 1 -> envelope NOT used (see Pt3SampleStep remarks)
        Assert.False(step0.NoiseDisabled);
        Assert.False(step0.ToneDisabled);
        Assert.False(step0.AccumulateTone);
        Assert.Equal(15, step0.Amplitude);
        Assert.Equal(256, step0.ToneOffset);
    }

    [Fact]
    public void Load_LeavesUnusedOrnamentSlotsNull_AndParsesAUsedOne()
    {
        if (!File.Exists(FixturePath)) return;

        Pt3Module module = Pt3Module.Load(FixturePath);

        Assert.Equal(16, module.Ornaments.Count);
        Assert.NotNull(module.Ornaments[0]);

        Pt3Ornament ornament0 = module.Ornaments[0]!;
        Assert.Equal(0, ornament0.Loop);
        Assert.Equal([(sbyte)0], ornament0.Values);
    }

    [Fact]
    public void Load_NotAPt3File_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Pt3Module.Load(new byte[300]));
    }

    [Fact]
    public void Load_AlternateVortexTrackerMagicPrefix_ParsesStructuralFieldsCorrectly()
    {
        // "MmcM - Hibernation...pt3": a real file whose magic/name/author region is one free-form
        // "Vortex Tracker II 1.0 module: ..." string instead of "ProTracker 3.X compilation of...".
        // The structural fields from $63 onward are unaffected - confirmed by hex dump before fixing
        // Pt3HeaderParser to accept this prefix too (see fixtures/SOURCES.md).
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "user_provided", "MmcM - Hibernation (2016) (DiHalt Lite 2016, 1).pt3");
        if (!File.Exists(path)) return;

        Pt3Module module = Pt3Module.Load(path);

        Assert.Equal(2, module.FrequencyTable);
        Assert.Equal(4, module.Speed);
        Assert.Equal(19, module.LoopPosition);
        Assert.True(module.Patterns.Count > 0);
        Assert.Null(module.Samples[0]); // pointer 0 - unused slot
        Assert.NotNull(module.Samples[1]); // pointer 0x1644 - lands well within the file
    }
}
