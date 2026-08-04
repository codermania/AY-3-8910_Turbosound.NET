using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Formats.Common;

namespace Yamaha.Psg.Formats.Pt3;

/// <summary>
/// Plays a parsed <see cref="Pt3Module"/> (milestone 11.2) into a flat <see cref="RegisterFrame"/>
/// sequence, the same shape PSG/VTX produce - so it plays through the existing, already-tested
/// <see cref="Yamaha.Psg.Player.FileDrivenPlaybackDriver"/> without any changes there. Notes,
/// samples, ornaments, envelope, volume (milestone 11.3) and the $01/$02/$03/$04/$05/$08/$09
/// effects (milestone 11.4 - glissando, portamento, sample/ornament offset, vibrato, envelope
/// glissando, speed change; note-skip/$B1 was already covered in 11.3, see Pt3ChannelState) are
/// all implemented; per-effect design decisions and open risks are in docs/PT3_TABLES.md. Only the
/// FIRST module in the file is played - a real TS/TurboSound file has a second module's data after
/// this one plus a trailer, both simply never referenced by this module's own header pointers, so
/// they're naturally ignored (milestone 11.5).
/// </summary>
/// <remarks>
/// The main loop is tick-boundary-driven (one "row boundary" = one <see cref="Pt3Module.Speed"/>
/// countdown, matching a real player's per-frame interrupt), not "wait for all 3 channels to
/// individually finish a pattern" as an earlier version had it. That earlier shape was found wrong
/// from a real symptom: a kick drum meant to hold a steady beat instead fired extra hits that got
/// denser over the course of a song. Cross-checked against a reference implementation's row-advance
/// driver: only channel A's own stream position is ever tested against $00, and only to decide
/// whether to advance to the next pattern - channels B and C never independently "finish"; they just
/// get their read position forcibly reseated to the new pattern whenever A advances, regardless of
/// what they were doing (mid-held-note or not), and otherwise progress purely on their own
/// per-channel skip countdown. This loop is the only place that decides *whether to advance the
/// song*, and it only ever looks at channel A's position for that; <see cref="Pt3ChannelState.ParseNextLine"/>
/// still stops defensively on its own $00 (a real file showed B/C can reach their own row-content's
/// end before A's, when their skip usage differs), it just never triggers a pattern change itself.
/// </remarks>
internal static class Pt3Interpreter
{
    public static IRegisterDumpPlayer Play(Pt3Module module)
    {
        var channels = new[] { new Pt3ChannelState(), new Pt3ChannelState(), new Pt3ChannelState() };
        var frames = new List<RegisterFrame>();

        byte currentEnvelopeShape = 0;

        // Envelope period (R11/R12) each tick is the sum of three independent, non-conflicting
        // sources - confirmed against a real player's source (Volutar/pt3player, MIT license,
        // read only, no code copied - see docs/PT3_TABLES.md): `envelopeBase` (only ever set by an
        // explicit $10-$1F/$B2-$BF command, chip-wide), `envSlideAccumulator` (chip-wide, driven only
        // by $08 - persists across note retriggers/pattern boundaries on ANY channel, only reset when
        // envelopeBase itself is freshly set), and each channel's own per-sample-step envelope
        // contribution (freshly summed every tick, see the tick loop below - this is NOT persistent
        // at the chip level, only each channel's own internal accumulator is). Earlier attempts to
        // fix an envelope-glissando-cut-off-by-retrigger bug by making `EnvGlissandoDelay` per-channel
        // survive a retrigger regressed badly, because that per-channel state was ALSO (wrongly)
        // carrying the per-tick sample contribution, which really must reset every tick regardless of
        // retriggers - separating the three sources here is what actually fixes it.
        int envelopeBase = 0;
        int envSlideAccumulator = 0;
        int envSlideDelay = 0;
        int envSlideStep = 0;
        int envSlideTickCounter = -1;

        // R6 (noise period) is likewise two separate additive terms, not one conflated value -
        // confirmed against the same real player's source: `noiseBase` (chip-wide, set only by a
        // `$20`-`$3F` pattern command, persists across rows and note retriggers, reset only when the
        // song advances to a new pattern-order position - see the `orderPos++` branch below) plus
        // `addToNoise` (each channel's own per-sample-step accumulator, last-channel-wins/overwrite
        // - unlike the envelope register's sum - see the tick loop below).
        int noiseBase = 0;
        int addToNoise = 0;
        int currentSpeed = module.Speed;

        int orderPos = 0;
        int loopFrame = 0; // correct by construction if LoopPosition == 0 (frames.Count is 0 here)

        Pt3Pattern firstPattern = module.Patterns[module.PatternOrder[orderPos]];
        channels[0].StartPattern(firstPattern.ChannelAAddress);
        channels[1].StartPattern(firstPattern.ChannelBAddress);
        channels[2].StartPattern(firstPattern.ChannelCAddress);

        while (true)
        {
            var rowEffects = new Pt3RowEffects();

            // Channel A alone drives pattern advancement - see class remarks.
            if (channels[0].SkipLinesRemaining > 0)
            {
                channels[0].SkipLinesRemaining--;
            }
            else
            {
                if (module.RawData[channels[0].StreamPosition] == 0x00)
                {
                    orderPos++;
                    if (orderPos >= module.PatternOrder.Count)
                    {
                        break; // played through the whole PatternOrder once
                    }

                    if (orderPos == module.LoopPosition)
                    {
                        loopFrame = frames.Count;
                    }

                    Pt3Pattern next = module.Patterns[module.PatternOrder[orderPos]];
                    channels[0].StartPattern(next.ChannelAAddress);
                    channels[1].StartPattern(next.ChannelBAddress);
                    channels[2].StartPattern(next.ChannelCAddress);

                    // Confirmed against a real player's source: the noise-offset base resets only
                    // here (advancing to a new pattern-order position), not on every row and not on
                    // any channel's note retrigger.
                    noiseBase = 0;
                }

                channels[0].ParseNextLine(module.RawData, rowEffects, module.Version);
            }

            for (int i = 1; i <= 2; i++)
            {
                if (channels[i].SkipLinesRemaining > 0)
                {
                    channels[i].SkipLinesRemaining--;
                }
                else
                {
                    channels[i].ParseNextLine(module.RawData, rowEffects, module.Version);
                }
            }

            if (rowEffects.Shape is { } freshShape)
            {
                currentEnvelopeShape = freshShape;
                envelopeBase = rowEffects.Period;

                // A freshly-set envelope period stops any in-flight $08 slide dead - confirmed
                // against a real player's source: Cur_Env_Slide/Cur_Env_Delay are unconditionally
                // zeroed inside the $10-$1F/$B2-$BF handlers, every time, not just when $08 also
                // happens to be present on the same row.
                envSlideAccumulator = 0;
                envSlideDelay = 0;
                envSlideStep = 0;
            }

            if (rowEffects.EnvSlideDelay is { } freshEnvSlideDelay)
            {
                // A fresh $08 changes the step/delay for future ticks but does NOT reset the
                // already-accumulated slide value (envSlideAccumulator) - confirmed against the real
                // source, which only resets Cur_Env_Delay (the countdown), never Cur_Env_Slide, here.
                envSlideDelay = freshEnvSlideDelay;
                envSlideStep = rowEffects.EnvSlideStep!.Value;
                envSlideTickCounter = -1; // see Pt3ChannelState's glissando remark on why -1, not 0
            }

            if (rowEffects.NoiseBase is { } freshNoiseBase)
            {
                noiseBase = freshNoiseBase;
            }

            if (rowEffects.NewSpeed is { } newSpeed)
            {
                currentSpeed = Math.Max(newSpeed, 1); // $09 "set speed" - chip-wide, from this row on
            }

            for (int tick = 0; tick < currentSpeed; tick++)
            {
                Pt3ChannelTick a = channels[0].ComputeTick(module);
                Pt3ChannelTick b = channels[1].ComputeTick(module);
                Pt3ChannelTick c = channels[2].ComputeTick(module);

                // `addToNoise` (one of R6's two additive terms - see `noiseBase`'s remarks above) is
                // driven by whichever channel(s) currently have noise enabled for their active sample
                // step, checked in A, B, C order (last one wins ties within the same tick) - see
                // Pt3ChannelTick.DrivesSharedNoise's remarks. A real PT3 format spec the user supplied
                // (ptdoc.txt) says the noise-offset *pattern command* ($20-$3F) only ever appears in
                // channel B's own byte stream, but that is a separate, narrower fact from this: the
                // per-sample-step noise accumulator (any channel's own sample data can carry it) is
                // what actually drives this term moment to moment, confirmed against a real Vortex
                // Tracker PSG export - channel A's own sample-driven contribution visibly took over
                // from channel B's the instant channel A's own R7 noise bit turned on, mid-note, with
                // no pattern command involved.
                if (a.DrivesSharedNoise)
                {
                    addToNoise = a.NoisePeriod;
                }

                if (b.DrivesSharedNoise)
                {
                    addToNoise = b.NoisePeriod;
                }

                if (c.DrivesSharedNoise)
                {
                    addToNoise = c.NoisePeriod;
                }

                // $08 (envelope glissando): one chip-wide countdown, advanced once per tick here
                // (not per channel) - see envSlideAccumulator's remarks above.
                if (envSlideDelay > 0 && ++envSlideTickCounter >= envSlideDelay)
                {
                    envSlideAccumulator += envSlideStep;
                    envSlideTickCounter = 0;
                }

                // Each channel's own per-sample-step envelope contribution is recomputed fresh every
                // tick (not accumulated at this level) and simply summed - confirmed against the real
                // source, which sums all three into one `AddToEnv` global, reset to 0 every tick,
                // unlike the noise register's overwrite/last-wins semantics.
                int addToEnv = a.EnvelopeContribution + b.EnvelopeContribution + c.EnvelopeContribution;
                int envelopePeriod = (envelopeBase + addToEnv + envSlideAccumulator) & 0xFFFF;

                var registers = new byte[RegisterFrame.RegisterCount];
                registers[0] = (byte)a.TonePeriod;
                registers[1] = (byte)(a.TonePeriod >> 8);
                registers[2] = (byte)b.TonePeriod;
                registers[3] = (byte)(b.TonePeriod >> 8);
                registers[4] = (byte)c.TonePeriod;
                registers[5] = (byte)(c.TonePeriod >> 8);
                registers[6] = (byte)((noiseBase + addToNoise) & 0x1F);
                registers[7] = EncodeMixer(a, b, c);
                registers[8] = a.AmplitudeByte;
                registers[9] = b.AmplitudeByte;
                registers[10] = c.AmplitudeByte;
                registers[11] = (byte)envelopePeriod;
                registers[12] = (byte)(envelopePeriod >> 8);
                registers[13] = currentEnvelopeShape;

                bool envelopeShapeWrittenThisTick = tick == 0 && rowEffects.Shape.HasValue;
                frames.Add(new RegisterFrame(registers, envelopeShapeWrittenThisTick));
            }
        }

        var metadata = new RegisterDumpMetadata
        {
            Title = module.Name,
            Author = module.Author,
            FrameRateHz = 50, // one tick = one ZX Spectrum interrupt frame
            ClockHz = PsgClockPresets.ZxSpectrum, // PT3 carries no clock field - see Pt3NoteTables
            LoopFrame = loopFrame,
        };

        return new RegisterDumpPlayer(metadata, frames);
    }

    /// <summary>R7: active-low, bit0-2 = tone A/B/C disabled, bit3-5 = noise A/B/C disabled (see Mixer.cs).</summary>
    private static byte EncodeMixer(Pt3ChannelTick a, Pt3ChannelTick b, Pt3ChannelTick c)
    {
        int r7 = 0b0011_1111;
        if (a.ToneOn) r7 &= ~0x01;
        if (b.ToneOn) r7 &= ~0x02;
        if (c.ToneOn) r7 &= ~0x04;
        if (a.MixerNoiseOn) r7 &= ~0x08;
        if (b.MixerNoiseOn) r7 &= ~0x10;
        if (c.MixerNoiseOn) r7 &= ~0x20;
        return (byte)r7;
    }
}
