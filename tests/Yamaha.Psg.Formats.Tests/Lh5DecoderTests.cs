using Yamaha.Psg.Formats.Vtx;

namespace Yamaha.Psg.Formats.Tests;

public class Lh5DecoderTests
{
    private static TestLh5Encoder.Command[] Literals(string s)
        => s.Select(c => (TestLh5Encoder.Command)new TestLh5Encoder.Literal((byte)c)).ToArray();

    [Fact]
    public void OnlyLiterals_MultipleDistinctValues_RoundTrips()
    {
        const string text = "AABBBCCCCDDDDDEEEEEEFFFFFFFGGGGGGGGHHHHHHHHH";
        var commands = Literals(text);

        var compressed = TestLh5Encoder.Encode(commands);
        var decoded = Lh5Decoder.Decode(compressed, text.Length);

        Assert.Equal(text, System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void SingleBackReference_RoundTrips()
    {
        // "ABCABC": ABC as literals, then Match(length:3, distance:3) repeats "ABC".
        var commands = new TestLh5Encoder.Command[]
        {
            new TestLh5Encoder.Literal((byte)'A'),
            new TestLh5Encoder.Literal((byte)'B'),
            new TestLh5Encoder.Literal((byte)'C'),
            new TestLh5Encoder.Match(Length: 3, Distance: 3),
        };

        var compressed = TestLh5Encoder.Encode(commands);
        var decoded = Lh5Decoder.Decode(compressed, 6);

        Assert.Equal("ABCABC", System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void OverlappingBackReference_DistanceShorterThanLength_RoundTrips()
    {
        // "A" + Match(length:9, distance:1) -> "AAAAAAAAAA" (10 letters): the classic
        // self-overlapping LZ77 copy, where the distance is shorter than the copy length.
        var commands = new TestLh5Encoder.Command[]
        {
            new TestLh5Encoder.Literal((byte)'A'),
            new TestLh5Encoder.Match(Length: 9, Distance: 1),
        };

        var compressed = TestLh5Encoder.Encode(commands);
        var decoded = Lh5Decoder.Decode(compressed, 10);

        Assert.Equal("AAAAAAAAAA", System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void MultipleMatchesAtVaryingOffsets_RoundTrips()
    {
        // Build "XY" + "ab" + a repeat of "XY" (distance=4) + a repeat of "ab" (distance=2) + a long
        // "cccccccccccccccc" (16 c's) as literals + a Match(distance=17) repeating the whole block
        // again, to exercise a larger offset category (needs several extra bits).
        var prefix = "XYabXYab";
        var longRun = new string('c', 16);
        string expected = prefix + longRun + longRun;

        var commands = new List<TestLh5Encoder.Command>();
        commands.Add(new TestLh5Encoder.Literal((byte)'X'));
        commands.Add(new TestLh5Encoder.Literal((byte)'Y'));
        commands.Add(new TestLh5Encoder.Literal((byte)'a'));
        commands.Add(new TestLh5Encoder.Literal((byte)'b'));
        commands.Add(new TestLh5Encoder.Match(Length: 4, Distance: 4)); // repeat of "XYab"
        foreach (char c in longRun)
        {
            commands.Add(new TestLh5Encoder.Literal((byte)c));
        }
        commands.Add(new TestLh5Encoder.Match(Length: 16, Distance: 16)); // repeat of the 16-'c' block

        var compressed = TestLh5Encoder.Encode(commands);
        var decoded = Lh5Decoder.Decode(compressed, expected.Length);

        Assert.Equal(expected, System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void EmptyInput_ProducesEmptyOutput()
    {
        var compressed = TestLh5Encoder.Encode([new TestLh5Encoder.Literal(0)]);
        // decompressedLength=0 -> the decoder should stop, effectively reading nothing.
        var decoded = Lh5Decoder.Decode(compressed, 0);

        Assert.Empty(decoded);
    }

    [Fact]
    public void AllByteValues_RoundTrip_ThroughLiteralsOnly()
    {
        var allBytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var commands = allBytes.Select(b => (TestLh5Encoder.Command)new TestLh5Encoder.Literal(b)).ToArray();

        var compressed = TestLh5Encoder.Encode(commands);
        var decoded = Lh5Decoder.Decode(compressed, allBytes.Length);

        Assert.Equal(allBytes, decoded);
    }
}
