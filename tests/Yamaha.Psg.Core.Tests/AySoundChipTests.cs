using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;
using Yamaha.Psg.Core.Timing;

namespace Yamaha.Psg.Core.Tests;

public class AySoundChipTests
{
    [Fact]
    public void Constructor_Custom_WithoutPanValues_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Custom));
    }

    [Fact]
    public void Constructor_Custom_WithPanValues_Succeeds()
    {
        var chip = new AySoundChip(
            ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Custom,
            new ChannelPan(1f, 0f), new ChannelPan(0.5f, 0.5f), new ChannelPan(0f, 1f));

        Assert.Equal(ChipVariant.Ay_3_8910, chip.Variant);
    }

    [Fact]
    public void WriteReadRegister_PassThroughToChip()
    {
        var chip = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100);

        chip.WriteRegister(13, 0xFF); // R13 is masked down to 4 bits inside Ay8910

        Assert.Equal(0x0F, chip.ReadRegister(13));
    }

    [Fact]
    public void RenderSamples_FillsExactlyRequestedFrameCount()
    {
        var chip = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100);
        chip.WriteRegister(0, 200);
        chip.WriteRegister(7, 0b0011_1110); // A: tone on, noise off
        chip.WriteRegister(8, 0x0F);

        var buffer = new short[200 * 2];
        int written = chip.RenderSamples(buffer, 200);

        Assert.Equal(200, written);
    }

    [Fact]
    public void RenderSamples_TooSmallBuffer_Throws()
    {
        var chip = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100);
        var buffer = new short[10];

        Assert.Throws<ArgumentException>(() => chip.RenderSamples(buffer, 100));
    }

    [Fact]
    public void RenderSamplesMono_FillsExactlyRequestedFrameCount_AndAveragesThreeChannels()
    {
        var chip = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100);
        // All three channels: tone and noise disabled -> gate always open, a pure constant from the DAC.
        chip.WriteRegister(7, 0x3F);
        chip.WriteRegister(8, 0x0F); // A: max volume
        chip.WriteRegister(9, 0x00); // B: silent
        chip.WriteRegister(10, 0x00); // C: silent

        var buffer = new short[100];
        int written = chip.RenderSamplesMono(buffer, 100);

        Assert.Equal(100, written);

        double expectedMono = DacTables.AyEnvelopeLevels[0x0F] / 3.0;
        short expected = StereoMixer.ToShort(expectedMono);

        // Well past the start (after the filter has settled) the value should be stable.
        for (int i = 50; i < 100; i++)
        {
            Assert.Equal(expected, buffer[i]);
        }
    }

    [Fact]
    public void RenderSamples_ScheduledWrite_ChangesOutput_WellBeforeAndWellAfterItsCycle()
    {
        var chip = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        chip.WriteRegister(7, 0b0011_1001); // channel A: tone and noise disabled -> gate always open
        chip.WriteRegister(8, 0x05); // starting volume

        const int frameCount = 2000;
        int approxTicksPerFrame = chip.ClockHz / chip.OutputSampleRate;
        int jumpCycle = (frameCount / 2) * approxTicksPerFrame;

        var writes = new[] { new TimedRegisterWrite(jumpCycle, 8, 0x0F) };
        var buffer = new short[frameCount * 2];

        int written = chip.RenderSamples(buffer, frameCount, writes);
        Assert.Equal(frameCount, written);

        short expectedBefore = StereoMixer.ToShort(DacTables.AyEnvelopeLevels[0x05]);
        short expectedAfter = StereoMixer.ToShort(DacTables.AyEnvelopeLevels[0x0F]);

        // Skip the very first frames: the FIR filter starts from a zeroed ring buffer, so it has
        // a short fill-up transient (a few frames) before a constant input gives a constant
        // (steady-state) output.
        for (int i = 10; i < 100; i++)
        {
            Assert.Equal(expectedBefore, buffer[i * 2]);
            Assert.Equal(expectedBefore, buffer[(i * 2) + 1]);
        }

        for (int i = frameCount - 100; i < frameCount; i++)
        {
            Assert.Equal(expectedAfter, buffer[i * 2]);
            Assert.Equal(expectedAfter, buffer[(i * 2) + 1]);
        }
    }

    [Fact]
    public void Reset_ClearsRegisterState()
    {
        var chip = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100);
        chip.WriteRegister(8, 0x0F);
        Assert.Equal(0x0F, chip.ReadRegister(8));

        chip.Reset();

        Assert.Equal(0, chip.ReadRegister(8));
    }
}
