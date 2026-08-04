# PT3 tables: derivation and open risks

## Source

Format knowledge comes from ["How to decode a Vortex Tracker II 'PT3' File"](http://www.deater.net/weave/vmwprod/pt3_player/README_pt3.txt)
by Vince Weaver — a public technical write-up (not code, not GPL) based on reverse engineering and
cross-checking against AY_emul's output. As with VTX/lh5 (milestone 8), no code is copied from any
tracker/player project (ZXTune and AY_emul are GPL) — only the documented behavior is used, and the
implementation below is original.

## Frequency (tone period) table

The PT3 pattern note byte encodes one of 96 notes (`$50`-`$AF`): 8 octaves x 12 semitones,
`C-1` at index 0 through `B-8` at index 95 (`A-4` is therefore index 45: `4*12 + 9`).

The source document states the frequency tables "can in theory be calculated at runtime" — i.e.
they are a derived engineering artifact (a period lookup for an equal-tempered scale), not
hardware-measured data like the DAC amplitude curves in [DAC_TABLES.md](DAC_TABLES.md). We derive
our own table with the standard AY tone-generator formula:

```
frequency(i) = 440 * 2^((i - 45) / 12)          // 12-tone equal temperament, A-4 = 440 Hz
period(i)    = round(clock / (16 * frequency(i)))
```

using `PsgClockPresets.ZxSpectrum` (1,773,400 Hz) — PT3 has no clock field of its own (unlike VTX),
and Vortex Tracker II natively targets the ZX Spectrum, so this is the natural default. Sample
values: `period(0)` (C-1) = 3389, `period(45)` (A-4) = 252, `period(95)` (B-8) = 14 — all comfortably
inside the AY's 12-bit tone period range across all 8 octaves.

**Known open risk**: the format header (offset `$63`) actually selects one of 4 distinct frequency
tables (`PT3NoteTable_PT`/`ST`/`ASM`/`REAL`, with a different set of 4 for tracker versions <=3.3
vs. 3.4+) — real player routines apparently differ slightly in period calculation between these.
The real test file the user provided (`EA - Proudly Loneliness ... .pt3`, version 7 i.e. PT3 3.7)
selects table #2, i.e. `PT3NoteTable_ASM_34_35`. We do not yet have confirmed numeric values or an
exact algorithm for how the 4 variants differ from each other, so `Pt3NoteTables` currently applies
the single derived table above regardless of the selector byte. This will be validated by ear once
playback exists (milestone 11.3) against the real file, the same "implement the plausible design,
verify by listening, revise if wrong" approach used for the envelope generator (milestone 2 -> 3).

## Volume (amplitude x channel volume) table

A sample step carries a 0-15 amplitude; a channel additionally has a 0-15 volume (pattern command
`$C1`-`$CF`). No numeric source for the real combination table was found (the README only notes
that "multiple 256-byte amplitude/volume lookup tables" exist and differ by tracker version).

**Working hypothesis** (`Pt3VolumeTables.Combine`), to be validated the same way as the frequency
table: `combined = round(amplitude * volume / 15)` — a straightforward bilinear scaling. This is
the single biggest unresolved accuracy risk in the PT3 milestone; expect to revisit it after the
first real-file listening test.

## Interpreter (milestone 11.3) — additional open risks

Header offsets for the raw parser (milestone 11.2) were empirically resolved against the real test
file (see `Pt3HeaderParser`'s remarks). The interpreter (`Pt3Interpreter`/`Pt3ChannelState`) adds a
few more design decisions the source document itself leaves ambiguous or doesn't state numerically.
All are implemented as a plausible, clearly-flagged working hypothesis, to be corrected after the
user listens to a real-file render — the same process used throughout this project (envelope
generator: milestone 2 -> 3; digi-drum frame-write bug: milestone 7).

**Sample step mixer bits (byte 1, bits 4 and 7) — RESOLVED after milestone 11.4 listening found real
bugs.** The first implementation (bit 6 = noise enable, tone always on) produced exactly the
symptoms you'd expect from wrong mixer bits: missing bass/drums from the start of `MmcM -
Hibernation...pt3`, garbage noise on the side channels, silence where there should be sound. Cross-
checked against an independent PT3 player's decode logic
([benbaker76/PT3PlayBlazor](https://github.com/benbaker76/PT3PlayBlazor), itself derived from
Sergey Bulba's/ayfly's GPL-licensed work) — used strictly as a *behavioral* reference (read to
understand bit meanings, no code copied or adapted), the same policy already applied to AY_emul/
ZXTune elsewhere in this project. Two real corrections, both now implemented in `Pt3SampleStep`/
`Pt3ChannelState.ComputeTick`:
  1. **Byte 0 bit 0 (`HasEnvelope`) is inverted** from the milestone-11.2 guess: envelope is used
     when the bit is *clear*, not set.
  2. **The actual mixer bits are byte 1 bit 4 (tone) and bit 7 (noise)** — not bit 4+6 as
     README_pt3.txt's own hedged phrasing suggested. Both are already stored active-low (1 =
     disabled), matching AY register R7 directly — no inversion needed when writing to R7.
Bit 6 (`AccumulateTone`) was independently confirmed correct by the same cross-check — unchanged.

**`AccumulateTone` persistence — a second real bug, found from a second round of listening** (the
mixer/envelope fix above removed clipping and brought bass/drums partway back, but the user still
heard chaos and something like a timing shift specifically where bass/drums played). The bug: the
per-channel tone accumulator was only ever *read* on a step where that step's own `AccumulateTone`
bit was set - on a step with the bit clear, the code fell back to that step's raw offset alone,
silently discarding whatever had accumulated. The reference implementation always adds the
accumulator into the tone calculation every tick (accumulator persists until the next note
retrigger), and only *updates* it on steps where `AccumulateTone` is set. This distinction is
exactly what makes a "digi-drum" pitch sweep work: a short run of `AccumulateTone` steps compounds
tick after tick into a fast rising/falling frequency sweep (the classic AY percussion trick), not a
single fixed pitch shift - the old code couldn't produce that sweep at all, which is consistent with
drum/bass samples specifically sounding wrong. Fixed in `Pt3ChannelState.ComputeTick`; also switched
the final tone period from clamping to masking with `& 0xFFF` (matching real hardware, which just
has a 12-bit register that wraps - clamping would flatten a sweep into a stuck plateau at the top of
the range instead of letting it wrap). Regression-tested in `Pt3InterpreterTests.
Play_AccumulateTone_PersistsAcrossStepsEvenWhenTheBitIsClear`.

**Newly discovered, not yet implemented**: the same reference shows byte 0 bits 4-1 (an unsigned
0-15 magnitude, sign given separately by byte 0 bit 5 — `Pt3SampleStep.EnvelopeSlideValue`/
`EnvelopeSlideUp`, parsed but currently unused) drive a per-tick nudge to either the shared envelope
period *or* the channel's own noise period, selected by byte 1 bit 7 (`NoiseDisabled`, doing double
duty), and byte 1 bit 5 (`SlideValuePersists`) controls whether that nudge keeps applying on later
steps. Not wired into `ComputeTick` yet — a further open risk, not yet linked to any reported
symptom, candidate for a future revisit if the render still sounds wrong after the bit-4/bit-7 fix.

**Envelope shape value** — the pattern command's 4-bit type field (`$10`-`$1F` bits 0-3, or
`$B2`-`$BF` bits 0-3 minus 1) is written directly as the R13 hardware shape value, with no
translation table. This assumes Vortex Tracker's "envelope type" is already the literal AY shape
code, since the source document gives no separate mapping (unlike the frequency/volume tables,
which it explicitly flags as needing one). Wrong envelope shapes in the render would point here.

**Ornament application** — README_pt3.txt only says ornament values are "applied to the notes" for
"?? effects" (its own uncertainty). Implemented as: `noteIndexUsed = clamp(note + ornamentValue, 0, 95)`
— the ornament's signed byte is a semitone offset added to the current note, standard arpeggio-style
behavior. Not independently confirmed.

**Envelope enable is two-layered**: a pattern-level command (`$10`-`$1F`/`$B2`-`$BF`/`$B0`) turns the
channel's envelope use on/off, and each sample step independently carries its own `HasEnvelope` bit
(byte 0 bit 0, parsed in milestone 11.2). A tick actually uses the envelope (R8/9/10 bit 4) only when
*both* are true - this layering isn't stated outright by the source document, but is the only reading
consistent with the per-step `HasEnvelope` bit's existence at all (otherwise it would be redundant).

**Shared envelope generator across channels**: R11-R13 are single chip-wide registers (one hardware
envelope generator), but PT3 lets any of the 3 channels issue an envelope command on the same row.
`Pt3Interpreter` resolves this by always parsing channels in A, B, C order and letting the last one
to issue a command in a given row win — an arbitrary but deterministic tie-break; real compositions
presumably avoid conflicting envelope commands on the same row across channels in practice.

**Note-skip pattern-boundary reset**: `SkipLinesRemaining` is reset to 0 whenever a channel starts a
new pattern (`Pt3ChannelState.StartPattern`), so a skip that hasn't finished counting down never
bleeds into the next pattern's own first row. Not stated in the source; a defensive design choice.

## `$10`-`$1F` (envelope command) byte layout — README_pt3.txt was wrong, found from real playback

The single most impactful bug found so far. README_pt3.txt describes this command's parameters as
`period (2 bytes, big-endian) + delay (1 byte) + sample number (1 byte)`. Implemented literally, real
playback was missing bass/drums almost entirely - cross-checking against a real file
(`MmcM - Hibernation...pt3`, channel B, pattern 0) by hand-decoding the raw bytes showed the "sample
number" byte this produced was 64, past the end of the 32-slot sample table - the channel silently
got a null sample and stayed inaudible for the rest of the note. That's consistent with the reported
symptom (missing bass/drums, or garbled sound) across three different real files - `$10`-`$1F` is a
very commonly used command wherever a track uses hardware envelope for bass/drums.

Cross-checked against the same behavioral reference used for the sample-step mixer bits
([benbaker76/PT3PlayBlazor](https://github.com/benbaker76/PT3PlayBlazor) - read only, no code
copied): **there is no delay byte at all.** The real layout is `period (2 bytes, big-endian) + sample
number (1 byte, halved)` - the sample byte uses the exact same "value/2" encoding as the `$F0`-`$FF`
command, which README_pt3.txt itself documents correctly elsewhere but apparently didn't connect to
this command. `$10` (envelope disable) has the same fix: its one parameter byte is also `value/2`,
not a plain index. Both are fixed in `Pt3ChannelState.ParseNextLine`, with the corrected byte counts
regression-tested in `Pt3ChannelStateTests`. The other effect byte layouts (`$01`-`$05`, `$08`,
`$09`) were independently cross-checked against the same reference and all matched README_pt3.txt
exactly - this specific command was the one place the source document's own description was wrong.

**Still open**: the reference's vibrato (`$05`) parameter order (`OnOff_Delay` then `OffOn_Delay`)
wasn't fully cross-checked against this project's `VibratoOffDelay`/`VibratoOnDelay` naming/order -
possible the two are swapped. Not yet linked to a reported symptom; revisit if needed.

## `$B1` (note-skip) hold count — off by one, found from a progressive-drift symptom

After the `$10`-`$1F` fix restored bass/drums, the user reported them still not landing on the beat -
drifting later over time. README_pt3.txt's own phrasing ("next byte is how many lines to skip for
this note") reads as "N *additional* rows skipped beyond this one" - what the first implementation
did (`SkipLinesRemaining = N`, holding the note for `N+1` total rows). Cross-checked against the same
reference's row-advance driver (`PT3_Play_Chip`): each channel has a `Note_Skip_Counter` that
decrements once per row-boundary and only triggers a new row-parse at exactly 0, and gets reset to
the raw `$B1` byte value right after a parse - meaning that byte is the *total* row count a note
holds for (this row plus `N-1` more), not `N` additional ones. Fixed as
`SkipLinesRemaining = value - 1`. Because `$B1` is a very commonly used command (any track with
syncopated or polyrhythmic parts leans on it constantly), the one-row-too-long hold from the old
formula compounded across every use, which is exactly what "drifts and lands later over time" looks
like - a per-use constant error, not a random glitch, so its effect accumulates monotonically through
a song. Regression-tested in `Pt3ChannelStateTests.ParseNextLine_NoteSkip_HoldsForNTotalRowsIncludingTheCurrentOne`.

## `$C0` (note off) missing state reset — found in the same cross-check pass

The reference's `$C0` handler resets sample/ornament step position and every slide/vibrato/tone-
accumulator state, the same reset a fresh note ($50-$AF) gets - this project's implementation only
set `Muted`/`EnvelopeEnabled` and left the rest untouched. A channel muted and later retriggered
(very common for drums, which mute-then-restart on nearly every beat) would resume mid-step in its
sample/ornament and with stale effect progress instead of starting clean. Fixed by having `$C0` call
the same `ResetPerNoteState()` helper a note retrigger uses (`Pt3ChannelState.cs`).

## Pattern advancement rearchitected — channel A alone drives it, not "wait for all 3"

The original interpreter loop waited for all 3 channels to independently reach their own `$00`
before moving to the next pattern-order entry. A user-reported symptom (a kick drum that should hold
a steady beat instead firing extra hits, getting audibly denser through the song) traced back to
this being architecturally wrong. Cross-checked against the reference's row-advance driver
(`PT3_Play_Chip`): it's tick-boundary-driven (one boundary = one `Speed` countdown, matching a real
player's per-interrupt cadence), and **only channel A's own stream position is ever tested against
`$00`**, only to decide whether to advance the whole song to the next pattern. Channels B and C never
independently "finish" - whenever A advances, B/C's read positions get forcibly reseated to the new
pattern regardless of what they were doing (mid-held-note or not), and otherwise they progress purely
on their own per-channel skip countdown, decoupled from A and from each other.

This explains the reported symptom precisely: the old "wait for all 3" design, combined with the
original `SkipLinesRemaining` reset in `StartPattern` (see the entry above it in this document),
meant a channel mid-hold got cut short and re-parsed the instant *any* channel's stream reached `$00`
- an event with no real timing relationship to that channel's own intended hold duration. Rearchitected
`Pt3Interpreter.Play` to be tick-boundary-driven with channel-A-only pattern advancement, matching the
reference. `Pt3ChannelState.ParseNextLine` no longer treats `$00` as ending anything by itself in the
row-scan sense (that decision moved entirely to the interpreter, and only for channel A) - though see
the next section for why it still stops defensively on `$00`, just without acting on it.

**Real-file regression found and fixed while verifying this**: removing `$00` handling from
`ParseNextLine` entirely (matching the reference's dispatch table, which has no explicit "quit on
$00" branch) crashed on `EA - Proudly Loneliness...pt3` with an "unrecognized effect code" exception
partway through the song. Root cause: channel B or C can legitimately reach *its own* row-content's
end (`$00`) on a different row-boundary than channel A, whenever that channel's skip usage differs
from A's - the reference's real-world safety here likely relies on properties of its actual byte
layout that weren't fully replicated. Rather than continuing to chase an exact behavioral match,
`ParseNextLine` now stops defensively the moment it sees `$00` in a row-scan - parking there without
consuming it (so a repeat call safely no-ops again) instead of reading past it into unrelated file
data. Regression-tested directly against the real file that crashed
(`Pt3InterpreterTests.Play_RealFile_ProducesAPlausibleNonEmptyFrameSequence`).

## Ground-truth verification method: diffing against a real Vortex Tracker PSG export

The user installed Vortex Tracker itself and exported `MmcM - Hibernation...pt3` to `.psg` directly
from the original software - a genuine ground truth, not a re-implementation. Since our own PT3
interpreter already produces the exact same `RegisterFrame` shape `PsgFileReader` does, the two can
be diffed register-by-register, tick-by-tick, which is far more precise than audio comparison. This
is now the primary verification method for remaining PT3 accuracy work (the fixture pair lives in
`tests/Yamaha.Psg.Formats.Tests/fixtures/user_provided/`, `.psg` + `.pt3` with the same base name).

**Two more real, confirmed bugs found this way**, both invisible to audio-only comparison because
they're small/subtle individually but compound across a whole song:

1. **Note-period table reference clock was wrong.** A systematic ~1.2-1.4% tone-period offset was
   present from frame 0 of the diff. Solved by algebra on the ground-truth data (for a note whose
   period is known, `clock = our_period * 16 * frequency`): the PT3 note table is baked from
   **1,750,000 Hz**, not the real ZX Spectrum hardware clock (`PsgClockPresets.ZxSpectrum`,
   1,773,400 Hz) that actual chip playback uses. Verified exactly (not just closely) against 3
   independent notes. `Pt3NoteTables.NoteTableReferenceClockHz` now holds this separately from the
   real playback clock in `Pt3Interpreter`. All the `Pt3TablesTests` expected values were
   re-derived from this corrected table (also confirmed against the diff).

2. **Volume table was a linear-scaling guess; the real thing is a lookup table, and amplitude
   sliding (byte 0 bits 7/6 of a sample step) wasn't wired in at all.** Diffing showed a channel's
   amplitude sitting flat where the reference smoothly faded (e.g. 15→14→14→13→...). Cross-checked
   against the same behavioral reference used for the mixer-bit and `$10`-`$1F` fixes: amplitude
   actually *slides* every tick while the bit is set (a running ±15 accumulator added to the sample
   step's own amplitude, clamped 0-15, persisting until note retrigger - the exact same shape as the
   tone accumulator), and the slid amplitude is combined with channel volume via one of **two
   16x16 lookup tables** (selected by PT3 version: `<= 3.4` vs `>= 3.5`), not
   `round(amplitude*volume/15)`. Both real tables are now in `Pt3VolumeTables.cs`. After this fix,
   our register output matches the Vortex Tracker export **exactly, frame-for-frame, through frame
   11** (previously the first mismatch was frame 3).

## `$B1` (note-skip) hold is persistent, like Volume, not a one-shot value

The frame-12 divergence above (channel A's notes landing twice as often as the reference - the
symptom the user described as "everything doubled/rushed") was root-caused with the same diff: row 0
of channel A has an explicit `$B1` (hold 2 rows), which matched the reference exactly; the very next
row change (no `$B1` at all in its raw bytes) *still* needed to hold for 2 rows to match the
reference, which a one-shot reading of `$B1` cannot produce.

Fix: `$B1`'s value is **persistent** channel state (`Pt3ChannelState.NoteHoldRows`, defaulting to 1 -
"no hold"), exactly like `Volume` or `SampleIndex` - set only when `$B1` appears, but *reapplied
after every row this channel parses*, not just the row that sets it. Previously the row-hold
countdown (`SkipLinesRemaining`) was computed once, inside the `$B1` case itself; now it's
recomputed unconditionally at the end of every `ParseNextLine` call, from whatever `NoteHoldRows`
currently holds.

**Result**: total frame count now matches the Vortex Tracker export *exactly* (4724 both sides -
previously 4487 vs 4724, a ~5% shortfall that compounded into "everything sounds sped up/doubled").
The first divergence moved from frame 12 to frame 468 (98% of the way through the song firing
byte-identical registers). Regression-tested (`Pt3VsVortexPsgTests`, frames 0-467 + exact frame-count
match).

## Frame-468 divergence: three separate bugs in how a muted channel is handled

The frame-468 divergence above (channel B going silent where the reference keeps playing) turned out
to be three separate bugs, all in how a muted/inactive channel is handled, all found and fixed
together via the same PSG-diff method:

1. **`$C0` (note off) was wrongly clearing `EnvelopeEnabled`.** An earlier, unverified addition (not
   actually checked against the reference at the time it was written) assumed muting should also
   turn off envelope use, so a later plain retrigger (no fresh envelope command of its own) would
   come back on the flat/non-envelope amplitude path instead of resuming envelope-driven amplitude -
   exactly the "goes silent instead" symptom. The reference's `$C0` handler never touches this flag,
   only mutes; fixed by removing the line entirely.
2. **The tone period register was being zeroed while muted; the reference just holds the last
   value.** Harmless for what's heard (amplitude 0 is silent either way) but broke the byte-exact
   comparison. Fixed with a persistent `_lastTonePeriod` field, only overwritten while the channel is
   actually audible.
3. **Sample/ornament step position was advancing even while muted; the reference gates this on
   audibility, same as the tone/amplitude computation itself.** A muted-then-retriggered channel
   must resume its sample at step 0 (already handled by the retrigger's state reset), not wherever an
   ungated advance silently walked it to while muted.

A fourth, related fix in the same area: **the R7 mixer bits were being forced to "disabled" while
muted; the reference defaults them to "enabled".** The reference's mixer-bit accumulation only ever
sets a bit when a channel actively contributes one - a muted channel's slot in the shared mixer byte
is simply never touched, and an untouched bit reads as enabled (0, active-low) by construction. This
required splitting `Pt3ChannelTick.NoiseOn` (still audible-gated - decides whether this channel's
`NoisePeriod` overwrites the *shared* noise-period register) from a new `MixerNoiseOn` (defaults
`true`, purely for R7 encoding, never gated by audibility).

**Result**: first divergence moved from frame 468 to frame 496. Regression bound in
`Pt3VsVortexPsgTests` raised accordingly.

## Glissando-family effects ($01/$02/$05/$08) applied their first step one tick too early

Found immediately after the frame-468 fixes, at the new frame-496 divergence: channel B's tone
period was a constant **+22 off** from the reference, starting the instant a note carrying a `$01`
glissando was retriggered, and staying off by that same amount for every later tick (expected, since
the running tone offset is a cumulative sum - one wrong early step shifts everything after it by a
fixed amount). Traced by dumping the channel's raw per-tick state (sample step, tone accumulator,
`EffectToneOffset`) tick-by-tick around the divergence: the base note period itself was already
correct (`Pt3NoteTables.Period(47) == 221`, matching the reference at frame 496), but our own output
was `243 == 221 + 22` - one glissando step already applied on the very same tick the note (and the
effect) was set.

The reference's timing is: the row that specifies a `$01`/`$02`/`$05`/`$08` effect still gets its
register write *that same tick* using the **unmodified** base value (the just-set note's period
untouched, vibrato's initial on-state untouched, the pre-effect envelope period untouched) - the
effect's own per-tick timer only starts counting from the *next* tick. The bug was a fencepost in
each effect's tick-counter reset: `ApplyEffect` reset the relevant counter (`_glissandoTickCounter`,
`_vibratoTickCounter`, `_envGlissandoTickCounter`) to `0`, and `ComputeTick` pre-increments before
comparing to the delay - so with a delay of 1 (the common case), the very first `ComputeTick` call
after the effect was set already fired. Fixed by resetting each counter to `-1` instead of `0`,
shifting every effect's first application to the tick after the one that specified it. Applies
uniformly to all four effects since they share the identical increment-then-compare mechanic;
`Pt3InterpreterTests`' glissando/portamento/vibrato/envelope-glissando tests all updated to the
now-confirmed-correct (one-tick-later) timing.

**Result**: first divergence moved from frame 496 to frame 512. Regression bound raised accordingly.

## The shared noise-period register (R6): three separate bugs, found via ptdoc.txt

The frame-512 divergence above turned into a much longer investigation, ultimately fixed by a second
source document the user supplied directly: `ptdoc.txt` ("Формат модуля Pro Tracker v3.7x"), a real
PT3 module-format spec covering ground README_pt3.txt didn't - the exact sample-format bit layout and
an explicit statement about which channel carries the noise-offset pattern command. Overall result
across all three fixes below: total byte-exact frames for the one real file under test went from
**41% to 88%** (548/4724 frames still differ, all still isolated to R6 and its knock-on effect on
R7/R9 in a handful of spots - see "still open" at the end).

**Bug 1 - the noise-offset *pattern command* ($20-$3F) is channel-B-exclusive, but that's not the
whole picture.** `ptdoc.txt` states outright: **"#20-#3f - указать смещение шума (бывает только в
канале B)"** ("...only ever occurs in channel B"), and, describing the pattern-data block layout,
**"Данные по смещению шума ... компилируются в канал B"** ("noise-offset data compiles into channel
B"). True as far as it goes - channels A and C never carry a `$20`-`$3F` byte - but treating this as
"R6 is *always* driven by channel B" (an intermediate, then-plausible reading) turned out to be
incomplete; see Bug 3.

**Bug 2 - each sample step's byte0 bits 5-1 field is dual-purpose, and PT3 only implemented one half.**
`ptdoc.txt`'s sample-format section: **"N4-0 - частота шума ИЛИ смещение огибающей (зависит от наличия
маски шума): смещение огибающей 0-15 - вниз, 16-31 - вверх (N4 интерпретируется как знак)"** ("N4-0 is
noise frequency OR envelope offset, depending on the noise mask: envelope offset 0-15 = down, 16-31 =
up, N4 as sign"). `Pt3SampleStep.EnvelopeSlideValue`/`EnvelopeSlideUp` had been parsed since milestone
11.4 but never consumed by `ComputeTick` (a documented, known gap - see the type's own remarks). Found
by tracing a recurring pattern in the diff: a drum sample's noise value climbed by a fixed step every
tick it was active (e.g. 6, then +3, +3, +3...) rather than jumping straight to a flat value - the
exact same shape as the tone accumulator. Fixed by adding `Pt3ChannelState.NoiseAccumulator`,
mechanically identical to `ToneAccumulator`/`AccumulateTone`: when the active step has noise enabled
(byte1 bit7 clear), its raw 5-bit value (0-31, unsigned - the up/down split is specific to the
*envelope* interpretation, not this one) is added to the accumulator, which becomes this channel's own
`NoisePeriod`; whether the addition *persists* for future steps is gated by byte1 bit5
(`SlideValuePersists`), same shape as `AccumulateTone`. Also required making this reset on note
retrigger/`$C0` (`NoiseAccumulator = 0` in `ResetPerNoteState`) - and, separately, making `NoisePeriod`
itself reset to 0 on retrigger *unless* the same row also carries a fresh `$20`-`$3F` (tracked via a
local `noiseSetThisRow` flag in `ParseNextLine`, since that command's parse happens earlier in the
same token-scan loop than the end-of-row retrigger reset, and must survive it) - confirmed by a case
where switching to a sample that never touches noise left the register stuck holding an unrelated
earlier sample's accumulated value instead of the reference's 0. The still-unimplemented other half
(byte1 bit7 *set* → the same field is an envelope-period nudge instead) is left alone: the shared
envelope-period register (R11/R12) already matches the reference exactly everywhere observed so far
without it, so there's no evidence yet that it's needed.

**Bug 3 - R6 isn't "channel B always"; it's "whichever channel(s) currently have noise enabled in
their own mixer bit," same as the very first (wrong) guess this session, corrected.** Bug 2 revealed
that the per-step noise accumulator is generic sample-step machinery, usable by *any* channel's own
sample data - `ptdoc.txt`'s channel-B-exclusivity claim is specifically about the pattern *command*
($20-$3F), a narrower, different fact. Confirmed directly: at one point in the file, channel A's own
R7 noise-mixer bit turns on mid-note (no pattern command involved, purely from A's own sample step),
and the reference's R6 output visibly switches from channel B's held value to channel A's own
(freshly-reset-to-0) value at exactly that tick. Fixed by reintroducing a priority mechanism (A, B, C
order, last one wins for a given tick) - the *same shape* as an earlier, explicitly-reverted "guess"
from earlier in this session, except this time backed by A/C's `NoisePeriod` actually being
meaningful (Bug 2's fix), rather than always 0 as it was before. Renamed the tick-level flag from the
old `NoiseOn` to `DrivesSharedNoise` (defaults `false`, unlike `MixerNoiseOn` which defaults `true`
while muted) to make clear it answers "does this channel's own `NoisePeriod` win the shared register
this tick," not "is noise audible in this channel's own mix."

**Result**: 512 → 794 → 1678 → 4212 (frames differing dropped through each fix in turn); overall
41% → 64% → 70% → 83% → 88% byte-exact across the whole file, in the order: Bug 1 (channel-B-only,
later partly superseded by Bug 3) → Bug 2 (accumulator) → the retrigger-reset half of Bug 2 → Bug 3
(priority). `Pt3VsVortexPsgTests` gained a second test locking in the overall (not just prefix) rate
as a non-regression floor, since the confirmed-correct *contiguous* prefix is still capped at frame
512 by the still-open issue below.

**Still open, narrow, found while confirming Bug 3**: frame 512 itself, plus a small number of
similar-shaped spots later in the file (roughly 2% of all frames, per the latest full diff) show a
reference value that doesn't match any channel's own currently-computed `NoisePeriod` at all - e.g.
at frame 512 the reference shows 16 where the only channel with an active `$20`-`$3F` command that row
decodes unambiguously to 8 (raw bytes `1E 00 53 04 40 CF 28 B1 01 78`). Ruled out so far: it isn't the
glissando-family one-tick-late timing (applying that shift here breaks the very next transition, which
the reference applies immediately); a larger recurring instance (frames 2064-2303 and 2576-2815, ~240
frames each - almost certainly the same musical phrase reused) shows the reference holding a small,
slowly-climbing value (1, 1, 1, ..., 2, 2, ..., 11...) across long stretches where *no* channel in our
model currently computes `DrivesSharedNoise = true` at all, meaning something continues to feed the
shared register a nonzero value during ticks our model treats as "nothing is driving it, hold the
last value." Not yet root-caused; likely a fourth, smaller piece of this same noise-register puzzle
rather than a new, unrelated bug, given how tightly bugs 1-3 already reduced the divergence. Next
step: same PSG-diff/raw-byte-trace method used above, focused on this specific recurring 240-frame
passage.

## `$02` (tone portamento) was wrongly treated identically to `$01` (glissando)

Reported directly by ear, independent of the PSG-diff work above: after the note-hold fix, a few
notes (heard ~4 times across a track) played as a long sliding "pew"/"wheee" where the original has
a short, clean note. `$01` and `$02` share a mechanism (periodically add a step to a running tone
offset) but are not otherwise the same effect, confirmed against the same behavioral reference used
for the mixer-bit and `$10`-`$1F` fixes: `$01` (glissando) has no target and keeps sliding until
something else replaces it - what the first implementation did for *both* effects, which is exactly
right for `$01` but wrong for `$02`. `$02` (portamento) slides **from the previous note to the new
one and automatically stops on arrival**: the row's note byte sets the target, the channel's
`NoteIndex` is then reverted to the *previous* note, and the running tone offset climbs/falls toward
`period(target) - period(previous)` in steps of the file's magnitude byte (sign taken from the
delta, not the file), snapping to the exact target note (and clearing the offset) the tick it
reaches or passes that delta - never overshooting or continuing past it, unlike `$01`. Implemented
as `Pt3ChannelState.PortamentoTargetNote`/`PortamentoTargetDelta`, reset on note retrigger/`$C0`
like the other effect state. Regression-tested in both `Pt3ChannelStateTests` (the values `$02`
computes) and `Pt3InterpreterTests.Play_Portamento_SlidesToTargetNoteThenStops` (full tick-by-tick
slide-then-settle behavior).

## `MmcM - How are you` frame 74: a single isolated note-retrigger anomaly, not a `$09` or direction bug

Investigated as a follow-up to the 11.4.1 ground-truth-diff session's open-risk list (which had
described this as "channel C gets a note retrigger on the same row as a `$09` (set speed) command,
and the reference appears to fully ignore that retrigger"). That description turned out to be a
mischaracterization once tested properly - both hypotheses it implied were disproved with real data
from this file:

- **Not about `$09` co-occurrence**: a channel-C-only trace of every note retrigger from song start
  shows `$09` (set speed) present on essentially *every* retrigger in this passage (frames 16, 32, 48,
  64, 74, 80, 90 all have it) - it's just the composer's normal way of writing this section, not
  something unique to the one that fails.
- **Not about note direction (down vs up)**: a full-file scan (`Pt3NoteTables`-based expected period
  vs. the reference `.psg`, cross-checked against our own engine's actual output to exclude
  ornament/glissando false positives) found **1221/1221 upward retriggers matching and 1405/1406
  downward retriggers matching** across the whole song, all three channels. Frame 74 (channel C,
  note 52 -> 50, a downward move) is the **only mismatch in 2627 total retriggers** - direction has no
  general correlation with correctness; a structurally identical row 16 frames later (frame 90, also
  `Volume + $09 + note + speed`) matches the reference exactly.

Given this is a single frame out of an entire song (0.04% of all retriggers), with no discovered
general rule that explains it (both plausible theories were tested and falsified against real data,
not just this one spot), it is being recorded here as a known, non-blocking, isolated anomaly rather
than chased with a speculative code change - the same category as Hibernation's still-unexplained
frame-512 noise anomaly above. A speculative fix aimed at this one row risks either overfitting to
this exact byte sequence (of no use for any other file) or breaking one of the other 2626 currently-
correct retriggers if the guessed "general rule" is wrong (concretely: any rule based on "note +
`$09` + Volume prefix" would immediately break frame 90, which has that exact shape and already
matches). Revisit once more real `.pt3`+`.psg` ground-truth pairs are available - if this shape of
anomaly recurs across multiple independent files, that would be enough signal to find the actual
rule; a single occurrence in one file is not.

## `MmcM - How are you` envelope-period (R11/R12) divergence — root cause found, fix attempted and reverted (regressed Hibernation)

Investigated the second open risk from the 11.4.1 session (R11/R12 diverging in this file, never
seen in Hibernation). A full-file scan found 242/8476 frames where R11/R12 (or R13) mismatch, in
three separate stretches (frames 496-511, 5146-5183, 8288-8293+). The shape is telling: the
reference's envelope period climbs by a constant +3 every single tick for the whole stretch while
ours stays flat - i.e. a genuine, active `$08` (envelope glissando) effect that the reference keeps
running but we silently stop.

**Root cause, confirmed by tracing internal channel state tick-by-tick**: channel B has an active
`$08` (`EnvGlissandoDelay=1, EnvGlissandoStep=3`) running right up to frame 495. At frame 496,
channel B's own pattern row retriggers a plain note (`$50`-`$AF`, no new `$08` on that row) - our
`ResetPerNoteState()` (called for every note retrigger, `Pt3ChannelState.cs`) unconditionally zeroes
`EnvGlissandoDelay`, stopping the slide. The reference does not stop it: R11/R12 keep climbing by the
same +3/tick straight through the retrigger, until something later (an explicit new envelope command,
outside the traced window) overwrites it. The three divergent stretches all end exactly where some
channel's *next* explicit envelope command resets R11/R12 to a fresh absolute value in both engines
anyway, which is why the reference and our output silently re-converge afterward regardless of the
bug - masking it until the next long gap between envelope commands.

**Why this isn't fixed yet**: `$08` is the one effect in this file that manipulates *shared*, chip-
wide state (R11/R12) rather than anything private to the channel, so "a per-channel note retrigger
has no business silencing a chip-wide effect" is a reasonable-sounding rule - but testing it against
Hibernation (which has zero R11/R12 divergence today) proved it wrong as stated:
- Never resetting `EnvGlissandoDelay` in `ResetPerNoteState()` at all: Hibernation's overall
  byte-exact rate collapsed from 548 to 3684 differing frames out of 4724.
- Only excluding the reset for a plain note retrigger (keeping it for `$C0`/mute, via a
  `resetEnvGlissando` parameter): still 1944 differing frames - much better than the first attempt,
  but still a large regression from 548.

Both attempts were reverted; the code is back to the original, unconditional reset. Hibernation
apparently has many legitimate cases where a note retrigger *should* cut off an in-flight envelope
glissando, so "shared chip-wide state survives any per-channel note event" is too broad a rule as
formulated - there must be a narrower, more specific condition (tied to something not yet identified:
possibly which channel currently "owns" R7's envelope-relevant bits, whether the retriggering row
also carries its own volume/sample command, or something else) that this investigation did not find.
Recorded here, per the same "document, don't guess further on high regression risk" policy as the
frame-74 anomaly above - revisit with more ground-truth files, or if a cleaner distinguishing signal
turns up.

## Two more ground-truth pairs (2026-08-03 continuation): confirms the two open issues recur, and surfaces a real Version-6-vs-7 divide

The user supplied two more real `.pt3`+`.psg` pairs to gather cross-file statistics, per the "wait
for more data before guessing further" plan from the sections above:
`MmcM - Man of Art (2015) (Multimatograf 11, 1)` and `Pator - Digital Espresso (2023) (Revision 2023, 12)`
(both added to `tests/Yamaha.Psg.Formats.Tests/fixtures/user_provided/`).

**`Man of Art` (Vortex Tracker II header, `Version=6`, `FrequencyTable=2` - same dialect as both
Hibernation and How-are-you)**: frame count matches exactly (8676 both sides), 81.6% byte-exact
overall (1599/8676 differ). Per-register diff breakdown is the single most useful finding here:
**R0-R5 (all 3 tone periods) are 100% exact, R7-R10 (mixer/amplitude) are 100% exact, R13 (envelope
shape) is 100% exact - only R6 (noise, 1491 frames) and R11/R12 (envelope period, 204+57 frames) show
any divergence at all.** Both are exactly the two open issues already identified and documented above
(the noise-register puzzle from the 11.4.1 session, and the envelope-glissando-cut-off-by-retrigger
issue from this session) - no new bug category shows up in this third independent file. This is good
signal that **these two issues are real, recurring, general bugs worth prioritizing a proper fix for**
(not one-off anomalies like the frame-74 case), once a correct general rule can be found - exactly the
kind of cross-file confirmation the "wait for more data" plan was hoping for.

**`Pator - Digital Espresso` (`"ProTracker 3.7 compilation of..."` header, `Version=7`,
`FrequencyTable=2`)**: frame count matches (8569 both sides) but only 61.9% byte-exact (3268/8569
differ, first divergence at frame 28 already) - and critically, **every single register (R0-R13)
shows some divergence**, unlike Man of Art's clean R0-R5/R7-R10/R13. The user pointed out the header
difference directly: `EA - Proudly Loneliness` (the only other `Version=7`/"ProTracker 3.7
compilation" file we have) has *never* been ground-truth-diffed against a real `.psg` export before -
it was only ever structurally parsed (milestone 11.2) and listened to, never run through
`Pt3VsVortexPsgTests`-style byte comparison. So this isn't a regression in the Version-6/Vortex-
Tracker-II code path (Man of Art still works about as well as Hibernation/How-are-you) - **it's the
first time the Version-7/"ProTracker 3.7 compilation" path has been ground-truth tested at all**, and
it's revealing real gaps, not necessarily the same bugs.

Two concrete things found in Digital Espresso, not yet fixed:
1. **A single-unit note-table rounding edge case at the very top of the note range**: at frame 28,
   channel A genuinely plays note 95 (`B-8`, the highest of the 96 notes - confirmed a real byte in
   the file, not a parsing error) where our table gives period 14 (`Math.Round(13.8412)`) but the
   reference has 13 (`(int)13.8412`, i.e. truncation). Tested the obvious fix (switch
   `Pt3NoteTables.BuildTable` from `Math.Round` to plain truncation) against the *whole* Hibernation
   file - regressed catastrophically (548 -> 4616 differing frames out of 4724), because most of the
   table's fractional parts are naturally < 0.5 where round and truncate agree, and Hibernation's
   already-matching notes apparently rely on proper rounding at exactly the boundary cases truncation
   would flip. **Reverted immediately** (`Pt3NoteTables.cs` back to `Math.Round`). This single frame-28
   case remains unexplained - global truncation is conclusively the wrong fix, so whatever's really
   happening at the extreme top of the range (if it's even a table-derivation issue at all, rather
   than something else specific to that note/effect) needs a different theory.
2. **A much larger cascading divergence starting at frame 36**: channel B retriggers from note 22 to
   19, but the reference keeps note 22's tone period, plus R7 (mixer) collapses from `0x30` to `0x00`
   and R8-R10 (amplitude) go through several wrong-looking states in the next couple of frames -
   shaped similarly to the already-documented "ignored retrigger" pattern (frame 74 in How-are-you),
   but with more registers affected at once here. Not investigated further this session (time/scope) -
   flagged as the concrete next thing to dig into for this file, likely still within the "Version 7 /
   ProTracker 3.7 compilation" code path that's now known to be under-tested.

## Systematic read-through of the real player's `PatternInterpreter`/`ChangeRegisters` found two more real bugs

Following the user's suggestion ("read through the real player method by method, cross-checking each
part, and sooner or later we'll have covered all the behavior"), read `pt3player.c`'s entire pattern-
command switch and per-tick register computation line by line (not just the commands already touched
by a reported symptom) and cross-checked every branch against this project's equivalent. Found two
more real, confirmed discrepancies:

1. **`OrnamentPosition` (`Position_In_Ornament`) resets on far more commands than implemented.** The
   real source resets it to 0 inside `$10`, `$11`-`$1F`, `$40`-`$4F`, `$B0`, `$B2`-`$BF`, `$C0`,
   `$50`-`$AF` (note), and `$F0`-`$FF` - i.e. essentially *any* command that touches
   envelope/sample/ornament selection, not just a plain note retrigger, `$C0`, or `$B0` (the only
   three this project had). Added the missing reset to `$10`, `$11`-`$1F`, `$40`-`$4F`, `$B2`-`$BF`,
   and `$F0`-`$FF`. This turned out to be the single biggest fix of the whole continuation: **Digital
   Espresso improved from 1429 to 743 differing frames (83.3% -> 91.3% byte-exact)** - evidently this
   file uses ornament/sample-switching commands (`$40`-`$4F` especially) far more heavily than the
   other three, which is why it went unnoticed until a file that actually exercises it was tested.
   Man of Art (100.00%), Hibernation (36/4724), How-are-you (17/8476) all unchanged - zero regression.

2. **Ornament note-index overflow uses an 8-bit wraparound quirk, not plain clamping.** The real
   source computes `j = (uint8_t)(Note + ornamentByte)` (a genuine byte-truncating addition, not
   full-range signed math) and then applies `j >= 128 -> 0` (lowest note), `j > 95 -> 95` (highest
   note). Because `Note` maxes at 95 and an ornament byte's signed range maxes at +127, a `Note +
   ornamentOffset` sum of *96 or higher can land anywhere in 96-255* depending on exact values - and
   this implementation's `>= 128` check can't distinguish "wrapped around from a large negative
   offset" from "a large *positive* sum that happens to exceed 127" - both collapse to `j=0` (lowest
   note) instead of clamping to the top. `ptdoc_pt3_format_ru.txt`'s own addendum admits upward
   overflow is "not defined" - this is this specific player's resolution of that ambiguity, reproduced
   exactly (`(NoteIndex + ornamentOffset) & 0xFF`, then the same two-tier check) rather than replaced
   with a cleaner `Math.Clamp`, since it's a confirmed real quirk. Only matters for extreme ornament
   offsets pushing several octaves past the table's ends; not isolated to a specific frame count in
   the fix above (bundled into the same change/measurement).

## Digital Espresso's remaining R11 divergence: a third instance of "ignored chip-wide command", not yet resolved

Investigated the largest remaining category after the ornament fix above (R11/R12, 373 of 743
differing frames). Traced one concrete instance precisely: at pattern row 0x14 (~frame 1418), **both**
channel A (`$BD` = `$B2`-`$BF`, shape 12/period 44) **and** channel B (`$BD`, same shape 12/period 44)
independently issue the identical fresh envelope command on the same row - confirmed directly from the
raw pattern bytes, not a parsing ambiguity. Yet the reference PSG keeps the *old* envelope period (28,
carried from two rows earlier) completely unchanged through this row and several after it, as if
neither channel's command took effect at all.

This doesn't fit a simple "last-channel-wins tie" explanation (both channels agree on the same value,
so tie-break order can't be the cause), nor a "duplicate value is suppressed" theory (28 -> 44 is a
genuine change, and other genuine changes in the same passage, e.g. 35 -> 28 one row earlier, *do*
take effect), nor a command-type distinction (`$10`-`$1F` vs `$B2`-`$BF` - both types appear among the
succeeding rows too). It's the same *shape* of anomaly as the two previously-accepted "ignored
retrigger" cases (How-are-you frame 74; Digital Espresso's own channel-B/vibrato case earlier in this
document) - but this is the first instance affecting the *envelope* command rather than a note
retrigger, and the first involving two channels agreeing rather than one channel acting alone.

**Not resolved this session** - per the user's explicit call to stop here after a long, otherwise very
productive continuation. Recorded as the next lead: a systematic full-file scan (similar to the
2627-retrigger up/down study done for How-are-you) counting *every* row where two-or-more channels'
envelope commands should combine or agree, cross-referenced against the reference's actual R11/R12 at
that frame, would be the natural next step - single-instance byte archaeology has now hit diminishing
returns on this specific question three times in a row (across two different files and two different
command categories).

**Takeaway confirmed**: the user's proposed method - read the whole real implementation systematically
rather than only chasing symptoms found via diffing - paid off immediately, finding the ornament-reset
bug that ground-truth diffing alone hadn't surfaced yet (no test file's specific divergence had been
traced to it directly; it was found by comparing code, not symptoms). Worth continuing this pass over
any remaining unread parts of `pt3player.c` (header/version parsing, TS/multi-chip mode, the tempo/
`DelayCounter` mechanism) before returning to symptom-driven diffing for what's left.

**Takeaway for future sessions**: when a new ground-truth file shows an unusually low match rate,
check the header magic prefix and `Version` field first (`Pt3HeaderParser`'s `KnownMagicPrefixes` /
`module.Version`) before assuming it's a new bug in already-tested code - `Version=7`/"ProTracker 3.7
compilation" is a materially less-tested code path than `Version=6`/"Vortex Tracker II" right now.

## Two real fixes found via a primary-source player implementation (2026-08-03, same continuation)

The user found and supplied the full `ptdoc_pt3_format_ru.txt` (a fuller version of the source
already cited above, with version-history addenda from 2002-2007 - saved to `docs/` this session)
and then a link to [Volutar/pt3player](https://github.com/Volutar/pt3player) (MIT license) - a real,
runnable C implementation of a PT3 player. Its source was read carefully (not copied - same policy as
every other behavioral reference used in this project) to resolve two things the addenda pointed at
but didn't fully spell out, and both turned into real, validated, zero-regression fixes.

### Note table: replaced the derived formula with the real table's exact values

`Pt3NoteTables.cs`'s formula (`round(clock / (16*frequency))` at 1,750,000 Hz - see the "Frequency
(tone period) table" section above) matched the real player's `PT3NoteTable_ASM_34_35` (the table for
frequency-selector byte 2, version >= 4 - i.e. every ground-truth file tested so far) at 93 of 96
entries exactly, but rounded the top 3 (notes 93, 94, 95 - the extreme high end of the range) one
unit too high (e.g. note 95: formula gives 14, real table has 13). This exactly explained the note-95
mismatch found earlier in `Pator - Digital Espresso...pt3` (the only file that happens to use notes
this high). Rather than special-case 3 entries, replaced the whole table with the real hardcoded
values (read from `pt3player.c`, MIT-licensed - factual/measured numbers, not copied expression, same
status as the AY/YM DAC tables and PT3 volume tables already sourced this way). Result: Digital
Espresso improved from 3268 to 2796 differing frames (32.6% still differ, but a real improvement);
Hibernation/How-are-you/Man-of-Art are byte-for-byte unchanged (they never touch notes 93-95) - a
clean, zero-regression fix, confirmed across all 4 files before keeping it.

This also permanently answers the "which table for which selector/version" open risk from milestone
11.1, for the ONE combination every real file so far actually uses (selector 2, version >= 4) - the
real player's source shows selector byte 2 with version <= 3 uses a *different* table
(`PT3NoteTable_ASM_34r`), and selectors 0/1/3 are different again, none of which any tested file uses
yet. Still not implemented; flagged as the same open risk, just narrower now.

### `$01` (glissando) with a raw delay of 0: version-dependent, not "apply every tick"

The milestone-11.4 guess (`GlissandoDelay = Math.Max(delay, 1)`, i.e. treat 0 the same as 1 - apply
every tick) was wrong. The real player's source shows the delay byte drives a plain countdown
(`Ton_Slide_Count`) that only fires when it reaches exactly 0 by decrementing - a raw 0 never reaches
0 *by decrementing*, so by itself it would just never fire, forever. Two real, version-dependent
behaviors follow from that single mechanical fact:
- **Version < 7**: a raw delay of 0 on `$01` is a genuine no-op - the glissando never fires for that
  note at all. Our own `GlissandoDelay > 0` guard in `ComputeTick` already produces exactly this once
  `GlissandoDelay` is set to the raw 0 instead of being floored to 1 - no new logic needed for this
  half of the fix, just removing the floor.
- **Version >= 7**: the real source has an explicit one-line special case
  (`if (Ton_Slide_Count == 0 && Version >= 7) Ton_Slide_Count++`) that bumps a raw 0 up to 1 *once*,
  at effect-set time only. Traced through the real per-tick update carefully (register writes happen
  *before* that tick's own countdown-and-maybe-apply step, so a value written this tick is only
  rendered starting the *next* tick): this produces exactly one application of the step, one tick
  after being set (same timing as any other single glissando application - matches this project's
  already-validated `_glissandoTickCounter = -1` convention), after which the countdown resets to the
  *original* delay (0 again, not re-bumped) and is permanently stuck below the `> 0` guard - freezing
  `EffectToneOffset` at that one-shot value for the rest of the note. This matches
  `ptdoc_pt3_format_ru.txt`'s v3.7x addendum ("если delay=0, то указанное смещение прибавляется к
  ноте на всём её протяжении") precisely, now that the actual mechanism behind that sentence is
  understood rather than guessed at.

Implemented as a new `_glissandoOneShotThenFreeze` flag on `Pt3ChannelState`, set only for
`$01`+delay=0+version>=7; `ComputeTick` forces `GlissandoDelay` to 0 right after that one application
fires, and the flag is cleared in `ResetPerNoteState` like the rest of the per-note effect state.
`$02` (portamento) was checked too - the real source's version-bump special case is only present in
the `$01` branch, so `$02`+delay=0 is a plain no-op at every version (just removed its own `Math.Max`
floor to match, no one-shot logic needed there). `ParseNextLine`/`ApplyEffect` now take the module's
`Version` (threaded from `Pt3Interpreter`, which already had it for the volume-table lookup) to
decide this. Result: Digital Espresso improved further, 2796 to 2792 differing frames - a small but
real, zero-regression gain (confirmed unchanged on all other 3 files); this technique is evidently
rare even in a `Version=7` file, but the fix is unconditionally correct per the real source, not a
guess tuned to one data point.

## The envelope-glissando-cut-off-by-retrigger bug: root-caused and fixed by reading the real source's full per-tick envelope computation

Continuing to read the same real player's source (Volutar/pt3player, MIT - read only, no code
copied) found the actual root cause of the envelope-period (R11/R12) divergence this whole 11.4.1
continuation kept circling back to, and both of the earlier two failed fix attempts (reverted
earlier in this document) turned out to share the same underlying mistake.

**The real per-tick formula** (`func_play_tick`/`ChangeRegisters` in the source): every tick, the
envelope period register is computed fresh as

```
R11/R12 = EnvBase + AddToEnv + CurEnvSlide      (all three summed, then masked to 16 bits - no clamp)
```

where the three terms are architecturally independent and never conflated:
- **`EnvBase`** - the chip-wide envelope period as last set by an explicit `$10`-`$1F`/`$B2`-`$BF`
  command. Never touched by a note retrigger, `$C0`, or anything else.
- **`AddToEnv`** - reset to exactly 0 at the *start of every tick*, then freshly summed from
  whichever channels' *currently active sample step* has the noise-disabled bit set (the dual-purpose
  byte0-bits-5-1 field in its *envelope* interpretation - see the "shared noise-period register"
  section above for the analogous *noise* interpretation of the same field). Each channel keeps its
  own persistent accumulator for this (reset on that channel's own note retrigger, exactly like the
  existing `NoiseAccumulator`) - contributions from multiple simultaneously-active channels genuinely
  **add together** here (unlike the noise register, which is last-channel-wins/overwrite).
- **`CurEnvSlide`** - the *chip-wide* running accumulator driven only by `$08` (envelope glissando).
  Persists across note retriggers and pattern boundaries on *any* channel - the only thing that resets
  it is a fresh `EnvBase` (i.e. `Cur_Env_Slide`/`Cur_Env_Delay` are unconditionally zeroed inside the
  `$10`-`$1F`/`$B2`-`$BF` handlers, every time). A fresh `$08` itself only updates the step/delay for
  *future* ticks, it does **not** reset the already-accumulated slide value.

**Why the two earlier fix attempts failed**: this project's original architecture stored
`EnvGlissandoDelay`/`EnvGlissandoStep` *per channel* on `Pt3ChannelState`, and folded both the
`$08`-driven slide *and* (once envelope-mode-sample-step support existed) any sample-driven
contribution into the *same* mutable `currentEnvelopePeriod` variable in the interpreter, adding
deltas to it permanently. Trying to make that per-channel state "survive a retrigger" (to fix `$08`
correctly) *also* prevented the per-tick sample contribution from resetting the way it must every
tick regardless of retriggers - conflating two genuinely different sources of state that the real
architecture keeps completely separate. That mismatch, not the reset-timing rule itself, is why
removing the reset regressed Hibernation catastrophically (548 -> 3684, then -> 1944) both times.

**The fix**: separated the three sources to match the real architecture exactly.
- `Pt3ChannelState.EnvGlissandoDelay`/`EnvGlissandoStep` (per-channel) removed entirely; `$08` now
  writes `EnvSlideDelay`/`EnvSlideStep` onto `Pt3RowEffects` instead (chip-wide, reported upward
  exactly like `$09`'s speed and the envelope shape/period commands already are).
- `Pt3Interpreter.Play` now owns `envelopeBase`, `envSlideAccumulator`, `envSlideDelay`, `envSlideStep`
  (chip-wide $08 state, advanced once per tick, not once per channel) - reset only when a fresh
  envelope shape/period arrives via `rowEffects.Shape`; a fresh `$08` updates delay/step without
  touching the accumulator.
- Added the previously-unimplemented *envelope* interpretation of the dual-purpose sample-step field:
  `Pt3ChannelState.EnvelopeAccumulator` (new, mirrors `NoiseAccumulator` exactly, reset on retrigger),
  feeding a new `Pt3ChannelTick.EnvelopeContribution` output that the interpreter sums fresh across
  all three channels every tick (replacing the old, wrongly-permanent
  `int? EnvelopePeriodDelta` design).
- Final register value: `(envelopeBase + addToEnv + envSlideAccumulator) & 0xFFFF` - masked (wraps),
  not clamped, matching the real `uint16_t` arithmetic.

**Result, confirmed across all 4 ground-truth files before keeping**:
- Hibernation: unchanged, 548/4724 differing frames (R6/R7/R9 only, the still-open noise puzzle) -
  zero regression, confirming the earlier attempts' failure really was the per-channel/conflation bug,
  not the underlying "don't reset on retrigger" rule.
- Man of Art: 1599 -> 1491 differing frames, and **R11/R12 go to exactly 0/0** (was 204/57) - the
  envelope-period divergence is completely gone; only R6 (noise) remains.
- How are you: 1858 -> 1649 differing frames, and **R11/R12 also go to exactly 0/0**.
- Digital Espresso: 2796 -> 2640 differing frames; R11 375 and R12 4 (down from much higher) - a big
  improvement, though not fully resolved (R0/R2/R4/R6 etc. still diverge substantially there, likely
  the still-open frame-36-area cascading issue, unrelated to envelope).

This closes the open risk recorded earlier in this document ("Next open thread to resume with") for
Hibernation and How-are-you's envelope-period divergence entirely, and substantially narrows it for
Digital Espresso.

## The noise-register puzzle (open since 11.4.1): same root cause, same fix, solved the same way

Immediately after the envelope fix above, kept reading the same real player source for the
noise-period register (R6) - the puzzle flagged since the very first ground-truth session
("Hibernation's frame-512 anomaly", never root-caused despite three earlier fix rounds). It turned
out to be the *exact same architectural mistake*, just for R6 instead of R11/R12.

**Real formula**: `R6 = (Noise_Base + AddToNoise) & 0x1F` - two separate, additive terms:
- **`Noise_Base`** - chip-wide, set only by a `$20`-`$3F` pattern command (channel B only, per spec).
  Persists across rows *and* note retriggers on any channel - the **only** thing that resets it is the
  song advancing to a new pattern-order position (confirmed in the real source: zeroed exactly at the
  same place channel A's stream hits `$00` and the next pattern is loaded, nowhere else).
- **`AddToNoise`** - each channel's own per-sample-step accumulator contribution, last-channel-wins
  (A/B/C order) - this part of our architecture was already right (already matched the real
  overwrite/last-wins semantics, unlike envelope's sum).

This project's old code conflated both into one per-channel `NoisePeriod` field (reset to 0 on note
retrigger/`$C0` unless a `$20`-`$3F` happened on the very same row) - exactly the same category of
mistake as the envelope bug, just less consequential in practice since `Noise_Base` is set relatively
rarely in most tracks (which is likely why this one went unsolved through three earlier attempts
without anyone spotting the missing term).

**Fix**: `$20`-`$3F` now writes `Pt3RowEffects.NoiseBase` (chip-wide, reported upward like every other
shared command) instead of the per-channel `NoisePeriod` field; all the `noiseSetThisRow`-guarded
"reset NoisePeriod on retrigger unless just set" logic in `ParseNextLine` was deleted outright (dead
weight once the two sources are properly separated - the per-channel `NoiseAccumulator` reset in
`ResetPerNoteState` was already correct and sufficient on its own). `Pt3Interpreter.Play` now owns a
chip-wide `noiseBase` (reset only at pattern-order advance) alongside the existing `addToNoise`
(renamed from `currentNoisePeriod`, semantics unchanged); the register value is
`(noiseBase + addToNoise) & 0x1F`.

**Result, confirmed across all 4 files**:
- **Man of Art: 100.00% byte-exact** - all 8676 frames, all 14 registers, zero differences. Locked in
  permanently as `Pt3VsVortexPsgTests.Play_ManOfArt_MatchesVortexTrackerPsgExport_Completely`.
- **Hibernation: 548 -> 36 differing frames (99.24% byte-exact)** - and critically, the divergence is
  no longer scattered (the old "frame 512 anomaly plus assorted spots") but a single **solid
  contiguous block at the very tail of the file**: frames 4688-4723, the last 36 frames of a
  4724-frame song, R7/R9 only. The confirmed-correct prefix in `Pt3VsVortexPsgTests` was raised from
  512 to 4688 frames; the floor test tightened from 560 to 45. This last block is not yet root-caused
  - likely something specific to how the song's final pattern/loop-point is handled, a new and much
  narrower lead than "noise register, somewhere" was.
- **How are you: 1858 -> 17 differing frames (99.8% byte-exact)** - only tiny residual R4/R7/R8/R10
  divergence remains, plausibly related to (or the same as) the already-documented, deliberately-not-
  fixed frame-74 single-retrigger anomaly from earlier in this document.
- **Digital Espresso: 2640 -> 1918 differing frames (77.6% byte-exact, up from 69.2%)** - R6 alone
  dropped from 895 to 131 differing frames. Still substantial divergence remains across several
  registers (R0/R2/R4/R7/R8/R9/R10/R11 all still show some), most likely still the unresolved
  frame-36-area cascading issue flagged earlier - not yet re-investigated after this round of fixes.

**Takeaway reinforced**: both the envelope and noise bugs were the *same shape* of mistake (conflating
two-or-three architecturally independent additive sources into one mutable per-channel value) -
worth checking for this same pattern in anything else that touches shared chip state, if more
divergence categories turn up later.

## `$05` (vibrato): parameter byte order was backwards, and delay=0 was wrongly floored (found investigating Digital Espresso)

Digging into Digital Espresso's remaining divergence (channel B's tone period off by a fixed,
unexplained +256 for long stretches, with `EffectToneOffset`/`ToneAccumulator`/glissando all
confirmed inactive) traced to a `$05` (vibrato) two rows earlier: `05 B1 01 63 02 00` - effect flag,
a `$B1` hold reset, the note, then vibrato's own two parameter bytes `02 00`. Reading the real
player's source for the exact tick mechanism (`Current_OnOff`/`OnOff_Delay`/`OffOn_Delay`, structurally
identical to the glissando countdown already understood) found **two real, independent bugs**, both
previously flagged as an unverified open risk in this same document:

1. **Byte order was backwards.** README_pt3.txt's own field names ("YEStime, NOtime") and the real
   source's field order (`OnOff_Delay` set from the first parameter byte, `OffOn_Delay` from the
   second) both put the ON-phase duration first - this project had it reading the first byte into
   `VibratoOffDelay` and the second into `VibratoOnDelay`, backwards.
2. **A raw delay of 0 must not be floored to 1** (the milestone-11.4 guess, same category of mistake
   already fixed twice for `$01`/`$08`): the real mechanism is a plain countdown that only re-triggers
   by decrementing to exactly 0 from a positive value - a raw 0 for whichever phase is *currently
   active* means the channel gets stuck in that phase permanently once reached, mechanically
   identical to `$01`'s delay=0 case, except there's no version-gate for `$05` at all in the real
   source - this applies at every version. Digital Espresso's channel B has `$05` with off-delay=0,
   so the real player mutes it permanently (until a later note/`$C0` resets `Current_OnOff`) the first
   time the off-phase is reached - our old code, unable to ever get truly "stuck" (both delays always
   floored to >= 1), kept flickering the channel back on, rendering stale tone/sample-position state
   from ticks the real player was actually silent for. That silent/flickering mismatch, not any tone-
   table or accumulator bug, is what produced the oddly-fixed +256 period offsets - a channel that's
   supposed to be frozen mid-note versus one that's still live and advancing.

**Fix**: swapped which parameter byte feeds `VibratoOnDelay`/`VibratoOffDelay`, and changed the
per-tick check from unconditional (`if (++counter >= phaseDelay)`) to guarded exactly like glissando's
(`if (phaseDelay > 0 && ++counter >= phaseDelay)`), so a 0 for the active phase now permanently
disables further toggling instead of retriggering every tick. Both the existing unit test
(`Pt3ChannelStateTests.ParseNextLine_Vibrato_ActivatesWithBothDelays`) and integration test
(`Pt3InterpreterTests.Play_Vibrato_GatesAmplitudeOffAndOnPerConfiguredDelays`) were updated for the
corrected byte order/expected amplitude sequence.

**Result, confirmed across all 4 files**: Digital Espresso improved from 1918 to 1429 differing
frames (77.6% -> 83.3% byte-exact) - the single biggest jump of any fix this continuation besides the
noise/envelope architecture fixes. Man of Art (still 100.00%), Hibernation (still 36/4724), and
How-are-you (still 17/8476) are all byte-for-byte unchanged - zero regression, confirming this file's
particular vibrato-with-zero-delay usage wasn't exercised (or at least not consequentially) by any of
the other three ground-truth files. Digital Espresso still has substantial remaining divergence
(R0/R1 channel-A tone now the largest category, R11 envelope period still significant) - not yet
investigated further; likely more distinct issues remain in this file specifically, consistent with
it being the least-tested `Version=7`/"ProTracker 3.7 compilation" code path.

## Hibernation's new tail anomaly (frames 4688-4723): investigated, not resolved, recorded as an open lead

Traced this precisely: channel B holds a note for a long stretch (`$B1` hold=20, stream stuck at file
offset `0x1614` from well before frame 4650), then at frame 4688 (exactly at the pattern-order
transition from the second-to-last pattern into the last/loop pattern) finally parses a fresh row
there: `$B1 04` (new hold=4) immediately followed by `$C0` (note off). Our `$C0` handling was checked
line-by-line against the real player's source and matches it exactly (same fields reset, `Enabled =
false` -> tone held at last value, amplitude flat 0, mixer bits default enabled) - **this is not a
`$C0`-handling bug**. But the reference PSG shows *zero change whatsoever* in channel B's registers
from frame 4688 all the way to the file's end (4724) - it keeps rendering the *pre-`$C0`* state
(tone+noise both disabled via the sample's own mixer bits, envelope-driven amplitude 0), as if the
reference's channel B never actually reaches this row at all.

Since the real `$C0` output would look nothing like what's still being rendered, the most likely
explanation is that some earlier hold-duration bookkeeping (not `$C0` itself) makes the *real*
player's channel B stay stuck holding its note for longer than our `NoteHoldRows=20` computes -
possibly something specific to this exact pattern-order transition (which happens to also be the
loop point, `LoopPosition=19` of 20 positions) rather than a general `$B1` bug, since `$B1` clearly
works correctly everywhere else in this same file (99%+ match up to this point). Not root-caused this
session - recorded here as the next concrete lead for Hibernation, narrower and more precisely
characterized than the old scattered "frame 512 area" picture ever was.
