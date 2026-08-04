namespace Yamaha.Psg.Player;

/// <summary>Where generated PCM flows to — a WAV file (via <see cref="BufferingPcmSink"/>) or live playback (<see cref="LivePlaybackSink"/>).</summary>
public interface IPcmSink
{
    void Write(short[] interleavedBuffer, int sampleCount);
}
