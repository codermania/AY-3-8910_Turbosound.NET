using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;
using Yamaha.Psg.Core.Timing;

namespace Yamaha.Psg.Core.Tests;

public class MultiChipAySoundChipTests
{
    [Fact]
    public void ChipCountOne_BehavesLikeAySoundChip()
    {
        var multi = new MultiChipAySoundChip(1, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        var single = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);

        multi.WriteRegister(0, 7, 0x3F);
        multi.WriteRegister(0, 8, 0x0A);
        single.WriteRegister(7, 0x3F);
        single.WriteRegister(8, 0x0A);

        var multiBuffer = new short[200 * 2];
        var singleBuffer = new short[200 * 2];
        multi.RenderSamples(multiBuffer, 200);
        single.RenderSamples(singleBuffer, 200);

        Assert.Equal(singleBuffer, multiBuffer);
    }

    [Fact]
    public void RegisterWrites_AreIndependentPerChipIndex()
    {
        var multi = new MultiChipAySoundChip(3, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100);

        multi.WriteRegister(0, 8, 0x01);
        multi.WriteRegister(1, 8, 0x02);
        multi.WriteRegister(2, 8, 0x03);

        Assert.Equal(0x01, multi.ReadRegister(0, 8));
        Assert.Equal(0x02, multi.ReadRegister(1, 8));
        Assert.Equal(0x03, multi.ReadRegister(2, 8));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void OutOfRangeChipIndex_Throws(int badIndex)
    {
        var multi = new MultiChipAySoundChip(3, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100);

        Assert.Throws<ArgumentOutOfRangeException>(() => multi.WriteRegister(badIndex, 8, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => multi.ReadRegister(badIndex, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => multi.SetChipPanning(badIndex, PanningPreset.Mono));
    }

    [Fact]
    public void MixedVariantChips_ConfiguredIndependently_ViaChipConfigConstructor()
    {
        var multi = new MultiChipAySoundChip(
            44_100,
            PanningPreset.Mono,
            new ChipConfig(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum),
            new ChipConfig(ChipVariant.Ym2149, PsgClockPresets.ZxSpectrum));

        Assert.Equal(2, multi.ChipCount);

        multi.WriteRegister(0, 7, 0x3F);
        multi.WriteRegister(0, 8, 0x08);
        multi.WriteRegister(1, 7, 0x3F);
        multi.WriteRegister(1, 8, 0x08);

        var buffer = new short[400 * 2];
        multi.RenderSamples(buffer, 400);

        // Reference: chip 0 rendered as a standalone AY, chip 1 as a standalone YM with the same registers.
        var referenceAy = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        referenceAy.WriteRegister(7, 0x3F);
        referenceAy.WriteRegister(8, 0x08);
        var ayBuffer = new short[400 * 2];
        referenceAy.RenderSamples(ayBuffer, 400);

        var referenceYm = new AySoundChip(ChipVariant.Ym2149, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        referenceYm.WriteRegister(7, 0x3F);
        referenceYm.WriteRegister(8, 0x08);
        var ymBuffer = new short[400 * 2];
        referenceYm.RenderSamples(ymBuffer, 400);

        // AY and YM must sound different on identical registers on their own — otherwise this test proves nothing.
        Assert.NotEqual(ayBuffer[300 * 2], ymBuffer[300 * 2]);

        for (int i = 50; i < 400; i++)
        {
            short expected = StereoMixer.ClampToShortRange(ayBuffer[i * 2] + ymBuffer[i * 2]);
            Assert.Equal(expected, buffer[i * 2]);
        }
    }

    [Fact]
    public void NineChannels_ThreeChipsOfThree_ChipCountReportsCorrectly()
    {
        var multi = new MultiChipAySoundChip(3, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100);

        Assert.Equal(3, multi.ChipCount); // 3 chips * 3 channels = 9 voices
    }

    [Fact]
    public void RenderSamples_SumsAllChipsOutput()
    {
        // Volume is deliberately low so no individual chip clips on its own, and the sum of two
        // identical chips can be checked precisely (doubling).
        var single = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        single.WriteRegister(7, 0x3F);
        single.WriteRegister(8, 0x05);
        var singleBuffer = new short[200 * 2];
        single.RenderSamples(singleBuffer, 200);

        var multi = new MultiChipAySoundChip(2, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        multi.WriteRegister(0, 7, 0x3F);
        multi.WriteRegister(0, 8, 0x05);
        multi.WriteRegister(1, 7, 0x3F);
        multi.WriteRegister(1, 8, 0x05);
        var multiBuffer = new short[200 * 2];
        multi.RenderSamples(multiBuffer, 200);

        // Steady-state region (after the FIR transient) — the sum should be roughly double.
        for (int i = 50; i < 200; i++)
        {
            short expectedDoubled = StereoMixer.ClampToShortRange(singleBuffer[i * 2] * 2.0);
            Assert.Equal(expectedDoubled, multiBuffer[i * 2]);
        }
    }

    [Fact]
    public void ScheduledWritesPerChip_OnlyAffectTheTargetedChip()
    {
        // Reference values come from the already-tested single-chip AySoundChip — this way the
        // test checks chip-index isolation specifically, instead of re-deriving DAC tables by hand.
        var referenceChip0 = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        referenceChip0.WriteRegister(7, 0x3F);
        referenceChip0.WriteRegister(8, 0x0F); // after applying the scheduled write

        var referenceChip1 = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        referenceChip1.WriteRegister(7, 0x3F);
        referenceChip1.WriteRegister(8, 0x05); // unchanged

        const int frameCount = 1000;
        var reference0Buffer = new short[frameCount * 2];
        var reference1Buffer = new short[frameCount * 2];
        referenceChip0.RenderSamples(reference0Buffer, frameCount);
        referenceChip1.RenderSamples(reference1Buffer, frameCount);

        var multi = new MultiChipAySoundChip(2, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        multi.WriteRegister(0, 7, 0x3F);
        multi.WriteRegister(0, 8, 0x05); // will be overwritten by the scheduled write on cycle 0
        multi.WriteRegister(1, 7, 0x3F);
        multi.WriteRegister(1, 8, 0x05); // unchanged — should match referenceChip1

        var scheduled = new IReadOnlyList<TimedRegisterWrite>?[]
        {
            [new TimedRegisterWrite(0, 8, 0x0F)], // chip 0: volume maxed out from the very start
            null, // chip 1: unchanged
        };

        var buffer = new short[frameCount * 2];
        multi.RenderSamples(buffer, frameCount, scheduled);

        for (int i = 50; i < frameCount; i++)
        {
            short expected = StereoMixer.ClampToShortRange(reference0Buffer[i * 2] + reference1Buffer[i * 2]);
            Assert.Equal(expected, buffer[i * 2]);
        }
    }

    [Fact]
    public void SetChannelPanning_ConfiguresEachChannelWithinChipIndependently()
    {
        var multi = new MultiChipAySoundChip(1, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100);

        multi.SetChannelPanning(0, new ChannelPan(1f, 0f), new ChannelPan(0f, 0f), new ChannelPan(0f, 1f));
        multi.WriteRegister(0, 7, 0x3F);
        multi.WriteRegister(0, 8, 0x0F); // channel A: max, should go only to the left
        multi.WriteRegister(0, 9, 0x00);
        multi.WriteRegister(0, 10, 0x00);

        var buffer = new short[200 * 2];
        multi.RenderSamples(buffer, 200);

        for (int i = 50; i < 200; i++)
        {
            Assert.True(buffer[i * 2] > 0);       // the left channel received signal
            Assert.Equal(0, buffer[(i * 2) + 1]);  // the right one didn't
        }
    }

    [Fact]
    public void Reset_ClearsAllChipsRegisters()
    {
        var multi = new MultiChipAySoundChip(2, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100);
        multi.WriteRegister(0, 8, 0x0F);
        multi.WriteRegister(1, 8, 0x0A);

        multi.Reset();

        Assert.Equal(0, multi.ReadRegister(0, 8));
        Assert.Equal(0, multi.ReadRegister(1, 8));
    }

    [Fact]
    public void Constructor_EmptyChipConfigs_Throws()
    {
        Assert.Throws<ArgumentException>(() => new MultiChipAySoundChip(44_100, PanningPreset.Abc));
    }

    [Fact]
    public void MixLimiter_DefaultsToHardClip_UnchangedFromPriorBehavior()
    {
        // Two chips, each at max volume on a single channel with Mono panning: AyFixedVolumeLevels[15]
        // is exactly 1.0, so each chip's own contribution is already exactly short.MaxValue on its
        // own — summing two of those is guaranteed to overflow, deterministically, with no dependence
        // on DAC curve specifics.
        var defaultCtor = new MultiChipAySoundChip(2, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        var explicitHardClip = new MultiChipAySoundChip(2, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono, MixLimiter.HardClip);

        foreach (var multi in new[] { defaultCtor, explicitHardClip })
        {
            multi.WriteRegister(0, 7, 0x3F);
            multi.WriteRegister(0, 8, 0x0F);
            multi.WriteRegister(1, 7, 0x3F);
            multi.WriteRegister(1, 8, 0x0F);
        }

        var defaultBuffer = new short[200 * 2];
        var explicitBuffer = new short[200 * 2];
        defaultCtor.RenderSamples(defaultBuffer, 200);
        explicitHardClip.RenderSamples(explicitBuffer, 200);

        Assert.Equal(explicitBuffer, defaultBuffer);
        for (int i = 50; i < 200; i++)
        {
            Assert.Equal(short.MaxValue, defaultBuffer[i * 2]); // truncated exactly at the boundary
        }
    }

    [Fact]
    public void MixLimiter_SoftLimit_CompressesInsteadOfTruncating_WhenChipsSaturateTogether()
    {
        // Chip 0 at max volume (level 1.0, i.e. already exactly short.MaxValue on its own) plus chip
        // 1 at a modest volume: the sum overshoots short.MaxValue only modestly (~34.3k), same
        // regime as the direct StereoMixer.SoftLimitToShortRange unit tests. A much larger overshoot
        // (e.g. two chips both at max, ~65.5k) pushes tanh's argument far enough that it numerically
        // saturates to 1.0 once quantized to an integer sample, making hard-clip and soft-limit
        // indistinguishable — this deliberately stays inside the range where they visibly differ.
        var hardClip = new MultiChipAySoundChip(2, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono, MixLimiter.HardClip);
        var softLimit = new MultiChipAySoundChip(2, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono, MixLimiter.SoftLimit);

        foreach (var multi in new[] { hardClip, softLimit })
        {
            multi.WriteRegister(0, 7, 0x3F);
            multi.WriteRegister(0, 8, 0x0F); // chip 0: max volume
            multi.WriteRegister(1, 7, 0x3F);
            multi.WriteRegister(1, 8, 0x05); // chip 1: modest volume — just enough combined overshoot
        }

        var hardBuffer = new short[200 * 2];
        var softBuffer = new short[200 * 2];
        hardClip.RenderSamples(hardBuffer, 200);
        softLimit.RenderSamples(softBuffer, 200);

        for (int i = 50; i < 200; i++)
        {
            Assert.Equal(short.MaxValue, hardBuffer[i * 2]); // hard-clip truncates exactly at the boundary
            Assert.True(softBuffer[i * 2] < short.MaxValue, "Soft limiting should compress towards full scale rather than truncate at it.");
            Assert.True(softBuffer[i * 2] > 0);
        }
    }

    [Fact]
    public void VolumeScaling_DivideByChipCount_KeepsSubMaxVolumeEnsembleBelowFullScale_UnlikeUnscaled()
    {
        const int chipCount = 4;
        var unscaled = new MultiChipAySoundChip(chipCount, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono, MixLimiter.HardClip, VolumeScaling.None);
        var scaled = new MultiChipAySoundChip(chipCount, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono, MixLimiter.HardClip, VolumeScaling.DivideByChipCount);

        for (int i = 0; i < chipCount; i++)
        {
            unscaled.WriteRegister(i, 7, 0x3F); // gate always open
            unscaled.WriteRegister(i, 8, 0x0A); // loud, but not maxed, fixed volume
            scaled.WriteRegister(i, 7, 0x3F);
            scaled.WriteRegister(i, 8, 0x0A);
        }

        var unscaledBuffer = new short[200 * 2];
        var scaledBuffer = new short[200 * 2];
        unscaled.RenderSamples(unscaledBuffer, 200);
        scaled.RenderSamples(scaledBuffer, 200);

        // Reference: one lone, unscaled chip at the same volume — DivideByChipCount is designed so
        // that N identical chips scaled by 1/N sum back to roughly this same single-chip loudness.
        var referenceSingle = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        referenceSingle.WriteRegister(7, 0x3F);
        referenceSingle.WriteRegister(8, 0x0A);
        var referenceBuffer = new short[200 * 2];
        referenceSingle.RenderSamples(referenceBuffer, 200);

        for (int i = 50; i < 200; i++)
        {
            Assert.Equal(short.MaxValue, unscaledBuffer[i * 2]); // 4 loud chips, unscaled -> clips
            Assert.True(scaledBuffer[i * 2] < short.MaxValue);   // same registers, scaled -> real headroom, no clip

            // Small tolerance: each chip's own DAC output rounds to a short independently before
            // the ensemble sums them (see TODO.md), so 4 independent roundings can differ from the
            // single-chip reference by a couple of PCM units — not exact equality.
            Assert.InRange(scaledBuffer[i * 2], (short)(referenceBuffer[i * 2] - 3), (short)(referenceBuffer[i * 2] + 3));
        }
    }

    [Fact]
    public void VolumeScaling_DivideBySqrtChipCount_IsLouderThanDivideByChipCount_ForMultipleChips()
    {
        const int chipCount = 4;
        var byCount = new MultiChipAySoundChip(chipCount, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono, MixLimiter.HardClip, VolumeScaling.DivideByChipCount);
        var bySqrt = new MultiChipAySoundChip(chipCount, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono, MixLimiter.HardClip, VolumeScaling.DivideBySqrtChipCount);

        for (int i = 0; i < chipCount; i++)
        {
            byCount.WriteRegister(i, 7, 0x3F);
            byCount.WriteRegister(i, 8, 0x08);
            bySqrt.WriteRegister(i, 7, 0x3F);
            bySqrt.WriteRegister(i, 8, 0x08);
        }

        var countBuffer = new short[200 * 2];
        var sqrtBuffer = new short[200 * 2];
        byCount.RenderSamples(countBuffer, 200);
        bySqrt.RenderSamples(sqrtBuffer, 200);

        for (int i = 50; i < 200; i++)
        {
            Assert.True(sqrtBuffer[i * 2] > countBuffer[i * 2], "DivideBySqrtChipCount should be louder than DivideByChipCount for the same registers.");
        }
    }

    [Fact]
    public void VolumeScaling_None_IsDefault_AndBitIdenticalToOmittingIt()
    {
        var withDefault = new MultiChipAySoundChip(2, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono);
        var explicitNone = new MultiChipAySoundChip(2, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, 44_100, PanningPreset.Mono, MixLimiter.HardClip, VolumeScaling.None);

        foreach (var multi in new[] { withDefault, explicitNone })
        {
            multi.WriteRegister(0, 7, 0x3F);
            multi.WriteRegister(0, 8, 0x0A);
            multi.WriteRegister(1, 7, 0x3F);
            multi.WriteRegister(1, 8, 0x0A);
        }

        var defaultBuffer = new short[200 * 2];
        var explicitBuffer = new short[200 * 2];
        withDefault.RenderSamples(defaultBuffer, 200);
        explicitNone.RenderSamples(explicitBuffer, 200);

        Assert.Equal(explicitBuffer, defaultBuffer);
    }

    [Fact]
    public void DesignatedConstructor_WithExplicitMixLimiterAndVolumeScaling_ViaChipConfigs()
    {
        var multi = new MultiChipAySoundChip(
            44_100,
            PanningPreset.Mono,
            MixLimiter.SoftLimit,
            VolumeScaling.DivideByChipCount,
            new ChipConfig(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum),
            new ChipConfig(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum));

        Assert.Equal(2, multi.ChipCount);
    }

    [Fact]
    public void OlderThreeAndFourArgOverloads_StillResolveToParamsArray_NotVolumeScaling()
    {
        // The exact scenario the API design needed to protect: calling the pre-existing
        // (rate, panning, mixLimiter, params configs) overload with several ChipConfig arguments
        // must keep landing them in chipConfigs — not fail to compile or bind to the newer
        // 5-parameter designated constructor's volumeScaling slot.
        var viaMixLimiterOverload = new MultiChipAySoundChip(
            44_100,
            PanningPreset.Mono,
            MixLimiter.SoftLimit,
            new ChipConfig(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum),
            new ChipConfig(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum));

        var viaBackCompatOverload = new MultiChipAySoundChip(
            44_100,
            PanningPreset.Mono,
            new ChipConfig(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum),
            new ChipConfig(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum));

        Assert.Equal(2, viaMixLimiterOverload.ChipCount);
        Assert.Equal(2, viaBackCompatOverload.ChipCount);
    }

    [Fact]
    public void MixLimiter_ViaChipConfigConstructor_AlsoDefaultsToHardClip()
    {
        var multi = new MultiChipAySoundChip(
            44_100,
            PanningPreset.Mono,
            new ChipConfig(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum),
            new ChipConfig(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum));

        multi.WriteRegister(0, 7, 0x3F);
        multi.WriteRegister(0, 8, 0x0F);
        multi.WriteRegister(1, 7, 0x3F);
        multi.WriteRegister(1, 8, 0x0F);

        var buffer = new short[200 * 2];
        multi.RenderSamples(buffer, 200);

        for (int i = 50; i < 200; i++)
        {
            Assert.Equal(short.MaxValue, buffer[i * 2]);
        }
    }
}
