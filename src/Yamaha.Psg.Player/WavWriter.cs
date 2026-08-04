namespace Yamaha.Psg.Player;

/// <summary>A minimal RIFF/WAV writer for 16-bit PCM — no external audio dependencies.</summary>
public static class WavWriter
{
    public static void Write(string path, short[] interleavedSamples, int sampleRate, int channels)
    {
        using var stream = File.Create(path);
        Write(stream, interleavedSamples, sampleRate, channels);
    }

    public static void Write(Stream stream, short[] interleavedSamples, int sampleRate, int channels)
    {
        const short bitsPerSample = 16;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));
        int dataSize = interleavedSamples.Length * sizeof(short);

        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16); // fmt chunk size for PCM
        writer.Write((short)1); // format 1 = PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        foreach (short sample in interleavedSamples)
        {
            writer.Write(sample);
        }
    }
}
