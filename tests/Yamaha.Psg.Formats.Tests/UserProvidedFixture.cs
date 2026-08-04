namespace Yamaha.Psg.Formats.Tests;

/// <summary>
/// Real chiptune files under fixtures/user_provided/ are not committed to the repo (see
/// fixtures/SOURCES.md) - tests that need them skip gracefully (rather than fail) when a file
/// isn't present locally.
/// </summary>
internal static class UserProvidedFixture
{
    public static bool TryResolve(out string[] paths, params string[] fileNames)
    {
        paths = fileNames.Select(f => Path.Combine(AppContext.BaseDirectory, "fixtures", "user_provided", f)).ToArray();
        return paths.All(File.Exists);
    }
}
