# AY-3-8910/YM2149 Turbosound.NET

A from-scratch, dependency-free C# (.NET 8) emulator for the **AY-3-8910** / **YM2149** (PSG) sound
chip — the chip behind the ZX Spectrum's AY sound interfaces, the Amstrad CPC, Atari ST, and MSX.
No code is copied from any existing emulator (MAME's `ay8910` core, ZXTune, etc. are GPL-licensed);
the implementation is original, built from publicly documented chip behavior and independently
measured/factual data. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for exactly what was
consulted and how.

Built with three priorities, in this order:

1. **Accurate envelope generator** — modeled as an explicit state machine (not a precomputed
   waveform table), with the real AY-3-8910 (16 steps, `/16` prescaler) vs. YM2149 (32 steps, `/8`
   prescaler) resolution difference. See [docs/DAC_TABLES.md](docs/DAC_TABLES.md).
2. **Real AY-3-8910 vs. YM2149 differences** — separate, independently-sourced DAC/volume tables per
   chip variant, not just a cosmetic label.
3. **Digi-drum / software-sample playback** — register writes can be scheduled to an exact chip
   cycle (`TimedRegisterWrite`), not just "once per output buffer," so the fast volume-register
   tricks these tracks depend on come through correctly.

## What's in the box

| Project | What it is |
|---|---|
| `Yamaha.Psg.Core` | The emulator itself. **Zero third-party dependencies** — safe to embed directly in a Godot 4 project (or anything else targeting net8.0). |
| `Yamaha.Psg.Formats` | Readers for `.PSG`, `.VTX`, and `.PT3` register-dump/tracker-module files, producing a common frame sequence. |
| `Yamaha.Psg.Player` | A small demo/test CLI: load a file, render to `.wav` and/or play it live (via [NAudio](https://github.com/naudio/NAudio), MIT). |

```
Yamaha.Psg.sln
src/
  Yamaha.Psg.Core/       Ay8910, EnvelopeGenerator, NoiseGenerator, DacTables, AySoundChip, MultiChipAySoundChip...
  Yamaha.Psg.Formats/    PsgFileReader, VtxFileReader, Pt3FileReader (+ Lh5Decoder for VTX's lh5 compression)
  Yamaha.Psg.Player/     Program.cs (CLI), WavWriter, LivePlaybackSink
tests/
  Yamaha.Psg.Core.Tests/
  Yamaha.Psg.Formats.Tests/
  Yamaha.Psg.Regression/
```

## Features

- **Accurate envelope generator**: full 16-shape state machine per the documented Continue/Attack/
  Alternate/Hold bits of R13, including the real hardware quirk that *any* write to R13 restarts the
  envelope, even with the same value.
- **AY-3-8910 vs. YM2149**: selectable per chip instance (`ChipVariant.Ay_3_8910` /
  `ChipVariant.Ym2149`), each with its own native-resolution DAC table and envelope-generator timing.
- **Digi-drum accuracy**: schedule register writes to an exact chip cycle within a render call via
  `TimedRegisterWrite` — not just at buffer boundaries.
- **Band-limited resampling**: a windowed-sinc FIR decimator from the chip's native clock down to any
  output sample rate, so square waves don't alias.
- **Stereo panning**: `Mono`, all 6 `ABC` permutations (`Abc`/`Acb`/`Bac`/`Bca`/`Cab`/`Cba`), or fully
  custom per-channel left/right gain.
- **Multi-chip / TurboSound-style polyphony**: `MultiChipAySoundChip` runs N independent, fully
  accurate `Ay8910` instances in parallel (the same way real TurboSound hardware mods work) instead
  of faking a wider single chip — `chipCount = 1` behaves exactly like `AySoundChip`.
- **File format support**:
  - **`.PSG`** — the simple, uncompressed per-frame register dump used by ZX Spectrum emulators and
    exported directly by Vortex Tracker II.
  - **`.VTX`** — register dump + metadata (chip variant, clock, panning), compressed with `-lh5-`
    (own from-scratch decoder, no third-party compression library) and column-wise interleaved;
    de-interleaved and decoded here.
  - **`.PT3`** (ProTracker 3 / Vortex Tracker II) — a real tracker module (patterns, samples,
    ornaments, effects), not a register dump: fully interpreted here into the same frame sequence,
    verified against real Vortex Tracker `.psg` exports register-by-register (see
    [docs/PT3_TABLES.md](docs/PT3_TABLES.md)).
- 16-bit `short[]` PCM output at any sample rate you choose, in stereo or mono.

## Quick start

### Render register writes you drive yourself

```csharp
using Yamaha.Psg.Core;
using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;

var chip = new AySoundChip(
    ChipVariant.Ay_3_8910,
    PsgClockPresets.ZxSpectrum,
    outputSampleRate: 44_100,
    panning: PanningPreset.Abc);

chip.WriteRegister(0, 0xFD); chip.WriteRegister(1, 0x00); // channel A tone period
chip.WriteRegister(8, 0x0F);                              // channel A volume, no envelope
chip.WriteRegister(7, 0x3E);                               // mixer: tone A enabled, everything else off

var buffer = new short[44_100 * 2]; // 1 second, stereo interleaved
chip.RenderSamples(buffer, frameCount: 44_100);
```

### Play a `.psg` / `.vtx` / `.pt3` file

```csharp
using Yamaha.Psg.Core;
using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;
using Yamaha.Psg.Formats.Common;
using Yamaha.Psg.Formats.Pt3;
using Yamaha.Psg.Player;

IRegisterDumpPlayer dump = Pt3FileReader.Load("track.pt3"); // or PsgFileReader/VtxFileReader

var chip = new AySoundChip(
    dump.Metadata.ChipVariant ?? ChipVariant.Ay_3_8910,
    dump.Metadata.ClockHz ?? PsgClockPresets.ZxSpectrum,
    outputSampleRate: 44_100,
    dump.Metadata.Panning ?? PanningPreset.Abc);

var sink = new BufferingPcmSink();
FileDrivenPlaybackDriver.Play(dump, chip, sink); // applies each frame's registers, renders in between

WavWriter.Write("track.wav", sink.ToArray(), sampleRate: 44_100, channels: 2);
```

### Multiple chips at once (TurboSound-style)

```csharp
using Yamaha.Psg.Core;
using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;

// 3 independent AY chips = 9 fully accurate channels, not one stretched chip.
var multi = new MultiChipAySoundChip(chipCount: 3, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum,
    outputSampleRate: 44_100, panning: PanningPreset.Abc);

multi.WriteRegister(chipIndex: 0, register: 8, value: 0x0F);
multi.WriteRegister(chipIndex: 2, register: 8, value: 0x0F);
multi.SetChipPanning(chipIndex: 1, PanningPreset.Acb); // pan each chip independently

var buffer = new short[44_100 * 2];
multi.RenderSamples(buffer, frameCount: 44_100);
```

### The demo CLI

```bash
dotnet run --project src/Yamaha.Psg.Player -- track.psg --wav track.wav
dotnet run --project src/Yamaha.Psg.Player -- track.vtx --live
dotnet run --project src/Yamaha.Psg.Player -- track.pt3 --variant ym --rate 48000
```

```
Usage: Yamaha.Psg.Player <file.psg|.vtx|.pt3> [--wav <output.wav>] [--live] [--variant ay|ym] [--rate 44100] [--clock 1773400]
```

`--live` plays through your speakers via NAudio; if neither `--live` nor `--wav` is given, a
`.wav` next to the input file is written by default. For `.vtx` files, chip variant/clock/panning
default to the file's own metadata; `--variant`/`--clock` override them.

## Building and testing

```bash
dotnet build Yamaha.Psg.slnx
dotnet test tests/Yamaha.Psg.Core.Tests
dotnet test tests/Yamaha.Psg.Formats.Tests
```

`tests/Yamaha.Psg.Regression` renders every fixture (including two real `.vtx` tracks) to audio and
diffs against pinned SHA-256 baselines — it's slower (several minutes) since it exercises the full
pipeline end-to-end; run it with `dotnet test tests/Yamaha.Psg.Regression` when you want that level
of confidence, not as part of routine iteration.

Some `.pt3`/`.psg`/`.vtx` ground-truth fixtures under `tests/*/fixtures/user_provided/` are real
third-party chiptune tracks and aren't committed to this repo (see the `SOURCES.md` next to each) —
tests that need them skip gracefully if the files aren't present locally.

## Design notes

- `Yamaha.Psg.Core` intentionally has no dependencies of any kind, so it can be referenced directly
  from a Godot 4 (GodotSharp, net8.0) project without pulling anything else in.
- `AySoundChip.RenderSamples` takes a stereo-pair frame count, matching the "pull" model of Godot's
  `AudioStreamGeneratorPlayback` — a future Godot wrapper should be a thin adapter, not a rewrite.
- Every file-format reader (`PsgFileReader`, `VtxFileReader`, `Pt3FileReader`) produces the exact
  same `IRegisterDumpPlayer` shape (a metadata block + a sequence of 14-register frames), so
  `FileDrivenPlaybackDriver` and everything downstream doesn't care which format it came from.
- `.PT3` is a tracker module, not a register dump — `Pt3Interpreter` plays the whole pattern order
  once up front to produce that same frame sequence, rather than needing any change to the playback
  pipeline. See [docs/PT3_TABLES.md](docs/PT3_TABLES.md) for the format's frequency/volume tables
  and the ground-truth verification process used to check it.
- `.AY` (ZXAYEMUL) is intentionally out of scope: it's a Z80 program + memory image that has to
  actually be *executed* to produce register writes, not a dump — a materially different (and much
  larger) undertaking than reading a file format.

## License

MIT — see [LICENSE](LICENSE). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party
sources consulted (NAudio, ayumi, lhasa, and PT3-format references) and how each was used.
