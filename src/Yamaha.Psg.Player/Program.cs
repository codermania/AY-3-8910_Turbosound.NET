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
    Console.WriteLine("For a TS/TurboSound .pt3 (2 chips): [--mix-limiter hardclip|softlimit] [--volume-scaling none|dividebychipcount|dividebysqrtchipcount]");
    return;
}

string inputPath = args[0];
string? wavPath = null;
bool live = false;
ChipVariant? variantOverride = null;
int? clockOverride = null;
int outputSampleRate = 44_100;
MixLimiter mixLimiter = MixLimiter.HardClip;
VolumeScaling volumeScaling = VolumeScaling.None;

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
        case "--mix-limiter":
            mixLimiter = string.Equals(args[++i], "softlimit", StringComparison.OrdinalIgnoreCase)
                ? MixLimiter.SoftLimit
                : MixLimiter.HardClip;
            break;
        case "--volume-scaling":
            volumeScaling = args[++i].ToLowerInvariant() switch
            {
                "dividebychipcount" => VolumeScaling.DivideByChipCount,
                "dividebysqrtchipcount" => VolumeScaling.DivideBySqrtChipCount,
                _ => VolumeScaling.None,
            };
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

// A real TS/TurboSound file is still just a `.pt3` on disk (there's no separate extension) - the
// only way to tell it apart from an ordinary single-module file is to look at its trailer, so `.pt3`
// files are read once up front and routed to whichever path applies.
if (string.Equals(Path.GetExtension(inputPath), ".pt3", StringComparison.OrdinalIgnoreCase))
{
    byte[] fileBytes = File.ReadAllBytes(inputPath);
    if (Pt3TsFileReader.IsTsContainer(fileBytes))
    {
        PlayTs(fileBytes);
        return;
    }
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

void PlayTs(byte[] fileBytes)
{
    IReadOnlyList<IRegisterDumpPlayer> modules = Pt3TsFileReader.Load(new MemoryStream(fileBytes));

    // PT3 carries no chip-variant/clock metadata for either module (same as an ordinary single
    // .pt3), so both chips default the same way the single-chip path does; --variant/--clock, if
    // given, apply to both chips uniformly since the CLI has no way to specify them per-chip yet.
    ChipVariant variant = variantOverride ?? ChipVariant.Ay_3_8910;
    int clockHz = clockOverride ?? PsgClockPresets.ZxSpectrum;

    if (modules[0].Metadata.Title is { Length: > 0 } title)
    {
        string author = modules[0].Metadata.Author is { Length: > 0 } a ? $" — {a}" : "";
        Console.WriteLine($"{title}{author}");
    }

    Console.WriteLine(
        $"TS/TurboSound: 2 chips, variant {variant}, clock {clockHz} Hz, "
        + $"frames: {modules[0].Frames.Count} + {modules[1].Frames.Count}");
    Console.WriteLine($"Headroom: mix limiter = {mixLimiter}, volume scaling = {volumeScaling}");

    // Real TurboSound setups pan the two chips apart (rather than stacking both dead-center) so the
    // extra 3 channels read as spatially distinct rather than just "louder" - ABC/ACB is a simple,
    // commonly-used choice for that; there's no per-file metadata to take this from instead.
    var chip = new MultiChipAySoundChip(
        outputSampleRate,
        PanningPreset.Abc,
        mixLimiter,
        volumeScaling,
        new ChipConfig(variant, clockHz),
        new ChipConfig(variant, clockHz));
    chip.SetChipPanning(0, PanningPreset.Abc);
    chip.SetChipPanning(1, PanningPreset.Acb);

    if (live)
    {
        using var sink = new LivePlaybackSink(outputSampleRate, channels: 2);
        SyncedMultiModulePlaybackDriver.Play(modules, chip, sink);
        sink.WaitUntilDrained();
        Console.WriteLine("Done (playback finished).");
    }

    if (wavPath is not null)
    {
        var buffering = new BufferingPcmSink();
        SyncedMultiModulePlaybackDriver.Play(modules, chip, buffering);
        WavWriter.Write(wavPath, buffering.ToArray(), outputSampleRate, channels: 2);
        Console.WriteLine($"WAV written: {wavPath}");
    }
}
