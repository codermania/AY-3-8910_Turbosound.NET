using Yamaha.Psg.Core.Chip;

namespace Yamaha.Psg.Core.Tests;

public class Ay8910Tests
{
    [Fact]
    public void Constructor_StoresVariantAndClock()
    {
        var ay = new Ay8910(ChipVariant.Ym2149, PsgClockPresets.AtariSt);

        Assert.Equal(ChipVariant.Ym2149, ay.Variant);
        Assert.Equal(PsgClockPresets.AtariSt, ay.ClockHz);
    }

    [Fact]
    public void WriteReadRegister_RoundTripsThroughMasking()
    {
        var ay = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum);

        ay.WriteRegister(13, 0xFF); // R13 is masked down to 4 bits

        Assert.Equal(0x0F, ay.ReadRegister(13));
    }

    [Fact]
    public void SampleChannelGates_ToneOnlyChannel_TogglesAtConfiguredPeriod()
    {
        var ay = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum);
        const int period = 5;
        ay.WriteRegister(0, period); // tone A fine
        ay.WriteRegister(1, 0);      // tone A coarse
        // Mixer: channel A - tone enabled (bit0=0), noise disabled (bit3=1); B/C fully disabled.
        ay.WriteRegister(7, 0b0011_1110);

        int toggles = 0;
        bool previous = ay.SampleChannelGates().A;
        for (int i = 0; i < 16 * period * 20; i++)
        {
            ay.Tick();
            bool current = ay.SampleChannelGates().A;
            if (current != previous)
            {
                toggles++;
                previous = current;
            }
        }

        Assert.InRange(toggles, 2 * 20 - 1, 2 * 20 + 1);
    }

    [Fact]
    public void SampleChannelGates_ToneAndNoiseBothDisabled_IsConstantHigh_DigiDrumPassthrough()
    {
        var ay = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum);
        // Channel A: both tone and noise disabled (bits 0 and 3 set) -> gate is always true,
        // regardless of generator state. This is exactly what the digi-drum trick relies on.
        ay.WriteRegister(7, 0b0011_1001);
        ay.WriteRegister(0, 3); // arbitrary tone period, must not affect the gate

        for (int i = 0; i < 1000; i++)
        {
            ay.Tick();
            Assert.True(ay.SampleChannelGates().A);
        }
    }

    [Fact]
    public void WriteRegister13_AlwaysRestartsEnvelope_EvenWithSameValue()
    {
        var ay = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum);
        ay.WriteRegister(11, 1); // envelope period fine = 1
        ay.WriteRegister(12, 0);
        ay.WriteRegister(13, 0x0C); // repeating attack sawtooth, starts at level 0

        for (int i = 0; i < 8 * 15; i++) // somewhere in the middle of the first pass
        {
            ay.Tick();
        }
        Assert.NotEqual(0, ay.EnvelopeLevel);

        ay.WriteRegister(13, 0x0C); // same shape — but must still restart the envelope

        Assert.Equal(0, ay.EnvelopeLevel); // attack=1 => instantly resets to the starting level 0
    }

    [Fact]
    public void SampleChannelLevels_GateClosed_IsZero()
    {
        var ay = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum);
        // Channel A: tone enabled, period is huge (won't toggle within the test) -> Output=false -> gate closed.
        ay.WriteRegister(0, 0xFF);
        ay.WriteRegister(1, 0x0F);
        ay.WriteRegister(7, 0b0011_1110); // A: tone on, noise off; B/C fully off
        ay.WriteRegister(8, 0x0F); // max volume — shouldn't matter, gate is closed

        ay.Tick();

        Assert.Equal(0.0, ay.SampleChannelLevels().A);
    }

    [Fact]
    public void SampleChannelLevels_FixedVolumeChannel_MatchesDacTable()
    {
        var ay = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum);
        // Channel A: both tone and noise disabled -> gate always open (see the digi-drum passthrough test above).
        ay.WriteRegister(7, 0b0011_1001);
        ay.WriteRegister(8, 0x0A); // fixed volume 10, envelope-enable bit (0x10) not set

        ay.Tick();

        Assert.Equal(DacTables.AyEnvelopeLevels[10], ay.SampleChannelLevels().A);
    }

    [Fact]
    public void SampleChannelLevels_EnvelopeChannel_TracksEnvelopeLevelThroughDacTable()
    {
        var ay = new Ay8910(ChipVariant.Ym2149, PsgClockPresets.ZxSpectrum);
        ay.WriteRegister(7, 0b0011_1001); // channel A: gate always open
        ay.WriteRegister(8, 0x10); // envelope-enable, no fixed level
        ay.WriteRegister(11, 1);
        ay.WriteRegister(13, 0x0C); // repeating attack — level will rise from 0

        ay.Tick();
        double levelAtStart = ay.SampleChannelLevels().A;
        Assert.Equal(DacTables.YmEnvelopeLevels[ay.EnvelopeLevel], levelAtStart);

        for (int i = 0; i < 8 * 20; i++)
        {
            ay.Tick();
        }

        double levelLater = ay.SampleChannelLevels().A;
        Assert.Equal(DacTables.YmEnvelopeLevels[ay.EnvelopeLevel], levelLater);
        Assert.True(levelLater > levelAtStart); // the attack ramp is rising
    }

    [Fact]
    public void VolumeScale_Default_IsBitIdenticalToExplicitOne()
    {
        var implicitScale = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum);
        var explicitScale = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, volumeScale: 1.0);

        foreach (var ay in new[] { implicitScale, explicitScale })
        {
            ay.WriteRegister(7, 0b0011_1001); // gate always open
            ay.WriteRegister(8, 0x0F);
            ay.Tick();
        }

        Assert.Equal(DacTables.AyEnvelopeLevels[15], implicitScale.SampleChannelLevels().A);
        Assert.Equal(implicitScale.SampleChannelLevels().A, explicitScale.SampleChannelLevels().A);
    }

    [Fact]
    public void VolumeScale_ScalesFixedVolumeLevel_ByExactFactor()
    {
        var ay = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, volumeScale: 0.5);
        ay.WriteRegister(7, 0b0011_1001); // gate always open
        ay.WriteRegister(8, 0x0A); // fixed volume 10, no envelope

        ay.Tick();

        Assert.Equal(DacTables.AyEnvelopeLevels[10] * 0.5, ay.SampleChannelLevels().A, precision: 12);
    }

    [Fact]
    public void VolumeScale_AlsoScalesEnvelopeLevel_NotJustFixedVolume()
    {
        // YM2149 specifically: FixedVolumeLevels isn't literally the same array as EnvelopeLevels
        // (see DacTables.cs) — the envelope path must still get the same protection.
        var ay = new Ay8910(ChipVariant.Ym2149, PsgClockPresets.ZxSpectrum, volumeScale: 0.25);
        ay.WriteRegister(7, 0b0011_1001); // gate always open
        ay.WriteRegister(8, 0x10); // envelope-enable
        ay.WriteRegister(11, 1);
        ay.WriteRegister(13, 0x0C);

        ay.Tick();

        Assert.Equal(DacTables.YmEnvelopeLevels[ay.EnvelopeLevel] * 0.25, ay.SampleChannelLevels().A, precision: 12);
    }

    [Fact]
    public void NaiveMonoPcm_SmokeTest_ProducesBoundedNonSilentSamples()
    {
        // Milestone-3 smoke test: naive decimation (no band-limiting filter yet — that's milestone
        // 4), just to make sure everything from registers to short[] PCM fits together.
        var ay = new Ay8910(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum);
        ay.WriteRegister(0, 100); // tone A period
        ay.WriteRegister(7, 0b0011_1110); // A: tone on, noise off
        ay.WriteRegister(8, 0x0F); // max fixed volume

        const int outputSampleRate = 44_100;
        int decimation = Math.Max(1, ay.ClockHz / outputSampleRate);

        var pcm = new short[1000];
        for (int i = 0; i < pcm.Length; i++)
        {
            for (int j = 0; j < decimation; j++)
            {
                ay.Tick();
            }

            var (a, b, c) = ay.SampleChannelLevels();
            double mixed = (a + b + c) / 3.0;
            pcm[i] = (short)(mixed * short.MaxValue);
        }

        Assert.Contains(pcm, sample => sample != 0);
        Assert.All(pcm, sample => Assert.InRange(sample, (short)0, short.MaxValue));
    }
}
