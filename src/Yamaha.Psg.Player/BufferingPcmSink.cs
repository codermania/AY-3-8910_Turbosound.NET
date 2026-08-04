namespace Yamaha.Psg.Player;

/// <summary>Accumulates every incoming sample in memory — used to save the result to WAV afterwards.</summary>
public sealed class BufferingPcmSink : IPcmSink
{
    private readonly List<short> _samples = [];

    public void Write(short[] interleavedBuffer, int sampleCount)
    {
        for (int i = 0; i < sampleCount; i++)
        {
            _samples.Add(interleavedBuffer[i]);
        }
    }

    public short[] ToArray() => _samples.ToArray();
}
