namespace Yamaha.Psg.Formats.Pt3;

/// <summary>
/// Mutable playback state for one PT3 channel (A, B or C), persisted across the whole song. Two
/// operations drive it: <see cref="ParseNextLine"/> (called once per pattern row, consuming command
/// bytes from the channel's stream) and <see cref="ComputeTick"/> (called once per tick — Speed
/// ticks per row — to derive this instant's tone/amplitude/mixer state and step the active
/// sample/ornament forward).
/// </summary>
internal sealed class Pt3ChannelState
{
    /// <summary>Cursor into <see cref="Pt3Module.RawData"/>. Only meaningful after <see cref="StartPattern"/>
    /// has been called; -1 is just the pre-start sentinel, not a runtime "finished" state (see
    /// <see cref="Pt3Interpreter"/> - pattern advancement is now driven externally, not by this
    /// channel reaching $00 on its own).</summary>
    public int StreamPosition { get; set; } = -1;

    public int NoteIndex { get; set; } = -1; // -1 = no note has ever been set on this channel yet
    public int SampleIndex { get; set; }
    public int OrnamentIndex { get; set; }
    public int SamplePosition { get; set; }
    public int OrnamentPosition { get; set; }
    public int Volume { get; set; } = 15;
    public bool EnvelopeEnabled { get; set; }

    /// <summary>This channel's own per-sample-step noise accumulator contribution for the current
    /// tick (see <see cref="NoiseAccumulator"/>/<see cref="ComputeTick"/>) - only meaningful when
    /// <c>DrivesSharedNoise</c> is also true for this tick. This is one of *two* additive terms that
    /// make up R6 (the shared, chip-wide noise-period register); the other, the `$20`-`$3F`
    /// pattern-command value, is chip-wide state living in <see cref="Pt3Interpreter"/>'s
    /// `noiseBase`, not here - see <see cref="Pt3RowEffects.NoiseBase"/>.</summary>
    public int NoisePeriod { get; set; }

    /// <summary>Running accumulator for the active sample step's byte0 bits 5-1 ("N4-0") field when
    /// that step has noise enabled (byte1 bit7 clear) - see <see cref="Pt3SampleStep"/> remarks and
    /// <see cref="ComputeTick"/>. Mechanically identical to <see cref="ToneAccumulator"/>: persists
    /// tick after tick (reset only on note retrigger), each active tick adds the current step's raw
    /// 5-bit value on top of whatever's already accumulated. Confirmed against a real Vortex Tracker
    /// PSG export: a repeating drum sample's noise value climbed by a fixed step (e.g. 6, then +3,
    /// +3, +3, ...) tick after tick rather than jumping straight to a flat value, exactly the same
    /// shape as a "digi-drum" tone sweep.</summary>
    public int NoiseAccumulator { get; set; }

    public int ToneAccumulator { get; set; }
    public int SkipLinesRemaining { get; set; }
    public bool Muted { get; set; } = true; // silent until the first real note

    /// <summary>Row-hold duration set by the last $B1 seen on this channel (1 = no hold, the
    /// default), *reapplied after every row this channel parses, not just the row $B1 itself
    /// appears on* - confirmed against a real Vortex Tracker PSG export: a row with no $B1 at all
    /// still inherited an earlier row's hold duration. This is genuinely persistent state, like
    /// <see cref="Volume"/>, not a one-shot value consumed by the row that sets it.</summary>
    public int NoteHoldRows { get; set; } = 1;

    /// <summary>Running amplitude-slide offset (byte0 bits 7/6 of the active sample step), -15..15,
    /// added to the step's own amplitude each tick before combining with channel volume. Confirmed
    /// against a real Vortex Tracker PSG export - without this, a sample meant to fade in/out (a very
    /// common technique) instead plays at a flat, unchanging volume.</summary>
    public int AmplitudeSlideAccumulator { get; set; }

    // --- Milestone 11.4/vs-Vortex-diff effect state. Both $01 (glissando) and $02 (portamento)
    // periodically add a step to a running tone offset, sharing these fields; they persist tick
    // after tick - not just for the row that specified them - until a new note, or a fresh effect
    // command, replaces them. They are NOT mechanically identical though (a real bug the first
    // implementation had, found by diffing against a real Vortex Tracker PSG export - it produced
    // long uncontrolled pitch slides where the original has short notes): $01 just keeps sliding
    // indefinitely with no target, while $02 slides FROM the previous note TO the new note and
    // auto-stops once it arrives - see PortamentoTargetNote/PortamentoTargetDelta and ApplyEffect's
    // $02 case.
    public int EffectToneOffset { get; set; }
    public int GlissandoDelay { get; set; } // 0 = no active glissando/portamento
    public int GlissandoStep { get; set; }
    private int _glissandoTickCounter;

    /// <summary>Set only for a `$01` glissando specified with a raw delay byte of 0 on a
    /// version&gt;=7 module (see ApplyEffect's `$01` case) - after that single, one-tick-delayed
    /// application, <see cref="GlissandoDelay"/> is forced to 0 (never re-applies), holding
    /// <see cref="EffectToneOffset"/> at that one-shot value for the rest of the note - see
    /// ComputeTick.</summary>
    private bool _glissandoOneShotThenFreeze;

    /// <summary>Non-null only for an active $02 (portamento): the note to snap to, and the tone-offset
    /// value at which to snap, once the slide reaches it. Null for a plain $01 glissando (no target).</summary>
    public int? PortamentoTargetNote { get; set; }
    public int PortamentoTargetDelta { get; set; }

    // Vibrato ($05): despite the name, README_pt3.txt describes a periodic amplitude on/off gate,
    // not a pitch wobble - "Periodic sound off/on in that channel" - implemented literally as that.
    public bool VibratoActive { get; set; }
    public int VibratoOffDelay { get; set; }
    public int VibratoOnDelay { get; set; }
    public bool VibratoIsOn { get; set; } = true;
    private int _vibratoTickCounter;

    /// <summary>Running accumulator for the active sample step's byte0 bits 5-1 field when that step
    /// has noise *disabled* (byte1 bit7 set) - the dual-purpose field's *envelope* interpretation,
    /// mechanically parallel to <see cref="NoiseAccumulator"/> but signed (bit5 is a true sign here,
    /// not folded into an unsigned 0-31 range like the noise case - see <see cref="Pt3SampleStep"/>
    /// remarks). This channel's per-tick contribution feeds into the *shared*, chip-wide envelope
    /// period as one of three additive terms (see <see cref="Pt3Interpreter"/>'s `AddToEnv`-equivalent
    /// summing, confirmed against a real player's source, Volutar/pt3player - read only, no code
    /// copied) - unlike the noise case, multiple channels' contributions genuinely add together here,
    /// there is no last-channel-wins.</summary>
    public int EnvelopeAccumulator { get; set; }

    /// <summary>The tone period register's last computed value - held as-is (not zeroed) whenever
    /// the channel is muted/inactive, matching real hardware/reference behavior. See ComputeTick.</summary>
    private int _lastTonePeriod;

    /// <summary>Starts this channel at a new pattern's command stream. Per-note state (note, sample,
    /// ornament, volume, envelope enable...) intentionally survives across patterns, matching how a
    /// real tracker continues the previous instrument/pitch into the next pattern unless a command
    /// changes it. <see cref="SkipLinesRemaining"/> is left untouched on purpose - confirmed against
    /// a reference implementation's row-advance driver, which never resets its note-skip counter at
    /// a pattern boundary, only reseats the read position. Forcing it to 0 here (the original,
    /// unverified design) cut a held note short and re-parsed a fresh row the instant a pattern
    /// happened to end mid-hold, which is exactly what a user-reported symptom traced back to: a
    /// kick drum meant to hold steady instead firing extra hits right at pattern boundaries, getting
    /// audibly denser as the song progressed through more of them.</summary>
    public void StartPattern(int channelAddress)
    {
        StreamPosition = channelAddress;
    }

    /// <summary>
    /// Parses one pattern row's worth of command bytes (README_pt3.txt "Pattern data"). Envelope
    /// commands ($10-$1F / $B2-$BF) and speed changes ($09) write into <paramref name="rowEffects"/>
    /// rather than directly to any register/interpreter field, because both are single, chip-wide
    /// state shared by all 3 channels — real hardware only has one envelope generator and one tick
    /// rate, so whichever channel issues a command last in a given row (the interpreter always
    /// parses A, then B, then C) wins for that row.
    /// </summary>
    /// <remarks>
    /// Callers must only invoke this when <see cref="SkipLinesRemaining"/> is already 0 - the
    /// interpreter's per-tick driver owns the skip countdown itself (see <see cref="Pt3Interpreter"/>),
    /// mirroring a reference implementation's per-channel Note_Skip_Counter. Pattern advancement is
    /// driven solely by channel A's own stream position against $00 (checked by the interpreter,
    /// before calling this method) - this method's own $00 handling is a defensive stop only, for
    /// when a non-A channel's row-content genuinely ends before A's within one pattern (confirmed on
    /// a real file - differing skip usage across channels means they don't all reach their own $00
    /// on the same row-boundary). It parks <see cref="StreamPosition"/> exactly at that $00 without
    /// consuming it, rather than reading past it into unrelated data.
    /// </remarks>
    public void ParseNextLine(byte[] data, Pt3RowEffects rowEffects, int version = 7)
    {
        int previousNoteIndex = NoteIndex; // captured before a note byte (if any) overwrites it - $02 needs it
        int pendingEffect = 0;
        bool retrigger = false;
        bool terminated = false;

        while (!terminated)
        {
            byte b = data[StreamPosition];

            if (b == 0x00)
            {
                // Defensive stop, not a literal match for the reference: real files can have B/C's
                // own row-content end (a genuine $00) at a different row-boundary than channel A's,
                // whenever that channel's own skip usage differs from A's - confirmed on a real file
                // (EA - Proudly Loneliness...pt3) where this caused an unrecognized-effect-code crash
                // after treating a stray $00 as a skippable no-op and reading straight into whatever
                // file bytes happened to follow it (garbage from an unrelated later pattern). Leaving
                // StreamPosition parked exactly here (not consumed) means every future call for this
                // channel safely re-hits the same $00 and no-ops again, holding its last state, rather
                // than crashing or drifting into unrelated data.
                terminated = true;
                break;
            }

            StreamPosition++;

            switch (b)
            {
                case >= 0x01 and <= 0x0F:
                    pendingEffect = b;
                    break;

                case 0x10:
                    EnvelopeEnabled = false;
                    SampleIndex = data[StreamPosition++] / 2; // matches $F0-$FF's sample encoding, not a plain index
                    // Confirmed against a real player's source (Volutar/pt3player, MIT - read only,
                    // no code copied): every command that changes envelope/sample/ornament state
                    // resets OrnamentPosition to 0, not just a fresh note or $B0/$C0 - found via a
                    // systematic read-through of every pattern-command case, not a reported symptom.
                    OrnamentPosition = 0;
                    break;

                case >= 0x11 and <= 0x1F:
                {
                    // README_pt3.txt describes this as period(2,BE) + delay(1) + sample(1), but that's
                    // wrong - cross-checked against a real file: that layout produced sample indices
                    // past the end of the 32-slot table (e.g. 64), silencing the channel outright,
                    // which is exactly the "missing bass/drums" bug the user heard. The real layout
                    // (confirmed via the same behavioral reference as the sample-step mixer bit fix)
                    // has no delay byte at all: period(2,BE) + sample(1, halved like $F0-$FF's).
                    int period = (data[StreamPosition] << 8) | data[StreamPosition + 1]; // big-endian, per spec
                    StreamPosition += 2;
                    SampleIndex = data[StreamPosition++] / 2;
                    EnvelopeEnabled = true;
                    rowEffects.Shape = (byte)(b & 0x0F);
                    rowEffects.Period = period;
                    OrnamentPosition = 0; // see $10's remark
                    break;
                }

                case >= 0x20 and <= 0x3F:
                    // Chip-wide (see Pt3Interpreter's `noiseBase`), not per-channel - only channel B
                    // ever carries this command per a real PT3 format spec, but reported upward via
                    // rowEffects like every other chip-wide command (shape/period/speed/$08), not
                    // stored locally. Confirmed against a real player's source (Volutar/pt3player,
                    // MIT - read only, no code copied): R6 = NoiseBase + AddToNoise, two genuinely
                    // separate, additive terms - NoiseBase persists across rows (reset only when the
                    // song advances to a new pattern-order position, not by any note retrigger),
                    // unlike this channel's own AddToNoise-feeding accumulator below.
                    rowEffects.NoiseBase = b - 0x20;
                    break;

                case >= 0x40 and <= 0x4F:
                    OrnamentIndex = b & 0x0F;
                    OrnamentPosition = 0; // see $10's remark
                    break;

                case >= 0x50 and <= 0xAF:
                    NoteIndex = b - 0x50;
                    Muted = false;
                    retrigger = true;
                    terminated = true;
                    break;

                case 0xB0:
                    EnvelopeEnabled = false;
                    OrnamentPosition = 0;
                    break;

                case 0xB1:
                    // Sets the PERSISTENT hold duration (see NoteHoldRows remarks) - not applied to
                    // SkipLinesRemaining directly here, since that must be (re)computed after every
                    // row this channel parses, not just this one.
                    NoteHoldRows = data[StreamPosition++];
                    break;

                case >= 0xB2 and <= 0xBF:
                {
                    int period = (data[StreamPosition] << 8) | data[StreamPosition + 1]; // big-endian
                    StreamPosition += 2;
                    EnvelopeEnabled = true;
                    rowEffects.Shape = (byte)((b & 0x0F) - 1);
                    rowEffects.Period = period;
                    OrnamentPosition = 0; // see $10's remark
                    break;
                }

                case 0xC0:
                    // Muting and the ResetPerNoteState() fields match the reference's $C0 handler
                    // exactly. EnvelopeEnabled does NOT - the reference never touches it here, only
                    // Enabled (our Muted). An earlier version of this code cleared it anyway (an
                    // unverified assumption, not something actually checked against the reference at
                    // the time), which broke a real, common pattern: a plain note retrigger right
                    // after a $C0, expected to just resume whatever envelope state was already
                    // active. Found by diffing against a real Vortex Tracker PSG export: a channel
                    // muted then retriggered went silent for an extra sample step instead of
                    // resuming envelope-driven amplitude partway through the row, exactly where the
                    // reference did.
                    Muted = true;
                    ResetPerNoteState();
                    terminated = true;
                    break;

                case >= 0xC1 and <= 0xCF:
                    Volume = b & 0x0F;
                    break;

                case 0xD0:
                    terminated = true;
                    break;

                case >= 0xD1 and <= 0xEF:
                    SampleIndex = b - 0xD0;
                    break;

                case >= 0xF0: // and <= 0xFF (byte's max)
                    OrnamentIndex = b & 0x0F;
                    SampleIndex = data[StreamPosition++] / 2;
                    EnvelopeEnabled = false;
                    OrnamentPosition = 0; // see $10's remark
                    break;
            }
        }

        // Reset stale carried-over effect state from a PREVIOUS row before applying this row's own
        // effect command (if any) - a note and its glissando/vibrato/etc. are routinely specified
        // together on the same row, and that combination must survive this reset, not be wiped by it.
        if (retrigger)
        {
            ResetPerNoteState();
        }

        if (pendingEffect != 0)
        {
            ApplyEffect(data, pendingEffect, rowEffects, previousNoteIndex, version);
        }

        // Reapply the persistent hold duration after every row this channel actually parses -
        // NoteHoldRows carries over from the last $B1 seen, even on rows with no $B1 of their own
        // (see its remarks). This must come last: SkipLinesRemaining is what the interpreter checks
        // on the *next* row-boundary to decide whether to call this method again at all.
        SkipLinesRemaining = Math.Max(NoteHoldRows - 1, 0);
    }

    /// <summary>
    /// Clears per-note playback progress: sample/ornament step position and every effect's running
    /// state (tone accumulator, glissando/portamento, vibrato, envelope glissando). Confirmed (via
    /// the same behavioral reference used for the mixer-bit and $10-$1F fixes) to apply not only on
    /// a fresh note ($50-$AF) but also on $C0 (note off) - missing it there was a real bug: a
    /// channel muted and later retriggered (very common for drums, which mute/retrigger every beat)
    /// would resume its sample step and any lingering glissando/vibrato progress from wherever they
    /// were left, rather than starting clean - audibly, notes drifting off the beat over time
    /// instead of landing consistently, exactly as reported after the $10-$1F fix.
    /// </summary>
    private void ResetPerNoteState()
    {
        SamplePosition = 0;
        OrnamentPosition = 0;
        ToneAccumulator = 0;
        NoiseAccumulator = 0;
        EnvelopeAccumulator = 0;
        EffectToneOffset = 0;
        GlissandoDelay = 0;
        _glissandoOneShotThenFreeze = false;
        VibratoActive = false;
        VibratoIsOn = true;
        AmplitudeSlideAccumulator = 0;
        PortamentoTargetNote = null;
    }

    /// <summary>
    /// Reads one $01-$0F effect's parameters (README_pt3.txt "Effects") and configures the
    /// corresponding per-tick state machine (or, for the two one-shot effects, applies immediately).
    /// </summary>
    private void ApplyEffect(byte[] data, int effectCode, Pt3RowEffects rowEffects, int previousNoteIndex, int version)
    {
        switch (effectCode)
        {
            case 0x01: // glissando: delay(1) + signed frequency delta(2, little-endian) - no target, never auto-stops
            {
                int delay = data[StreamPosition++];
                short delta = ReadInt16LE(data, StreamPosition);
                StreamPosition += 2;

                // A raw delay of 0 is NOT "apply every tick" (the milestone-11.4 guess) - confirmed
                // against a real player's source (Volutar/pt3player, MIT, pt3player.c - read only,
                // no code copied): the delay byte drives a countdown that must reach 0 via
                // decrementing to fire, so a raw 0 never naturally reaches 0 by itself. PT3 versions
                // < 7 leave it exactly like that: the glissando never fires at all for this note
                // (GlissandoDelay=0 here, and ComputeTick's own `GlissandoDelay > 0` guard already
                // skips it forever - no extra code needed). Version >= 7 special-cases a raw 0 by
                // bumping the countdown to 1 once, so it fires exactly one time (one tick after being
                // set, same timing as any other single application - see _glissandoTickCounter's -1
                // remark below), then gets stuck at 0 forever after (never re-armed), holding
                // EffectToneOffset at that one-shot value for the rest of the note - matches
                // ptdoc_pt3_format_ru.txt's "v3.7x: delay=0 - смещение прибавляется к ноте на всём её
                // протяжении" (the offset is added to the note for its whole duration).
                if (delay == 0 && version >= 7)
                {
                    GlissandoDelay = 1;
                    _glissandoOneShotThenFreeze = true;
                }
                else
                {
                    GlissandoDelay = delay;
                    _glissandoOneShotThenFreeze = false;
                }

                GlissandoStep = delta;
                // -1, not 0: the row that specifies the effect already gets a register write this
                // same tick using the UNSLID value (confirmed against a real Vortex Tracker PSG
                // export - the first slid tick lands one frame later than a naive "count from 0"
                // reset produces, which instead applied the first step immediately on the
                // effect-setting tick itself, in the middle of a real note being retriggered).
                _glissandoTickCounter = -1;
                PortamentoTargetNote = null;
                break;
            }

            case 0x02: // tone portamento: delay(1) + ignored(2) + signed slide step(2, little-endian)
            {
                int delay = data[StreamPosition++];
                StreamPosition += 2; // ignored, per README_pt3.txt's own "ignored?" note
                short rawStep = ReadInt16LE(data, StreamPosition);
                StreamPosition += 2;

                // Slides FROM the previous note TO the new one (already set as NoteIndex by this
                // row's note byte, before ApplyEffect runs) and auto-stops on arrival - confirmed
                // against a real Vortex Tracker PSG export after the "no target, keeps sliding
                // forever" bug (borrowed from $01) produced long uncontrolled pitch slides where the
                // real track has short, clean notes. The note is reverted to the previous one here;
                // the slide brings it back up/down to the real (new) note over time.
                int targetNote = NoteIndex;
                int delta = Pt3NoteTables.Period(Math.Clamp(targetNote, 0, Pt3NoteTables.NoteCount - 1))
                          - Pt3NoteTables.Period(Math.Clamp(previousNoteIndex, 0, Pt3NoteTables.NoteCount - 1));
                NoteIndex = previousNoteIndex;

                int step = Math.Abs((int)rawStep);
                // Unlike $01, a raw delay of 0 has no version-dependent special case for $02 in the
                // real player's source - it's simply never armed (GlissandoDelay=0, same
                // never-fires-at-all reasoning as $01's version<7 case above), for every version.
                GlissandoDelay = delay;
                _glissandoOneShotThenFreeze = false;
                GlissandoStep = delta < 0 ? -step : step;
                _glissandoTickCounter = -1; // see $01's remark on why this isn't 0
                EffectToneOffset = 0;
                PortamentoTargetNote = targetNote;
                PortamentoTargetDelta = delta;
                break;
            }

            case 0x03: // sample offset - one-shot
                SamplePosition = data[StreamPosition++];
                break;

            case 0x04: // ornament offset - one-shot
                OrnamentPosition = data[StreamPosition++];
                break;

            case 0x05: // vibrato ("tremolo" in real player terms): on-delay(1) + off-delay(1)
            {
                // Two real bugs found together by reading a real player's source
                // (Volutar/pt3player, MIT - read only, no code copied), both previously flagged as
                // an open, unverified risk in this file's docs:
                // 1. Byte order was backwards - README_pt3.txt's own naming ("YEStime, NOtime") and
                //    the real source's field order (`OnOff_Delay` then `OffOn_Delay`) both put the
                //    ON-phase duration FIRST, but this project had it reading into VibratoOffDelay
                //    first - meaning the on/off durations were swapped.
                // 2. A raw 0 must NOT be floored to 1 (the milestone-11.4 guess): the real mechanism
                //    is a plain countdown that only re-triggers when it reaches 0 *by decrementing a
                //    positive value* - a raw 0 for whichever phase is currently active means it gets
                //    stuck in that phase forever once reached (mechanically identical to `$01`
                //    glissando's delay=0 case, just without a version-gate - real source has no
                //    version check here at all, applies to every version).
                int onDelay = data[StreamPosition++];
                int offDelay = data[StreamPosition++];
                VibratoOnDelay = onDelay;
                VibratoOffDelay = offDelay;
                VibratoActive = true;
                VibratoIsOn = true;
                _vibratoTickCounter = -1; // see $01's remark on why this isn't 0
                break;
            }

            case 0x08: // envelope glissando: delay(1) + signed slide add(2, little-endian)
            {
                int delay = data[StreamPosition++];
                short slideAdd = ReadInt16LE(data, StreamPosition);
                StreamPosition += 2;

                // Chip-wide, not per-channel (see Pt3Interpreter's `EnvSlide*` state) - confirmed
                // against a real player's source (Volutar/pt3player, MIT - read only, no code
                // copied): the running slide accumulator this drives lives once per chip, not once
                // per channel, so it must be communicated upward via rowEffects (like $09's speed
                // and the envelope-shape/period commands already are), the same way any other
                // shared, chip-wide state is - not stored locally on this channel.
                rowEffects.EnvSlideDelay = delay;
                rowEffects.EnvSlideStep = slideAdd;
                break;
            }

            case 0x09: // set speed - one-shot, applies chip-wide (see Pt3Interpreter)
                rowEffects.NewSpeed = data[StreamPosition++];
                break;

            default:
                throw new FormatException(
                    $"Unrecognized PT3 effect code 0x{effectCode:X2} - not in the documented set {{1,2,3,4,5,8,9}}.");
        }
    }

    private static short ReadInt16LE(byte[] data, int pos) => (short)(data[pos] | (data[pos + 1] << 8));

    /// <summary>
    /// Derives this instant's tone period / amplitude / tone+noise mixer gate from the currently
    /// active sample step and ornament step, advances the glissando/vibrato/envelope-glissando
    /// effect state machines by one tick, then advances the sample/ornament one step forward
    /// (wrapping to their Loop point) for the next tick. See docs/PT3_TABLES.md "Sample step mixer
    /// bits" for the two bits (byte1 bit4/bit6) whose exact hardware meaning README_pt3.txt itself
    /// leaves uncertain, and the working interpretation used here.
    /// </summary>
    public Pt3ChannelTick ComputeTick(Pt3Module module)
    {
        if (GlissandoDelay > 0 && ++_glissandoTickCounter >= GlissandoDelay)
        {
            EffectToneOffset += GlissandoStep;
            _glissandoTickCounter = 0;

            if (_glissandoOneShotThenFreeze)
            {
                // v3.7x $01 delay=0 case (see ApplyEffect): fires exactly once, then must never
                // re-arm - forcing GlissandoDelay to 0 makes this whole block's own `> 0` guard skip
                // forever afterward, freezing EffectToneOffset at this one-shot value.
                GlissandoDelay = 0;
                _glissandoOneShotThenFreeze = false;
            }

            // Portamento ($02) only: stop and snap to the real note once the slide reaches (or
            // overshoots) its target delta - a plain $01 glissando (PortamentoTargetNote null) has
            // no target and keeps sliding until a new note/effect replaces it.
            if (PortamentoTargetNote is { } targetNote)
            {
                bool reached = GlissandoStep < 0
                    ? EffectToneOffset <= PortamentoTargetDelta
                    : EffectToneOffset >= PortamentoTargetDelta;
                if (reached)
                {
                    NoteIndex = targetNote;
                    EffectToneOffset = 0;
                    GlissandoDelay = 0;
                    PortamentoTargetNote = null;
                }
            }
        }

        if (VibratoActive)
        {
            // A phase whose own delay is 0 never re-triggers (see ApplyEffect's $05 remarks) - the
            // `phaseDelay > 0` guard mirrors GlissandoDelay's, so the channel gets stuck in whichever
            // phase it's currently in, rather than flipping every tick.
            int phaseDelay = VibratoIsOn ? VibratoOnDelay : VibratoOffDelay;
            if (phaseDelay > 0 && ++_vibratoTickCounter >= phaseDelay)
            {
                VibratoIsOn = !VibratoIsOn;
                _vibratoTickCounter = 0;
            }
        }

        Pt3Sample? sample = SampleIndex < module.Samples.Count ? module.Samples[SampleIndex] : null;
        Pt3Ornament? ornament = OrnamentIndex < module.Ornaments.Count ? module.Ornaments[OrnamentIndex] : null;

        Pt3SampleStep? step = sample is { Steps.Count: > 0 } s1
            ? s1.Steps[Math.Clamp(SamplePosition, 0, s1.Steps.Count - 1)]
            : null;
        int ornamentOffset = ornament is { Values.Count: > 0 } o1
            ? o1.Values[Math.Clamp(OrnamentPosition, 0, o1.Values.Count - 1)]
            : 0;

        bool audible = !Muted && NoteIndex >= 0 && (!VibratoActive || VibratoIsOn);

        byte amplitudeByte = 0;
        // Both default to "enabled" (mixer bit clear) - matching the reference exactly: an
        // inactive/muted channel simply never contributes its tone/noise mask bits at all (its
        // per-tick computation is skipped outright, same as the tone period above), and the R7
        // convention treats a bit that's never set as enabled. Harmless either way for what's
        // actually heard (amplitude 0 stays silent regardless), but matters for matching the
        // reference byte-for-byte - found by diffing against a real Vortex Tracker PSG export.
        bool toneOn = true;
        bool mixerNoiseOn = true;

        // Unlike mixerNoiseOn (the R7 bit, defaults enabled while muted - see remarks above), this
        // must default to false: it decides whether this channel's own NoisePeriod should overwrite
        // the *shared*, chip-wide noise-period register this tick - a muted/inactive channel, or one
        // whose current step has noise disabled, must never claim that shared register even though
        // its R7 bit still reads as "enabled" by convention. See ComputeTick's per-step noise
        // accumulator above and Pt3Interpreter, which applies this across all three channels (not
        // just channel B - the noise-*offset pattern command* is channel-B-exclusive per a real PT3
        // format spec the user supplied, but this per-sample-step accumulator is generic to any
        // channel's own sample data, confirmed against a real Vortex Tracker PSG export where channel
        // A's own sample-driven noise value took over from channel B's the instant channel A's own R7
        // noise bit turned on).
        bool drivesSharedNoise = false;

        // This channel's own additive contribution to the *shared*, chip-wide envelope period this
        // tick (see Pt3SampleStep remarks and the dual-purpose-field comment below) - unlike
        // DrivesSharedNoise (an overwrite, last-channel-wins), multiple channels' envelope
        // contributions genuinely sum together, confirmed against a real player's source
        // (Volutar/pt3player, MIT - read only, no code copied). 0 when this channel is muted or its
        // current step isn't in envelope mode.
        int envelopeContribution = 0;

        if (audible)
        {
            // Not a plain Math.Clamp - confirmed against a real player's source
            // (Volutar/pt3player, MIT - read only, no code copied): the real combine is
            // `j = (byte)(Note + ornamentByte)` (an 8-bit wraparound, not full-range signed math),
            // then `j >= 128 -> 0` (lowest note), `j > 95 -> 95` (highest note), matching
            // ptdoc_pt3_format_ru.txt's own "переполнение вниз -> C-1, переполнение вверх ->
            // не определено" - this specific player resolves "not defined" as clamp-to-95, but
            // only for results 96-127; anything that wraps into the 128-255 byte range (which
            // includes some *positive* overflows past 127, since Note maxes at 95 and an ornament
            // step maxes at +127) collapses to 0 instead, not 95. Matters only for extreme ornament
            // offsets pushing several octaves past the table's ends - reproduced exactly rather than
            // simplified, since it's a confirmed real quirk, not a guess.
            int wrapped = (NoteIndex + ornamentOffset) & 0xFF;
            int noteIndex = wrapped >= 128 ? 0 : Math.Min(wrapped, Pt3NoteTables.NoteCount - 1);
            int basePeriod = Pt3NoteTables.Period(noteIndex);

            int toneOffset = EffectToneOffset;

            if (step is { } s)
            {
                // ToneAccumulator persists across ticks regardless of THIS step's AccumulateTone bit
                // (it's only ever cleared by a note retrigger, see ParseNextLine) - a step with the
                // bit clear still has whatever was last accumulated added in, it just doesn't grow it
                // further. This is what a "digi-drum" pitch sweep actually is: a short run of
                // AccumulateTone steps compounds the offset tick after tick (each step's stored
                // accumulator already includes every earlier one), producing a fast rising/falling
                // frequency sweep rather than a fixed pitch shift. Getting this wrong (treating a
                // clear bit as "ignore the accumulator entirely", the milestone-11.4 bug) makes any
                // sample using the trick sound like noise instead of a sweep.
                int rawTone = s.ToneOffset + ToneAccumulator;
                if (s.AccumulateTone)
                {
                    ToneAccumulator = rawTone;
                }

                toneOffset += rawTone;
                toneOn = !s.ToneDisabled;
                mixerNoiseOn = !s.NoiseDisabled;
                bool useEnvelope = EnvelopeEnabled && s.HasEnvelope;

                // Byte0 bits 5-1 ("N4-0", see Pt3SampleStep remarks) are dual-purpose: when this
                // step has noise enabled (byte1 bit7 clear), the raw 5-bit value (0-31, no sign - the
                // up/down split only applies to the *envelope* interpretation, not this one) is a
                // running accumulator contribution to the *shared* noise-period register, mechanically
                // identical to ToneAccumulator/AccumulateTone above. Confirmed against a real Vortex
                // Tracker PSG export. When noise is disabled for the step instead, the same field is
                // an envelope-period nudge - this branch, added after reading a real player's source
                // (Volutar/pt3player, MIT): bit5 is a genuine sign here (up/down), not folded into an
                // unsigned 0-31 range like the noise case, and the accumulator is added into the
                // *shared* envelope period every tick this step is active, alongside every other
                // audible channel's own contribution (they sum, unlike noise's overwrite).
                if (!s.NoiseDisabled)
                {
                    int raw5BitField = (s.EnvelopeSlideUp ? 16 : 0) | s.EnvelopeSlideValue;
                    int rawNoise = raw5BitField + NoiseAccumulator;
                    if (s.SlideValuePersists)
                    {
                        NoiseAccumulator = rawNoise;
                    }

                    NoisePeriod = rawNoise;
                    drivesSharedNoise = true;
                }
                else
                {
                    int signedValue = s.EnvelopeSlideUp ? s.EnvelopeSlideValue : -s.EnvelopeSlideValue;
                    int rawEnv = signedValue + EnvelopeAccumulator;
                    if (s.SlideValuePersists)
                    {
                        EnvelopeAccumulator = rawEnv;
                    }

                    envelopeContribution = rawEnv;
                }

                // Amplitude slide (byte0 bits 7/6): persists across ticks like the tone accumulator,
                // clamped to +-15 - a very common fade in/out technique that a flat "just use the
                // step's own amplitude" reading (the milestone-11.4 gap) misses entirely, confirmed
                // against a real Vortex Tracker PSG export. Computed and combined with channel volume
                // the same way regardless of envelope use - real hardware ignores bits 3-0 of the
                // amplitude register while the envelope-enable bit (bit 4) is set, but the register
                // byte a real player actually writes there isn't just a flat 0x10: confirmed against
                // a real Vortex Tracker PSG export, which showed the *same* volume/amplitude-combine
                // result in the low nibble whether or not envelope was active (a flat 0x10 only
                // happened to match by coincidence whenever that combine result was 0). The envelope
                // bit is simply OR'd on top, not a replacement for the whole byte.
                if (s.AmplitudeSliding)
                {
                    if (s.AmplitudeSlideUp)
                    {
                        AmplitudeSlideAccumulator = Math.Min(AmplitudeSlideAccumulator + 1, 15);
                    }
                    else
                    {
                        AmplitudeSlideAccumulator = Math.Max(AmplitudeSlideAccumulator - 1, -15);
                    }
                }

                int slidAmplitude = Math.Clamp(s.Amplitude + AmplitudeSlideAccumulator, 0, 15);
                amplitudeByte = (byte)(Pt3VolumeTables.Combine(slidAmplitude, Volume, module.Version) & 0x0F);
                if (useEnvelope)
                {
                    amplitudeByte |= 0x10;
                }
            }

            // Real hardware just has a 12-bit tone period register - it wraps, it doesn't clamp.
            // Clamping a fast-sweeping digi-drum tone at the top of the range would flatten the
            // sweep into a stuck plateau instead of letting it wrap around, audibly different.
            _lastTonePeriod = (basePeriod + toneOffset) & 0xFFF;

            // Sample/ornament step position only advances while the channel is actually active -
            // matching the reference exactly (its whole per-tick tone/amplitude computation, this
            // included, is skipped outright when muted). Confirmed by diffing against a real Vortex
            // Tracker PSG export: a muted-then-retriggered channel's first post-retrigger step must
            // be the sample's *own* step 0 (already handled by the note retrigger's reset), not
            // wherever an ungated advance would have silently walked it to while muted.
            if (sample is { Steps.Count: > 0 })
            {
                SamplePosition = Advance(SamplePosition, sample.Steps.Count, sample.Loop);
            }

            if (ornament is { Values.Count: > 0 })
            {
                OrnamentPosition = Advance(OrnamentPosition, ornament.Values.Count, ornament.Loop);
            }
        }

        // The tone period register isn't reset just because the channel went quiet - it holds its
        // last value (matching the reference, which simply skips recomputing chan.Ton at all while
        // disabled). Found by diffing against a real Vortex Tracker PSG export: this project used to
        // zero the period out here, which was harmless for the muted frames themselves (amplitude 0
        // is silent either way) but broke the byte-exact register comparison used to validate
        // everything else in this file.
        return new Pt3ChannelTick(_lastTonePeriod, amplitudeByte, ToneOn: toneOn, MixerNoiseOn: mixerNoiseOn, DrivesSharedNoise: drivesSharedNoise, NoisePeriod, envelopeContribution);
    }

    private static int Advance(int position, int length, int loop)
    {
        position++;
        return position >= length ? Math.Clamp(loop, 0, length - 1) : position;
    }
}

/// <summary>One row's worth of chip-wide state a channel's commands may set (see <see cref="Pt3ChannelState.ParseNextLine"/>).</summary>
internal sealed class Pt3RowEffects
{
    public byte? Shape { get; set; }
    public int Period { get; set; }
    public int? NewSpeed { get; set; }

    /// <summary>Set by a `$08` (envelope glissando) on any channel this row - chip-wide state (see
    /// <see cref="Pt3Interpreter"/>'s `EnvSlide*` fields), not per-channel; last channel to issue it
    /// in a row wins, same as <see cref="Shape"/>/<see cref="Period"/>.</summary>
    public int? EnvSlideDelay { get; set; }
    public int? EnvSlideStep { get; set; }

    /// <summary>Set by a `$20`-`$3F` (noise offset) on any channel this row - only channel B ever
    /// carries this command per a real PT3 format spec, but it's chip-wide state regardless (see
    /// <see cref="Pt3Interpreter"/>'s `noiseBase`), reported upward the same way as the other
    /// chip-wide commands rather than stored per-channel.</summary>
    public int? NoiseBase { get; set; }
}

/// <summary>
/// One channel's derived state for a single output tick. <see cref="MixerNoiseOn"/> (the R7 bit)
/// defaults to "enabled" even while the channel is muted, matching the reference (a skipped/muted
/// channel simply never contributes any mixer bit at all, and the R7 convention treats a never-set
/// bit as enabled - harmless for what's heard, since amplitude 0 stays silent regardless, but
/// required to match the reference byte-for-byte, confirmed by diffing against a real Vortex Tracker
/// PSG export). <see cref="DrivesSharedNoise"/> is deliberately different from <see cref="MixerNoiseOn"/>
/// (and defaults to false, not true): it decides whether this channel's own <see cref="NoisePeriod"/>
/// should overwrite the *shared*, chip-wide noise-period register this tick - see
/// <see cref="Pt3Interpreter"/>, which checks it for all three channels (A, B and C, in that order -
/// last one wins), not just channel B. The noise-offset *pattern command* ($20-$3F) is channel-B-only
/// per a real PT3 format spec the user supplied, but the per-sample-step noise accumulator (see
/// <see cref="Pt3ChannelState.ComputeTick"/>) is generic to any channel's own sample data - confirmed
/// against a real Vortex Tracker PSG export where channel A's own sample-driven value visibly took
/// over the shared register from channel B's the instant channel A's own R7 noise bit turned on.
/// </summary>
internal readonly record struct Pt3ChannelTick(
    int TonePeriod, byte AmplitudeByte, bool ToneOn, bool MixerNoiseOn, bool DrivesSharedNoise, int NoisePeriod, int EnvelopeContribution);
