using Yamaha.Psg.Formats.Pt3;

namespace Yamaha.Psg.Formats.Tests;

public class Pt3TablesTests
{
    // Values are the real "ASM or PSC" (selector 2, version >= 4) table, hardcoded from a real
    // player's source (see Pt3NoteTables.cs remarks) - not the equal-tempered formula this project
    // originally derived, which matched 93/96 entries but rounded the top 3 (93-95) one unit high.
    [Theory]
    [InlineData(0, 3344)]  // C-1
    [InlineData(45, 249)]  // A-4 (440 Hz reference)
    [InlineData(57, 124)]  // A-5 — one octave above A-4 halves the period
    [InlineData(95, 13)]   // B-8, top of the table - confirmed against a real Vortex Tracker PSG
                            // export of a track that actually uses this note (Digital Espresso)
    public void Period_MatchesEqualTemperedDerivation(int noteIndex, ushort expectedPeriod)
    {
        Assert.Equal(expectedPeriod, Pt3NoteTables.Period(noteIndex));
    }

    [Fact]
    public void Period_IsMonotonicallyNonIncreasing_AcrossTheWholeTable()
    {
        for (int i = 1; i < Pt3NoteTables.NoteCount; i++)
        {
            Assert.True(Pt3NoteTables.Period(i) <= Pt3NoteTables.Period(i - 1));
        }
    }

    // Real lookup table, not the milestone-11.1 linear-scaling guess (round(amplitude*volume/15)) -
    // confirmed by diffing our interpreter's output against a real Vortex Tracker PSG export (see
    // docs/PT3_TABLES.md). Version 7 (">= 3.5") selects the newer table.
    [Theory]
    [InlineData(15, 15, 7, 15)]
    [InlineData(0, 15, 7, 0)]
    [InlineData(15, 0, 7, 0)]
    [InlineData(15, 8, 7, 8)]
    [InlineData(10, 10, 7, 7)]
    public void Combine_UsesTheRealLookupTable(int amplitude, int volume, int version, int expected)
    {
        Assert.Equal(expected, Pt3VolumeTables.Combine(amplitude, volume, version));
    }

    [Fact]
    public void Combine_SelectsTableByVersion()
    {
        // Version <= 4 ("<= 3.4") and version 7 ("3.7") give genuinely different combined levels here.
        Assert.Equal(0, Pt3VolumeTables.Combine(amplitude: 4, volume: 2, version: 4));
        Assert.Equal(1, Pt3VolumeTables.Combine(amplitude: 4, volume: 2, version: 7));
    }
}
