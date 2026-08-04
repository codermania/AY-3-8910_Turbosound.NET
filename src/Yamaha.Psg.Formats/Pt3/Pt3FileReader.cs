using Yamaha.Psg.Formats.Common;

namespace Yamaha.Psg.Formats.Pt3;

/// <summary>
/// Public entry point for an ordinary single-module .PT3 file: parses (<see cref="Pt3Module"/>) and
/// interprets (<see cref="Pt3Interpreter"/>) it into the same <see cref="IRegisterDumpPlayer"/> shape
/// as PSG/VTX, so it plays through the existing <c>FileDrivenPlaybackDriver</c> unchanged. For a real
/// TS/TurboSound file (2 modules + trailer, milestone 11.5), this reads only the first module.
/// </summary>
public static class Pt3FileReader
{
    public static IRegisterDumpPlayer Load(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.ToArray());
    }

    public static IRegisterDumpPlayer Load(string path) => Load(File.ReadAllBytes(path));

    private static IRegisterDumpPlayer Load(byte[] data) => Pt3Interpreter.Play(Pt3Module.Load(data));
}
