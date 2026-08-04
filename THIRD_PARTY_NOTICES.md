# Third-party notices

This project's own code is an original, clean-room implementation — no source code was copied
from any GPL or ambiguously-licensed project (MAME, ZXTune, AY_emul/Bulba's original PT3 player).
Those projects were consulted only to understand documented chip/format *behavior*, never as a
source of code. The table below lists everything actually reused (as a runtime dependency, as
factual/measured data, or as a behavioral reference) and its license.

| Source | License | How it's used here |
|---|---|---|
| [NAudio](https://github.com/naudio/NAudio) (v2.3.0, NuGet) | MIT | Real runtime dependency, isolated to `Yamaha.Psg.Player` (the demo CLI) for live audio playback. `Yamaha.Psg.Core`/`Yamaha.Psg.Formats` have no third-party dependencies. |
| [ayumi](https://github.com/true-grue/ayumi) (Peter Sovietov) | Unlicense (public domain) | The numeric AY/YM DAC envelope-level tables in `DacTables.cs` reproduce published, independently-measured chip output levels as data — not code. See `docs/DAC_TABLES.md`. |
| [lhasa](https://github.com/fragglet/lhasa) (fragglet) | ISC | Used only as a behavioral reference for the `-lh5-` bitstream algorithm while writing `Lh5Decoder.cs` from scratch. See `docs/DAC_TABLES.md`/milestone 8 notes. |
| [Volutar/pt3player](https://github.com/Volutar/pt3player) | MIT | Read as a behavioral reference to resolve ambiguities in the PT3 tracker format not covered by the prose specs below; a handful of factual/measured table values were taken from it as data. See `docs/PT3_TABLES.md`. |
| ["How to decode a Vortex Tracker II 'PT3' File"](http://www.deater.net/weave/vmwprod/pt3_player/README_pt3.txt) (Vince Weaver) | Public technical write-up, not code | Primary specification source for the PT3 file format. See `docs/PT3_TABLES.md`. |
| `ptdoc.txt` / `docs/ptdoc_pt3_format_ru.txt` (community document) | No formal license attached; not code | Secondary specification source for the PT3 file format, used the same way as the document above. |

## Development-only tooling
xunit, xunit.runner.visualstudio, coverlet.collector, and Microsoft.NET.Test.Sdk (all MIT/Apache 2.0)
are used only by the test projects and are never part of the shipped library.

## Explicitly avoided as code sources
MAME's ay8910 core, ZXTune, and AY_emul/Bulba's original PT3 player are all GPL-licensed (or, in
Bulba's case, under a specific non-permissive copyright notice) and were never used as a source of
code — only, where cited above, as documentation of observed chip/format behavior.
