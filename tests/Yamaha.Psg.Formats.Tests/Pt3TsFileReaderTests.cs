using Yamaha.Psg.Formats.Common;
using Yamaha.Psg.Formats.Pt3;

namespace Yamaha.Psg.Formats.Tests;

public class Pt3TsFileReaderTests
{
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory, "fixtures", "user_provided", "CjSplinter, MmcM - Stellar one (2011) (DiHalt 2011, 1).pt3");

    [Fact]
    public void IsTsContainer_RecognizesARealTsFile()
    {
        byte[] data = File.ReadAllBytes(FixturePath);

        Assert.True(Pt3TsFileReader.IsTsContainer(data));
    }

    [Fact]
    public void IsTsContainer_RejectsAnOrdinarySingleModuleFile()
    {
        string ordinaryPath = Path.Combine(
            AppContext.BaseDirectory, "fixtures", "user_provided", "MmcM - Hibernation (2016) (DiHalt Lite 2016, 1).pt3");
        byte[] data = File.ReadAllBytes(ordinaryPath);

        Assert.False(Pt3TsFileReader.IsTsContainer(data));
    }

    [Fact]
    public void Load_SplitsARealTsFileIntoTwoPlayableModules()
    {
        IReadOnlyList<IRegisterDumpPlayer> modules = Pt3TsFileReader.Load(FixturePath);

        Assert.Equal(2, modules.Count);
        Assert.True(modules[0].Frames.Count > 0);
        Assert.True(modules[1].Frames.Count > 0);

        // Confirmed by hex-dumping the real file's trailer: Size1=5495, Size2=8002 bytes,
        // Size1+Size2+16 (trailer) == the file's exact total length (13513 bytes) - both halves are
        // real, independent PT3 modules with their own "Vortex Tracker II 1.0 module: ..." header.
        Assert.Equal("stellar I", modules[0].Metadata.Title?.TrimEnd());
        Assert.Equal("Stellar I", modules[1].Metadata.Title?.TrimEnd());
    }
}
