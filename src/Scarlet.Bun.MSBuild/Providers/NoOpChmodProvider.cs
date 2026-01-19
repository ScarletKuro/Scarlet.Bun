namespace Scarlet.Bun.MSBuild.Providers;

internal sealed class NoOpChmodProvider : IChmodProvider
{
    public void EnsureExecutablePermissions(string filePath)
    {
        // Intentionally does nothing on Windows
    }
}
