namespace Yamaha.Psg.Core.Output;

/// <summary>
/// How <see cref="MultiChipAySoundChip"/> handles the final PCM range when summing multiple
/// already-mixed chips together — see <see cref="StereoMixer.ClampToShortRange"/> /
/// <see cref="StereoMixer.SoftLimitToShortRange"/>.
/// </summary>
public enum MixLimiter
{
    /// <summary>Today's original behavior: truncate at the short boundaries. The default, so existing consumers see no change.</summary>
    HardClip,

    /// <summary>Compress towards the boundary instead of truncating once several chips panned the same way add up past full scale.</summary>
    SoftLimit,
}
