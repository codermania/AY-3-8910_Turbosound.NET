namespace Yamaha.Psg.Core.Timing;

/// <summary>A register write tied to an offset (in chip cycles) from the start of the current render buffer.</summary>
public readonly struct TimedRegisterWrite
{
    public TimedRegisterWrite(int cycleOffset, byte register, byte value)
    {
        CycleOffset = cycleOffset;
        Register = register;
        Value = value;
    }

    public int CycleOffset { get; }
    public byte Register { get; }
    public byte Value { get; }
}
