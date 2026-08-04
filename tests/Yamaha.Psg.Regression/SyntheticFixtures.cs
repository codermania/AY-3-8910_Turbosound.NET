using Yamaha.Psg.Formats.Common;

namespace Yamaha.Psg.Regression;

/// <summary>
/// Minimal hand-built fixtures (assembled directly from RegisterFrame, not from a file),
/// each isolating one specific aspect of the emulation — as planned for milestone 9.
/// </summary>
internal static class SyntheticFixtures
{
    /// <summary>A sweep through all 16 R13 shapes in sequence, 25 frames (0.5s @ 50Hz) per shape.</summary>
    public static IRegisterDumpPlayer EnvelopeShapeSweep()
    {
        var frames = new List<RegisterFrame>();

        for (byte shape = 0; shape <= 0x0F; shape++)
        {
            var snapshot = new byte[RegisterFrame.RegisterCount];
            snapshot[7] = 0x3F; // tone and noise disabled on all channels -> gate always open
            snapshot[8] = 0x10; // channel A: envelope-enable
            snapshot[11] = 0x64; // envelope period = 100
            snapshot[12] = 0x00;
            snapshot[13] = shape;

            for (int f = 0; f < 25; f++)
            {
                frames.Add(new RegisterFrame(snapshot, envelopeShapeWritten: f == 0));
            }
        }

        return new RegisterDumpPlayer(new RegisterDumpMetadata { FrameRateHz = 50 }, frames);
    }

    /// <summary>Synthetic digi-drum: a per-frame "hit" envelope on channel C (passthrough), repeated 10 times.</summary>
    public static IRegisterDumpPlayer DigiDrumPattern()
    {
        int[] hit = [15, 12, 9, 6, 3, 0, 0, 0, 0, 0];
        var frames = new List<RegisterFrame>();
        var snapshot = new byte[RegisterFrame.RegisterCount];
        snapshot[7] = 0x3F; // channel C (and the rest): tone and noise disabled -> gate always open

        for (int rep = 0; rep < 10; rep++)
        {
            foreach (int level in hit)
            {
                snapshot[10] = (byte)level;
                frames.Add(new RegisterFrame(snapshot));
            }
        }

        return new RegisterDumpPlayer(new RegisterDumpMetadata { FrameRateHz = 50 }, frames);
    }
}
