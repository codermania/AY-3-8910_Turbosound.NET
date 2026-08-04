using Yamaha.Psg.Core;
using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;
using Yamaha.Psg.Formats.Common;
using Yamaha.Psg.Formats.Psg;
using Yamaha.Psg.Formats.Pt3;
using Yamaha.Psg.Formats.Vtx;
using Yamaha.Psg.Player;

if (args.Length == 0)
{
    Console.WriteLine("Usage: Yamaha.Psg.Player <file.psg|.vtx|.pt3> [--wav <output.wav>] [--live] [--variant ay|ym] [--rate 44100] [--clock 1773400]");
    Console.WriteLine("For .vtx, chip variant/clock/panning default to the file's own metadata (--variant/--clock override them).");
    return;
}

string inputPath = args[0];
string? wavPath = null;
bool live = false;
ChipVariant? variantOverride = null;
int? clockOverride = null;
int outputSampleRate = 44_100;

for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--wav":
            wavPath = args[++i];
            break;
        case "--live":
            live = true;
            break;
        case "--variant":
            variantOverride = string.Equals(args[++i], "ym", StringComparison.OrdinalIgnoreCase)
                ? ChipVariant.Ym2149
                : ChipVariant.Ay_3_8910;
            break;
        case "--rate":
            outputSampleRate = int.Parse(args[++i]);
            break;
        case "--clock":
            clockOverride = int.Parse(args[++i]);
            break;
        default:
            Console.WriteLine($"Unknown flag: {args[i]}");
            return;
    }
}

if (!live && wavPath is null)
{
    wavPath = Path.ChangeExtension(inputPath, ".wav");
}

IRegisterDumpPlayer dump = Path.GetExtension(inputPath).ToLowerInvariant() switch
{
    ".vtx" => VtxFileReader.Load(inputPath),
    ".pt3" => Pt3FileReader.Load(inputPath),
    _ => PsgFileReader.Load(inputPath),
};

// For VTX, variant/clock/panning default to the file's own metadata — the format itself carries
// them (unlike PSG, which stores neither). CLI flags, if given, take priority.
ChipVariant variant = variantOverride ?? dump.Metadata.ChipVariant ?? ChipVariant.Ay_3_8910;
int clockHz = clockOverride ?? dump.Metadata.ClockHz ?? PsgClockPresets.ZxSpectrum;
PanningPreset panning = dump.Metadata.Panning ?? PanningPreset.Abc;

if (dump.Metadata.Title is { Length: > 0 } title)
{
    string author = dump.Metadata.Author is { Length: > 0 } a ? $" — {a}" : "";
    Console.WriteLine($"{title}{author}");
}

Console.WriteLine($"Chip: {variant}, clock: {clockHz} Hz, panning: {panning}, frames: {dump.Frames.Count}");

if (live)
{
    var chip = new AySoundChip(variant, clockHz, outputSampleRate, panning);
    using var sink = new LivePlaybackSink(outputSampleRate, channels: 2);
    FileDrivenPlaybackDriver.Play(dump, chip, sink);
    sink.WaitUntilDrained();
    Console.WriteLine("Done (playback finished).");
}

if (wavPath is not null)
{
    var chip = new AySoundChip(variant, clockHz, outputSampleRate, panning);
    var buffering = new BufferingPcmSink();
    FileDrivenPlaybackDriver.Play(dump, chip, buffering);
    WavWriter.Write(wavPath, buffering.ToArray(), outputSampleRate, channels: 2);
    Console.WriteLine($"WAV written: {wavPath}");
}
