using Yamaha.Psg.Core.Chip;

namespace Yamaha.Psg.Core.Tests;

public class MixerTests
{
    [Fact]
    public void Decode_BitsAreActiveLow_ZeroMeansEnabled()
    {
        var allEnabled = Mixer.Decode(0x00);
        Assert.False(allEnabled.ToneADisabled);
        Assert.False(allEnabled.ToneBDisabled);
        Assert.False(allEnabled.ToneCDisabled);
        Assert.False(allEnabled.NoiseADisabled);
        Assert.False(allEnabled.NoiseBDisabled);
        Assert.False(allEnabled.NoiseCDisabled);

        var allDisabled = Mixer.Decode(0x3F);
        Assert.True(allDisabled.ToneADisabled);
        Assert.True(allDisabled.ToneBDisabled);
        Assert.True(allDisabled.ToneCDisabled);
        Assert.True(allDisabled.NoiseADisabled);
        Assert.True(allDisabled.NoiseBDisabled);
        Assert.True(allDisabled.NoiseCDisabled);
    }

    [Fact]
    public void Decode_ChannelABitsDoNotAffectOtherChannels()
    {
        // bit0 = tone A, bit3 = noise A
        var mixer = Mixer.Decode(0b0000_1001);

        Assert.True(mixer.ToneADisabled);
        Assert.True(mixer.NoiseADisabled);
        Assert.False(mixer.ToneBDisabled);
        Assert.False(mixer.ToneCDisabled);
        Assert.False(mixer.NoiseBDisabled);
        Assert.False(mixer.NoiseCDisabled);
    }

    [Theory]
    // toneOutput, toneDisabled, noiseOutput, noiseDisabled, expected gate
    [InlineData(true, false, true, false, true)]
    [InlineData(true, false, false, false, false)]   // tone and noise both enabled and don't match -> AND gives false
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, true, false, false, false)]    // tone disabled (passthrough=true), gate = noise
    [InlineData(false, true, true, false, true)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, true, false)]     // noise disabled (passthrough=true), gate = tone
    [InlineData(true, false, false, true, true)]
    [InlineData(false, true, false, true, true)]      // both tone and noise disabled -> digi-drum: constant gate
    public void ChannelGate_MatchesAndOrTruthTable(
        bool toneOutput, bool toneDisabled, bool noiseOutput, bool noiseDisabled, bool expected)
    {
        bool actual = Mixer.ChannelGate(toneOutput, toneDisabled, noiseOutput, noiseDisabled);

        Assert.Equal(expected, actual);
    }
}
