using Yamaha.Psg.Core;
using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;
using Yamaha.Psg.Formats.Common;
using Yamaha.Psg.Player;

namespace Yamaha.Psg.Regression;

/// <summary>
/// Regression for a real bug (found while listening to an actual .psg track): the driver
/// unconditionally wrote all 14 registers every frame, including R13 — and any write to R13
/// restarts the envelope (see EnvelopeGenerator), even if the value didn't change. Because of
/// this, the envelope restarted every ~20ms and never advanced past the first few percent of its
/// cycle — an audible artifact instead of a smooth droning bass. Fixed via the explicit
/// RegisterFrame.EnvelopeShapeWritten flag (source: PSG — the (13, value) pair was actually
/// present in the frame; VTX/YM3 — the R13 value in the frame != 0xFF).
/// </summary>
public class FileDrivenPlaybackDriverTests
{
    [Fact]
    public void Play_DoesNotRestartEnvelope_WhenR13IsNotMarkedAsWritten()
    {
        const int outputSampleRate = 44_100;
        const int frameRateHz = 50;
        int samplesPerFrame = outputSampleRate / frameRateHz;

        var snapshot = new byte[RegisterFrame.RegisterCount];
        snapshot[7] = 0x3F; // all channels: tone and noise disabled -> gate always open (simplifies comparison)
        snapshot[8] = 0x10; // channel A: envelope-enable
        snapshot[11] = 0xD0; // envelope period = 2000 (0x7D0) -> a full pass takes ~0.29s, longer than one frame (20ms)
        snapshot[12] = 0x07;
        snapshot[13] = 0x0A; // continuous alternating triangle — shouldn't "jump" when there's no restart

        // As in a real format: a real R13 write only in the first frame, then "unchanged" afterwards.
        var frames = new List<RegisterFrame> { new(snapshot, envelopeShapeWritten: true) };
        for (int i = 1; i < 30; i++)
        {
            frames.Add(new RegisterFrame(snapshot, envelopeShapeWritten: false));
        }

        var dump = new RegisterDumpPlayer(new RegisterDumpMetadata { FrameRateHz = frameRateHz }, frames);
        var chip = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, outputSampleRate, PanningPreset.Mono);
        var sink = new BufferingPcmSink();

        FileDrivenPlaybackDriver.Play(dump, chip, sink);
        var pcm = sink.ToArray();

        // Skip the very first frame (the FIR filter transient + the one legitimate envelope
        // restart). Compare the second and third frames: if the bug comes back, the envelope will
        // restart at the start of every frame, and both frames will show the same short initial
        // segment of the cycle — i.e. they'll be byte-for-byte identical.
        int frameSamples = samplesPerFrame * 2; // stereo
        var frame1 = pcm.Skip(frameSamples).Take(frameSamples).ToArray();
        var frame2 = pcm.Skip(frameSamples * 2).Take(frameSamples).ToArray();

        Assert.False(
            frame1.SequenceEqual(frame2),
            "Frames are identical — the envelope appears to be restarting every frame instead of continuing its cycle.");
    }

    [Fact]
    public void Play_DoesRestartEnvelope_WhenExplicitlyMarkedAsWritten_EvenWithSameValue()
    {
        // The flip side of the same mechanism: YM3/VTX explicitly encodes "a real write, with the
        // same value" (not 0xFF) — and this MUST restart the envelope every time, not only when
        // the value changes. Otherwise chopped digi-drum effects built on repeatedly restarting
        // the same shape would be lost.
        const int outputSampleRate = 44_100;
        const int frameRateHz = 50;
        int samplesPerFrame = outputSampleRate / frameRateHz;

        var snapshot = new byte[RegisterFrame.RegisterCount];
        snapshot[7] = 0x3F;
        snapshot[8] = 0x10;
        snapshot[11] = 0xD0; // the same long period — without a restart, the envelope would advance far past a frame
        snapshot[12] = 0x07;
        snapshot[13] = 0x0A;

        var frames = new List<RegisterFrame>();
        for (int i = 0; i < 5; i++)
        {
            frames.Add(new RegisterFrame(snapshot, envelopeShapeWritten: true)); // every frame is an explicit restart
        }

        var dump = new RegisterDumpPlayer(new RegisterDumpMetadata { FrameRateHz = frameRateHz }, frames);
        var chip = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, outputSampleRate, PanningPreset.Mono);
        var sink = new BufferingPcmSink();

        FileDrivenPlaybackDriver.Play(dump, chip, sink);
        var pcm = sink.ToArray();

        int frameSamples = samplesPerFrame * 2;
        var frame1 = pcm.Skip(frameSamples).Take(frameSamples).ToArray();
        var frame2 = pcm.Skip(frameSamples * 2).Take(frameSamples).ToArray();

        Assert.True(
            frame1.SequenceEqual(frame2),
            "Frames should be identical — the envelope must restart at the start of every frame, since that's explicitly indicated.");
    }

    [Fact]
    public void Play_StillAppliesRegisterChange_WhenValueActuallyDiffers()
    {
        const int outputSampleRate = 44_100;
        const int frameRateHz = 50;

        var frameA = new byte[RegisterFrame.RegisterCount];
        frameA[7] = 0x3F;
        frameA[8] = 0x05;

        var frameB = (byte[])frameA.Clone();
        frameB[8] = 0x0F; // a real volume change — must be applied

        var frames = new List<RegisterFrame> { new(frameA), new(frameB) };
        var dump = new RegisterDumpPlayer(new RegisterDumpMetadata { FrameRateHz = frameRateHz }, frames);
        var chip = new AySoundChip(ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, outputSampleRate, PanningPreset.Mono);
        var sink = new BufferingPcmSink();

        FileDrivenPlaybackDriver.Play(dump, chip, sink);
        var pcm = sink.ToArray();

        int samplesPerFrame = (outputSampleRate / frameRateHz) * 2;
        short lastOfFrameA = pcm[samplesPerFrame - 2];
        short lastOfFrameB = pcm[(samplesPerFrame * 2) - 2];

        Assert.NotEqual(lastOfFrameA, lastOfFrameB);
    }
}
