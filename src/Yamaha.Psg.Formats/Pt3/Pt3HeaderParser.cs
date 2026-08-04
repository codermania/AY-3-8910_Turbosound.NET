using System.Text;

namespace Yamaha.Psg.Formats.Pt3;

/// <summary>
/// Parses the fixed 201-byte ($00-$C8) PT3 header, plus the $FF-terminated pattern order list that
/// immediately follows at $C9. Field knowledge is from README_pt3.txt by Vince Weaver
/// (http://www.deater.net/weave/vmwprod/pt3_player/README_pt3.txt, see docs/PT3_TABLES.md) - not
/// copied code, just documented byte layout. That document's own printed offset ranges for the
/// Name/Author string fields are internally inconsistent by one byte (a labeled size that doesn't
/// match its own printed start/end pair). The offsets below were resolved empirically against the
/// real PT3.7 test file (Name ends at $3D not the document's printed $3E; Author is 33 bytes, $42-$62,
/// not the document's labeled "32 bytes") - this is the byte alignment that produces a correctly
/// $FF-terminated pattern order list and plausible, in-range sample/ornament pointers on that file.
/// </summary>
internal static class Pt3HeaderParser
{
    private const int NameOffset = 0x1E;
    private const int NameLength = 32;
    private const int AuthorOffset = 0x42;
    private const int FrequencyTableOffset = 0x63;
    private const int AuthorLength = FrequencyTableOffset - AuthorOffset; // 33 bytes - see class remarks
    private const int SpeedOffset = 0x64;
    private const int PatternsCountOffset = 0x65;
    private const int LoopPositionOffset = 0x66;
    private const int PatsPtrsOffset = 0x67;
    private const int SamplePointersOffset = 0x69;
    private const int SampleCount = 32;
    private const int OrnamentPointersOffset = 0xA9;
    private const int OrnamentCount = 16;
    private const int PatternOrderOffset = 0xC9;

    // Two magic prefixes seen in the wild in this project's real test files: "ProTracker 3." (the
    // one README_pt3.txt documents, used by newer/compiled files) and "Vortex Tracker II" (an older
    // export, confirmed on a real file - "MmcM - Hibernation...pt3" - where the whole 99-byte
    // magic+version+name+" by "+author region is instead one free-form human-readable string, e.g.
    // "Vortex Tracker II 1.0 module: <title> :: <subtitle> by <author> <date> <panning> <chip>").
    // The decorative text differs, but the structural fields from $63 onward sit at the exact same
    // fixed offsets either way - confirmed by hex-dumping that file's sample/ornament pointers and
    // pattern order list, all of which land in-range and correctly multiple-of-3/terminated. Name and
    // Author are read from their usual fixed offsets regardless of which prefix matched; for this
    // "Vortex Tracker II" style they just end up as an arbitrary, cosmetic slice of the free-form text.
    private static readonly byte[][] KnownMagicPrefixes =
    [
        "ProTracker 3."u8.ToArray(),
        "Vortex Tracker II"u8.ToArray(),
    ];

    public static Pt3Header Parse(byte[] data)
    {
        bool magicMatches = data.Length >= PatternOrderOffset
            && KnownMagicPrefixes.Any(magic => data.AsSpan(0, magic.Length).SequenceEqual(magic));

        if (!magicMatches)
        {
            throw new FormatException("Doesn't look like a .PT3 file: missing a known PT3 magic prefix.");
        }

        byte versionDigit = data[0x0D];
        int version = versionDigit is >= (byte)'0' and <= (byte)'9' ? versionDigit - '0' : 6;

        string name = ReadFixedString(data, NameOffset, NameLength);
        string author = ReadFixedString(data, AuthorOffset, AuthorLength);

        int frequencyTable = data[FrequencyTableOffset];
        int speed = data[SpeedOffset];
        int patternsCountField = data[PatternsCountOffset];
        int loopPosition = data[LoopPositionOffset];
        int patsPtrsBase = ReadUInt16LE(data, PatsPtrsOffset);

        var samplePointers = new int[SampleCount];
        for (int i = 0; i < SampleCount; i++)
        {
            samplePointers[i] = ReadUInt16LE(data, SamplePointersOffset + (i * 2));
        }

        var ornamentPointers = new int[OrnamentCount];
        for (int i = 0; i < OrnamentCount; i++)
        {
            ornamentPointers[i] = ReadUInt16LE(data, OrnamentPointersOffset + (i * 2));
        }

        // The pattern is multiplied by 3 in the on-disk list (README_pt3.txt, "Pattern list");
        // we store the plain pattern number here so callers never have to re-divide.
        var patternOrder = new List<int>();
        int pos = PatternOrderOffset;
        while (pos < data.Length && data[pos] != 0xFF)
        {
            if (data[pos] % 3 != 0)
            {
                throw new FormatException($"Pattern order entry at offset 0x{pos:X} (value {data[pos]}) is not a multiple of 3.");
            }

            patternOrder.Add(data[pos] / 3);
            pos++;
        }

        return new Pt3Header(
            version,
            name,
            author,
            frequencyTable,
            speed,
            patternsCountField,
            loopPosition,
            patsPtrsBase,
            samplePointers,
            ornamentPointers,
            patternOrder);
    }

    private static ushort ReadUInt16LE(byte[] data, int offset) => (ushort)(data[offset] | (data[offset + 1] << 8));

    private static string ReadFixedString(byte[] data, int offset, int length)
        => Encoding.Latin1.GetString(data, offset, length).TrimEnd(' ', '\0');
}

/// <summary>Raw fields parsed straight out of the PT3 header, before samples/ornaments/patterns are resolved.</summary>
internal sealed class Pt3Header
{
    public Pt3Header(
        int version,
        string name,
        string author,
        int frequencyTable,
        int speed,
        int patternsCountField,
        int loopPosition,
        int patsPtrsBase,
        IReadOnlyList<int> samplePointers,
        IReadOnlyList<int> ornamentPointers,
        IReadOnlyList<int> patternOrder)
    {
        Version = version;
        Name = name;
        Author = author;
        FrequencyTable = frequencyTable;
        Speed = speed;
        PatternsCountField = patternsCountField;
        LoopPosition = loopPosition;
        PatsPtrsBase = patsPtrsBase;
        SamplePointers = samplePointers;
        OrnamentPointers = ornamentPointers;
        PatternOrder = patternOrder;
    }

    public int Version { get; }
    public string Name { get; }
    public string Author { get; }
    public int FrequencyTable { get; }
    public int Speed { get; }

    /// <summary>Raw header byte ($65): "Number of patterns+1" per README_pt3.txt. Not used for parsing -
    /// which patterns actually exist is derived from <see cref="PatternOrder"/> instead (see Pt3Module).</summary>
    public int PatternsCountField { get; }

    public int LoopPosition { get; }
    public int PatsPtrsBase { get; }
    public IReadOnlyList<int> SamplePointers { get; }
    public IReadOnlyList<int> OrnamentPointers { get; }
    public IReadOnlyList<int> PatternOrder { get; }
}
