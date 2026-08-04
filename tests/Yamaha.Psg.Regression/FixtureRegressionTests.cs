using System.Security.Cryptography;
using Yamaha.Psg.Core.Chip;
using Yamaha.Psg.Core.Output;
using Yamaha.Psg.Formats.Common;
using Yamaha.Psg.Formats.Psg;
using Yamaha.Psg.Formats.Vtx;
using Yamaha.Psg.Player;

namespace Yamaha.Psg.Regression;

/// <summary>
/// Golden-file regression (milestone 9): renders fixtures with the same code as
/// Yamaha.Psg.Player and compares the SHA-256 hash of the result against a pinned baseline.
/// The hashes below were obtained by running this same test after manually listening to the
/// corresponding WAVs and having the user confirm the render sounds correct (see the plan,
/// milestone 9) — the comparison only catches UNINTENDED changes from future refactoring,
/// not correctness against real hardware.
/// </summary>
public class FixtureRegressionTests
{
    private const int OutputSampleRate = 44_100;
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static string RenderHash(IRegisterDumpPlayer dump, ChipVariant variant, int clockHz, PanningPreset panning)
    {
        var chip = new Core.AySoundChip(variant, clockHz, OutputSampleRate, panning);
        var sink = new BufferingPcmSink();
        FileDrivenPlaybackDriver.Play(dump, chip, sink);
        short[] pcm = sink.ToArray();

        var bytes = new byte[pcm.Length * sizeof(short)];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910, "C4FEA6510A3E075F508A29C821BC1890572633F55EB095531DBC2DD0F1844476")]
    [InlineData(ChipVariant.Ym2149, "3C759FB645E7EDADC5EA6048CEBCBEB30847C5CC66EA006FC17EBD81EB06F7AD")]
    public void SyntheticMelodyBassDrum_MatchesApprovedRender(ChipVariant variant, string expectedHash)
    {
        var dump = PsgFileReader.Load(Path.Combine(FixturesDir, "synthetic_melody_bass_drum.psg"));
        string hash = RenderHash(dump, variant, PsgClockPresets.ZxSpectrum, PanningPreset.Abc);
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910, "21A3744BA625EB6F4E15BF675CFFE2DCF19C31F67DEA8F04EB0246D36AAC9B71")]
    [InlineData(ChipVariant.Ym2149, "09A40B3EF0BCD6A912011D626BD856E5F956998A4C70D23CA2C324C22F37C26F")]
    public void Detrimentum_MatchesApprovedRender(ChipVariant variant, string expectedHash)
    {
        string path = Path.Combine(FixturesDir, "user_provided", "Pator - Detrimentum (2026) (Speccy.pl party 2026, 1).psg");
        if (!File.Exists(path)) return;

        var dump = PsgFileReader.Load(path);
        string hash = RenderHash(dump, variant, PsgClockPresets.ZxSpectrum, PanningPreset.Abc);
        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void SpikeInTransylvania_MatchesApprovedRender()
    {
        string path = Path.Combine(FixturesDir, "user_provided", "Lyndon Sharp - Spike in Transylvania (1991).vtx");
        if (!File.Exists(path)) return;

        var dump = VtxFileReader.Load(path);
        string hash = RenderHash(
            dump,
            dump.Metadata.ChipVariant ?? ChipVariant.Ay_3_8910,
            dump.Metadata.ClockHz ?? PsgClockPresets.ZxSpectrum,
            dump.Metadata.Panning ?? PanningPreset.Abc);

        Assert.Equal("3A9406343DD46C9C015FBC9543DECAF1B761C4CDAD7E95FCF6B75461FDB67144", hash);
    }

    [Fact]
    public void Twilight_MatchesApprovedRender()
    {
        string path = Path.Combine(FixturesDir, "user_provided", "Noro - Twilight - level 1 (1995).vtx");
        if (!File.Exists(path)) return;

        var dump = VtxFileReader.Load(path);
        string hash = RenderHash(
            dump,
            dump.Metadata.ChipVariant ?? ChipVariant.Ay_3_8910,
            dump.Metadata.ClockHz ?? PsgClockPresets.ZxSpectrum,
            dump.Metadata.Panning ?? PanningPreset.Abc);

        Assert.Equal("F616B734194AA3D85D2352A50BCA2EA929C4E7B79945B4771FADE67F6F43D284", hash);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910, "91494456F6F72E1C14AA476B22AF09D7B50BAB16A53C3E88EA670C44797DFCAC")]
    [InlineData(ChipVariant.Ym2149, "DB63606A869B9BE69C5C4CE17D613307893FE87AB75226E282B264CC929A9557")]
    public void EnvelopeShapeSweep_MatchesApprovedRender(ChipVariant variant, string expectedHash)
    {
        var dump = SyntheticFixtures.EnvelopeShapeSweep();
        string hash = RenderHash(dump, variant, PsgClockPresets.ZxSpectrum, PanningPreset.Mono);
        Assert.Equal(expectedHash, hash);
    }

    [Theory]
    [InlineData(ChipVariant.Ay_3_8910, "D3A664DB42CF3A94A2D88DC050DC575BA09D37BBC23A11247B48D63E42610FD4")]
    [InlineData(ChipVariant.Ym2149, "4505558594FBD8A3EDE5184A6C310B1AC352B4AB55312E2C5B956755712FCCA9")]
    public void DigiDrumPattern_MatchesApprovedRender(ChipVariant variant, string expectedHash)
    {
        var dump = SyntheticFixtures.DigiDrumPattern();
        string hash = RenderHash(dump, variant, PsgClockPresets.ZxSpectrum, PanningPreset.Mono);
        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void EnvelopeShapeSweep_AyAndYm_ProduceAudiblyDifferentRenders()
    {
        // An explicit check of the user's priority #2 at the level of the full file pipeline
        // (not just Core unit tests): AY and YM must sound different on the same
        // envelope-heavy material.
        var dump = SyntheticFixtures.EnvelopeShapeSweep();
        string ayHash = RenderHash(dump, ChipVariant.Ay_3_8910, PsgClockPresets.ZxSpectrum, PanningPreset.Mono);
        string ymHash = RenderHash(dump, ChipVariant.Ym2149, PsgClockPresets.ZxSpectrum, PanningPreset.Mono);

        Assert.NotEqual(ayHash, ymHash);
    }
}
