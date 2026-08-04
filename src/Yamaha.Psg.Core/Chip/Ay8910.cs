namespace Yamaha.Psg.Core.Chip;

/// <summary>
/// Top-level model of a single AY-3-8910/YM2149 chip: exactly 3 channels, exactly 16 registers,
/// one shared noise generator. Knows nothing about output sample rate, stereo, or PCM — those are
/// higher-level concerns (Timing/Output) implemented on top of this.
/// </summary>
public sealed class Ay8910
{
    private readonly RegisterFile _registers = new();
    private readonly ToneGenerator _toneA = new();
    private readonly ToneGenerator _toneB = new();
    private readonly ToneGenerator _toneC = new();
    private readonly NoiseGenerator _noise = new();
    private readonly EnvelopeGenerator _envelope;
    private readonly double[] _envelopeDac;
    private readonly double[] _fixedVolumeDac;

    public Ay8910(ChipVariant variant, int clockHz)
    {
        Variant = variant;
        ClockHz = clockHz;
        _envelope = new EnvelopeGenerator(variant);
        _envelopeDac = DacTables.EnvelopeLevels(variant);
        _fixedVolumeDac = DacTables.FixedVolumeLevels(variant);
    }

    public ChipVariant Variant { get; }
    public int ClockHz { get; }

    public void WriteRegister(int reg, byte value)
    {
        _registers.Write(reg, value);

        // Writing R13 always restarts the envelope, even if the value did not change.
        if (reg == 13)
        {
            _envelope.SetShape(_registers.EnvelopeShape);
        }
    }

    public byte ReadRegister(int reg) => _registers.Read(reg);

    /// <summary>Advances the emulation by one chip clock cycle.</summary>
    internal void Tick()
    {
        _toneA.SetPeriod(_registers.ToneAPeriod);
        _toneB.SetPeriod(_registers.ToneBPeriod);
        _toneC.SetPeriod(_registers.ToneCPeriod);
        _noise.SetPeriod(_registers.NoisePeriod);
        _envelope.SetPeriod(_registers.EnvelopePeriod);

        _toneA.Tick();
        _toneB.Tick();
        _toneC.Tick();
        _noise.Tick();
        _envelope.Tick();
    }

    /// <summary>Current 5-bit level (0-31) of the envelope generator — used by tests and the DAC stage.</summary>
    internal int EnvelopeLevel => _envelope.CurrentLevel;

    /// <summary>
    /// Raw boolean gates per channel (tone+noise mixed through <see cref="Mixer"/>, but still
    /// without envelope/DAC applied). Used by milestone-1 unit tests to check frequencies.
    /// </summary>
    internal (bool A, bool B, bool C) SampleChannelGates()
    {
        var mixer = Mixer.Decode(_registers.Mixer);
        bool noiseOutput = _noise.Output;

        return (
            Mixer.ChannelGate(_toneA.Output, mixer.ToneADisabled, noiseOutput, mixer.NoiseADisabled),
            Mixer.ChannelGate(_toneB.Output, mixer.ToneBDisabled, noiseOutput, mixer.NoiseBDisabled),
            Mixer.ChannelGate(_toneC.Output, mixer.ToneCDisabled, noiseOutput, mixer.NoiseCDisabled)
        );
    }

    /// <summary>
    /// Normalized (0..1) output level for each of the three channels: the mixer gate (tone/noise)
    /// closes the channel to 0, otherwise the level comes from the envelope (if enabled via bit 4
    /// of the amplitude register) or from the fixed volume (bits 0-3) — through the DAC table of
    /// the selected chip variant.
    /// </summary>
    internal (double A, double B, double C) SampleChannelLevels()
    {
        var gates = SampleChannelGates();

        return (
            ChannelLevel(gates.A, _registers.AmplitudeA),
            ChannelLevel(gates.B, _registers.AmplitudeB),
            ChannelLevel(gates.C, _registers.AmplitudeC)
        );
    }

    private double ChannelLevel(bool gate, byte amplitudeRegister)
    {
        if (!gate)
        {
            return 0.0;
        }

        bool useEnvelope = (amplitudeRegister & 0x10) != 0;
        return useEnvelope
            ? _envelopeDac[_envelope.CurrentLevel]
            : _fixedVolumeDac[amplitudeRegister & 0x0F];
    }
}
