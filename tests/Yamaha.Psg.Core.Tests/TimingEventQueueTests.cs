using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Timing;

namespace Yamaha.Psg.Core.Tests;

/// <summary>
/// Integration check: the SubFrameEventQueue + Ay8910 combination reproduces a digi-drum-style
/// volume change exactly on the given buffer cycle, not just at its boundaries.
/// </summary>
public class TimingEventQueueTests
{
    [Fact]
    public void ScheduledWrite_ChangesOutput_ExactlyAtItsCycleOffset_NotBeforeOrAfter()
    {
        var ay = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum);
        ay.WriteRegister(7, 0b0011_1001); // channel A: both tone and noise disabled -> gate always open
        ay.WriteRegister(8, 0x05); // starting volume

        const int jumpCycle = 137;
        var queue = new SubFrameEventQueue();
        queue.Load([new TimedRegisterWrite(jumpCycle, 8, 0x0F)]);

        const int bufferLength = 300;
        double[] levels = new double[bufferLength];
        for (int cycle = 0; cycle < bufferLength; cycle++)
        {
            foreach (var write in queue.DrainDue(cycle))
            {
                ay.WriteRegister(write.Register, write.Value);
            }

            ay.Tick();
            levels[cycle] = ay.SampleChannelLevels().A;
        }

        double before = DacTables.AyEnvelopeLevels[0x05];
        double after = DacTables.AyEnvelopeLevels[0x0F];

        for (int cycle = 0; cycle < jumpCycle; cycle++)
        {
            Assert.Equal(before, levels[cycle]);
        }

        for (int cycle = jumpCycle; cycle < bufferLength; cycle++)
        {
            Assert.Equal(after, levels[cycle]);
        }
    }

    [Fact]
    public void MultipleScheduledWrites_EachAppliesAtItsOwnCycle_SimulatingRapidDigiDrumSteps()
    {
        var ay = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum);
        ay.WriteRegister(7, 0b0011_1001); // channel A: gate always open

        // Simulate a crude "digital" burst: volume jumps 0 -> 5 -> 10 -> 15 at equal intervals.
        var queue = new SubFrameEventQueue();
        queue.Load(
        [
            new TimedRegisterWrite(0, 8, 0x00),
            new TimedRegisterWrite(50, 8, 0x05),
            new TimedRegisterWrite(100, 8, 0x0A),
            new TimedRegisterWrite(150, 8, 0x0F),
        ]);

        const int bufferLength = 200;
        var levels = new double[bufferLength];
        for (int cycle = 0; cycle < bufferLength; cycle++)
        {
            foreach (var write in queue.DrainDue(cycle))
            {
                ay.WriteRegister(write.Register, write.Value);
            }

            ay.Tick();
            levels[cycle] = ay.SampleChannelLevels().A;
        }

        Assert.Equal(DacTables.AyEnvelopeLevels[0x00], levels[0]);
        Assert.Equal(DacTables.AyEnvelopeLevels[0x00], levels[49]);
        Assert.Equal(DacTables.AyEnvelopeLevels[0x05], levels[50]);
        Assert.Equal(DacTables.AyEnvelopeLevels[0x05], levels[99]);
        Assert.Equal(DacTables.AyEnvelopeLevels[0x0A], levels[100]);
        Assert.Equal(DacTables.AyEnvelopeLevels[0x0A], levels[149]);
        Assert.Equal(DacTables.AyEnvelopeLevels[0x0F], levels[150]);
        Assert.Equal(DacTables.AyEnvelopeLevels[0x0F], levels[bufferLength - 1]);
    }
}
