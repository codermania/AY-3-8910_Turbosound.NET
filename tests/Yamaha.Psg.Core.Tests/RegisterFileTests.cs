using Yamaha.Psg.Core.Chip;

namespace Yamaha.Psg.Core.Tests;

public class RegisterFileTests
{
    [Theory]
    [InlineData(0, 0xFF)]  // R0 tone A fine
    [InlineData(1, 0x0F)]  // R1 tone A coarse
    [InlineData(2, 0xFF)]  // R2 tone B fine
    [InlineData(3, 0x0F)]  // R3 tone B coarse
    [InlineData(4, 0xFF)]  // R4 tone C fine
    [InlineData(5, 0x0F)]  // R5 tone C coarse
    [InlineData(6, 0x1F)]  // R6 noise period
    [InlineData(7, 0xFF)]  // R7 mixer
    [InlineData(8, 0x1F)]  // R8 amplitude A
    [InlineData(9, 0x1F)]  // R9 amplitude B
    [InlineData(10, 0x1F)] // R10 amplitude C
    [InlineData(11, 0xFF)] // R11 envelope period fine
    [InlineData(12, 0xFF)] // R12 envelope period coarse
    [InlineData(13, 0x0F)] // R13 envelope shape
    [InlineData(14, 0xFF)] // R14 I/O port A
    [InlineData(15, 0xFF)] // R15 I/O port B
    public void Write_MasksUnusedHighBits(int register, byte expectedMask)
    {
        var registers = new RegisterFile();

        registers.Write(register, 0xFF);

        Assert.Equal(expectedMask, registers.Read(register));
    }

    [Fact]
    public void ToneAPeriod_CombinesFineAndCoarseRegisters()
    {
        var registers = new RegisterFile();

        registers.Write(0, 0xCD); // fine
        registers.Write(1, 0x0A); // coarse (masked to 4 bits)

        Assert.Equal(0x0ACD, registers.ToneAPeriod);
    }

    [Fact]
    public void EnvelopePeriod_CombinesFineAndCoarseInto16Bits()
    {
        var registers = new RegisterFile();

        registers.Write(11, 0x34);
        registers.Write(12, 0x12);

        Assert.Equal(0x1234, registers.EnvelopePeriod);
    }

    [Fact]
    public void NoisePeriod_ReadsMaskedR6()
    {
        var registers = new RegisterFile();

        registers.Write(6, 0xFF);

        Assert.Equal(0x1F, registers.NoisePeriod);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void Write_OutOfRangeRegister_IsIgnored(int register)
    {
        var registers = new RegisterFile();

        registers.Write(register, 0xFF);

        Assert.Equal(0, registers.Read(register));
    }
}
