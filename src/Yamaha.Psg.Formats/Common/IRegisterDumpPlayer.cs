namespace Yamaha.Psg.Formats.Common;

/// <summary>The common "sequence of frames + metadata" abstraction shared by the PSG/VTX/... formats.</summary>
public interface IRegisterDumpPlayer
{
    RegisterDumpMetadata Metadata { get; }
    IReadOnlyList<RegisterFrame> Frames { get; }
}
