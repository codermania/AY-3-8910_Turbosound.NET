namespace Yamaha.Psg.Core.Chip;

/// <summary>
/// One of the three tone generators (A/B/C). Clocked by each <see cref="Tick"/> (one call per chip
/// clock cycle); the internal /8 prescaler plus toggling the output on every counter reload gives
/// the overall frequency clock / (16 * period), matching the documented formula for AY-3-8910/YM2149.
/// </summary>
public sealed class ToneGenerator
{
    private int _period = 1;
    private int _counter;
    private int _prescaler;

    public bool Output { get; private set; }

    /// <summary>A period value of 0 is treated as 1, matching real hardware.</summary>
    public void SetPeriod(int period)
    {
        _period = period <= 0 ? 1 : period;
    }

    public void Tick()
    {
        _prescaler++;
        if (_prescaler < 8)
        {
            return;
        }

        _prescaler = 0;

        _counter--;
        if (_counter <= 0)
        {
            _counter = _period;
            Output = !Output;
        }
    }
}
