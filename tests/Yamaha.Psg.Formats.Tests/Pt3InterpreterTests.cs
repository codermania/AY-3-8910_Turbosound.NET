using Yamaha.Psg.Formats.Common;
using Yamaha.Psg.Formats.Pt3;

namespace Yamaha.Psg.Formats.Tests;

/// <summary>
/// End-to-end tests: a hand-built <see cref="Pt3Module"/> (bypassing the header/byte parser
/// entirely) exercised through <see cref="Pt3Interpreter.Play"/>, so the tick/row loop and
/// register-encoding logic can be checked in isolation from real-file specifics. Only channel A's
/// bytes are supplied per test - channel A alone drives pattern advancement (see
/// <see cref="Pt3Interpreter"/> remarks), so B/C are given a long, reusable run of $D0 ("done
/// processing this note", 1 byte, no-op) rows, enough for any test here to read from without
/// running off the end.
/// </summary>
public class Pt3InterpreterTests
{
    private const int SilentRowCount = 64;

    private static Pt3Sample MakeSample(bool hasEnvelope, int amplitude, short toneOffset = 0, bool noise = false, bool accumulateTone = false)
    {
        var step = new Pt3SampleStep(
            amplitudeSliding: false, amplitudeSlideUp: false, envelopeSlideUp: false, envelopeSlideValue: 0,
            hasEnvelope: hasEnvelope, noiseDisabled: !noise, accumulateTone: accumulateTone,
            slideValuePersists: false, toneDisabled: false, amplitude: amplitude, toneOffset: toneOffset);
        return new Pt3Sample(loop: 0, steps: [step]);
    }

    private static Pt3Module BuildModule(byte[] channelAData, int speed = 1, int loopPosition = 0, List<int>? patternOrder = null, Pt3Sample?[]? samples = null)
    {
        byte[] silentChannel = Enumerable.Repeat((byte)0xD0, SilentRowCount).ToArray();
        var raw = new byte[channelAData.Length + (silentChannel.Length * 2)];
        Array.Copy(channelAData, raw, channelAData.Length);
        int bAddress = channelAData.Length;
        Array.Copy(silentChannel, 0, raw, bAddress, silentChannel.Length);
        int cAddress = bAddress + silentChannel.Length;
        Array.Copy(silentChannel, 0, raw, cAddress, silentChannel.Length);

        samples ??= new Pt3Sample?[32];
        samples[0] ??= MakeSample(hasEnvelope: false, amplitude: 15);

        var ornaments = new Pt3Ornament?[16];
        ornaments[0] = new Pt3Ornament(loop: 0, values: [0]);

        var patterns = new Dictionary<int, Pt3Pattern> { [0] = new Pt3Pattern(0, bAddress, cAddress) };

        return new Pt3Module(
            version: 7, name: "Test", author: "Tester", frequencyTable: 0, speed: speed, loopPosition: loopPosition,
            patternOrder: patternOrder ?? [0], samples: samples, ornaments: ornaments, patterns: patterns, rawData: raw);
    }

    [Fact]
    public void Play_SingleNoteOnChannelA_ProducesExpectedRegisters()
    {
        // A: play note 0 (C-1), then end.
        Pt3Module module = BuildModule([0x50, 0x00], speed: 1);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        Assert.Single(player.Frames);
        RegisterFrame frame = player.Frames[0];

        int expectedPeriod = Pt3NoteTables.Period(0);
        Assert.Equal(expectedPeriod, frame[0] | (frame[1] << 8));
        Assert.Equal(15, frame[8]); // Combine(sample amplitude 15, default channel volume 15) = 15, no envelope bit
        Assert.Equal(0, frame[9]); // channel B never played a note - silent
        Assert.Equal(0, frame[10]);

        // R7: tone A enabled (real sample data, bit0 clear), noise A disabled (sample4 has noise
        // off, bit3 set) - both real values, since channel A is audible. B/C are muted and never
        // contribute mixer bits at all (matching the reference exactly - a skipped channel's bits
        // default to "enabled", confirmed by diffing against a real Vortex Tracker PSG export;
        // harmless either way since amplitude 0 stays silent regardless of mixer state).
        Assert.Equal(0x08, frame[7]);
    }

    [Fact]
    public void Play_Speed_RepeatsEachLineForSpeedTicks()
    {
        Pt3Module module = BuildModule([0x50, 0x00], speed: 4);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        Assert.Equal(4, player.Frames.Count);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(15, player.Frames[i][8]);
        }
    }

    [Fact]
    public void Play_EnvelopeCommand_SetsEnvelopeShapeWrittenOnlyOnFirstTickOfTheLine()
    {
        // $B2: envelope type (2 & 0xF) - 1 = 1, period big-endian 0x00,0x0A = 10. Then note, then end.
        Pt3Module module = BuildModule([0xB2, 0x00, 0x0A, 0x50, 0x00], speed: 3);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        Assert.Equal(3, player.Frames.Count);
        Assert.True(player.Frames[0].EnvelopeShapeWritten);
        Assert.Equal(1, player.Frames[0][13]);
        Assert.Equal(10, player.Frames[0][11] | (player.Frames[0][12] << 8));

        Assert.False(player.Frames[1].EnvelopeShapeWritten);
        Assert.False(player.Frames[2].EnvelopeShapeWritten);
    }

    [Fact]
    public void Play_NoteOff_SilencesChannel()
    {
        // Line 1: note. Line 2: $C0 (note off).
        Pt3Module module = BuildModule([0x50, 0xC0, 0x00], speed: 1);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        Assert.Equal(2, player.Frames.Count);
        Assert.Equal(15, player.Frames[0][8]);
        Assert.Equal(0, player.Frames[1][8]);
        // All 3 channels are muted at this point, so none contributes a mixer bit - the reference
        // leaves a skipped channel's bits at their "enabled" default (see BuildModule/class remarks
        // on Pt3ChannelTick), giving an all-zero R7 here. Amplitude 0 keeps it silent regardless.
        Assert.Equal(0x00, player.Frames[1][7]);
    }

    [Fact]
    public void Play_LoopPosition_RecordsTheFrameIndexWhereThatOrderPositionBegan()
    {
        // Two identical pattern-order entries pointing at pattern 0; loop at order position 1.
        Pt3Module module = BuildModule([0x50, 0x00], speed: 2, loopPosition: 1, patternOrder: [0, 0]);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        Assert.Equal(4, player.Frames.Count); // 2 pattern-order entries x speed 2
        Assert.Equal(2, player.Metadata.LoopFrame); // frames from the first entry (2) precede the loop point
    }

    [Fact]
    public void Play_Glissando_AddsStepToTonePeriodEveryDelayTicks()
    {
        // $01, note, then params: delay=2, delta=+5. Speed=4.
        Pt3Module module = BuildModule([0x01, 0x50, 0x02, 0x05, 0x00, 0x00], speed: 4);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        // The row that specifies the effect still gets its register write this same tick using the
        // UNSLID base period - confirmed against a real Vortex Tracker PSG export: the first slid
        // tick lands one frame later than counting "delay" ticks from the effect-setting tick itself
        // would produce (see Pt3ChannelState.ApplyEffect's glissando tick-counter remarks).
        int basePeriod = Pt3NoteTables.Period(0);
        int[] expected = [basePeriod, basePeriod, basePeriod + 5, basePeriod + 5];
        for (int i = 0; i < 4; i++)
        {
            int actual = player.Frames[i][0] | (player.Frames[i][1] << 8);
            Assert.Equal(expected[i], actual);
        }
    }

    [Fact]
    public void Play_Portamento_SlidesToTargetNoteThenStops()
    {
        // Row1: note 40. Row2: $02 portamento (delay=1, magnitude=10) to note 45. Speed=20 (plenty
        // of ticks to both observe the slide and confirm it settles exactly at the target instead
        // of continuing past it forever, unlike $01 - the bug this fixes: treating $02 identically
        // to $01 produced long uncontrolled pitch slides where a real track (found by diffing
        // against a Vortex Tracker PSG export) has short, clean notes.
        byte[] data = [0x78, 0x02, 0x7D, 0x01, 0xAA, 0xBB, 0x0A, 0x00, 0x00]; // notes 40 (0x78), 45 (0x7D), magnitude 10
        Pt3Module module = BuildModule(data, speed: 20);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        int periodFrom = Pt3NoteTables.Period(40);
        int periodTo = Pt3NoteTables.Period(45);
        int delta = periodTo - periodFrom;
        int step = delta < 0 ? -10 : 10;

        for (int i = 0; i < 20; i++) // row1: holds note 40 throughout
        {
            Assert.Equal(periodFrom, player.Frames[i][0] | (player.Frames[i][1] << 8));
        }

        // Row2's own first tick (i=20) still writes the un-slid periodFrom - the slide's first step
        // lands one tick later, same reasoning as Play_Glissando_AddsStepToTonePeriodEveryDelayTicks.
        Assert.Equal(periodFrom, player.Frames[20][0] | (player.Frames[20][1] << 8));

        int offset = 0;
        bool reached = false;
        for (int i = 21; i < 40; i++) // rest of row2: slides, then settles at the target and stays there
        {
            if (!reached)
            {
                offset += step;
                reached = step < 0 ? offset <= delta : offset >= delta;
                if (reached)
                {
                    offset = 0;
                }
            }

            int expected = reached ? periodTo : periodFrom + offset;
            int actual = player.Frames[i][0] | (player.Frames[i][1] << 8);
            Assert.Equal(expected, actual);
        }

        Assert.Equal(periodTo, player.Frames[39][0] | (player.Frames[39][1] << 8));
    }

    [Fact]
    public void Play_Vibrato_GatesAmplitudeOffAndOnPerConfiguredDelays()
    {
        // $05, note, then params: on-delay=2, off-delay=3 (first byte is the ON-phase duration -
        // README_pt3.txt's own "YEStime, NOtime" naming and a real player's source both put it
        // first; this project used to read it backwards). Speed=6. Starts "on".
        Pt3Module module = BuildModule([0x05, 0x50, 0x02, 0x03, 0x00], speed: 6);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        // Same one-tick-later shift as the other tick-counter effects: the phase timer's first
        // increment lands on the row's first tick, not before it.
        int[] expectedAmplitude = [15, 15, 0, 0, 0, 15];
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(expectedAmplitude[i], player.Frames[i][8]);
        }
    }

    [Fact]
    public void Play_SetSpeed_ChangesTickCountStartingTheSameRow()
    {
        // $09, note, then param: new speed = 2. Module's own header speed is 5.
        Pt3Module module = BuildModule([0x09, 0x50, 0x02, 0x00], speed: 5);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        Assert.Equal(2, player.Frames.Count);
    }

    [Fact]
    public void Play_EnvelopeGlissando_NudgesTheSharedEnvelopePeriodEveryTick()
    {
        // $B2 (envelope type 1, period 5, big-endian 00 05) and $08 (glissando code) are both
        // pre-note tokens; $08's own params (delay=1, slide add=+10) come after the note. Speed=3.
        Pt3Module module = BuildModule([0xB2, 0x00, 0x05, 0x08, 0x50, 0x01, 0x0A, 0x00, 0x00], speed: 3);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        // Same one-tick-later shift: this row's own first tick writes the un-nudged base period.
        int[] expectedPeriod = [5, 15, 25];
        for (int i = 0; i < 3; i++)
        {
            int actual = player.Frames[i][11] | (player.Frames[i][12] << 8);
            Assert.Equal(expectedPeriod[i], actual);
        }
    }

    [Fact]
    public void Play_AccumulateTone_PersistsAcrossStepsEvenWhenTheBitIsClear()
    {
        // A 2-step "digi-drum"-style sample: step 0 accumulates (+5), step 1 doesn't (+2) - but the
        // accumulated offset from step 0 must still carry into step 1's tone, and step 1's own
        // offset must still be added on top of it for step 0's *next* pass (tick 2, after looping).
        var step0 = new Pt3SampleStep(
            amplitudeSliding: false, amplitudeSlideUp: false, envelopeSlideUp: false, envelopeSlideValue: 0,
            hasEnvelope: false, noiseDisabled: true, accumulateTone: true,
            slideValuePersists: false, toneDisabled: false, amplitude: 15, toneOffset: 5);
        var step1 = new Pt3SampleStep(
            amplitudeSliding: false, amplitudeSlideUp: false, envelopeSlideUp: false, envelopeSlideValue: 0,
            hasEnvelope: false, noiseDisabled: true, accumulateTone: false,
            slideValuePersists: false, toneDisabled: false, amplitude: 15, toneOffset: 2);

        var samples = new Pt3Sample?[32];
        samples[0] = new Pt3Sample(loop: 0, steps: [step0, step1]);
        Pt3Module module = BuildModule([0x50, 0x00], speed: 3, samples: samples);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        int basePeriod = Pt3NoteTables.Period(0);
        // tick0 (step0, accumulates): 5+0=5, accumulator->5.
        // tick1 (step1, doesn't accumulate): 2+5(frozen)=7, accumulator stays 5.
        // tick2 (step0 again, accumulates): 5+5(still frozen from tick0)=10, accumulator->10.
        int[] expectedOffset = [5, 7, 10];
        for (int i = 0; i < 3; i++)
        {
            int actual = (player.Frames[i][0] | (player.Frames[i][1] << 8)) - basePeriod;
            Assert.Equal(expectedOffset[i], actual);
        }
    }

    [Fact]
    public void Play_NoteSkip_HoldsTheNoteForTheConfiguredRowCountAcrossThePatternBoundary()
    {
        // $B1 value 3 (holds for 3 rows total: this one + 2 more), then note. The pattern is only
        // 1 row tall for this test (the very next byte is the true $00 end) - the skip must still
        // be honored fully even though it spans past where the pattern "ends": channel A itself is
        // what's mid-skip, so the interpreter must not even peek at $00 until A's skip reaches 0
        // (see Pt3Interpreter remarks - this is the exact bug that caused a held note to cut short
        // and re-trigger right at a pattern boundary).
        Pt3Module module = BuildModule([0xB1, 0x03, 0x50, 0x00], speed: 2);

        IRegisterDumpPlayer player = Pt3Interpreter.Play(module);

        // 3 rows held x speed 2 = 6 ticks, all still playing the same note (amplitude 15 throughout).
        Assert.Equal(6, player.Frames.Count);
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(15, player.Frames[i][8]);
        }
    }

    [Theory]
    [InlineData("EA - Proudly Loneliness (2018) (DiHalt 2018, 4).pt3")]
    [InlineData("MmcM - Hibernation (2016) (DiHalt Lite 2016, 1).pt3")]
    public void Play_RealFile_ProducesAPlausibleNonEmptyFrameSequence(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "user_provided", fileName);
        if (!File.Exists(path)) return;

        IRegisterDumpPlayer player = Pt3FileReader.Load(path);

        Assert.True(player.Frames.Count > 0);
        Assert.Equal(50, player.Metadata.FrameRateHz);
    }
}
