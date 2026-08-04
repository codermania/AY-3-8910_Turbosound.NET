namespace Yamaha.Psg.Core.Chip;

/// <summary>
/// Envelope generator: a state machine clocked by the period from R11/R12. The resolution and
/// prescaler divisor depend on the chip variant — the documented difference between AY-3-8910 and
/// YM2149: AY uses a 4-bit counter (16 steps, /16 prescaler), YM2149 a 5-bit one (32 steps, /8
/// prescaler). For an equal period value, a full pass takes the SAME amount of time on both chips
/// (16*16 = 32*8 = 256 ticks per unit of period) — YM just does it in twice as fine steps. The
/// output is a level 0..MaxStep (15 for AY, 31 for YM), which is then (milestone 3) used to index
/// the correspondingly-sized DAC table.
/// </summary>
public sealed class EnvelopeGenerator
{
    private readonly int _maxStep;
    private readonly int _prescalerLimit;

    private int _period = 1;
    private int _counter;
    private int _prescaler;

    private bool _continueFlag;
    private bool _attack;
    private bool _alternate;
    private bool _hold;
    private bool _holding;

    public EnvelopeGenerator(ChipVariant variant)
    {
        if (variant == ChipVariant.Ym2149)
        {
            _maxStep = 31;
            _prescalerLimit = 8;
        }
        else
        {
            _maxStep = 15;
            _prescalerLimit = 16;
        }
    }

    public int CurrentLevel { get; private set; }

    internal int MaxStep => _maxStep;
    internal int Step { get; private set; }
    internal bool IsHolding => _holding;

    /// <summary>A period value of 0 is treated as 1, matching real hardware.</summary>
    public void SetPeriod(int period)
    {
        _period = period <= 0 ? 1 : period;
    }

    /// <summary>
    /// Writes the envelope shape (low 4 bits of R13: Continue/Attack/Alternate/Hold) and, critically,
    /// ALWAYS restarts the generator — even if the new value equals the old one. This is exactly what
    /// digi-drum tricks rely on: rapidly rewriting R13 forces sharp amplitude jumps.
    /// </summary>
    public void SetShape(byte shapeBits)
    {
        _continueFlag = (shapeBits & 0x08) != 0;
        _attack = (shapeBits & 0x04) != 0;
        _alternate = (shapeBits & 0x02) != 0;
        _hold = (shapeBits & 0x01) != 0;

        Step = 0;
        _holding = false;
        _counter = 0;
        _prescaler = 0;
        UpdateLevel();
    }

    public void Tick()
    {
        if (_holding)
        {
            return;
        }

        _prescaler++;
        if (_prescaler < _prescalerLimit)
        {
            return;
        }

        _prescaler = 0;

        _counter--;
        if (_counter > 0)
        {
            return;
        }

        _counter = _period;

        Step++;
        if (Step > _maxStep)
        {
            AdvancePass();
        }
        else
        {
            UpdateLevel();
        }
    }

    private void AdvancePass()
    {
        if (_alternate)
        {
            _attack = !_attack;
        }

        if (!_continueFlag)
        {
            // Some shapes flip direction here, but the chip forces the ending to 0 regardless of
            // Attack/Alternate — hence the audible "jump" in the one-shot attack shapes
            // (R13 = 0x04-0x07, 0x0F).
            _attack = false;
            _holding = true;
            Step = _maxStep;
        }
        else if (_hold)
        {
            _holding = true;
            Step = _maxStep;
        }
        else
        {
            Step = 0;
        }

        UpdateLevel();
    }

    private void UpdateLevel()
    {
        CurrentLevel = _attack ? Step : _maxStep - Step;
    }
}
