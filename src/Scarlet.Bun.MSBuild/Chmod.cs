using Scarlet.Bun.MSBuild.Providers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// Factory for creating the platform-correct <see cref="IChmodProvider"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public static class Chmod
{
    public static IChmodProvider CreateProvider()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return NoOpChmodProvider.Instance;
        }

        // Assume POSIX-compatible platform (Linux, macOS, Android, etc.)
        return UnixChmodProvider.Instance;
    }
}