namespace Yamaha.Psg.Core.Chip;

/// <summary>Per-channel bits decoded from register R7. Bits are active-low: 1 means "disabled".</summary>
public readonly struct MixerControl
{
    public MixerControl(byte r7)
    {
        ToneADisabled = (r7 & 0x01) != 0;
        ToneBDisabled = (r7 & 0x02) != 0;
        ToneCDisabled = (r7 & 0x04) != 0;
        NoiseADisabled = (r7 & 0x08) != 0;
        NoiseBDisabled = (r7 & 0x10) != 0;
        NoiseCDisabled = (r7 & 0x20) != 0;
    }

    public bool ToneADisabled { get; }
    public bool ToneBDisabled { get; }
    public bool ToneCDisabled { get; }
    public bool NoiseADisabled { get; }
    public bool NoiseBDisabled { get; }
    public bool NoiseCDisabled { get; }
}

/// <summary>
/// Decodes register R7 and AND-ORs tone/noise into a single channel gate: the channel is open
/// when (tone bit is high OR tone disabled) AND (noise bit is high OR noise disabled). Disabling
/// both tone and noise at once yields a constant "open" gate — this is exactly what digi-drum
/// tricks rely on: the channel starts directly reflecting the volume register with no tone/noise
/// modulation.
/// </summary>
public static class Mixer
{
    public static MixerControl Decode(byte r7) => new(r7);

    public static bool ChannelGate(bool toneOutput, bool toneDisabled, bool noiseOutput, bool noiseDisabled)
        => (toneOutput || toneDisabled) && (noiseOutput || noiseDisabled);
}
