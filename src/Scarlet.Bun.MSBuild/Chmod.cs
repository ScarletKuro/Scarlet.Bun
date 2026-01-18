using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
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
    /// <summary>
    /// Ensures that a file has executable permissions for user, group, and others.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <remarks>
    /// On Unix-like systems, this method is equivalent to running:
    /// <code>chmod +x filePath</code>.
    /// <para />
    /// Existing permissions are preserved; only execute bits are added.
    /// On Windows, this method is a no-op.
    /// </remarks>
    /// <exception cref="System.ComponentModel.Win32Exception">
    /// Thrown if retrieving or updating file permissions fails.
    /// </exception>
    /// <summary>
    /// Ensures that a file has executable permissions for user, group, and others.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <remarks>
    /// On Unix-like systems, this method is equivalent to running:
    /// <code>chmod a+x filePath</code>.
    /// <para />
    /// Sets permissions to 0755 (rwxr-xr-x).
    /// On Windows, this method is a no-op.
    /// </remarks>
    /// <exception cref="System.IO.IOException">
    /// Thrown if setting file permissions fails.
    /// </exception>
    public static void EnsureExecutablePermissions(string filePath)
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            return;
        }

        // Set to 0755 (rwxr-xr-x) which is standard for executables
        // 0755 = 0x1ED in hex = 493 in decimal
        const int mode0755 = 0x1ED;

        if (chmod(filePath, mode0755) != 0)
        {
            int errno = Marshal.GetLastWin32Error();
            throw new IOException(
                $"Failed to set executable permissions for {filePath} (errno: {errno})",
                new Win32Exception(errno));
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int chmod(string path, int mode);
}