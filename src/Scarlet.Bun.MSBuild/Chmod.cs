using System;
using System.Diagnostics;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// Provides Unix-style file permission helpers.
/// </summary>
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
    public static void EnsureExecutablePermissions(string filePath)
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            return;
        }

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            if (process is not null)
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Debug.WriteLine($"chmod warning: {error}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not set execute permissions: {ex.Message}");
        }
    }
}