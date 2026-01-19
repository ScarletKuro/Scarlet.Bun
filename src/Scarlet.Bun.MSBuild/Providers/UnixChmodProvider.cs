using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace Scarlet.Bun.MSBuild.Providers;

[ExcludeFromCodeCoverage]
internal sealed class UnixChmodProvider : IChmodProvider
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
    /// <exception cref="Win32Exception">
    /// Thrown if retrieving or updating file permissions fails.
    /// </exception>
    public void EnsureExecutablePermissions(string filePath)
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            return;
        }

        // Set to 0755 (rwxr-xr-x) which is standard for executables
        // 0755 = 0x1ED in hex = 493 in decimal
        const int mode0755 = 0x1ED;

        if (LibsC.chmod(filePath, mode0755) != 0)
        {
            int errno = Marshal.GetLastWin32Error();
            throw new IOException(
                $"Failed to set executable permissions for {filePath} (errno: {errno})",
                new Win32Exception(errno));
        }
    }

    private static class LibsC
    {
        /// <summary>
        /// Changes the permissions of a file.
        /// </summary>
        /// <param name="path">Path to the file.</param>
        /// <param name="mode">
        /// The new file mode, including permission bits as defined by <c>chmod(2)</c>.
        /// </param>
        /// <returns>
        /// <c>0</c> on success; <c>-1</c> on failure with errno set.
        /// </returns>
        [DllImport("libc", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int chmod(string path, int mode);
    }
}