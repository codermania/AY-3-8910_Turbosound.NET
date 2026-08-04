namespace Yamaha.Psg.Formats.Pt3;

/// <summary>
/// A fully-parsed but uninterpreted PT3 module: header fields, the pattern play order, the 32 sample
/// and 16 ornament tables, and the file offsets of each used pattern's three channel command streams.
/// This is the "raw parser" stage (milestone 11.2) - it does not walk/tokenize the per-channel note
/// and effect command streams; that belongs to the interpreter (milestone 11.3+), because doing so
/// correctly requires knowing each effect's parameter byte count to tell a real end-of-stream $00 from
/// a $00 that happens to be an effect parameter value (README_pt3.txt itself flags zero-delay effect
/// parameters as an unresolved corner case) - so channel streams are exposed here only as
/// (RawData, start address) pairs for the interpreter to walk itself.
/// </summary>
internal sealed class Pt3Module
{
    public Pt3Module(
        int version,
        string name,
        string author,
        int frequencyTable,
        int speed,
        int loopPosition,
        IReadOnlyList<int> patternOrder,
        IReadOnlyList<Pt3Sample?> samples,
        IReadOnlyList<Pt3Ornament?> ornaments,
        IReadOnlyDictionary<int, Pt3Pattern> patterns,
        byte[] rawData)
    {
        Version = version;
        Name = name;
        Author = author;
        FrequencyTable = frequencyTable;
        Speed = speed;
        LoopPosition = loopPosition;
        PatternOrder = patternOrder;
        Samples = samples;
        Ornaments = ornaments;
        Patterns = patterns;
        RawData = rawData;
    }

    public int Version { get; }
    public string Name { get; }
    public string Author { get; }

    /// <summary>Selects one of 4 documented frequency tables (0-3). Not yet used to pick a
    /// <see cref="Pt3NoteTables"/> variant - see the open risk noted in docs/PT3_TABLES.md.</summary>
    public int FrequencyTable { get; }

    public int Speed { get; }

    /// <summary>Play-order position (index into <see cref="PatternOrder"/>) where playback loops back to.</summary>
    public int LoopPosition { get; }

    /// <summary>Pattern numbers in play order (already divided by 3 - see README_pt3.txt "Pattern list").</summary>
    public IReadOnlyList<int> PatternOrder { get; }

    /// <summary>32 sample slots; a slot is null when its header pointer is 0 (unused).</summary>
    public IReadOnlyList<Pt3Sample?> Samples { get; }

    /// <summary>16 ornament slots; a slot is null when its header pointer is 0 (unused).</summary>
    public IReadOnlyList<Pt3Ornament?> Ornaments { get; }

    /// <summary>Every pattern number referenced by <see cref="PatternOrder"/>, keyed by pattern number.</summary>
    public IReadOnlyDictionary<int, Pt3Pattern> Patterns { get; }

    /// <summary>The whole file, for the interpreter to read channel command streams from (see class remarks).</summary>
    public byte[] RawData { get; }

    public static Pt3Module Load(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.ToArray());
    }

    public static Pt3Module Load(string path) => Load(File.ReadAllBytes(path));

    internal static Pt3Module Load(byte[] data)
    {
        Pt3Header header = Pt3HeaderParser.Parse(data);

        var samples = new Pt3Sample?[header.SamplePointers.Count];
        for (int i = 0; i < samples.Length; i++)
        {
            int pointer = header.SamplePointers[i];
            samples[i] = pointer == 0 ? null : ParseSample(data, pointer);
        }

        var ornaments = new Pt3Ornament?[header.OrnamentPointers.Count];
        for (int i = 0; i < ornaments.Length; i++)
        {
            int pointer = header.OrnamentPointers[i];
            ornaments[i] = pointer == 0 ? null : ParseOrnament(data, pointer);
        }

        var patterns = new Dictionary<int, Pt3Pattern>();
        foreach (int patternNumber in header.PatternOrder)
        {
            if (!patterns.ContainsKey(patternNumber))
            {
                patterns[patternNumber] = ParsePattern(data, header.PatsPtrsBase, patternNumber);
            }
        }

        return new Pt3Module(
            header.Version,
            header.Name,
            header.Author,
            header.FrequencyTable,
            header.Speed,
            header.LoopPosition,
            header.PatternOrder,
            samples,
            ornaments,
            patterns,
            data);
    }

    private static Pt3Sample ParseSample(byte[] data, int pointer)
    {
        int loop = data[pointer];
        int length = data[pointer + 1];
        var steps = new Pt3SampleStep[length];

        int pos = pointer + 2;
        for (int i = 0; i < length; i++)
        {
            byte b0 = data[pos];
            byte b1 = data[pos + 1];
            short toneOffset = (short)(data[pos + 2] | (data[pos + 3] << 8));

            steps[i] = new Pt3SampleStep(
                amplitudeSliding: (b0 & 0x80) != 0,
                amplitudeSlideUp: (b0 & 0x40) != 0,
                envelopeSlideUp: (b0 & 0x20) != 0,
                envelopeSlideValue: (b0 >> 1) & 0x0F, // unsigned magnitude - see Pt3SampleStep remarks
                hasEnvelope: (b0 & 0x01) == 0, // inverted from the milestone-11.2 guess - see Pt3SampleStep remarks
                noiseDisabled: (b1 & 0x80) != 0,
                accumulateTone: (b1 & 0x40) != 0,
                slideValuePersists: (b1 & 0x20) != 0,
                toneDisabled: (b1 & 0x10) != 0,
                amplitude: b1 & 0x0F,
                toneOffset: toneOffset);

            pos += 4;
        }

        return new Pt3Sample(loop, steps);
    }

    private static Pt3Ornament ParseOrnament(byte[] data, int pointer)
    {
        int loop = data[pointer];
        int length = data[pointer + 1];
        var values = new sbyte[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = (sbyte)data[pointer + 2 + i];
        }

        return new Pt3Ornament(loop, values);
    }

    private static Pt3Pattern ParsePattern(byte[] data, int patsPtrsBase, int patternNumber)
    {
        int chunk = patsPtrsBase + (patternNumber * 6);
        int channelA = data[chunk] | (data[chunk + 1] << 8);
        int channelB = data[chunk + 2] | (data[chunk + 3] << 8);
        int channelC = data[chunk + 4] | (data[chunk + 5] << 8);
        return new Pt3Pattern(channelA, channelB, channelC);
    }
}

/// <summary>A PT3 sample: applied to a channel's notes step by step while a note is held.</summary>
internal sealed class Pt3Sample
{
    public Pt3Sample(int loop, IReadOnlyList<Pt3SampleStep> steps)
    {
        Loop = loop;
        Steps = steps;
    }

    /// <summary>Step index to return to once playback reaches the end of <see cref="Steps"/>.</summary>
    public int Loop { get; }

    public IReadOnlyList<Pt3SampleStep> Steps { get; }
}

/// <summary>
/// One 4-byte sample step (README_pt3.txt "Sample format"). README_pt3.txt itself marks two of
/// byte1's bits as uncertain ("parameter to envelope sliding?" and "Bit 6+4 used to set
/// Noise/Channel mixer value"). Milestone 11.4 listening turned up real, audible bugs from the
/// milestone-11.2 guesses (missing bass/drums, garbage noise on side channels) - resolved by
/// cross-checking against an independent PT3 player's decode logic (benbaker76/PT3PlayBlazor,
/// itself derived from Sergey Bulba's/ayfly's GPL-licensed work - used here strictly as a
/// *behavioral* reference to understand bit meanings, no code copied, same policy as AY_emul/ZXTune
/// elsewhere in this project). Two real, confirmed corrections from that cross-check:
///   1. <see cref="HasEnvelope"/> (byte0 bit0) is inverted from the milestone-11.2 guess: envelope
///      is used when the bit is CLEAR, not set.
///   2. The "Noise/Channel mixer" bits are byte1 bit4 (tone mask) and bit4 bit7 (noise mask) -
///      *not* bit4+bit6 as README_pt3.txt's own phrasing suggested - and both are already stored
///      active-low (1 = disabled), matching AY register R7's convention directly, no inversion
///      needed when writing to R7.
/// Byte0 bits 4-1 (envelope/noise slide magnitude) and byte1 bit5 (whether that slide value
/// persists across steps) are parsed here per the same behavioral cross-check, but still unused by
/// the interpreter - the reference shows they drive a per-tick envelope-*or*-noise period nudge
/// (selected by byte1 bit7) that isn't implemented yet; a further open risk, not yet linked to any
/// reported symptom.
/// </summary>
internal readonly struct Pt3SampleStep
{
    public Pt3SampleStep(
        bool amplitudeSliding,
        bool amplitudeSlideUp,
        bool envelopeSlideUp,
        int envelopeSlideValue,
        bool hasEnvelope,
        bool noiseDisabled,
        bool accumulateTone,
        bool slideValuePersists,
        bool toneDisabled,
        int amplitude,
        short toneOffset)
    {
        AmplitudeSliding = amplitudeSliding;
        AmplitudeSlideUp = amplitudeSlideUp;
        EnvelopeSlideUp = envelopeSlideUp;
        EnvelopeSlideValue = envelopeSlideValue;
        HasEnvelope = hasEnvelope;
        NoiseDisabled = noiseDisabled;
        AccumulateTone = accumulateTone;
        SlideValuePersists = slideValuePersists;
        ToneDisabled = toneDisabled;
        Amplitude = amplitude;
        ToneOffset = toneOffset;
    }

    public bool AmplitudeSliding { get; }
    public bool AmplitudeSlideUp { get; }

    /// <summary>Sign of <see cref="EnvelopeSlideValue"/> (byte0 bit5) - not sign-extension of the magnitude itself.</summary>
    public bool EnvelopeSlideUp { get; }

    /// <summary>Unsigned magnitude 0-15 (byte0 bits 4-1); combine with <see cref="EnvelopeSlideUp"/> for the signed value. Not yet consumed by the interpreter.</summary>
    public int EnvelopeSlideValue { get; }

    /// <summary>True when this step drives the channel's amplitude from the envelope generator (byte0 bit0 CLEAR - see class remarks).</summary>
    public bool HasEnvelope { get; }

    /// <summary>Byte1 bit7: this step's R7 noise-disable bit for this channel (already active-low). Also selects envelope-vs-noise for the byte0 slide value (not yet consumed).</summary>
    public bool NoiseDisabled { get; }

    public bool AccumulateTone { get; }

    /// <summary>Byte1 bit5: whether this step's slide value keeps applying to later steps. Not yet consumed by the interpreter.</summary>
    public bool SlideValuePersists { get; }

    /// <summary>Byte1 bit4: this step's R7 tone-disable bit for this channel (already active-low).</summary>
    public bool ToneDisabled { get; }

    public int Amplitude { get; }
    public short ToneOffset { get; }
}

/// <summary>A PT3 ornament: a sequence of note (semitone) offsets applied step by step while a note is held.</summary>
internal sealed class Pt3Ornament
{
    public Pt3Ornament(int loop, IReadOnlyList<sbyte> values)
    {
        Loop = loop;
        Values = values;
    }

    /// <summary>Step index to return to once playback reaches the end of <see cref="Values"/>.</summary>
    public int Loop { get; }

    public IReadOnlyList<sbyte> Values { get; }
}

/// <summary>
/// File offsets of one pattern's three channel command streams. Each stream starts here and runs
/// until a real end-of-pattern $00 - see <see cref="Pt3Module.RawData"/> remarks for why that scan
/// isn't done at this parsing stage.
/// </summary>
internal sealed class Pt3Pattern
{
    public Pt3Pattern(int channelAAddress, int channelBAddress, int channelCAddress)
    {
        ChannelAAddress = channelAAddress;
        ChannelBAddress = channelBAddress;
        ChannelCAddress = channelCAddress;
    }

    public int ChannelAAddress { get; }
    public int ChannelBAddress { get; }
    public int ChannelCAddress { get; }
}
