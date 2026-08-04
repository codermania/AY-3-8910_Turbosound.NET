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
/// </summary>
public sealed class MultiChipAySoundChip
{
    private readonly AySoundChip[] _chips;

    public MultiChipAySoundChip(int outputSampleRate, PanningPreset panning, params ChipConfig[] chipConfigs)
    {
        if (chipConfigs is null || chipConfigs.Length == 0)
        {
            throw new ArgumentException("At least one ChipConfig is required.", nameof(chipConfigs));
        }

        OutputSampleRate = outputSampleRate;
        ChipCount = chipConfigs.Length;

        _chips = new AySoundChip[ChipCount];
        for (int i = 0; i < ChipCount; i++)
        {
            _chips[i] = new AySoundChip(chipConfigs[i].Variant, chipConfigs[i].ClockHz, outputSampleRate, panning);
        }
    }

    /// <summary>Convenience constructor for the most common case — <paramref name="chipCount"/> identical chips.</summary>
    public MultiChipAySoundChip(int chipCount, ChipVariant variant, int clockHz, int outputSampleRate, PanningPreset panning = PanningPreset.Abc)
        : this(outputSampleRate, panning, CreateUniformConfigs(chipCount, variant, clockHz))
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
    /// and clipped again — the same documented "hard clipping" simplification as the single-chip facade.
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
            outputBuffer[f * 2] = StereoMixer.ClampToShortRange(mixLeft[f]);
            outputBuffer[(f * 2) + 1] = StereoMixer.ClampToShortRange(mixRight[f]);
        }

        return frameCount;
    }

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
