# Fixture provenance

**Not committed to the repo.** The `user_provided/` folder is gitignored — these are real
third-party tracks kept local-only (see `Legal status` in
`tests/Yamaha.Psg.Regression/fixtures/SOURCES.md` for the reasoning). Tests that use them skip
gracefully (via `UserProvidedFixture`/a plain `File.Exists` guard) when a file isn't present, so a
fresh clone builds and tests green without them — you just won't get the ground-truth PT3 coverage
described below until you drop your own copies in place.

## `user_provided/EA - Proudly Loneliness (2018) (DiHalt 2018, 4).pt3`
Provided by the user. A real ProTracker 3.7 module, from the DiHalt 2018 demoparty (4th place AY
music compo entry). Confirmed via hex dump to be a genuine TS/TurboSound 2-module container
(`...PT3!?PT3!?02TS` trailer). Used to develop and unit-test the PT3 raw parser (milestone 11.2)
against real header/sample/ornament/pattern data, not just synthetic fixtures — same legal
reasoning as `Yamaha.Psg.Regression/fixtures/SOURCES.md` (demoscene track, freely distributed by
its author for listening/use in emulators; used here only for local testing).

## `user_provided/MmcM - Hibernation (2016) (DiHalt Lite 2016, 1).pt3`
Provided by the user. A real single-module PT3 file, DiHalt Lite 2016 demoparty (1st place AY music
compo entry). Its header uses a different, older magic prefix ("Vortex Tracker II 1.0 module: ..."
free-form text instead of "ProTracker 3.X compilation of..."), discovered when testing milestone
11.3 against it — used to confirm `Pt3HeaderParser` handles both real-world header dialects (the
structural fields from offset $63 onward sit at the same fixed offsets regardless of which prefix
is present). Same legal reasoning as the file above.

## `user_provided/MmcM - Hibernation (2016) (DiHalt Lite 2016, 1).psg`
Provided by the user - exported directly from Vortex Tracker (the original authoring software) for
the same track as the `.pt3` file above. Used as ground truth to diff against our PT3 interpreter's
own register output, tick-by-tick (see docs/PT3_TABLES.md "Ground-truth verification method") - this
found the note-table clock and volume-table/amplitude-slide bugs documented there. Same legal
reasoning as the files above.

## `user_provided/MmcM - How are you (2016) (DiHalt 2016, 1).pt3` / `.psg`
Provided by the user - a second, independent track (same author, different demoparty entry) plus its
own Vortex Tracker PSG export, used the same way as the Hibernation pair above: a second ground-truth
diff to check whether fixes found on one file generalize, or whether this file exercises features the
first one didn't. As of the fixes documented in docs/PT3_TABLES.md through the noise-register work,
this file is 78% byte-exact overall with the frame count matching exactly (8476 both sides) - a good
sign the underlying architecture is sound - but it surfaced at least two not-yet-investigated new
issues (a note-retrigger-plus-`$09`-speed-change interaction, and envelope-period divergences not
seen in the Hibernation file). Both later investigated (see docs/PT3_TABLES.md): the first turned out
to be a single isolated anomaly (1 mismatch out of 2627 note retriggers, not a general rule), the
second's root cause was found (envelope glissando cut off by a per-channel note retrigger) but a fix
attempt regressed Hibernation badly and was reverted - both documented as open, not fixed.

## `user_provided/MmcM - Man of Art (2015) (Multimatograf 11, 1).pt3` / `.psg`
Provided by the user - a third independent track, same "Vortex Tracker II" header dialect and
`Version=6` as Hibernation/How-are-you, plus its own PSG export. Used to check whether the two open
issues above (noise-register puzzle, envelope-glissando-cut-off-by-retrigger) are general/recurring
or one-off: 81.6% byte-exact, frame count matches exactly (8676 both sides), and critically **only
R6 (noise) and R11/R12 (envelope period) diverge at all** - every other register is 100% exact. This
confirms both open issues are real, recurring bugs (not anomalies) worth a proper fix once the correct
general rule is found - see docs/PT3_TABLES.md. Same legal reasoning as the files above.

## `user_provided/CjSplinter, MmcM - Stellar one (2011) (DiHalt 2011, 1).pt3`
Provided by the user for milestone 11.5 (TS/TurboSound support). A real 2-module TS container -
confirmed by reading the trailer directly: `Type1="PT3!"`, `Size1=5495`, `Type2="PT3!"`, `Size2=8002`,
`TSID="02TS"`, and `Size1 + Size2 + 16 (trailer) == 13513` (the file's exact total length) - both
module sizes precisely account for the whole file with no gap or padding, confirming the trailer's
`Size` fields are each module's own byte length, not some other quantity. Both halves are complete,
independent "Vortex Tracker II 1.0 module: ..." headers (titled "stellar I"/"Stellar I", by
CjSplin7er &amp; MmcM, DiHalt 2011). Used to build and test `Pt3TsFileReader`. No ground-truth `.psg`
pair yet for this one (the user may supply per-chip exports later for verification).

## `user_provided/Pator - Digital Espresso (2023) (Revision 2023, 12).pt3` / `.psg`
Provided by the user - first ground-truth pair from a different composer/tool: header
`"ProTracker 3.7 compilation of..."`, `Version=7` (matching `EA - Proudly Loneliness`'s dialect, the
only other `Version=7` file we have, but that one has never been ground-truth-diffed against a real
`.psg` before this). Only 61.9% byte-exact, frame count matches (8569 both sides), and unlike Man of
Art, *every* register shows some divergence - meaning this is exercising the materially less-tested
`Version=7`/"ProTracker 3.7 compilation" code path, not a regression in the already-well-tested
`Version=6`/"Vortex Tracker II" path. Surfaced a note-table rounding edge case at the very top of the
note range (note 95) and a larger, not-yet-investigated cascading divergence starting at frame 36 -
both documented as open leads in docs/PT3_TABLES.md, not fixed. Same legal reasoning as the files
above.
