namespace Yamaha.Psg.Core.Output;

/// <summary>
/// The panning matrix that mixes three channels into stereo. Mono is a degenerate case of Custom
/// (equal left/right gain for every channel), with no separate code path. The final mix is a
/// simple linear sum with hard clipping at the short boundaries (a documented simplification,
/// matching what most existing AY players do).
/// </summary>
public sealed class StereoMixer
{
    private ChannelPan _panA;
    private ChannelPan _panB;
    private ChannelPan _panC;

    public StereoMixer(PanningPreset preset = PanningPreset.Abc)
    {
        SetPanning(preset);
    }

    /// <summary>Selects one of the built-in presets (Mono/Abc/Acb/...). For Custom use <see cref="SetCustomPanning"/>.</summary>
    public void SetPanning(PanningPreset preset)
    {
        (_panA, _panB, _panC) = preset switch
        {
            PanningPreset.Mono => (new ChannelPan(1f, 1f), new ChannelPan(1f, 1f), new ChannelPan(1f, 1f)),
            // The preset name reads as [left][center][right] channel, hence the matrices below.
            PanningPreset.Abc => (new ChannelPan(1f, 0f), new ChannelPan(0.5f, 0.5f), new ChannelPan(0f, 1f)),
            PanningPreset.Acb => (new ChannelPan(1f, 0f), new ChannelPan(0f, 1f), new ChannelPan(0.5f, 0.5f)),
            PanningPreset.Bac => (new ChannelPan(0.5f, 0.5f), new ChannelPan(1f, 0f), new ChannelPan(0f, 1f)),
            PanningPreset.Bca => (new ChannelPan(0f, 1f), new ChannelPan(1f, 0f), new ChannelPan(0.5f, 0.5f)),
            PanningPreset.Cab => (new ChannelPan(0.5f, 0.5f), new ChannelPan(0f, 1f), new ChannelPan(1f, 0f)),
            PanningPreset.Cba => (new ChannelPan(0f, 1f), new ChannelPan(0.5f, 0.5f), new ChannelPan(1f, 0f)),
            PanningPreset.Custom => throw new ArgumentException(
                $"{nameof(PanningPreset.Custom)} requires explicit values — use {nameof(SetCustomPanning)}.",
                nameof(preset)),
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };
    }

    public void SetCustomPanning(ChannelPan a, ChannelPan b, ChannelPan c)
    {
        _panA = a;
        _panB = b;
        _panC = c;
    }

    /// <summary>Linear mix with no clipping — used by the render pipeline before decimation.</summary>
    public (double Left, double Right) MixRaw(double levelA, double levelB, double levelC)
    {
        double left = (levelA * _panA.Left) + (levelB * _panB.Left) + (levelC * _panC.Left);
        double right = (levelA * _panA.Right) + (levelB * _panB.Right) + (levelC * _panC.Right);
        return (left, right);
    }

    /// <summary>A ready-to-output stereo sample (short, clipped) — convenient for direct unit tests of panning.</summary>
    public (short Left, short Right) Mix(double levelA, double levelB, double levelC)
    {
        var (left, right) = MixRaw(levelA, levelB, levelC);
        return (ToShort(left), ToShort(right));
    }

    public static short ToShort(double normalized) => ClampToShortRange(normalized * short.MaxValue);

    /// <summary>
    /// Clamps a value that is already in PCM scale (not -1..1) — e.g. the sum of several already
    /// converted short samples (see <see cref="MultiChipAySoundChip"/>), where multiplying by
    /// short.MaxValue again would be a bug.
    /// </summary>
    public static short ClampToShortRange(double pcmScaleValue)
    {
        if (pcmScaleValue >= short.MaxValue) return short.MaxValue;
        if (pcmScaleValue <= short.MinValue) return short.MinValue;
        return (short)Math.Round(pcmScaleValue);
    }
}
