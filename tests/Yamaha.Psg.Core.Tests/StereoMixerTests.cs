using Yamaha.Psg.Core.Output;

namespace Yamaha.Psg.Core.Tests;

public class StereoMixerTests
{
    [Fact]
    public void Abc_PansAHardLeft_CHardRight_BCenter()
    {
        var mixer = new StereoMixer(PanningPreset.Abc);

        var (leftA, rightA) = mixer.MixRaw(1.0, 0.0, 0.0);
        Assert.Equal(1.0, leftA);
        Assert.Equal(0.0, rightA);

        var (leftC, rightC) = mixer.MixRaw(0.0, 0.0, 1.0);
        Assert.Equal(0.0, leftC);
        Assert.Equal(1.0, rightC);

        var (leftB, rightB) = mixer.MixRaw(0.0, 1.0, 0.0);
        Assert.Equal(leftB, rightB);
        Assert.True(leftB > 0.0);
    }

    [Fact]
    public void Acb_SwapsBAndCComparedToAbc()
    {
        var mixer = new StereoMixer(PanningPreset.Acb);

        var (leftB, rightB) = mixer.MixRaw(0.0, 1.0, 0.0);
        Assert.Equal(0.0, leftB);
        Assert.Equal(1.0, rightB); // in ACB channel B is on the right, not centered

        var (leftC, rightC) = mixer.MixRaw(0.0, 0.0, 1.0);
        Assert.Equal(leftC, rightC); // in ACB channel C is centered
    }

    [Fact]
    public void Mono_SendsEveryChannelEquallyToBothEars()
    {
        var mixer = new StereoMixer(PanningPreset.Mono);

        var (left, right) = mixer.MixRaw(0.3, 0.5, 0.2);

        Assert.Equal(left, right);
        Assert.Equal(1.0, left, precision: 10); // 0.3+0.5+0.2 = 1.0, gain x1 per channel
    }

    [Fact]
    public void SetPanning_Custom_ThrowsAndRequiresSetCustomPanning()
    {
        var mixer = new StereoMixer();

        Assert.Throws<ArgumentException>(() => mixer.SetPanning(PanningPreset.Custom));

        mixer.SetCustomPanning(new ChannelPan(0.2f, 0.8f), new ChannelPan(1f, 1f), new ChannelPan(0.9f, 0.1f));
        var (left, right) = mixer.MixRaw(1.0, 0.0, 0.0);
        Assert.Equal(0.2, left, precision: 5);
        Assert.Equal(0.8, right, precision: 5);
    }

    [Theory]
    [InlineData(2.0, short.MaxValue)]   // overflow above -> clipped
    [InlineData(-2.0, short.MinValue)]  // overflow below -> clipped
    [InlineData(0.0, (short)0)]
    public void ToShort_ClampsToShortRange(double normalized, short expected)
    {
        Assert.Equal(expected, StereoMixer.ToShort(normalized));
    }

    [Fact]
    public void Mix_SummingThreeFullChannelsOnSameEar_ClipsInsteadOfOverflowing()
    {
        var mixer = new StereoMixer();
        mixer.SetCustomPanning(new ChannelPan(1f, 0f), new ChannelPan(1f, 0f), new ChannelPan(1f, 0f));

        var (left, right) = mixer.Mix(1.0, 1.0, 1.0); // sum of 3.0 on the left — far beyond short's range

        Assert.Equal(short.MaxValue, left);
        Assert.Equal((short)0, right);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(15000.0)]
    [InlineData(-15000.0)]
    [InlineData(29490.0)] // just under the 0.9 * short.MaxValue knee
    public void SoftLimitToShortRange_BelowTheKnee_IsBitIdenticalToHardClamp(double pcmScaleValue)
    {
        Assert.Equal(StereoMixer.ClampToShortRange(pcmScaleValue), StereoMixer.SoftLimitToShortRange(pcmScaleValue));
    }

    [Fact]
    public void SoftLimitToShortRange_ModestOvershoot_CompressesTowardsButNeverReachesFullScale()
    {
        // A modest overshoot past the knee — not so extreme that tanh's asymptote rounds away to
        // exactly short.MaxValue once quantized to an integer sample (that's expected once the
        // input is many headroom-widths past the knee; see the "extreme input" test below).
        const double pcmScaleValue = 35_000.0;
        short result = StereoMixer.SoftLimitToShortRange(pcmScaleValue);

        Assert.True(result < short.MaxValue, $"Expected {result} < {short.MaxValue} — soft limiting should approach full scale, not reach it.");
        Assert.True(result > 0.9 * short.MaxValue); // still well above the knee — not silenced

        short hardClipped = StereoMixer.ClampToShortRange(pcmScaleValue);
        Assert.True(result < hardClipped, "The soft-limited result should sit below where hard clipping would have truncated to.");
    }

    [Theory]
    [InlineData(50_000.0)]
    [InlineData(1_000_000.0)]
    public void SoftLimitToShortRange_IsSymmetricForNegativeSamples(double magnitude)
    {
        short positive = StereoMixer.SoftLimitToShortRange(magnitude);
        short negative = StereoMixer.SoftLimitToShortRange(-magnitude);

        Assert.Equal(-positive, negative);
    }

    [Fact]
    public void SoftLimitToShortRange_NeverExceedsShortRange_EvenForExtremeInput()
    {
        Assert.Equal(short.MaxValue, StereoMixer.SoftLimitToShortRange(double.MaxValue / 2));
        Assert.Equal(-short.MaxValue, StereoMixer.SoftLimitToShortRange(-double.MaxValue / 2));
    }
}
