using Yamaha.Psg.Core.Chip;

namespace Yamaha.Psg.Core.Tests;

public class ToneGeneratorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(255)]
    public void Tick_TogglesOutputAtExpectedRate(int period)
    {
        // A full square-wave cycle (frequency clock/(16*period)) consists of 2 toggles per
        // 16*period chip ticks. Run exactly 50 periods and check the toggle count.
        const int cycles = 50;
        var tone = new ToneGenerator();
        tone.SetPeriod(period);

        int toggles = 0;
        bool previous = tone.Output;
        for (int i = 0; i < 16 * period * cycles; i++)
        {
            tone.Tick();
            if (tone.Output != previous)
            {
                toggles++;
                previous = tone.Output;
            }
        }

        Assert.InRange(toggles, 2 * cycles - 1, 2 * cycles + 1);
    }

    [Fact]
    public void SetPeriod_Zero_IsTreatedAsOne()
    {
        var withZero = new ToneGenerator();
        withZero.SetPeriod(0);

        var withOne = new ToneGenerator();
        withOne.SetPeriod(1);

        for (int i = 0; i < 16 * 20; i++)
        {
            withZero.Tick();
            withOne.Tick();
            Assert.Equal(withOne.Output, withZero.Output);
        }
    }
}
