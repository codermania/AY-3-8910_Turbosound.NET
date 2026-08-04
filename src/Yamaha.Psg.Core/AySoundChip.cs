using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;
using Yamaha.Psg.Core.Timing;

namespace Yamaha.Psg.Core;

/// <summary>
/// Public facade of the library: a single AY-3-8910/YM2149 chip, stereo panning, and resampling
/// from the chip's clock rate to an arbitrary output rate. The main entry point for most
/// consumers — the internal <see cref="Ay8910"/>/generators/DAC tables are only reachable through
/// this class.
/// </summary>
public sealed class AySoundChip
{
    private readonly StereoMixer _mixer;
    private readonly SubFrameEventQueue _eventQueue = new();
    private Ay8910 _chip;
    private SampleRateConverter _leftConverter;
    private SampleRateConverter _rightConverter;
    private SampleRateConverter _monoConverter;

    public AySoundChip(
        ChipVariant variant,
        int clockHz,
        int outputSampleRate,
        PanningPreset panning = PanningPreset.Abc,
        ChannelPan? customA = null,
        ChannelPan? customB = null,
        ChannelPan? customC = null)
    {
        Variant = variant;
        ClockHz = clockHz;
        OutputSampleRate = outputSampleRate;

        _mixer = new StereoMixer();
        ApplyPanning(panning, customA, customB, customC);

        _chip = new Ay8910(variant, clockHz);
        _leftConverter = new SampleRateConverter(clockHz, outputSampleRate);
        _rightConverter = new SampleRateConverter(clockHz, outputSampleRate);
        _monoConverter = new SampleRateConverter(clockHz, outputSampleRate);
    }

    public ChipVariant Variant { get; }
    public int ClockHz { get; }
    public int OutputSampleRate { get; }

    public void SetPanning(PanningPreset preset) => _mixer.SetPanning(preset);

    public void SetCustomPanning(ChannelPan a, ChannelPan b, ChannelPan c) => _mixer.SetCustomPanning(a, b, c);

    public void WriteRegister(int register, byte value) => _chip.WriteRegister(register, value);

    public byte ReadRegister(int register) => _chip.ReadRegister(register);

    /// <summary>Fully resets the chip and resampler state (equivalent to a fresh power-on).</summary>
    public void Reset()
    {
        _chip = new Ay8910(Variant, ClockHz);
        _leftConverter = new SampleRateConverter(ClockHz, OutputSampleRate);
        _rightConverter = new SampleRateConverter(ClockHz, OutputSampleRate);
        _monoConverter = new SampleRateConverter(ClockHz, OutputSampleRate);
    }

    /// <summary>
    /// Renders <paramref name="frameCount"/> stereo pairs (L,R,L,R,...) into <paramref name="outputBuffer"/>.
    /// The chip is clocked at full internal resolution; <paramref name="scheduledWrites"/> (if given)
    /// are applied exactly on their own cycle — this is what digi-drum accuracy is built on.
    /// </summary>
    public int RenderSamples(short[] outputBuffer, int frameCount, IReadOnlyList<TimedRegisterWrite>? scheduledWrites = null)
    {
        if (outputBuffer.Length < frameCount * 2)
        {
            throw new ArgumentException($"{nameof(outputBuffer)} is too small for {frameCount} stereo pairs.", nameof(outputBuffer));
        }

        _eventQueue.Load(scheduledWrites);

        int framesWritten = 0;
        int cycle = 0;
        while (framesWritten < frameCount)
        {
            ApplyDueWrites(cycle);
            _chip.Tick();

            var (a, b, c) = _chip.SampleChannelLevels();
            var (rawLeft, rawRight) = _mixer.MixRaw(a, b, c);

            bool leftReady = _leftConverter.Push(rawLeft, out double left);
            bool rightReady = _rightConverter.Push(rawRight, out double right);
            if (leftReady && rightReady)
            {
                outputBuffer[framesWritten * 2] = StereoMixer.ToShort(left);
                outputBuffer[(framesWritten * 2) + 1] = StereoMixer.ToShort(right);
                framesWritten++;
            }

            cycle++;
        }

        return framesWritten;
    }

    /// <summary>Like <see cref="RenderSamples"/>, but writes the mono sum of the three channels (ignoring panning) to a single value per frame.</summary>
    public int RenderSamplesMono(short[] outputBuffer, int frameCount, IReadOnlyList<TimedRegisterWrite>? scheduledWrites = null)
    {
        if (outputBuffer.Length < frameCount)
        {
            throw new ArgumentException($"{nameof(outputBuffer)} is too small for {frameCount} samples.", nameof(outputBuffer));
        }

        _eventQueue.Load(scheduledWrites);

        int framesWritten = 0;
        int cycle = 0;
        while (framesWritten < frameCount)
        {
            ApplyDueWrites(cycle);
            _chip.Tick();

            var (a, b, c) = _chip.SampleChannelLevels();
            double mono = (a + b + c) / 3.0;

            if (_monoConverter.Push(mono, out double monoOut))
            {
                outputBuffer[framesWritten] = StereoMixer.ToShort(monoOut);
                framesWritten++;
            }

            cycle++;
        }

        return framesWritten;
    }

    private void ApplyDueWrites(int cycle)
    {
        foreach (var write in _eventQueue.DrainDue(cycle))
        {
            _chip.WriteRegister(write.Register, write.Value);
        }
    }

    private void ApplyPanning(PanningPreset panning, ChannelPan? customA, ChannelPan? customB, ChannelPan? customC)
    {
        if (panning == PanningPreset.Custom)
        {
            if (customA is null || customB is null || customC is null)
            {
                throw new ArgumentException(
                    $"{nameof(PanningPreset.Custom)} requires customA/customB/customC to be provided.");
            }

            _mixer.SetCustomPanning(customA.Value, customB.Value, customC.Value);
        }
        else
        {
            _mixer.SetPanning(panning);
        }
    }
}
