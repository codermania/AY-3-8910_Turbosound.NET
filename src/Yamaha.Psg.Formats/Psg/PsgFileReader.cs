using Yamaha.Psg.Formats.Common;

namespace Yamaha.Psg.Formats.Psg;

/// <summary>
/// Reads the .PSG format (a frame-based AY register dump used by ZX Spectrum emulators).
/// Header: "PSG" (3 bytes) + 0x1A + version (1 byte) + frame rate (1 byte, version 10+) + 10
/// reserved bytes = 16 bytes. After that, a stream where 0xFF starts a frame followed by
/// (register, value) pairs up to the next 0xFF/0xFE/end of file; 0xFE + N inserts N*4 repeated
/// frames with no register changes. Register numbers 14-252 (AY I/O ports and third-party device
/// extensions) are intentionally skipped — they don't affect sound.
/// </summary>
public static class PsgFileReader
{
    private const int HeaderSize = 16;
    private static readonly byte[] Magic = "PSG"u8.ToArray();
    private const byte EndOfTextMarker = 0x1A;
    private const byte InterruptMarker = 0xFF;
    private const byte RepeatMarker = 0xFE;

    public static IRegisterDumpPlayer Load(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        int headerRead = stream.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false);
        if (headerRead < HeaderSize
            || header[0] != Magic[0] || header[1] != Magic[1] || header[2] != Magic[2]
            || header[3] != EndOfTextMarker)
        {
            throw new FormatException("Doesn't look like a .PSG file: invalid header signature.");
        }

        byte version = header[4];
        byte frequencyByte = header[5];
        int frameRateHz = version >= 10 && frequencyByte != 0 ? frequencyByte : 50;

        var frames = new List<RegisterFrame>();
        var current = new byte[RegisterFrame.RegisterCount];

        int b = stream.ReadByte();
        while (b >= 0)
        {
            if (b == InterruptMarker)
            {
                bool envelopeShapeWritten = false;
                b = stream.ReadByte();
                while (b >= 0 && b != InterruptMarker && b != RepeatMarker)
                {
                    int register = b;
                    int value = stream.ReadByte();
                    if (value < 0)
                    {
                        break; // file truncated mid (register, value) pair
                    }

                    if (register < RegisterFrame.RegisterCount)
                    {
                        current[register] = (byte)value;
                        if (register == 13)
                        {
                            envelopeShapeWritten = true;
                        }
                    }
                    // registers 14/15 (I/O ports) and 16-252 (non-AY device extensions) are ignored

                    b = stream.ReadByte();
                }

                frames.Add(new RegisterFrame(current, envelopeShapeWritten));
                continue; // b already holds the next marker/EOF — handled without reading again
            }

            if (b == RepeatMarker)
            {
                int n = stream.ReadByte();
                if (n < 0)
                {
                    break;
                }

                int repeatCount = n * 4;
                for (int i = 0; i < repeatCount; i++)
                {
                    frames.Add(new RegisterFrame(current, envelopeShapeWritten: false));
                }

                b = stream.ReadByte();
                continue;
            }

            // Only 0xFF/0xFE were expected at the top level — defensively skip an unexpected byte.
            b = stream.ReadByte();
        }

        var metadata = new RegisterDumpMetadata { FrameRateHz = frameRateHz };
        return new RegisterDumpPlayer(metadata, frames);
    }

    public static IRegisterDumpPlayer Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }
}
