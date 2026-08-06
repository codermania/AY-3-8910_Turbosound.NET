using Yamaha.Psg.Core;
using Yamaha.Psg.Formats.Common;

namespace Yamaha.Psg.Player;

/// <summary>
/// Plays N independent register dumps (e.g. the two modules of a real TS/TurboSound `.pt3` file - see
/// <see cref="Yamaha.Psg.Formats.Pt3.Pt3TsFileReader"/>) through a <see cref="MultiChipAySoundChip"/>
/// with a matching chip count, one module per chip index - the direct multi-module analogue of
/// <see cref="FileDrivenPlaybackDriver"/>. Not TS-specific itself: any N independently-produced
/// dumps can be played together this way, TS is just the one real file format that produces exactly
/// 2 of them.
/// </summary>
public static class SyncedMultiModulePlaybackDriver
{
    public static void Play(IReadOnlyList<IRegisterDumpPlayer> modules, MultiChipAySoundChip chip, IPcmSink sink)
    {
        if (modules.Count != chip.ChipCount)
        {
            throw new ArgumentException(
                $"{nameof(modules)} has {modules.Count} entries but {nameof(chip)} has {chip.ChipCount} chips - one module per chip is required.",
                nameof(modules));
        }

        // All modules share one frame rate (the ZX Spectrum/PAL interrupt tick both PT3 modules in a
        // real TS file are compiled against) - there's no per-module rate to reconcile.
        int frameRateHz = modules[0].Metadata.FrameRateHz;
        int samplesPerFrame = Math.Max(1, chip.OutputSampleRate / frameRateHz);
        var frameBuffer = new short[samplesPerFrame * 2];

        var previous = new byte[modules.Count][];
        for (int i = 0; i < modules.Count; i++)
        {
            previous[i] = new byte[RegisterFrame.RegisterCount];
        }

        // A real TS file's two modules are compiled from the same pattern-order length and finish
        // together, but nothing enforces that in general - play until the longest one ends, holding
        // a shorter module's last register state for any trailing frames rather than erroring out.
        int frameCount = 0;
        foreach (IRegisterDumpPlayer module in modules)
        {
            frameCount = Math.Max(frameCount, module.Frames.Count);
        }

        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (frameIndex >= modules[i].Frames.Count)
                {
                    continue;
                }

                RegisterFrame frame = modules[i].Frames[frameIndex];
                for (int register = 0; register < RegisterFrame.RegisterCount; register++)
                {
                    byte value = frame[register];
                    // See FileDrivenPlaybackDriver's remarks on why R13 is written unconditionally
                    // per RegisterFrame.EnvelopeShapeWritten rather than a value comparison - the
                    // same reasoning applies per-chip here.
                    bool shouldWrite = register == 13
                        ? frame.EnvelopeShapeWritten
                        : value != previous[i][register];

                    if (shouldWrite)
                    {
                        chip.WriteRegister(i, register, value);
                    }

                    previous[i][register] = value;
                }
            }

            int written = chip.RenderSamples(frameBuffer, samplesPerFrame);
            sink.Write(frameBuffer, written * 2);
        }
    }
}
