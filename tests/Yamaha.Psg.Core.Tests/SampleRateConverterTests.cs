using Yamaha.Psg.Core.Timing;

namespace Yamaha.Psg.Core.Tests;

public class SampleRateConverterTests
{
    private const int InputRate = 1_773_400; // ZX Spectrum
    private const int OutputRate = 44_100;

    private static double RmsOfDecimatedSineWave(double frequencyHz, double seconds)
    {
        var converter = new SampleRateConverter(InputRate, OutputRate);
        int totalInputSamples = (int)(InputRate * seconds);

        double sumSquares = 0.0;
        int outputCount = 0;

        for (int i = 0; i < totalInputSamples; i++)
        {
            double t = i / (double)InputRate;
            double input = Math.Sin(2 * Math.PI * frequencyHz * t);

            if (converter.Push(input, out double output))
            {
                sumSquares += output * output;
                outputCount++;
            }
        }

        return outputCount == 0 ? 0.0 : Math.Sqrt(sumSquares / outputCount);
    }

    [Fact]
    public void InBandFrequency_PassesThroughWithSubstantialAmplitude()
    {
        // 1 kHz is well within the passband (cutoff at OutputRate/2 = 22050 Hz).
        double rms = RmsOfDecimatedSineWave(1000.0, seconds: 0.05);

        // For a unit-amplitude sine wave RMS ~ 0.707; we expect the filter to barely attenuate it.
        Assert.InRange(rms, 0.5, 0.8);
    }

    [Fact]
    public void DeepStopbandFrequency_IsStronglyAttenuated_PreventingAliasing()
    {
        // The filter cutoff is OutputRate/2 = 22050 Hz, but the transition band of a 63-tap Hamming
        // window is fairly wide (~90 kHz at a ~1.77 MHz chip clock), so to check real stopband
        // attenuation we use a frequency well past the transition band (100 kHz) — without
        // filtering, decimation would fold it right back into the audible range.
        double rmsInBand = RmsOfDecimatedSineWave(1000.0, seconds: 0.05);
        double rmsDeepStopband = RmsOfDecimatedSineWave(100_000.0, seconds: 0.05);

        Assert.True(
            rmsDeepStopband < rmsInBand * 0.05,
            $"Expected strong attenuation: rmsDeepStopband={rmsDeepStopband}, rmsInBand={rmsInBand}");
    }

    [Fact]
    public void JustAboveOutputNyquist_IsPartiallyAttenuated_ConfirmingRollOffStartsAtCutoff()
    {
        // Right at the cutoff boundary (22050 Hz) the filter should already noticeably attenuate
        // the signal relative to unmodified passband transmission (1 kHz).
        double rmsInBand = RmsOfDecimatedSineWave(1000.0, seconds: 0.05);
        double rmsAtCutoff = RmsOfDecimatedSineWave(22_050.0, seconds: 0.05);

        Assert.True(
            rmsAtCutoff < rmsInBand * 0.8,
            $"Expected noticeable attenuation at the cutoff: rmsAtCutoff={rmsAtCutoff}, rmsInBand={rmsInBand}");
    }

    [Fact]
    public void Push_ProducesOutputAtApproximatelyTheConfiguredRatio()
    {
        var converter = new SampleRateConverter(InputRate, OutputRate);
        const double seconds = 0.1;
        int totalInputSamples = (int)(InputRate * seconds);

        int outputCount = 0;
        for (int i = 0; i < totalInputSamples; i++)
        {
            if (converter.Push(0.0, out _))
            {
                outputCount++;
            }
        }

        int expected = (int)(OutputRate * seconds);
        Assert.InRange(outputCount, expected - 2, expected + 2);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveRates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SampleRateConverter(0, 44_100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SampleRateConverter(1_000_000, 0));
    }
}
