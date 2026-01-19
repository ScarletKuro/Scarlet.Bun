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

        if (LibsC.stat(filePath, out var st) != 0)
        {
            throw new IOException(
                "Failed to set executable permissions.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        int newMode = (int)(st.st_mode | LibsC.S_IXUSR | LibsC.S_IXGRP | LibsC.S_IXOTH);

        if (LibsC.chmod(filePath, newMode) != 0)
        {
            throw new IOException(
                "Failed to set executable permissions.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private static class LibsC
    {
        // Execute permission bits (see chmod(2))
        public const int S_IXUSR = 0x40; // Owner execute (0100)
        public const int S_IXGRP = 0x08; // Group execute (0010)
        public const int S_IXOTH = 0x01; // Others execute (0001)

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
        [DllImport("libc", SetLastError = true)]
        public static extern int chmod(string path, int mode);

        /// <summary>
        /// Retrieves information about a file.
        /// </summary>
        /// <param name="path">Path to the file.</param>
        /// <param name="buf">
        /// A <see cref="Stat"/> structure that receives file metadata,
        /// including permission bits.
        /// </param>
        /// <returns>
        /// <c>0</c> on success; <c>-1</c> on failure with errno set.
        /// </returns>
        [DllImport("libc", SetLastError = true)]
        public static extern int stat(string path, out Stat buf);

        /// <summary>
        /// Represents a subset of the native <c>struct stat</c>.
        /// </summary>
        /// <remarks>
        /// Only the fields required to read file permission bits are included.
        /// The layout matches common Linux and macOS ABIs for these fields.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct Stat
        {
            public ulong st_dev;
            public ulong st_ino;
            public ulong st_nlink;
            public uint st_mode;
            public uint st_uid;
            public uint st_gid;
            public ulong __pad0;
            public ulong st_rdev;
            public long st_size;
            public long st_blksize;
            public long st_blocks;
            public long st_atime;
            public ulong st_atime_nsec;
            public long st_mtime;
            public ulong st_mtime_nsec;
            public long st_ctime;
            public ulong st_ctime_nsec;
            public long __unused4;
            public long __unused5;
        }
    }
}