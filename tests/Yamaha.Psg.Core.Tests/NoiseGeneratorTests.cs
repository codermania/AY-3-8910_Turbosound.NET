using Yamaha.Psg.Core.Chip;

namespace Yamaha.Psg.Core.Tests;

public class NoiseGeneratorTests
{
    [Fact]
    public void Shift_ProducesMaximalLengthSequence_ReturningToSeedAfterFullPeriod()
    {
        const long fullPeriod = 131_071; // 2^17 - 1, the documented maximal LFSR sequence length
        var noise = new NoiseGenerator();
        noise.SetPeriod(1);
        uint seed = noise.RawState;

        long halfwayState = 0;
        while (noise.ShiftCount < fullPeriod)
        {
            noise.Tick();
            if (noise.ShiftCount == fullPeriod / 2)
            {
                halfwayState = noise.RawState;
            }
        }

        Assert.Equal(fullPeriod, noise.ShiftCount);
        Assert.Equal(seed, noise.RawState);
        Assert.NotEqual((long)seed, halfwayState); // not a trivially short period
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Tick_ShiftsAtExpectedRate(int period)
    {
        // A /16 prescaler per period tick (vs /8 for tone) — the documented "divide by 2"
        // quirk of the noise generator relative to the tone generator.
        const int shifts = 100;
        var noise = new NoiseGenerator();
        noise.SetPeriod(period);

        for (int i = 0; i < 16 * period * shifts; i++)
        {
            noise.Tick();
        }

        Assert.InRange(noise.ShiftCount, shifts - 1, shifts + 1);
    }

    [Fact]
    public void SetPeriod_Zero_IsTreatedAsOne()
    {
        var withZero = new NoiseGenerator();
        withZero.SetPeriod(0);

        var withOne = new NoiseGenerator();
        withOne.SetPeriod(1);

        for (int i = 0; i < 16 * 20; i++)
        {
            withZero.Tick();
            withOne.Tick();
        }

        Assert.Equal(withOne.ShiftCount, withZero.ShiftCount);
        Assert.Equal(withOne.RawState, withZero.RawState);
    }
}
