namespace Yamaha.Psg.Formats.Common;

/// <summary>The simplest concrete <see cref="IRegisterDumpPlayer"/> implementation — a ready-made list of frames + metadata.</summary>
public sealed class RegisterDumpPlayer : IRegisterDumpPlayer
{
    public RegisterDumpPlayer(RegisterDumpMetadata metadata, IReadOnlyList<RegisterFrame> frames)
    {
        Metadata = metadata;
        Frames = frames;
    }

    public RegisterDumpMetadata Metadata { get; }
    public IReadOnlyList<RegisterFrame> Frames { get; }
}
