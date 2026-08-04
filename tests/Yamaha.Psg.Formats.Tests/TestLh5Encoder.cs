using Yamaha.Psg.Formats.Vtx;

namespace Yamaha.Psg.Formats.Tests;

/// <summary>
/// A test-only (not for production) lh5 encoder, needed only to generate known-valid compressed
/// streams for round-trip testing <see cref="Lh5Decoder"/>. Reuses the internal
/// <see cref="Lh5Decoder.HuffmanTree"/> — the exact same tree-building logic as the decoder —
/// so the encoder and decoder are guaranteed to agree on which bit code maps to which symbol;
/// this is not an independent implementation of the lh5 format.
/// </summary>
internal static class TestLh5Encoder
{
    private const int NumCodes = 510;
    private const int MaxOffsetCodes = 15;
    private const int CopyThreshold = 3;

    internal abstract record Command;
    internal sealed record Literal(byte Value) : Command;
    internal sealed record Match(int Length, int Distance) : Command; // Distance = how many bytes back (>=1)

    public static byte[] Encode(IReadOnlyList<Command> commands)
    {
        var writer = new BitWriter();

        var codeSymbols = new List<int>(commands.Count);
        var offsetCategories = new List<int>();
        var offsetExtraBits = new List<(int Value, int Bits)>();

        foreach (var command in commands)
        {
            if (command is Literal lit)
            {
                codeSymbols.Add(lit.Value);
            }
            else if (command is Match match)
            {
                codeSymbols.Add(256 + (match.Length - CopyThreshold));
                int offset = match.Distance - 1;
                int category = OffsetCategory(offset);
                offsetCategories.Add(category);
                offsetExtraBits.Add(ExtraBits(offset, category));
            }
        }

        var codeLengths = HuffmanLengthsFor(codeSymbols, NumCodes);
        var offsetLengths = HuffmanLengthsFor(offsetCategories, MaxOffsetCodes);

        // Temp-table symbols: 0/1/2 (skips) + (length+2) for each actually used length.
        var tempSymbolStream = BuildTempSymbolStream(codeLengths, out int codeTableN);
        var tempSymbolCounts = tempSymbolStream.Select(s => s.Symbol).ToList();
        var tempLengths = HuffmanLengthsFor(tempSymbolCounts, 31);

        writer.WriteBits(commands.Count, 16); // block length = number of commands

        int tempN = Math.Max(1, HighestUsedIndex(tempLengths) + 1);
        WriteRawLengthTable(writer, tempLengths, tempN, nBits: 5, includeSkipField: true);
        WriteCodeTable(writer, tempLengths, tempSymbolStream, codeTableN);
        int offsetN = Math.Max(1, HighestUsedIndex(offsetLengths) + 1);
        WriteRawLengthTable(writer, offsetLengths, offsetN, nBits: 4, includeSkipField: false);

        var codeCodes = BuildTreeAndExtractCodes(codeLengths, NumCodes);
        var offsetCodes = BuildTreeAndExtractCodes(offsetLengths, MaxOffsetCodes);

        int matchIndex = 0;
        foreach (var symbol in codeSymbols)
        {
            var (bits, length) = codeCodes[symbol];
            writer.WriteBits(bits, length);

            if (symbol >= 256)
            {
                int category = offsetCategories[matchIndex];
                var (extraValue, extraBitsCount) = offsetExtraBits[matchIndex];
                matchIndex++;

                var (offBits, offLength) = offsetCodes[category];
                writer.WriteBits(offBits, offLength);
                if (extraBitsCount > 0)
                {
                    writer.WriteBits(extraValue, extraBitsCount);
                }
            }
        }

        return writer.GetBytes();
    }

    private static int OffsetCategory(int offset)
    {
        if (offset == 0) return 0;
        if (offset == 1) return 1;

        int category = 1;
        int value = offset;
        while (value > 1)
        {
            value >>= 1;
            category++;
        }

        return category; // offset in [2^(category-1), 2^category - 1]
    }

    private static (int Value, int Bits) ExtraBits(int offset, int category)
    {
        if (category <= 1)
        {
            return (0, 0);
        }

        int bitsCount = category - 1;
        int value = offset - (1 << bitsCount);
        return (value, bitsCount);
    }

    private static Dictionary<int, (int Bits, int Length)> BuildTreeAndExtractCodes(byte[] lengths, int maxCodes)
    {
        var tree = new Lh5Decoder.HuffmanTree(maxCodes);
        int usedCount = HighestUsedIndex(lengths) + 1;
        if (usedCount <= 0)
        {
            usedCount = 1;
        }

        tree.Build(lengths, usedCount);
        return tree.ExtractCodes();
    }

    private static int HighestUsedIndex(byte[] lengths)
    {
        for (int i = lengths.Length - 1; i >= 0; i--)
        {
            if (lengths[i] != 0)
            {
                return i;
            }
        }

        return -1;
    }

    private readonly record struct TempSymbol(int Symbol, int ExtraValue, int ExtraBits);

    /// <summary>
    /// Encodes codeLengths[0..n-1] into a stream of temp-table symbols: consecutive runs of zero
    /// lengths collapse into skips (categories 0/1/2), non-zero lengths are encoded as (length+2).
    /// </summary>
    private static List<TempSymbol> BuildTempSymbolStream(byte[] codeLengths, out int n)
    {
        n = HighestUsedIndex(codeLengths) + 1;
        if (n <= 0)
        {
            n = 1;
        }

        var result = new List<TempSymbol>();
        int i = 0;
        while (i < n)
        {
            if (codeLengths[i] == 0)
            {
                int runStart = i;
                while (i < n && codeLengths[i] == 0)
                {
                    i++;
                }

                int remaining = i - runStart;
                while (remaining > 0)
                {
                    if (remaining >= 20)
                    {
                        int chunk = Math.Min(remaining, 20 + 511);
                        result.Add(new TempSymbol(2, chunk - 20, 9));
                        remaining -= chunk;
                    }
                    else if (remaining >= 3)
                    {
                        int chunk = Math.Min(remaining, 18);
                        result.Add(new TempSymbol(1, chunk - 3, 4));
                        remaining -= chunk;
                    }
                    else
                    {
                        result.Add(new TempSymbol(0, 0, 0));
                        remaining -= 1;
                    }
                }
            }
            else
            {
                result.Add(new TempSymbol(codeLengths[i] + 2, 0, 0));
                i++;
            }
        }

        return result;
    }

    private static void WriteCodeTable(BitWriter writer, byte[] tempLengths, List<TempSymbol> tempSymbolStream, int n)
    {
        writer.WriteBits(n, 9);

        var tempCodes = BuildTreeAndExtractCodes(tempLengths, 31);
        foreach (var symbol in tempSymbolStream)
        {
            var (bits, length) = tempCodes[symbol.Symbol];
            writer.WriteBits(bits, length);
            if (symbol.ExtraBits > 0)
            {
                writer.WriteBits(symbol.ExtraValue, symbol.ExtraBits);
            }
        }
    }

    /// <summary>
    /// Writes a length table "directly" (like the temp/offset tables in the format itself — no
    /// Huffman coding). includeSkipField: only the temp table has the special 2-bit skip field
    /// right after the third value (i==2) — the offset table doesn't have this quirk.
    /// </summary>
    private static void WriteRawLengthTable(BitWriter writer, byte[] lengths, int n, int nBits, bool includeSkipField)
    {
        writer.WriteBits(n, nBits);

        for (int i = 0; i < n; i++)
        {
            WriteLengthValue(writer, lengths[i]);
            if (includeSkipField && i == 2)
            {
                writer.WriteBits(0, 2); // no skip — all n values are written individually
            }
        }
    }

    private static void WriteLengthValue(BitWriter writer, int len)
    {
        if (len < 7)
        {
            writer.WriteBits(len, 3);
            return;
        }

        writer.WriteBits(7, 3);
        for (int i = 0; i < len - 7; i++)
        {
            writer.WriteBits(1, 1);
        }

        writer.WriteBits(0, 1);
    }

    /// <summary>Counts symbol frequencies and builds valid code lengths (satisfying the Kraft inequality).</summary>
    private static byte[] HuffmanLengthsFor(List<int> symbols, int alphabetSize)
    {
        var frequencies = new int[alphabetSize];
        foreach (var s in symbols)
        {
            frequencies[s]++;
        }

        var used = new List<int>();
        for (int i = 0; i < alphabetSize; i++)
        {
            if (frequencies[i] > 0)
            {
                used.Add(i);
            }
        }

        var lengths = new byte[alphabetSize];

        if (used.Count == 0)
        {
            return lengths;
        }

        if (used.Count == 1)
        {
            lengths[used[0]] = 1;
            return lengths;
        }

        var nodes = used
            .Select(i => (Freq: (long)frequencies[i], Depths: new Dictionary<int, int> { [i] = 0 }))
            .ToList();

        while (nodes.Count > 1)
        {
            nodes.Sort((a, b) => a.Freq.CompareTo(b.Freq));
            var a = nodes[0];
            var b = nodes[1];
            nodes.RemoveRange(0, 2);

            var merged = new Dictionary<int, int>();
            foreach (var kv in a.Depths) merged[kv.Key] = kv.Value + 1;
            foreach (var kv in b.Depths) merged[kv.Key] = kv.Value + 1;

            nodes.Add((a.Freq + b.Freq, merged));
        }

        foreach (var kv in nodes[0].Depths)
        {
            lengths[kv.Key] = (byte)kv.Value;
        }

        return lengths;
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _current;
        private int _bitsInCurrent;

        public void WriteBits(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                int bit = (value >> i) & 1;
                _current = (_current << 1) | bit;
                _bitsInCurrent++;
                if (_bitsInCurrent == 8)
                {
                    _bytes.Add((byte)_current);
                    _current = 0;
                    _bitsInCurrent = 0;
                }
            }
        }

        public byte[] GetBytes()
        {
            if (_bitsInCurrent > 0)
            {
                _current <<= 8 - _bitsInCurrent;
                _bytes.Add((byte)_current);
                _current = 0;
                _bitsInCurrent = 0;
            }

            return _bytes.ToArray();
        }
    }
}
