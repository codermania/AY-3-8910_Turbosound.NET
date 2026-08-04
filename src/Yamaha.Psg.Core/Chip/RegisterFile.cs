namespace Yamaha.Psg.Core.Chip;

/// <summary>
/// Storage for R0-R15 of the real AY-3-8910/YM2149 chip, masking each register to its real bit
/// width on write (unused high bits are truncated, matching real hardware).
/// </summary>
public sealed class RegisterFile
{
    private static readonly byte[] Masks =
    [
        0xFF, 0x0F, // R0/R1  - tone A period (fine/coarse, 12 bit)
        0xFF, 0x0F, // R2/R3  - tone B period
        0xFF, 0x0F, // R4/R5  - tone C period
        0x1F,       // R6     - noise period (5 bit)
        0xFF,       // R7     - mixer (tone/noise enable x3 + I/O port control)
        0x1F,       // R8     - amplitude A (bit4 = envelope enable, bits0-3 = fixed level)
        0x1F,       // R9     - amplitude B
        0x1F,       // R10    - amplitude C
        0xFF, 0xFF, // R11/R12 - envelope period (fine/coarse, 16 bit)
        0x0F,       // R13    - envelope shape (4 bit)
        0xFF, 0xFF, // R14/R15 - I/O ports (not audio-relevant)
    ];

    private readonly byte[] _registers = new byte[16];

    public void Write(int register, byte value)
    {
        if (register is < 0 or > 15)
        {
            return;
        }

        _registers[register] = (byte)(value & Masks[register]);
    }

    public byte Read(int register)
        => register is >= 0 and <= 15 ? _registers[register] : (byte)0;

    public int ToneAPeriod => CombinePeriod(0, 1);
    public int ToneBPeriod => CombinePeriod(2, 3);
    public int ToneCPeriod => CombinePeriod(4, 5);
    public int NoisePeriod => _registers[6];
    public byte Mixer => _registers[7];
    public byte AmplitudeA => _registers[8];
    public byte AmplitudeB => _registers[9];
    public byte AmplitudeC => _registers[10];
    public int EnvelopePeriod => _registers[11] | (_registers[12] << 8);
    public byte EnvelopeShape => _registers[13];

    private int CombinePeriod(int fineIndex, int coarseIndex)
        => _registers[fineIndex] | (_registers[coarseIndex] << 8);
}
