using Yamaha.Psg.Core.Chip;

namespace Yamaha.Psg.Core.Tests;

public class EnvelopeGeneratorTests
{
    private static int PrescalerLimit(ChipVariant variant) => variant == ChipVariant.Ym2149 ? 8 : 16;

    private static void TickPeriods(EnvelopeGenerator env, ChipVariant variant, int periodTicks, int period = 1)
    {
        for (int i = 0; i < periodTicks * PrescalerLimit(variant) * period; i++)
        {
            env.Tick();
        }
    }

    public static IEnumerable<object[]> AllShapesBothVariants()
    {
        foreach (var variant in new[] { ChipVariant.Ay_3_8910, ChipVariant.Ym2149 })
        {
            for (int shape = 0; shape <= 0x0F; shape++)
            {
                yield return [variant, (byte)shape];
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllShapesBothVariants))]
    public void SetShape_InitialLevel_MatchesAttackBit(ChipVariant variant, byte shape)
    {
        bool initialAttack = (shape & 0x04) != 0;
        var env = new EnvelopeGenerator(variant);

        env.SetShape(shape);

        Assert.Equal(initialAttack ? 0 : env.MaxStep, env.CurrentLevel);
        Assert.Equal(0, env.Step);
        Assert.False(env.IsHolding);
    }

    [Theory]
    [MemberData(nameof(AllShapesBothVariants))]
    public void FirstPass_AlwaysRampsToOppositeExtremeAfterMaxStepSteps(ChipVariant variant, byte shape)
    {
        bool initialAttack = (shape & 0x04) != 0;
        var env = new EnvelopeGenerator(variant);
        env.SetShape(shape);

        TickPeriods(env, variant, env.MaxStep);

        Assert.Equal(initialAttack ? env.MaxStep : 0, env.CurrentLevel);
        Assert.Equal(env.MaxStep, env.Step);
        Assert.False(env.IsHolding);
    }

    // CONT=0 (0x00-0x07): a single pass, then forced hold at 0, regardless of Attack/Alternate/Hold.
    [Theory]
    [InlineData(ChipVariant.Ay_3_8910, 0x00)]
    [InlineData(ChipVariant.Ay_3_8910, 0x01)]
    [InlineData(ChipVariant.Ay_3_8910, 0x02)]
    [InlineData(ChipVariant.Ay_3_8910, 0x03)]
    [InlineData(ChipVariant.Ay_3_8910, 0x04)]
    [InlineData(ChipVariant.Ay_3_8910, 0x05)]
    [InlineData(ChipVariant.Ay_3_8910, 0x06)]
    [InlineData(ChipVariant.Ay_3_8910, 0x07)]
    [InlineData(ChipVariant.Ym2149, 0x00)]
    [InlineData(ChipVariant.Ym2149, 0x01)]
    [InlineData(ChipVariant.Ym2149, 0x02)]
    [InlineData(ChipVariant.Ym2149, 0x03)]
    [InlineData(ChipVariant.Ym2149, 0x04)]
    [InlineData(ChipVariant.Ym2149, 0x05)]
    [InlineData(ChipVariant.Ym2149, 0x06)]
    [InlineData(ChipVariant.Ym2149, 0x07)]
    public void NonContinueShapes_HoldAtZeroAfterOnePass(ChipVariant variant, byte shape)
    {
        var env = new EnvelopeGenerator(variant);
        env.SetShape(shape);

        TickPeriods(env, variant, env.MaxStep + 1); // to the opposite extreme + 1 step across the pass boundary

        Assert.True(env.IsHolding);
        Assert.Equal(0, env.CurrentLevel);

        TickPeriods(env, variant, 100);
        Assert.Equal(0, env.CurrentLevel); // stays at 0 indefinitely
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void Shape08_RepeatingDecaySawtooth(ChipVariant variant)
    {
        var env = new EnvelopeGenerator(variant);
        env.SetShape(0x08);

        TickPeriods(env, variant, env.MaxStep);
        Assert.Equal(0, env.CurrentLevel);

        TickPeriods(env, variant, 1); // pass boundary
        Assert.False(env.IsHolding);
        Assert.Equal(env.MaxStep, env.CurrentLevel); // jump back to the start of a new decay

        TickPeriods(env, variant, env.MaxStep);
        Assert.Equal(0, env.CurrentLevel);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void Shape09_SingleDecayThenHoldAtZero(ChipVariant variant)
    {
        var env = new EnvelopeGenerator(variant);
        env.SetShape(0x09);

        TickPeriods(env, variant, env.MaxStep + 1);

        Assert.True(env.IsHolding);
        Assert.Equal(0, env.CurrentLevel);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void Shape0A_AlternatingTriangle_IsContinuousAcrossPassBoundary(ChipVariant variant)
    {
        var env = new EnvelopeGenerator(variant); // CONT=1,ATT=0,ALT=1,HOLD=0
        env.SetShape(0x0A);

        TickPeriods(env, variant, env.MaxStep);
        Assert.Equal(0, env.CurrentLevel);

        TickPeriods(env, variant, 1); // alternate flips attack, the new pass starts at the same level
        Assert.False(env.IsHolding);
        Assert.Equal(0, env.CurrentLevel); // no jump

        TickPeriods(env, variant, env.MaxStep);
        Assert.Equal(env.MaxStep, env.CurrentLevel);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void Shape0B_DecayThenHoldAtMax(ChipVariant variant)
    {
        var env = new EnvelopeGenerator(variant); // CONT=1,ATT=0,ALT=1,HOLD=1
        env.SetShape(0x0B);

        TickPeriods(env, variant, env.MaxStep + 1);

        Assert.True(env.IsHolding);
        Assert.Equal(env.MaxStep, env.CurrentLevel);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void Shape0C_RepeatingAttackSawtooth(ChipVariant variant)
    {
        var env = new EnvelopeGenerator(variant);
        env.SetShape(0x0C);

        TickPeriods(env, variant, env.MaxStep);
        Assert.Equal(env.MaxStep, env.CurrentLevel);

        TickPeriods(env, variant, 1);
        Assert.False(env.IsHolding);
        Assert.Equal(0, env.CurrentLevel); // jump back to the start of a new attack

        TickPeriods(env, variant, env.MaxStep);
        Assert.Equal(env.MaxStep, env.CurrentLevel);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void Shape0D_SingleAttackThenHoldAtMax(ChipVariant variant)
    {
        var env = new EnvelopeGenerator(variant);
        env.SetShape(0x0D);

        TickPeriods(env, variant, env.MaxStep + 1);

        Assert.True(env.IsHolding);
        Assert.Equal(env.MaxStep, env.CurrentLevel);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void Shape0E_AlternatingTriangle_InversePhaseOf0A(ChipVariant variant)
    {
        var env = new EnvelopeGenerator(variant); // CONT=1,ATT=1,ALT=1,HOLD=0
        env.SetShape(0x0E);

        TickPeriods(env, variant, env.MaxStep);
        Assert.Equal(env.MaxStep, env.CurrentLevel);

        TickPeriods(env, variant, 1);
        Assert.False(env.IsHolding);
        Assert.Equal(env.MaxStep, env.CurrentLevel); // no jump — now decaying, starting from the max

        TickPeriods(env, variant, env.MaxStep);
        Assert.Equal(0, env.CurrentLevel);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void Shape0F_SingleAttackThenHoldAtZero(ChipVariant variant)
    {
        var env = new EnvelopeGenerator(variant);
        env.SetShape(0x0F);

        TickPeriods(env, variant, env.MaxStep + 1);

        Assert.True(env.IsHolding);
        Assert.Equal(0, env.CurrentLevel);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void SetShape_MidCycle_AlwaysRestartsStepToZero_EvenWithSameShapeValue(ChipVariant variant)
    {
        var env = new EnvelopeGenerator(variant);
        env.SetShape(0x0C); // repeating attack sawtooth

        TickPeriods(env, variant, env.MaxStep / 2); // somewhere in the middle of a pass
        Assert.NotEqual(0, env.Step);

        env.SetShape(0x0C); // same value — but must still restart

        Assert.Equal(0, env.Step);
        Assert.Equal(0, env.CurrentLevel); // attack=1 => starts at 0
        Assert.False(env.IsHolding);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910)]
    [InlineData(ChipVariant.Ym2149)]
    public void SetShape_MidCycle_RestartsEvenWhenPreviouslyHolding(ChipVariant variant)
    {
        var env = new EnvelopeGenerator(variant);
        env.SetShape(0x09); // single decay, hold at 0
        TickPeriods(env, variant, env.MaxStep + 1);
        Assert.True(env.IsHolding);

        env.SetShape(0x09); // rewriting with the same value clears the hold and restarts

        Assert.False(env.IsHolding);
        Assert.Equal(env.MaxStep, env.CurrentLevel); // attack=0 => starts at the max
    }

    [Fact]
    public void Ay_HasSixteenNativeLevels_Ym_HasThirtyTwo()
    {
        var ay = new EnvelopeGenerator(ChipVariant.Ay_3_8910);
        var ym = new EnvelopeGenerator(ChipVariant.Ym2149);

        Assert.Equal(15, ay.MaxStep);
        Assert.Equal(31, ym.MaxStep);
    }

    // The period counter starts at 0, so the very first pass after SetShape is one prescaler
    // interval short (the same startup effect as Tone/NoiseGenerator). To compare the steady-state
    // pass duration specifically, we measure the interval between the second and third time Step
    // wraps back to 0, skipping the distorted startup transition.
    private static int SteadyStatePassDurationTicks(EnvelopeGenerator env)
    {
        int tick = 0;
        int wraps = 0;
        int wrapTickA = -1;
        int wrapTickB = -1;
        int previousStep = env.Step;

        while (wrapTickB < 0)
        {
            env.Tick();
            tick++;
            if (env.Step == 0 && previousStep != 0)
            {
                wraps++;
                if (wraps == 2) wrapTickA = tick;
                else if (wraps == 3) wrapTickB = tick;
            }
            previousStep = env.Step;

            Assert.True(tick < 100_000, "Generator did not wrap back to Step=0 within a reasonable number of iterations");
        }

        return wrapTickB - wrapTickA;
    }

    [Fact]
    public void EqualPeriod_ProducesEqualSteadyStatePassDuration_OnBothVariants()
    {
        // AY: 16 steps * /16 prescaler = 256 ticks per unit of period.
        // YM: 32 steps * /8 prescaler  = 256 ticks per unit of period — the steady-state pass
        // duration must match despite the differing resolution.
        const int period = 3;
        const int expectedTicks = 256 * period;

        var ay = new EnvelopeGenerator(ChipVariant.Ay_3_8910);
        ay.SetShape(0x0C); // repeating attack, no holding — convenient for catching the jump back to 0
        ay.SetPeriod(period);

        var ym = new EnvelopeGenerator(ChipVariant.Ym2149);
        ym.SetShape(0x0C);
        ym.SetPeriod(period);

        Assert.Equal(expectedTicks, SteadyStatePassDurationTicks(ay));
        Assert.Equal(expectedTicks, SteadyStatePassDurationTicks(ym));
    }
}
