using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;
using Yamaha.Psg.Core.Timing;

namespace Yamaha.Psg.Core;

/// <summary>
/// An array of N independent chips (TurboSound-style: 2-3 AY/YM chips running in parallel on ZX
/// Spectrum and similar systems) instead of a generic N-channel engine — each inner chip remains
/// an accurate model of real hardware (see <see cref="Ay8910"/>), and polyphony grows by adding
/// whole chips rather than stretching a single chip beyond its real 3 channels. <c>chipCount = 1</c>
/// behaves identically to <see cref="AySoundChip"/>.
///
/// Two independent, complementary headroom controls are available for ensembles where several
/// chips panned the same way can add up past full scale: <see cref="VolumeScaling"/> scales each
/// chip's own DAC tables down at the source (opt-in, defaults to <see cref="VolumeScaling.None"/>),
/// while <see cref="MixLimiter"/> controls how the final post-mix range is enforced (opt-in,
/// defaults to <see cref="MixLimiter.HardClip"/>).
/// </summary>
public sealed class MultiChipAySoundChip
{
    private readonly AySoundChip[] _chips;
    private readonly MixLimiter _mixLimiter;

    /// <summary>
    /// The designated constructor — every other overload chains here. Takes both post-mix
    /// (<paramref name="mixLimiter"/>) and at-the-source (<paramref name="volumeScaling"/>) headroom
    /// controls explicitly. This is a distinct overload from the older
    /// <c>(outputSampleRate, panning, mixLimiter, params chipConfigs)</c> one (rather than adding
    /// <paramref name="volumeScaling"/> as a new parameter to it) specifically so that existing calls
    /// like <c>new MultiChipAySoundChip(rate, panning, MixLimiter.SoftLimit, cfg1, cfg2)</c> keep
    /// compiling unchanged — inserting a required-by-position parameter ahead of an existing
    /// <c>params</c> array would silently break any call that relies on positional
    /// <c>ChipConfig</c> arguments landing in that array.
    /// </summary>
    public MultiChipAySoundChip(int outputSampleRate, PanningPreset panning, MixLimiter mixLimiter, VolumeScaling volumeScaling, params ChipConfig[] chipConfigs)
    {
        if (chipConfigs is null || chipConfigs.Length == 0)
        {
            throw new ArgumentException("At least one ChipConfig is required.", nameof(chipConfigs));
        }

        OutputSampleRate = outputSampleRate;
        ChipCount = chipConfigs.Length;
        _mixLimiter = mixLimiter;

        double volumeScale = volumeScaling switch
        {
            VolumeScaling.DivideByChipCount => 1.0 / ChipCount,
            VolumeScaling.DivideBySqrtChipCount => 1.0 / Math.Sqrt(ChipCount),
            _ => 1.0,
        };

        _chips = new AySoundChip[ChipCount];
        for (int i = 0; i < ChipCount; i++)
        {
            _chips[i] = new AySoundChip(chipConfigs[i].Variant, chipConfigs[i].ClockHz, outputSampleRate, panning, volumeScale: volumeScale);
        }
    }

    /// <summary>Same as the designated constructor, defaulting to <see cref="VolumeScaling.None"/> — today's original behavior.</summary>
    public MultiChipAySoundChip(int outputSampleRate, PanningPreset panning, MixLimiter mixLimiter, params ChipConfig[] chipConfigs)
        : this(outputSampleRate, panning, mixLimiter, VolumeScaling.None, chipConfigs)
    {
    }

    /// <summary>Same as the designated constructor, defaulting to <see cref="MixLimiter.HardClip"/> and <see cref="VolumeScaling.None"/> — today's original behavior.</summary>
    public MultiChipAySoundChip(int outputSampleRate, PanningPreset panning, params ChipConfig[] chipConfigs)
        : this(outputSampleRate, panning, MixLimiter.HardClip, VolumeScaling.None, chipConfigs)
    {
    }

    /// <summary>Convenience constructor for the most common case — <paramref name="chipCount"/> identical chips.</summary>
    public MultiChipAySoundChip(
        int chipCount,
        ChipVariant variant,
        int clockHz,
        int outputSampleRate,
        PanningPreset panning = PanningPreset.Abc,
        MixLimiter mixLimiter = MixLimiter.HardClip,
        VolumeScaling volumeScaling = VolumeScaling.None)
        : this(outputSampleRate, panning, mixLimiter, volumeScaling, CreateUniformConfigs(chipCount, variant, clockHz))
    {
    }

    public int ChipCount { get; }
    public int OutputSampleRate { get; }

    public void WriteRegister(int chipIndex, int register, byte value) => Chip(chipIndex).WriteRegister(register, value);

    public byte ReadRegister(int chipIndex, int register) => Chip(chipIndex).ReadRegister(register);

    /// <summary>Panning for all three channels of one specific chip, independent of the rest of the ensemble.</summary>
    public void SetChannelPanning(int chipIndex, ChannelPan channelA, ChannelPan channelB, ChannelPan channelC)
        => Chip(chipIndex).SetCustomPanning(channelA, channelB, channelC);

    /// <summary>A built-in preset (Mono/Abc/Acb/...) for all three channels of one specific chip.</summary>
    public void SetChipPanning(int chipIndex, PanningPreset preset) => Chip(chipIndex).SetPanning(preset);

    public void Reset()
    {
        foreach (var chip in _chips)
        {
            chip.Reset();
        }
    }

    /// <summary>
    /// Renders <paramref name="frameCount"/> stereo pairs, summing the contribution of every chip
    /// in the ensemble. Each chip is rendered (and clipped) independently through
    /// <see cref="AySoundChip.RenderSamples"/> at its own rate, after which the results are summed
    /// and the range is enforced again per the constructor's <see cref="MixLimiter"/> choice — by
    /// default (<see cref="MixLimiter.HardClip"/>) this is the same "hard clipping" simplification
    /// as the single-chip facade; <see cref="MixLimiter.SoftLimit"/> compresses instead of truncating
    /// when several chips panned the same way add up past full scale (see
    /// <see cref="StereoMixer.SoftLimitToShortRange"/>). See TODO.md for a related, separate
    /// simplification this doesn't address: each chip's own contribution is already hard-clipped to
    /// short range before it ever reaches this sum.
    /// </summary>
    public int RenderSamples(short[] outputBuffer, int frameCount, IReadOnlyList<TimedRegisterWrite>?[]? scheduledWritesPerChip = null)
    {
        if (outputBuffer.Length < frameCount * 2)
        {
            throw new ArgumentException($"{nameof(outputBuffer)} is too small for {frameCount} stereo pairs.", nameof(outputBuffer));
        }

        var mixLeft = new double[frameCount];
        var mixRight = new double[frameCount];
        var scratch = new short[frameCount * 2];

        for (int i = 0; i < _chips.Length; i++)
        {
            var writes = scheduledWritesPerChip is not null && i < scheduledWritesPerChip.Length
                ? scheduledWritesPerChip[i]
                : null;

            _chips[i].RenderSamples(scratch, frameCount, writes);

            for (int f = 0; f < frameCount; f++)
            {
                mixLeft[f] += scratch[f * 2];
                mixRight[f] += scratch[(f * 2) + 1];
            }
        }

        for (int f = 0; f < frameCount; f++)
        {
            outputBuffer[f * 2] = ApplyLimiter(mixLeft[f]);
            outputBuffer[(f * 2) + 1] = ApplyLimiter(mixRight[f]);
        }

        return frameCount;
    }

    private short ApplyLimiter(double pcmScaleValue) => _mixLimiter == MixLimiter.SoftLimit
        ? StereoMixer.SoftLimitToShortRange(pcmScaleValue)
        : StereoMixer.ClampToShortRange(pcmScaleValue);

    private AySoundChip Chip(int index)
    {
        if (index < 0 || index >= _chips.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Chip index must be in the range 0..{_chips.Length - 1}.");
        }

        return _chips[index];
    }

    private static ChipConfig[] CreateUniformConfigs(int chipCount, ChipVariant variant, int clockHz)
    {
        if (chipCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chipCount));
        }

        var configs = new ChipConfig[chipCount];
        for (int i = 0; i < chipCount; i++)
        {
            configs[i] = new ChipConfig(variant, clockHz);
        }

        return configs;
    }
}
