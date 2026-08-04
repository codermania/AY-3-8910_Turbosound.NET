namespace Yamaha.Psg.Core.Chip;

/// <summary>
/// DAC tables. The envelope table size matches the chip variant's native resolution (16 levels
/// for AY-3-8910, 32 for YM2149 — see <see cref="EnvelopeGenerator"/>), so the generator indexes
/// its own variant's table directly, with no rescaling. For the origin of these numbers and the
/// derivation of the fixed-volume table, see docs/DAC_TABLES.md.
/// </summary>
public static class DacTables
{
    /// <summary>AY-3-8910: 16 native envelope levels.</summary>
    public static readonly double[] AyEnvelopeLevels =
    [
        0.0,
        0.00999465934234,
        0.0144502937362,
        0.0210574502174,
        0.0307011520562,
        0.0455481803616,
        0.0644998855573,
        0.107362478065,
        0.126588845655,
        0.20498970016,
        0.292210269322,
        0.372838941024,
        0.492530708782,
        0.635324635691,
        0.805584802014,
        1.0,
    ];

    /// <summary>YM2149: 32 native envelope levels (twice the resolution of AY-3-8910).</summary>
    public static readonly double[] YmEnvelopeLevels =
    [
        0.0, 0.0,
        0.00465400167849, 0.00772106507973,
        0.0109559777218, 0.0139620050355,
        0.0169985503929, 0.0200198367285,
        0.024368657969, 0.029694056611,
        0.0350652323186, 0.0403906309606,
        0.0485389486534, 0.0583352407111,
        0.0680552376593, 0.0777752346075,
        0.0925154497597, 0.111085679408,
        0.129747463188, 0.148485542077,
        0.17666895552, 0.211551079576,
        0.246387426566, 0.281101701381,
        0.333730067903, 0.400427252613,
        0.467383840696, 0.53443198291,
        0.635172045472, 0.75800717174,
        0.879926756695, 1.0,
    ];

    /// <summary>
    /// AY-3-8910: the fixed volume path (R8-R10, bits 0-3) uses the same physical DAC as the
    /// envelope — here that's literally the same 16-level array.
    /// </summary>
    public static readonly double[] AyFixedVolumeLevels = AyEnvelopeLevels;

    /// <summary>
    /// YM2149: the 4-bit fixed volume (0-15) addresses the same 32-level DAC as the envelope,
    /// via odd indices (n -> 2n+1) — see docs/DAC_TABLES.md.
    /// </summary>
    public static readonly double[] YmFixedVolumeLevels = DeriveYmFixedVolumeLevels();

    public static double[] EnvelopeLevels(ChipVariant variant)
        => variant == ChipVariant.Ym2149 ? YmEnvelopeLevels : AyEnvelopeLevels;

    public static double[] FixedVolumeLevels(ChipVariant variant)
        => variant == ChipVariant.Ym2149 ? YmFixedVolumeLevels : AyFixedVolumeLevels;

    private static double[] DeriveYmFixedVolumeLevels()
    {
        var result = new double[16];
        for (int n = 0; n < 16; n++)
        {
            result[n] = YmEnvelopeLevels[(n * 2) + 1];
        }

        return result;
    }
}
