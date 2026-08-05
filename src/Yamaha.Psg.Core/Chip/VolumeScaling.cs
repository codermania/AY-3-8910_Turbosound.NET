namespace Yamaha.Psg.Core.Chip;

/// <summary>
/// How <see cref="MultiChipAySoundChip"/> scales each chip's own DAC output tables before
/// rendering, to leave headroom for summing N chips together at the source rather than only
/// compressing/clipping after the fact (see <see cref="Output.MixLimiter"/>, a complementary,
/// downstream safety net). The DAC tables are logarithmic (see DacTables.cs), so a uniform scale
/// factor preserves the relative dB steps between volume/envelope levels — only the ceiling moves.
/// </summary>
public enum VolumeScaling
{
    /// <summary>No scaling (factor 1.0) — today's original behavior, unchanged.</summary>
    None,

    /// <summary>
    /// Divide every chip's DAC tables by the ensemble's chip count. Guarantees no clipping even in
    /// the worst case (every chip at max volume, perfectly in phase) — the classic "+6dB per
    /// doubling" headroom for summing correlated signals.
    /// </summary>
    DivideByChipCount,

    /// <summary>
    /// Divide every chip's DAC tables by the square root of the ensemble's chip count — headroom
    /// sized for energy-summing uncorrelated sources ("-3dB per doubling"), louder and livelier in
    /// the typical case. Rare moments where several chips peak in sync can still clip; pair with
    /// <see cref="Output.MixLimiter.SoftLimit"/> as a safety net for that case.
    /// </summary>
    DivideBySqrtChipCount,
}
