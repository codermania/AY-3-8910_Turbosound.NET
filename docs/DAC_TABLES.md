# DAC tables: provenance and rationale

## Envelope resolution: AY-3-8910 (16 steps) vs YM2149 (32 steps)

This is a documented difference between the chips, confirmed by multiple independent sources
(not just the volume table, but the resolution of the envelope generator itself):

- **AY-3-8910**: 4-bit internal envelope counter → 16 steps, period prescaler `/16`.
- **YM2149**: 5-bit counter → 32 steps, prescaler `/8`.

Given the same period register value (R11/R12), both chips complete a full envelope cycle in
**the same amount of time** (16×16 = 32×8 = 256 ticks per unit of period) — the YM2149 simply
does it in twice as many, finer steps. `EnvelopeGenerator` (see [EnvelopeGenerator.cs](../src/Yamaha.Psg.Core/Chip/EnvelopeGenerator.cs))
implements this via `MaxStep`/prescaler parameters that depend on `ChipVariant`, rather than a
shared 32-step generator with a table "fudge" — so the result is correct in both sound and timing.

Sources (behavioral description, not code):
- https://maidavale.org/blog/ay-ym-differences/
- https://forums.nesdev.org/viewtopic.php?t=18639

## DAC table numeric values

The `AyEnvelopeLevels` (16 entries) and `YmEnvelopeLevels` (32 entries) values in
[DacTables.cs](../src/Yamaha.Psg.Core/Chip/DacTables.cs) are published, independently measured
chip output levels for each envelope step, reproduced (as data, not code) from the
[ayumi](https://github.com/true-grue/ayumi) project's tables (Peter Sovietov, Unlicense/public
domain) — a source of real-hardware measurements widely recognized in the AY/YM emulation
community. The numbers themselves are facts (measurement results), not copyrightable; the code
around them (generator structure, timing, mixing logic) is original.

The original `AY_dac_table` in ayumi stores 32 entries, where each adjacent pair of indices
`(2n, 2n+1)` duplicates the same value — a literal reflection of the fact that the AY-3-8910
only has 16 physically distinguishable levels. Since our `EnvelopeGenerator` for AY already
natively operates in the `0..15` range (see above), the `AyEnvelopeLevels` table stores those 16
values without duplication — semantically the same, just without the redundancy.

## Fixed volume table (R8-R10, bits 0-3)

Fixed volume and the envelope share **the same physical DAC** on real hardware — just addressed
differently (4 bits directly, vs. a 5-bit envelope counter). Therefore:

- **AY-3-8910**: `FixedVolumeLevels` is literally the same 16-element array as
  `AyEnvelopeLevels` (resolutions match exactly, so no separate table is needed).
- **YM2149**: the 4-bit register addresses the same 32-level DAC via odd indices
  (`level[n] = YmEnvelopeLevels[2n + 1]`). Odd rather than even indices is a convention backed
  by the published fact that "envelope(YM) level 31 corresponds to maximum volume" (index 31 is
  odd, the last entry of the table); no separately, independently measured 16-level source for YM
  was found, so this is a documented approximation rather than a hardware measurement.

## Known simplification (superseded)

The plan originally called for keeping a single shared 32-step generator and reducing the AY/YM
difference to the DAC table alone. During milestone 3 it turned out real hardware also differs
in the resolution/timing of the generator itself (see above), and after clarifying with the user
the decision was revised in favor of the more accurate approach — `EnvelopeGenerator` is now
parameterized by `ChipVariant`. This note is kept so future edits don't accidentally reintroduce
the "simplified" variant.
