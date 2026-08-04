namespace Yamaha.Psg.Formats.Pt3;

/// <summary>
/// PT3 tone-period lookup table: 96 entries (8 octaves x 12 notes, C-1..B-8 per the pattern note
/// encoding — see README_pt3.txt), each the AY tone generator period for that note. See
/// docs/PT3_TABLES.md for the derivation history.
/// </summary>
/// <remarks>
/// This is the "ASM or PSC" table (frequency-table selector byte 2, the value every ground-truth
/// file tested so far uses) for PT3 versions &gt;= 4 (<c>PT3NoteTable_ASM_34_35</c> in real player
/// terminology) - hardcoded from a real player's table rather than derived by formula. It was
/// originally computed here as `round(clock / (16 * frequency))` at a 1,750,000 Hz reference clock
/// (see docs/PT3_TABLES.md for how that clock constant was found), which matches this real table at
/// 93 of its 96 entries - but the formula rounds the top 3 entries (notes 93-95, the extreme high
/// end of the range) one unit high (e.g. note 95: formula gives 14, real table has 13). Found by
/// diffing against a real Vortex Tracker PSG export of a track that actually uses note 95
/// ("Pator - Digital Espresso...pt3" - no previously-tested file exercised the top 3 notes at all).
/// Confirmed against the real table's values (Volutar/pt3player, MIT license, `pt3player.c`'s
/// `PT3NoteTable_ASM_34_35` - read only for these factual numbers, no code copied, same status as
/// the AY/YM DAC tables in DAC_TABLES.md and the volume tables in Pt3VolumeTables.cs) - replaced the
/// formula with these exact values rather than special-casing the 3 divergent entries, since the
/// table is what real playback actually uses. **Still-open risk, unchanged from milestone 11.1**:
/// this is only the table for selector byte 2 / version &gt;= 4 - the other 3 selector values
/// (0=ProTracker, 1=SoundTracker, 3=RealSound) and the version &lt;= 3 variants of each are genuinely
/// different tables (per the real player's source and ptdoc_pt3_format_ru.txt's version-history
/// addenda) and are not yet implemented; every ground-truth file tested so far happens to use
/// selector 2 with version &gt;= 4, so this has not yet been a practical problem.
/// </remarks>
internal static class Pt3NoteTables
{
    public const int NoteCount = 96;

    private static readonly ushort[] Table =
    [
        0xD10, 0xC55, 0xBA4, 0xAFC, 0xA5F, 0x9CA, 0x93D, 0x8B8, 0x83B, 0x7C5, 0x755, 0x6EC,
        0x688, 0x62A, 0x5D2, 0x57E, 0x52F, 0x4E5, 0x49E, 0x45C, 0x41D, 0x3E2, 0x3AB, 0x376,
        0x344, 0x315, 0x2E9, 0x2BF, 0x298, 0x272, 0x24F, 0x22E, 0x20F, 0x1F1, 0x1D5, 0x1BB,
        0x1A2, 0x18B, 0x174, 0x160, 0x14C, 0x139, 0x128, 0x117, 0x107, 0x0F9, 0x0EB, 0x0DD,
        0x0D1, 0x0C5, 0x0BA, 0x0B0, 0x0A6, 0x09D, 0x094, 0x08C, 0x084, 0x07C, 0x075, 0x06F,
        0x069, 0x063, 0x05D, 0x058, 0x053, 0x04E, 0x04A, 0x046, 0x042, 0x03E, 0x03B, 0x037,
        0x034, 0x031, 0x02F, 0x02C, 0x029, 0x027, 0x025, 0x023, 0x021, 0x01F, 0x01D, 0x01C,
        0x01A, 0x019, 0x017, 0x016, 0x015, 0x014, 0x012, 0x011, 0x010, 0x00F, 0x00E, 0x00D,
    ];

    public static ushort Period(int noteIndex) => Table[noteIndex];
}
