# TODO / known simplifications

## `MultiChipAySoundChip` clips twice, not once

`MultiChipAySoundChip.RenderSamples` (`src/Yamaha.Psg.Core/MultiChipAySoundChip.cs`) sums the
contribution of each chip in the ensemble, but each chip has *already* been hard-clipped to 16-bit
`short` range independently first: `_chips[i].RenderSamples(scratch, ...)` internally converts its
own 3-channel mix to `short` via `StereoMixer.ToShort` (see `AySoundChip.RenderSamples`) before
`MultiChipAySoundChip` ever sees it. So by the time the ensemble sums `scratch` back into a `double`
and applies the chosen `MixLimiter` (hard clip or soft limit, see the `SoftLimit` mode added
2026-08-04), some headroom/precision has already been lost per-chip, before the ensemble-level
limiter — of either kind — gets a chance to work with the real, unclipped values.

A more correct design would have each chip expose its raw, pre-clip per-frame stereo `double` pair
(a new method/overload on `AySoundChip`, since today `RenderSamples` only returns already-clamped
`short`s), and only clip/limit once, at the very end, after summing every chip's raw contribution.

**Not fixed yet** — deliberately out of scope for the `MixLimiter` change (2026-08-04): it would
require changing `AySoundChip`'s render pipeline itself, which risks the already-locked single-chip
regression hashes (`tests/Yamaha.Psg.Regression`) and the various PT3 ground-truth-diff fixtures that
render through a single `AySoundChip`. Worth doing as its own, narrowly-scoped follow-up if
multi-chip consumers (e.g. a tracker app built on `MultiChipAySoundChip`) need the extra headroom in
practice.

**Update (2026-08-04, `VolumeScaling` added)**: `VolumeScaling.DivideByChipCount`/`DivideBySqrtChipCount`
mitigate this in practice (each chip's own DAC output is scaled down before it ever reaches its own
internal 3-channel mix, so the per-chip clip this note describes becomes less likely to fire at all,
not just less likely to matter) but don't eliminate the underlying double-clip architecture — a chip
with several loud channels of its own can still saturate internally before the ensemble-level
`MixLimiter`/`VolumeScaling` ever see the loss. The proper fix (exposing each chip's raw, pre-clip
`double` mix) is still the same size of undertaking described above and still not done.
