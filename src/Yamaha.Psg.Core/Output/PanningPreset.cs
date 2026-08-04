namespace Yamaha.Psg.Core.Output;

/// <summary>
/// Bac/Bca/Cab/Cba were added while implementing VTX support — VTX encodes all 6 permutations of
/// ABC as a stereo-mode byte in its header (see VtxFileReader), not just ABC/ACB.
/// </summary>
public enum PanningPreset
{
    Mono,
    Abc,
    Acb,
    Bac,
    Bca,
    Cab,
    Cba,
    Custom,
}
