using Yamaha.Psg.Core;
using Yamaha.Psg.Formats.Common;

namespace Yamaha.Psg.Player;

/// <summary>
/// Plays a parsed register dump (PSG/VTX/...) through <see cref="AySoundChip"/>: for each frame,
/// applies register writes, then renders exactly outputSampleRate/frameRateHz samples and hands
/// them to <paramref name="sink"/>. This is frame-based playback — what the PSG/VTX formats
/// actually carry (see the note about sub-frame digi-drum timing in docs/DAC_TABLES.md and the plan).
/// </summary>
public static class FileDrivenPlaybackDriver
{
    public static void Play(IRegisterDumpPlayer dump, AySoundChip chip, IPcmSink sink)
    {
        int samplesPerFrame = Math.Max(1, chip.OutputSampleRate / dump.Metadata.FrameRateHz);
        var frameBuffer = new short[samplesPerFrame * 2];

        // Dump frames are full state snapshots (a register value persists until overwritten), not
        // deltas. For ordinary registers it's enough to write only values that actually changed
        // (compared against the previous frame). But R13 (envelope shape) is special: ANY write to
        // it restarts the envelope generator (see EnvelopeGenerator), even with the same value —
        // so the "write or not" decision for R13 is taken directly from
        // RegisterFrame.EnvelopeShapeWritten (what the source format itself knows for certain),
        // not from a byte comparison: an unconditional write every frame would restart it every
        // ~20ms (which is exactly what happened in the original version of this driver), while a
        // value comparison would, conversely, silently swallow an intentional restart with the
        // same value (as encoded by YM3/VTX).
        var previous = new byte[RegisterFrame.RegisterCount];

        foreach (var frame in dump.Frames)
        {
            for (int register = 0; register < RegisterFrame.RegisterCount; register++)
            {
                byte value = frame[register];
                bool shouldWrite = register == 13
                    ? frame.EnvelopeShapeWritten
                    : value != previous[register];

                if (shouldWrite)
                {
                    chip.WriteRegister(register, value);
                }

                previous[register] = value;
            }

            int written = chip.RenderSamples(frameBuffer, samplesPerFrame);
            sink.Write(frameBuffer, written * 2);
        }
    }
}
