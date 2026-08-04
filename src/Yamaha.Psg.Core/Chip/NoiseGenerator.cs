namespace Yamaha.Psg.Core.Chip;

/// <summary>
/// Shared noise generator (one per chip, used by all three channels) — a 17-bit LFSR with
/// feedback from bits 0 and 3, giving a maximal sequence length of 2^17-1 before repeating.
/// Unlike the tone generator, there is no "toggling" here — the register simply shifts on every
/// period counter reload, so the /16 (not /8) prescaler is what yields the overall clock / (16 *
/// period) frequency — this is the documented "divide by 2" quirk relative to tone.
/// </summary>
public sealed class NoiseGenerator
{
    private uint _lfsr = 1; // must never be 0, or the register would get stuck at zero forever
    private int _period = 1;
    private int _counter;
    private int _prescaler;

    public bool Output { get; private set; } = true;

    /// <summary>Full LFSR state — for unit tests checking sequence length.</summary>
    internal uint RawState => _lfsr;

    /// <summary>How many times the register has actually shifted — for unit tests checking timing.</summary>
    internal long ShiftCount { get; private set; }

    /// <summary>A period value of 0 is treated as 1, matching real hardware.</summary>
    public void SetPeriod(int period)
    {
        _period = period <= 0 ? 1 : period;
    }

    public void Tick()
    {
        _prescaler++;
        if (_prescaler < 16)
        {
            return;
        }

        _prescaler = 0;

        _counter--;
        if (_counter <= 0)
        {
            _counter = _period;
            Shift();
        }
    }

    private void Shift()
    {
        uint feedback = (_lfsr ^ (_lfsr >> 3)) & 1u;
        _lfsr = (_lfsr >> 1) | (feedback << 16);
        Output = (_lfsr & 1u) != 0;
        ShiftCount++;
    }
}
