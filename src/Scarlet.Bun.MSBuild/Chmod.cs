using Scarlet.Bun.MSBuild.Providers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// Provides Unix-style file permission helpers.
/// </summary>
/// <remarks>
/// This implementation uses P/Invoke to call libc <c>stat</c> and <c>chmod</c>
/// and is intended for Unix-like platforms only (Linux, macOS).
/// </remarks>
[ExcludeFromCodeCoverage]
public static class Chmod
{
    // Default provider, but tests can override it
    internal static IChmodProvider Provider { get; set; } = InitializeProvider();

    public static void EnsureExecutablePermissions(string filePath) => Provider.EnsureExecutablePermissions(filePath);

    private static IChmodProvider InitializeProvider()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return NoOpChmodProvider.Instance;
        }

        // Assume POSIX-compatible platform (Linux, macOS, Android, etc.)
        return UnixChmodProvider.Instance;
    }
}