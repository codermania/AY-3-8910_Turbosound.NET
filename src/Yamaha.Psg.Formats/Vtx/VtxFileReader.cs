using System.Text;
using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;
using Yamaha.Psg.Formats.Common;

namespace Yamaha.Psg.Formats.Vtx;

/// <summary>
/// Reads the .VTX format (Roman Scherbakov / V_Soft). The header (16 bytes + 5 null-terminated
/// strings) carries metadata that PSG lacks: chip variant, clock frequency, panning — see the spec
/// at https://github.com/gasman/libayemu/blob/master/vtx_format.txt (read directly, not through an
/// intermediary). The data after the header is a bare lh5 stream (with the LHA archive container
/// already stripped by whoever created the VTX file), which unpacks into a column-wise ("structure
/// of arrays") frame layout (YM3-compatible): all R0 values across every frame first, then all R1
/// values, and so on. Register 13 (envelope shape) is encoded specially: a value of 0xFF means
/// "unchanged in this frame" rather than a 4-bit value (see RegisterFrame.EnvelopeShapeWritten).
/// </summary>
public static class VtxFileReader
{
    private const int HeaderSize = 16;

    public static IRegisterDumpPlayer Load(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.ToArray());
    }

    public static IRegisterDumpPlayer Load(string path) => Load(File.ReadAllBytes(path));

    private static IRegisterDumpPlayer Load(byte[] data)
    {
        if (data.Length < HeaderSize)
        {
            throw new FormatException("File is too short for a .VTX header.");
        }

        ChipVariant variant = (data[0], data[1]) switch
        {
            ((byte)'a', (byte)'y') => ChipVariant.Ay_3_8910,
            ((byte)'y', (byte)'m') => ChipVariant.Ym2149,
            _ => throw new FormatException("Doesn't look like a .VTX file: invalid chip identifier (expected 'ay' or 'ym')."),
        };

        int stereoMode = data[2] & 0x07;
        var panning = stereoMode switch
        {
            0 => PanningPreset.Mono,
            1 => PanningPreset.Abc,
            2 => PanningPreset.Acb,
            3 => PanningPreset.Bac,
            4 => PanningPreset.Bca,
            5 => PanningPreset.Cab,
            6 => PanningPreset.Cba,
            _ => PanningPreset.Abc, // value 7 is undefined by the spec — a safe default
        };

        int loopFrame = ReadUInt16LE(data, 3);
        int clockHz = unchecked((int)ReadUInt32LE(data, 5));
        byte frameRateByte = data[9];
        int frameRateHz = frameRateByte != 0 ? frameRateByte : 50;
        int year = ReadUInt16LE(data, 10);
        int unpackedSize = unchecked((int)ReadUInt32LE(data, 12));

        int pos = HeaderSize;
        string title = ReadNullTerminatedString(data, ref pos);
        string author = ReadNullTerminatedString(data, ref pos);
        string trackerName = ReadNullTerminatedString(data, ref pos);
        _ = ReadNullTerminatedString(data, ref pos); // music editor name — not kept, not important
        string comment = ReadNullTerminatedString(data, ref pos);

        if (unpackedSize % RegisterFrame.RegisterCount != 0)
        {
            throw new FormatException(
                $"Unpacked data size ({unpackedSize}) is not a multiple of {RegisterFrame.RegisterCount} — doesn't look like a YM3-compatible layout.");
        }

        var compressed = data.AsSpan(pos);
        byte[] unpacked = Lh5Decoder.Decode(compressed, unpackedSize);

        int frameCount = unpackedSize / RegisterFrame.RegisterCount;
        var frames = new List<RegisterFrame>(frameCount);
        var current = new byte[RegisterFrame.RegisterCount];

        for (int f = 0; f < frameCount; f++)
        {
            bool envelopeShapeWritten = false;

            for (int r = 0; r < RegisterFrame.RegisterCount; r++)
            {
                byte value = unpacked[(r * frameCount) + f];

                if (r == 13)
                {
                    if (value != 0xFF)
                    {
                        current[13] = value;
                        envelopeShapeWritten = true;
                    }
                    // value == 0xFF: R13 was unchanged in this frame, current[13] already holds the right value
                }
                else
                {
                    current[r] = value;
                }
            }

            frames.Add(new RegisterFrame(current, envelopeShapeWritten));
        }

        var metadata = new RegisterDumpMetadata
        {
            Title = title,
            Author = author,
            TrackerName = trackerName,
            Comment = comment,
            FrameRateHz = frameRateHz,
            ChipVariant = variant,
            Panning = panning,
            ClockHz = clockHz,
            LoopFrame = loopFrame,
            Year = year,
        };

        return new RegisterDumpPlayer(metadata, frames);
    }

    private static ushort ReadUInt16LE(byte[] data, int offset)
        => (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadUInt32LE(byte[] data, int offset)
        => (uint)data[offset]
         | ((uint)data[offset + 1] << 8)
         | ((uint)data[offset + 2] << 16)
         | ((uint)data[offset + 3] << 24);

    /// <summary>
    /// VTX header strings — bytes in an encoding the spec doesn't pin down precisely (likely a DOS
    /// codepage from the ZX Spectrum scene era). Latin1 preserves bytes 1:1 with no exceptions;
    /// this only affects how title/author/comment are displayed, not the audio.
    /// </summary>
    private static string ReadNullTerminatedString(byte[] data, ref int pos)
    {
        int start = pos;
        while (pos < data.Length && data[pos] != 0)
        {
            pos++;
        }

        string result = Encoding.Latin1.GetString(data, start, pos - start);

        if (pos < data.Length)
        {
            pos++; // skip the null terminator itself
        }

        return result;
    }
}
