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
}
