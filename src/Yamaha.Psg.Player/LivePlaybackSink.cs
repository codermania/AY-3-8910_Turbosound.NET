using NAudio.Wave;

namespace Yamaha.Psg.Player;

/// <summary>
/// Live playback via NAudio (WaveOutEvent + BufferedWaveProvider) — to listen to the render in
/// headphones right away during development/debugging, without an intermediate WAV file.
/// </summary>
public sealed class LivePlaybackSink : IPcmSink, IDisposable
{
    private readonly WaveOutEvent _output;
    private readonly BufferedWaveProvider _buffer;

    public LivePlaybackSink(int sampleRate, int channels)
    {
        var format = new WaveFormat(sampleRate, 16, channels);
        _buffer = new BufferedWaveProvider(format)
        {
            BufferDuration = TimeSpan.FromMinutes(5),
            DiscardOnBufferOverflow = false,
        };

        _output = new WaveOutEvent();
        _output.Init(_buffer);
        _output.Play();
    }

    public void Write(short[] interleavedBuffer, int sampleCount)
    {
        var bytes = new byte[sampleCount * sizeof(short)];
        Buffer.BlockCopy(interleavedBuffer, 0, bytes, 0, bytes.Length);
        _buffer.AddSamples(bytes, 0, bytes.Length);
    }

    /// <summary>Blocks the calling thread until all queued audio has finished playing.</summary>
    public void WaitUntilDrained()
    {
        while (_buffer.BufferedDuration > TimeSpan.Zero)
        {
            Thread.Sleep(50);
        }
    }

    public void Dispose()
    {
        _output.Stop();
        _output.Dispose();
    }
}
