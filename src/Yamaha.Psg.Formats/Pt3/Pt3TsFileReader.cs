using System.Text;
using Yamaha.Psg.Formats.Common;

namespace Yamaha.Psg.Formats.Pt3;

/// <summary>
/// Public entry point for a real TS/TurboSound `.pt3` file: two independent 3-channel PT3 modules,
/// meant to be played together on two AY/YM chips in parallel (the actual TurboSound hardware mod -
/// see <see cref="MultiChipAySoundChip"/>). Confirmed on a real user-supplied file
/// ("Stellar one", CjSplinter &amp; MmcM, DiHalt 2011): the trailer's two `Size` fields are exactly
/// each module's own byte length, not some other quantity - `Size1 + Size2 + 16 (trailer) == file
/// length` held exactly.
/// </summary>
public static class Pt3TsFileReader
{
    /// <summary>Trailer layout (last 16 bytes of the file): Type1(4) + Size1(2, LE) + Type2(4) +
    /// Size2(2, LE) + TSID(4, "02TS"). Both types are "PT3!" for the only variant seen in practice
    /// (two PT3 modules) - the format could in theory carry other 4-byte type tags for other module
    /// kinds, but that's speculative; only "PT3!" is recognized here.</summary>
    private const int TrailerLength = 16;

    public static IReadOnlyList<IRegisterDumpPlayer> Load(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.ToArray());
    }

    public static IReadOnlyList<IRegisterDumpPlayer> Load(string path) => Load(File.ReadAllBytes(path));

    /// <summary>True if <paramref name="data"/> ends with a valid two-PT3-module TS trailer. Callers
    /// (the CLI player) use this to decide between <see cref="Pt3FileReader"/> (ordinary single
    /// module) and this reader - a `.pt3` extension alone doesn't say which one a given file is.</summary>
    public static bool IsTsContainer(byte[] data) => TryReadTrailer(data, out _, out _);

    private static IReadOnlyList<IRegisterDumpPlayer> Load(byte[] data)
    {
        if (!TryReadTrailer(data, out int module1Length, out int module2Length))
        {
            throw new FormatException("Not a TS/TurboSound PT3 container (missing or invalid trailer).");
        }

        byte[] module1 = data[..module1Length];
        byte[] module2 = data[module1Length..(module1Length + module2Length)];

        // Each module is a complete, independent PT3 structure with its own header at offset 0 of
        // its own slice - every internal pointer (sample/ornament/pattern addresses) is relative to
        // that module's own start, exactly like an ordinary single-module .pt3 file, so the existing
        // parser/interpreter need no changes at all to handle either half.
        return new IRegisterDumpPlayer[]
        {
            Pt3Interpreter.Play(Pt3Module.Load(module1)),
            Pt3Interpreter.Play(Pt3Module.Load(module2)),
        };
    }

    private static bool TryReadTrailer(byte[] data, out int module1Length, out int module2Length)
    {
        module1Length = 0;
        module2Length = 0;

        if (data.Length < TrailerLength)
        {
            return false;
        }

        int trailerStart = data.Length - TrailerLength;
        string type1 = Encoding.ASCII.GetString(data, trailerStart, 4);
        int size1 = data[trailerStart + 4] | (data[trailerStart + 5] << 8);
        string type2 = Encoding.ASCII.GetString(data, trailerStart + 6, 4);
        int size2 = data[trailerStart + 10] | (data[trailerStart + 11] << 8);
        string tsId = Encoding.ASCII.GetString(data, trailerStart + 12, 4);

        if (type1 != "PT3!" || type2 != "PT3!" || tsId != "02TS")
        {
            return false;
        }

        // The two module sizes must exactly account for the whole file (module bytes + this 16-byte
        // trailer, nothing else) - a real TS file has no padding or extra data anywhere.
        if (size1 + size2 + TrailerLength != data.Length)
        {
            return false;
        }

        module1Length = size1;
        module2Length = size2;
        return true;
    }
}
