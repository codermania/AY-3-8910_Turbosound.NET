namespace Yamaha.Psg.Core.Output;

/// <summary>Arbitrary channel gain into the left/right outputs (0..1, no upper clamp).</summary>
public readonly struct ChannelPan
{
    public ChannelPan(float left, float right)
    {
        Left = left;
        Right = right;
    }

    public float Left { get; }
    public float Right { get; }
}
