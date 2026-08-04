namespace Yamaha.Psg.Core.Chip;

/// <summary>Variant and clock frequency of one chip within a <see cref="MultiChipAySoundChip"/>.</summary>
public readonly struct ChipConfig
{
    public ChipConfig(ChipVariant variant, int clockHz)
    {
        Variant = variant;
        ClockHz = clockHz;
    }

    public ChipVariant Variant { get; }
    public int ClockHz { get; }
}
