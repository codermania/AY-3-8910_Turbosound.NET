namespace Yamaha.Psg.Formats.Vtx;

/// <summary>
/// Decoder for the LHA -lh5- algorithm (adaptive Huffman coding of code lengths + a 16KB sliding
/// window, LZSS-style back-references). An original implementation written from publicly
/// documented algorithm behavior (block structure, table sizes NC=510/NP=15/NT=31, MSB-first bit
/// order), without copying any existing decoder's code — see docs/DAC_TABLES.md for the project's
/// general approach to using this kind of source as a behavioral reference rather than as code to copy.
///
/// For VTX the data has already had the LHA archive container stripped (see VtxFileReader) — this
/// expects a bare lh5 stream, decoded exactly up to the known-in-advance uncompressed length.
/// </summary>
public static class Lh5Decoder
{
    private const int HistoryBits = 14;
    private const int RingBufferSize = 1 << HistoryBits; // 16384
    private const int OffsetBits = 4;
    private const int MaxOffsetCodes = (1 << OffsetBits) - 1; // 15
    private const int NumCodes = 510;
    private const int TempCodeBits = 5;
    private const int MaxTempCodes = (1 << TempCodeBits) - 1; // 31
    private const int CopyThreshold = 3;

    public static byte[] Decode(ReadOnlySpan<byte> compressed, int decompressedLength)
    {
        var bits = new BitReader(compressed);
        var output = new byte[decompressedLength];
        var ringBuffer = new byte[RingBufferSize];
        Array.Fill(ringBuffer, (byte)' ');
        int ringPos = 0;

        var tempTree = new HuffmanTree(MaxTempCodes);
        var codeTree = new HuffmanTree(NumCodes);
        var offsetTree = new HuffmanTree(MaxOffsetCodes);

        int blockRemaining = 0;
        int outPos = 0;

        while (outPos < decompressedLength)
        {
            if (blockRemaining == 0)
            {
                blockRemaining = StartNewBlock(bits, tempTree, codeTree, offsetTree);
                if (blockRemaining <= 0)
                {
                    break; // stream ended earlier than the expected length
                }
            }

            blockRemaining--;

            int code = codeTree.Decode(bits);

            if (code < 256)
            {
                byte b = (byte)code;
                output[outPos++] = b;
                ringBuffer[ringPos] = b;
                ringPos = (ringPos + 1) % RingBufferSize;
            }
            else
            {
                int copyCount = code - 256 + CopyThreshold;
                int offset = ReadOffsetCode(bits, offsetTree);
                int start = ringPos + RingBufferSize - offset - 1;

                for (int i = 0; i < copyCount && outPos < decompressedLength; i++)
                {
                    byte b = ringBuffer[(start + i) % RingBufferSize];
                    output[outPos++] = b;
                    ringBuffer[ringPos] = b;
                    ringPos = (ringPos + 1) % RingBufferSize;
                }
            }
        }

        return output;
    }

    private static int StartNewBlock(BitReader bits, HuffmanTree tempTree, HuffmanTree codeTree, HuffmanTree offsetTree)
    {
        int blockLength = bits.ReadBits(16);

        ReadTempTable(bits, tempTree);
        ReadCodeTable(bits, tempTree, codeTree);
        ReadOffsetTable(bits, offsetTree);

        return blockLength;
    }

    /// <summary>Reads a length value (usually 0..7, but can be extended past 7 via a unary code).</summary>
    private static int ReadLengthValue(BitReader bits)
    {
        int len = bits.ReadBits(3);
        if (len == 7)
        {
            while (bits.ReadBit() == 1)
            {
                len++;
            }
        }

        return len;
    }

    /// <summary>Reads the temporary table (used to Huffman-decode the code table).</summary>
    private static void ReadTempTable(BitReader bits, HuffmanTree tempTree)
    {
        int n = bits.ReadBits(TempCodeBits);
        if (n == 0)
        {
            int singleCode = bits.ReadBits(5);
            tempTree.SetSingle(singleCode);
            return;
        }

        if (n > MaxTempCodes)
        {
            n = MaxTempCodes;
        }

        var codeLengths = new byte[MaxTempCodes];
        int i = 0;
        while (i < n)
        {
            int len = ReadLengthValue(bits);
            codeLengths[i] = (byte)len;

            // After the third length (index 2) there is a 2-bit field to skip up to 3 further
            // lengths (treated as zero/unused) — the exact reason for this format quirk is
            // unknown, but it's documented in reference lh5 decoders.
            if (i == 2)
            {
                int skip = bits.ReadBits(2);
                for (int j = 0; j < skip; j++)
                {
                    i++;
                    codeLengths[i] = 0;
                }
            }

            i++;
        }

        tempTree.Build(codeLengths, n);
    }

    /// <summary>Reads the NC(510) code-length table, itself Huffman-decoded via the temp table.</summary>
    private static void ReadCodeTable(BitReader bits, HuffmanTree tempTree, HuffmanTree codeTree)
    {
        int n = bits.ReadBits(9);
        if (n == 0)
        {
            int singleCode = bits.ReadBits(9);
            codeTree.SetSingle(singleCode);
            return;
        }

        if (n > NumCodes)
        {
            n = NumCodes;
        }

        var codeLengths = new byte[NumCodes];
        int i = 0;
        while (i < n)
        {
            int symbol = tempTree.Decode(bits);

            if (symbol <= 2)
            {
                int skipCount = ReadSkipCount(bits, symbol);
                for (int j = 0; j < skipCount && i < n; j++)
                {
                    codeLengths[i] = 0;
                    i++;
                }
            }
            else
            {
                codeLengths[i] = (byte)(symbol - 2);
                i++;
            }
        }

        codeTree.Build(codeLengths, n);
    }

    /// <summary>Temp-table symbols 0-2 mean "skip N codes" — reads N according to the range.</summary>
    private static int ReadSkipCount(BitReader bits, int skipRange)
    {
        return skipRange switch
        {
            0 => 1,
            1 => bits.ReadBits(4) + 3,
            _ => bits.ReadBits(9) + 20,
        };
    }

    /// <summary>Reads the NP(15) offset code-length table — structured like the temp table, but without the skip field at index 2.</summary>
    private static void ReadOffsetTable(BitReader bits, HuffmanTree offsetTree)
    {
        int n = bits.ReadBits(OffsetBits);
        if (n == 0)
        {
            int singleCode = bits.ReadBits(OffsetBits);
            offsetTree.SetSingle(singleCode);
            return;
        }

        if (n > MaxOffsetCodes)
        {
            n = MaxOffsetCodes;
        }

        var codeLengths = new byte[MaxOffsetCodes];
        for (int i = 0; i < n; i++)
        {
            codeLengths[i] = (byte)ReadLengthValue(bits);
        }

        offsetTree.Build(codeLengths, n);
    }

    /// <summary>
    /// The decoded offset code "bits" specifies the number of offset bits: 0 -> offset 0; 1 -> offset 1;
    /// N -> the following (N-1) bits are read as the low bits, with the high bit implicitly 1.
    /// </summary>
    private static int ReadOffsetCode(BitReader bits, HuffmanTree offsetTree)
    {
        int code = offsetTree.Decode(bits);
        if (code == 0)
        {
            return 0;
        }

        if (code == 1)
        {
            return 1;
        }

        int lowBits = bits.ReadBits(code - 1);
        return lowBits + (1 << (code - 1));
    }

    /// <summary>Reads bits from a byte array, starting from the most significant bit of each byte (MSB-first).</summary>
    internal sealed class BitReader
    {
        private readonly ReadOnlyMemory<byte> _data;
        private int _bytePos;
        private int _bitPos;

        public BitReader(ReadOnlySpan<byte> data)
        {
            _data = data.ToArray();
        }

        public int ReadBit()
        {
            var span = _data.Span;
            if (_bytePos >= span.Length)
            {
                return 0; // past the end of the stream — a safe default, decoding is bounded by decompressedLength
            }

            int bit = (span[_bytePos] >> (7 - _bitPos)) & 1;
            _bitPos++;
            if (_bitPos == 8)
            {
                _bitPos = 0;
                _bytePos++;
            }

            return bit;
        }

        public int ReadBits(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++)
            {
                value = (value << 1) | ReadBit();
            }

            return value;
        }
    }

    /// <summary>
    /// A canonical Huffman tree, built from an array of code lengths (0 = code unused). Stored as
    /// a flat array: a non-negative value is the index of the left child (right = +1), a negative
    /// value is a leaf, and the real code is the bitwise NOT (~) of the stored value.
    /// Internal (not private) with <see cref="ExtractCodes"/> — so the test-only encoder in
    /// Yamaha.Psg.Formats.Tests can reuse EXACTLY THE SAME tree-building logic as the decoder,
    /// instead of a parallel (and potentially out-of-sync) implementation.
    /// </summary>
    internal sealed class HuffmanTree
    {
        private readonly int[] _tree;
        private int _allocated;
        private int _nextEntry;

        public HuffmanTree(int maxCodes)
        {
            _tree = new int[Math.Max(1, maxCodes) * 2];
        }

        public void SetSingle(int code)
        {
            _tree.AsSpan().Fill(~0);
            _tree[0] = ~code;
        }

        public void Build(byte[] codeLengths, int count)
        {
            _tree.AsSpan().Fill(~0);
            _allocated = 1;
            _nextEntry = 0;

            int codeLen = 0;
            bool remaining;
            do
            {
                ExpandQueue();
                codeLen++;
                remaining = AddCodesWithLength(codeLengths, count, codeLen);
            } while (remaining);
        }

        private void ExpandQueue()
        {
            int newNodes = (_allocated - _nextEntry) * 2;
            if (_allocated + newNodes > _tree.Length)
            {
                return;
            }

            int endOffset = _allocated;
            while (_nextEntry < endOffset)
            {
                _tree[_nextEntry] = _allocated;
                _allocated += 2;
                _nextEntry++;
            }
        }

        private int ReadNextEntry()
        {
            if (_nextEntry >= _allocated)
            {
                return 0;
            }

            return _nextEntry++;
        }

        private bool AddCodesWithLength(byte[] codeLengths, int count, int codeLen)
        {
            bool remaining = false;
            for (int i = 0; i < count; i++)
            {
                if (codeLengths[i] == codeLen)
                {
                    int node = ReadNextEntry();
                    _tree[node] = ~i;
                }
                else if (codeLengths[i] > codeLen)
                {
                    remaining = true;
                }
            }

            return remaining;
        }

        public int Decode(BitReader reader)
        {
            int node = _tree[0];
            while (node >= 0)
            {
                int bit = reader.ReadBit();
                node = _tree[node + bit];
            }

            return ~node;
        }

        /// <summary>
        /// Walks the already-built tree and returns, for each leaf symbol, its bit code (value +
        /// bit length). Only used by the test-only encoder (Yamaha.Psg.Formats.Tests) — the
        /// decoder itself only needs <see cref="Decode"/>.
        /// </summary>
        internal Dictionary<int, (int Bits, int Length)> ExtractCodes()
        {
            var result = new Dictionary<int, (int Bits, int Length)>();
            Traverse(_tree[0], 0, 0, result);
            return result;
        }

        private void Traverse(int node, int bits, int length, Dictionary<int, (int Bits, int Length)> result)
        {
            if (node < 0)
            {
                result[~node] = (bits, length);
                return;
            }

            Traverse(_tree[node], bits << 1, length + 1, result); // "0"
            Traverse(_tree[node + 1], (bits << 1) | 1, length + 1, result); // "1"
        }
    }
}
