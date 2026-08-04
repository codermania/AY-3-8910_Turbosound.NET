using Yamaha.Psg.Formats.Pt3;

namespace Yamaha.Psg.Formats.Tests;

/// <summary>
/// Milestone 11.3 tokenizer tests: synthetic byte streams, isolated from any real file, so the
/// row/effect-skip scanning logic in <see cref="Pt3ChannelState.ParseNextLine"/> can be pinned down
/// precisely (README_pt3.txt's own effect-parameter byte counts are the highest-risk part of this
/// milestone - getting one wrong desyncs every row after the first effect in a real file).
/// </summary>
public class Pt3ChannelStateTests
{
    [Fact]
    public void ParseNextLine_PlainNote_SetsNoteAndUnmutes()
    {
        var data = new byte[] { 0x50, 0x00 }; // note C-1 (index 0), then end of pattern
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.Equal(0, channel.NoteIndex);
        Assert.False(channel.Muted);
        Assert.Equal(1, channel.StreamPosition); // positioned right after the note byte, at $00
    }

    [Fact]
    public void ParseNextLine_ZeroByte_StopsWithoutConsumingItOrTouchingState()
    {
        // Pattern advancement is driven solely by channel A's own position against $00 (checked by
        // the interpreter, before calling this method). This method's own $00 handling is a
        // defensive stop for when a non-A channel's row-content genuinely ends before A's within one
        // pattern (confirmed on a real file - reading past it as a no-op walked into unrelated data
        // and crashed on a garbage byte that looked like an effect code). The byte must not be
        // consumed, so a repeat call safely re-hits it and stays a no-op rather than drifting.
        var data = new byte[] { 0x00, 0x50 };
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());
        Assert.Equal(-1, channel.NoteIndex); // untouched - the $00 was never consumed as a note
        Assert.Equal(0, channel.StreamPosition); // parked exactly on the $00, not past it

        channel.ParseNextLine(data, new Pt3RowEffects()); // repeat call: still safe, still a no-op
        Assert.Equal(-1, channel.NoteIndex);
        Assert.Equal(0, channel.StreamPosition);
    }

    [Fact]
    public void ParseNextLine_NoteOff_MutesAndTerminatesLine()
    {
        var data = new byte[] { 0xC0, 0x00 };
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.True(channel.Muted);
        Assert.Equal(1, channel.StreamPosition);
    }

    [Fact]
    public void ParseNextLine_D0_TerminatesLineWithoutTouchingNote()
    {
        var data = new byte[] { 0xC5, 0xD0, 0x00 }; // volume=5, then "done processing this note"
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.Equal(5, channel.Volume);
        Assert.Equal(-1, channel.NoteIndex); // untouched - no note byte was seen
        Assert.Equal(2, channel.StreamPosition);
    }

    [Theory]
    [InlineData(0x01, 3)] // glissando: delay + signed frequency delta
    [InlineData(0x02, 5)] // tone portamento: delay + ignored(2) + slide step(2)
    [InlineData(0x03, 1)] // sample offset
    [InlineData(0x04, 1)] // ornament offset
    [InlineData(0x05, 2)] // vibrato
    [InlineData(0x08, 3)] // envelope glissando
    [InlineData(0x09, 1)] // set speed
    public void ParseNextLine_EffectCode_SkipsExactlyItsDocumentedParameterByteCount(byte effectCode, int paramByteCount)
    {
        // [effect code][note][paramByteCount junk bytes] - the cursor must land exactly at the end.
        var data = new List<byte> { effectCode, 0x50 };
        data.AddRange(Enumerable.Repeat((byte)0xAA, paramByteCount));

        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data.ToArray(), new Pt3RowEffects());

        Assert.Equal(2 + paramByteCount, channel.StreamPosition);
    }

    [Fact]
    public void ParseNextLine_UnrecognizedEffectCode_Throws()
    {
        var data = new byte[] { 0x06, 0x50, 0x00 }; // $06 is not in the documented effect set
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        Assert.Throws<FormatException>(() => channel.ParseNextLine(data, new Pt3RowEffects()));
    }

    [Fact]
    public void ParseNextLine_NoteSkip_SetsSkipCountToNMinusOne()
    {
        // $B1 value 3, then note. The file's byte is the TOTAL row count this note holds for
        // (this row plus 2 more held) - confirmed against a reference implementation's
        // Note_Skip_Counter mechanism; the naive "N additional rows skipped" reading
        // (README_pt3.txt's own phrasing) is off by one and, since $B1 is used often, compounds
        // into a real drift over a whole song. The countdown/no-op *mechanism* itself is now owned
        // by the interpreter (Pt3ChannelState.ParseNextLine is only ever called once skip has
        // already reached 0 - see its remarks) - covered end-to-end in Pt3InterpreterTests.
        var data = new byte[] { 0xB1, 0x03, 0x50 };
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.Equal(2, channel.SkipLinesRemaining);
    }

    [Fact]
    public void ParseNextLine_EnvelopeCommand_WritesSharedEnvelopeAndEnablesIt()
    {
        // $15: envelope type 5, period 0x0102 (big-endian: 01 02), sample = 0x08/2 = 4.
        // No delay byte here - README_pt3.txt claims one, but a real file proved that wrong (see
        // Pt3ChannelState remarks): the sample byte immediately follows the period, halved just
        // like $F0-$FF's sample encoding.
        var data = new byte[] { 0x15, 0x01, 0x02, 0x08, 0x50, 0x00 };
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);
        var envelope = new Pt3RowEffects();

        channel.ParseNextLine(data, envelope);

        Assert.Equal((byte)5, envelope.Shape);
        Assert.Equal(0x0102, envelope.Period);
        Assert.True(channel.EnvelopeEnabled);
        Assert.Equal(4, channel.SampleIndex);
    }

    [Fact]
    public void ParseNextLine_EnvelopeDisable_ReadsHalvedSampleNumberAndDisablesEnvelope()
    {
        var data = new byte[] { 0x10, 0x0E, 0x50, 0x00 }; // $10: disable envelope, sample = 0x0E/2 = 7
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);
        channel.EnvelopeEnabled = true;

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.False(channel.EnvelopeEnabled);
        Assert.Equal(7, channel.SampleIndex);
    }

    // In all the tests below, the effect CODE ($01-$0F) is a pre-note token like noise/ornament, but
    // its PARAMETER bytes are read only after the note terminator (README_pt3.txt: "parameters to
    // the effect appear in the bytestream *after* the note to play") - so the byte layout here is
    // [code][note][params...], not [code][params...][note].

    [Fact]
    public void ParseNextLine_Glissando_ReadsDelayAndSignedDelta()
    {
        // $01, note, then params: delay=5, delta = LE(0xFE,0xFF) = -2 (signed)
        var data = new byte[] { 0x01, 0x50, 0x05, 0xFE, 0xFF, 0x00 };
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.Equal(5, channel.GlissandoDelay);
        Assert.Equal(-2, channel.GlissandoStep);
    }

    [Fact]
    public void ParseNextLine_Portamento_SlidesFromThePreviousNoteToTheNewOneWithATarget()
    {
        // Row1: plain note 40. Row2: $02 (delay=1, ignored x2, magnitude=5) to note 45. Unlike $01,
        // $02 has a target and reverts NoteIndex to the *previous* note - the slide is what carries
        // it back up to the real (new) note over subsequent ticks (see ApplyEffect's $02 remarks;
        // the first implementation treated $01/$02 as identical, producing long uncontrolled pitch
        // slides where a real track has short notes, found by diffing against a Vortex Tracker
        // PSG export of a real file).
        byte[] data = [0x78, 0x02, 0x7D, 0x01, 0xAA, 0xBB, 0x05, 0x00]; // notes 40 (0x78) and 45 (0x7D)
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());
        Assert.Equal(40, channel.NoteIndex);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.Equal(40, channel.NoteIndex); // reverted - not 45 yet, the slide gets it there
        Assert.Equal(45, channel.PortamentoTargetNote);
        int expectedDelta = Pt3NoteTables.Period(45) - Pt3NoteTables.Period(40);
        Assert.Equal(expectedDelta, channel.PortamentoTargetDelta);
        Assert.Equal(1, channel.GlissandoDelay);
        Assert.Equal(expectedDelta < 0 ? -5 : 5, channel.GlissandoStep);
    }

    [Fact]
    public void ParseNextLine_SampleOffset_AppliesImmediately()
    {
        var data = new byte[] { 0x03, 0x50, 0x05, 0x00 }; // $03, note, then sample position = 5
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.Equal(5, channel.SamplePosition);
    }

    [Fact]
    public void ParseNextLine_OrnamentOffset_AppliesImmediately()
    {
        var data = new byte[] { 0x04, 0x50, 0x02, 0x00 }; // $04, note, then ornament position = 2
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.Equal(2, channel.OrnamentPosition);
    }

    [Fact]
    public void ParseNextLine_Vibrato_ActivatesWithBothDelays()
    {
        var data = new byte[] { 0x05, 0x50, 0x04, 0x03, 0x00 }; // $05, note, then on-delay=4, off-delay=3
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.True(channel.VibratoActive);
        Assert.Equal(4, channel.VibratoOnDelay);
        Assert.Equal(3, channel.VibratoOffDelay);
        Assert.True(channel.VibratoIsOn);
    }

    [Fact]
    public void ParseNextLine_EnvelopeGlissando_WritesDelayAndSignedSlideAddToRowEffects()
    {
        // $08, note, then params: delay=2, slide add = LE(0x0A,0x00) = 10 - chip-wide state (see
        // Pt3Interpreter), reported upward via rowEffects like $09's speed, not stored per-channel.
        var data = new byte[] { 0x08, 0x50, 0x02, 0x0A, 0x00, 0x00 };
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);
        var rowEffects = new Pt3RowEffects();

        channel.ParseNextLine(data, rowEffects);

        Assert.Equal(2, rowEffects.EnvSlideDelay);
        Assert.Equal(10, rowEffects.EnvSlideStep);
    }

    [Fact]
    public void ParseNextLine_SetSpeed_WritesToRowEffects()
    {
        var data = new byte[] { 0x09, 0x50, 0x03, 0x00 }; // $09, note, then new speed = 3
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);
        var rowEffects = new Pt3RowEffects();

        channel.ParseNextLine(data, rowEffects);

        Assert.Equal(3, rowEffects.NewSpeed);
    }

    [Fact]
    public void ParseNextLine_GlissandoOnSameRowAsNote_SurvivesTheRetriggerReset()
    {
        // Glissando and its note are specified together on one row - a very common combination.
        // The retrigger reset (clearing stale effect state from a PREVIOUS row) must not also wipe
        // out an effect this SAME row just configured.
        var data = new byte[] { 0x01, 0x50, 0x05, 0x0A, 0x00, 0x00 };
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.Equal(5, channel.GlissandoDelay);
        Assert.Equal(10, channel.GlissandoStep);
    }

    [Fact]
    public void ParseNextLine_PlainNoteOnANewRow_ClearsAPreviouslyActiveGlissando()
    {
        // Row 1: glissando + note. Row 2: a plain new note, no effect - the glissando must not
        // carry over onto the new note.
        var data = new byte[] { 0x01, 0x50, 0x05, 0x0A, 0x00, 0x51, 0x00 };
        var channel = new Pt3ChannelState();
        channel.StartPattern(0);

        channel.ParseNextLine(data, new Pt3RowEffects());
        Assert.Equal(5, channel.GlissandoDelay);

        channel.ParseNextLine(data, new Pt3RowEffects());

        Assert.Equal(0, channel.GlissandoDelay);
        Assert.Equal(1, channel.NoteIndex);
    }
}
