namespace Yamaha.Psg.Core.Timing;

/// <summary>
/// A band-limited rational decimator: lowers the sample rate from the chip's internal clock down
/// to an arbitrary output rate. Naive decimation ("just take every Nth sample") produces audible
/// aliasing on the tone generators' square waves — here the input stream is first passed through
/// an FIR low-pass filter (windowed-sinc, Hamming window, cutoff at outputRate/2), and the moments
/// at which output samples are emitted are driven by a phase accumulator (no interpolation — at a
/// typical ~40:1 rate ratio, jitter within a single input tick is inaudible).
/// </summary>
public sealed class SampleRateConverter
{
    private readonly int _inputRate;
    private readonly int _outputRate;
    private readonly double[] _coefficients;
    private readonly double[] _ring;
    private int _ringPos;
    private long _phaseAccumulator;

    public SampleRateConverter(int inputRate, int outputRate, int tapCount = 63)
    {
        if (inputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
        if (outputRate <= 0) throw new ArgumentOutOfRangeException(nameof(outputRate));

        if (tapCount % 2 == 0)
        {
            tapCount++; // odd tap count for a symmetric linear-phase filter
        }

        _inputRate = inputRate;
        _outputRate = outputRate;
        _coefficients = DesignLowPassFir(tapCount, cutoffHz: outputRate / 2.0, sampleRateHz: inputRate);
        _ring = new double[tapCount];
    }

    /// <summary>
    /// Feeds one input sample (chip rate). Returns true and writes <paramref name="outputSample"/>
    /// if an output sample (output rate) should be emitted on this tick — on average this happens
    /// outputRate times per inputRate samples fed in.
    /// </summary>
    public bool Push(double inputSample, out double outputSample)
    {
        _ring[_ringPos] = inputSample;
        _ringPos = (_ringPos + 1) % _ring.Length;

        _phaseAccumulator += _outputRate;
        if (_phaseAccumulator < _inputRate)
        {
            outputSample = 0.0;
            return false;
        }

        _phaseAccumulator -= _inputRate;
        outputSample = Convolve();
        return true;
    }

    private double Convolve()
    {
        double sum = 0.0;
        int n = _ring.Length;

        // _ringPos points at the oldest sample (next to be overwritten); walk from there to the
        // newest, pairing up with the coefficients in order.
        for (int i = 0; i < n; i++)
        {
            int sampleIndex = (_ringPos + i) % n;
            sum += _ring[sampleIndex] * _coefficients[i];
        }

        return sum;
    }

    private static double[] DesignLowPassFir(int tapCount, double cutoffHz, double sampleRateHz)
    {
        var coefficients = new double[tapCount];
        double fc = cutoffHz / sampleRateHz; // normalized cutoff frequency, 0..0.5
        int middle = tapCount / 2;
        double sum = 0.0;

        for (int i = 0; i < tapCount; i++)
        {
            int k = i - middle;
            double sinc = k == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * k) / (Math.PI * k);
            double window = 0.54 - (0.46 * Math.Cos(2 * Math.PI * i / (tapCount - 1))); // Hamming
            coefficients[i] = sinc * window;
            sum += coefficients[i];
        }

        // Normalize to unity gain at DC.
        for (int i = 0; i < tapCount; i++)
        {
            coefficients[i] /= sum;
        }

        return coefficients;
    }
}
