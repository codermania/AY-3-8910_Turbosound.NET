namespace Yamaha.Psg.Formats.Common;

/// <summary>
/// A snapshot of the AY sound registers (R0-R13) for one frame of a frame-based register dump.
/// </summary>
/// <remarks>
/// <see cref="EnvelopeShapeWritten"/> separately records whether THIS frame contained an actual
/// write to R13 — comparing the value against the previous frame is not enough: writing R13 always
/// restarts the envelope generator, even with the same value (see EnvelopeGenerator), and different
/// formats encode "value unchanged" differently: PSG simply omits the (13, value) pair from the
/// frame, YM3/VTX use a sentinel value of 255 in the R13 slot. Both readers must set this flag
/// explicitly, so the player doesn't restart the envelope where there was no real write (and doesn't
/// skip a restart where the format explicitly says a write happened, even with the same value).
/// </remarks>
public readonly struct RegisterFrame
{
    public const int RegisterCount = 14;

    private readonly byte[] _registers;

    public RegisterFrame(ReadOnlySpan<byte> registers, bool envelopeShapeWritten = true)
    {
        if (registers.Length != RegisterCount)
        {
            throw new ArgumentException($"Expected exactly {RegisterCount} bytes (R0-R13).", nameof(registers));
        }

        _registers = registers.ToArray();
        EnvelopeShapeWritten = envelopeShapeWritten;
    }

    public byte this[int register] => _registers[register];

    public bool EnvelopeShapeWritten { get; }
}
