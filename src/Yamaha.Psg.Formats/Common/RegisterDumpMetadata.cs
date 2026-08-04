using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;

namespace Yamaha.Psg.Formats.Common;

/// <summary>Metadata for a register dump. Fields a given format doesn't carry are left null.</summary>
public sealed class RegisterDumpMetadata
{
    public string? Title { get; init; }
    public string? Author { get; init; }

    /// <summary>Frame rate of the dump in Hz (usually 50 — the ZX Spectrum/PAL interrupt rate).</summary>
    public int FrameRateHz { get; init; } = 50;

    /// <summary>Chip variant, if the format carries it (e.g. VTX metadata) — otherwise null, left to the caller.</summary>
    public ChipVariant? ChipVariant { get; init; }

    /// <summary>Panning, if the format carries it (e.g. VTX metadata) — otherwise null.</summary>
    public PanningPreset? Panning { get; init; }

    /// <summary>Chip clock frequency in Hz, if the format carries it (VTX) — otherwise null, left to the caller.</summary>
    public int? ClockHz { get; init; }

    /// <summary>Frame number the loop starts at (VTX) — looped playback isn't implemented yet.</summary>
    public int? LoopFrame { get; init; }

    public int? Year { get; init; }
    public string? TrackerName { get; init; }
    public string? Comment { get; init; }
}
