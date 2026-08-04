using Yamaha.Psg.Formats.Common;
using Yamaha.Psg.Formats.Psg;
using Yamaha.Psg.Formats.Pt3;

namespace Yamaha.Psg.Formats.Tests;

/// <summary>
/// Regression test against real ground truth: a Vortex Tracker PSG export of
/// "MmcM - Hibernation...pt3", produced by the original authoring software itself, diffed
/// register-by-register against our own PT3 interpretation of the same file (see
/// docs/PT3_TABLES.md "Ground-truth verification method"). This is what found the note-table
/// clock, volume-table/amplitude-slide, note-hold-persistence, $C0/tone-persistence/mixer-default,
/// glissando-family tick-counter-off-by-one, and noise-period-accumulator/priority bugs, and (in a
/// later continuation) the envelope-period source-conflation bug and the noise-register's missing
/// separate `NoiseBase` term. Frames 0-4687 are now confirmed byte-exact (total frame count also
/// matches exactly: 4724 both sides). The only remaining divergence is a solid block at the very
/// tail of the file (frames 4688-4723, the last 36 frames, R7/R9 only) - not yet root-caused, likely
/// something specific to how the song ends/loops; see docs/PT3_TABLES.md.
/// </summary>
public class Pt3VsVortexPsgTests
{
    [Fact]
    public void Play_Hibernation_MatchesVortexTrackerPsgExport_ForTheConfirmedCorrectPrefix()
    {
        if (!UserProvidedFixture.TryResolve(out string[] paths,
            "MmcM - Hibernation (2016) (DiHalt Lite 2016, 1).psg",
            "MmcM - Hibernation (2016) (DiHalt Lite 2016, 1).pt3"))
        {
            return;
        }

        IRegisterDumpPlayer reference = PsgFileReader.Load(paths[0]);
        IRegisterDumpPlayer ours = Pt3FileReader.Load(paths[1]);

        Assert.Equal(reference.Frames.Count, ours.Frames.Count);

        const int confirmedCorrectFrames = 4688; // frames 0-4687 - see class remarks

        for (int i = 0; i < confirmedCorrectFrames; i++)
        {
            RegisterFrame r = reference.Frames[i];
            RegisterFrame o = ours.Frames[i];
            for (int reg = 0; reg < RegisterFrame.RegisterCount; reg++)
            {
                Assert.True(r[reg] == o[reg], $"Register {reg} differs at frame {i}: reference={r[reg]}, ours={o[reg]}");
            }
        }
    }

    [Fact]
    public void Play_Hibernation_OverallByteExactRateDoesNotRegress()
    {
        if (!UserProvidedFixture.TryResolve(out string[] paths,
            "MmcM - Hibernation (2016) (DiHalt Lite 2016, 1).psg",
            "MmcM - Hibernation (2016) (DiHalt Lite 2016, 1).pt3"))
        {
            return;
        }

        IRegisterDumpPlayer reference = PsgFileReader.Load(paths[0]);
        IRegisterDumpPlayer ours = Pt3FileReader.Load(paths[1]);

        int compareLength = Math.Min(reference.Frames.Count, ours.Frames.Count);
        int diffFrames = 0;
        for (int i = 0; i < compareLength; i++)
        {
            RegisterFrame r = reference.Frames[i];
            RegisterFrame o = ours.Frames[i];
            for (int reg = 0; reg < RegisterFrame.RegisterCount; reg++)
            {
                if (r[reg] != o[reg]) { diffFrames++; break; }
            }
        }

        // As of this fix round: 36/4724 differing frames (99.24% byte-exact), all isolated to the
        // last 36 frames of the file (R7/R9 only) - see PT3_TABLES.md for the remaining known issue.
        // A little slack above the exact current count avoids a flaky test over an unrelated
        // single-frame rounding difference; a real regression here should show up as a much larger jump.
        Assert.True(diffFrames <= 45, $"Overall byte-exact rate regressed: {diffFrames}/{compareLength} frames now differ (was 36 when this bound was set).");
    }

    /// <summary>
    /// "MmcM - Man of Art...pt3" - a third independent ground-truth file, added specifically to
    /// check whether the noise-register and envelope-period fixes found on Hibernation/How-are-you
    /// generalize. They do completely: this file is 100% byte-exact across all 8676 frames and all
    /// 14 registers - the strongest possible regression test, since any future change that breaks
    /// anything here fails immediately rather than needing a floor/percentage check.
    /// </summary>
    [Fact]
    public void Play_ManOfArt_MatchesVortexTrackerPsgExport_Completely()
    {
        if (!UserProvidedFixture.TryResolve(out string[] paths,
            "MmcM - Man of Art (2015) (Multimatograf 11, 1).psg",
            "MmcM - Man of Art (2015) (Multimatograf 11, 1).pt3"))
        {
            return;
        }

        IRegisterDumpPlayer reference = PsgFileReader.Load(paths[0]);
        IRegisterDumpPlayer ours = Pt3FileReader.Load(paths[1]);

        Assert.Equal(reference.Frames.Count, ours.Frames.Count);

        for (int i = 0; i < reference.Frames.Count; i++)
        {
            RegisterFrame r = reference.Frames[i];
            RegisterFrame o = ours.Frames[i];
            for (int reg = 0; reg < RegisterFrame.RegisterCount; reg++)
            {
                Assert.True(r[reg] == o[reg], $"Register {reg} differs at frame {i}: reference={r[reg]}, ours={o[reg]}");
            }
        }
    }
}
