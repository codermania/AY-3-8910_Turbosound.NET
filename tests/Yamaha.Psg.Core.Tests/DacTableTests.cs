using Yamaha.Psg.Core.Chip;

namespace Yamaha.Psg.Core.Tests;

public class DacTableTests
{
    [Fact]
    public void EnvelopeTables_HaveNativeResolutionPerVariant()
    {
        Assert.Equal(16, DacTables.AyEnvelopeLevels.Length);
        Assert.Equal(32, DacTables.YmEnvelopeLevels.Length);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void EnvelopeLevels_StartAtZero_EndAtOne_AndAreMonotonic(ChipVariant variant)
    {
        var table = DacTables.EnvelopeLevels(variant);

        Assert.Equal(0.0, table[0]);
        Assert.Equal(1.0, table[^1]);

        for (int i = 1; i < table.Length; i++)
        {
            Assert.True(table[i] >= table[i - 1], $"Table should be non-decreasing at index {i}");
        }
    }

    [Fact]
    public void AyAndYmEnvelopeTables_AreDistinctData()
    {
        // The differing resolution (16 vs 32) alone proves they differ, but let's also check the
        // curve shape: YM is closer to logarithmic, so the midpoint shouldn't match when compared
        // at the same normalized position in the 0..1 range.
        double ayMidpoint = DacTables.AyEnvelopeLevels[8]; // midpoint of the 16-level table
        double ymMidpoint = DacTables.YmEnvelopeLevels[16]; // midpoint of the 32-level table (same relative step)

        Assert.NotEqual(ayMidpoint, ymMidpoint);
    }

    [Fact]
    public void FixedVolumeLevels_Ay_IsSameArrayAsEnvelopeLevels()
    {
        // On the AY-3-8910, fixed volume and the envelope share the same 16-level DAC.
        Assert.Same(DacTables.AyEnvelopeLevels, DacTables.FixedVolumeLevels(ChipVariant.Ay_3_8910));
    }

    [Fact]
    public void FixedVolumeLevels_Ym_IsDerivedFromOddIndicesOfEnvelopeTable()
    {
        var fixedLevels = DacTables.FixedVolumeLevels(ChipVariant.Ym2149);

        Assert.Equal(16, fixedLevels.Length);
        Assert.Equal(0.0, fixedLevels[0]);
        Assert.Equal(1.0, fixedLevels[15]);
        for (int n = 0; n < 16; n++)
        {
            Assert.Equal(DacTables.YmEnvelopeLevels[(n * 2) + 1], fixedLevels[n]);
        }
    }

    [Fact]
    public void FixedVolumeLevels_AyAndYm_AreDifferentTables()
    {
        var ay = DacTables.FixedVolumeLevels(ChipVariant.Ay_3_8910);
        var ym = DacTables.FixedVolumeLevels(ChipVariant.Ym2149);

        Assert.NotEqual(ay, ym);
    }
}
