using System;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.InteropServices;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// Helper class for detecting and resolving Bun runtime paths.
/// </summary>
public static class BunRuntimeResolver
{
    /// <summary>
    /// Gets the current platform.
    /// </summary>
    public static Platform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Platform.WindowsX64;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? Platform.LinuxArm64
                : Platform.LinuxX64;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? Platform.MacOsArm64
                : Platform.MacOsX64;
        }

        throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");
    }

    /// <summary>
    /// Gets the runtime identifier (RID) for the specified platform.
    /// </summary>
    public static string GetRuntimeIdentifier(Platform platform)
    {
        return platform switch
        {
            Platform.WindowsX64 => "win-x64",
            Platform.LinuxX64 => "linux-x64",
            Platform.LinuxArm64 => "linux-arm64",
            Platform.MacOsX64 => "osx-x64",
            Platform.MacOsArm64 => "osx-arm64",
            _ => throw new ArgumentException($"Unknown platform: {platform}", nameof(platform))
        };
    }

    /// <summary>
    /// Gets the runtime directory name for the specified platform (for backwards compatibility).
    /// </summary>
    public static string GetRuntimeDirectoryName(Platform platform)
    {
        return platform switch
        {
            Platform.WindowsX64 => "bun-windows-x64-baseline",
            Platform.LinuxX64 => "bun-linux-x64-baseline",
            Platform.LinuxArm64 => "bun-linux-aarch64",
            Platform.MacOsX64 => "bun-darwin-x64-baseline",
            Platform.MacOsArm64 => "bun-darwin-aarch64",
            _ => throw new ArgumentException($"Unknown platform: {platform}", nameof(platform))
        };
    }

    /// <summary>
    /// Gets the Bun executable name for the specified platform.
    /// </summary>
    public static string GetExecutableName(Platform platform)
    {
        return platform == Platform.WindowsX64 ? "bun.exe" : "bun";
    }

    /// <summary>
    /// Gets the runtime package name for the specified platform.
    /// </summary>
    internal static string GetRuntimePackageName(Platform platform)
    {
        return platform switch
        {
            Platform.WindowsX64 => "Scarlet.Bun.Runtime.windows-x64-baseline",
            Platform.LinuxX64 => "Scarlet.Bun.Runtime.linux-x64-baseline",
            Platform.LinuxArm64 => "Scarlet.Bun.Runtime.linux-aarch64",
            Platform.MacOsX64 => "Scarlet.Bun.Runtime.darwin-x64-baseline",
            Platform.MacOsArm64 => "Scarlet.Bun.Runtime.darwin-aarch64",
            _ => throw new ArgumentException($"Unknown platform: {platform}", nameof(platform))
        };
    }

    /// <summary>
    /// Resolves the full path to the Bun executable.
    /// </summary>
    /// <param name="taskAssemblyPath">Path to the task assembly.</param>
    /// <param name="platform">Target platform.</param>
    /// <param name="runtimeDirectory">Optional path to the runtime directory. If not specified, throws an error.</param>
    /// <param name="fileSystem">File system abstraction for testability. If null, uses the real file system.</param>
    /// <returns>Full path to the Bun executable.</returns>
    public static string ResolveBunExecutable(string taskAssemblyPath, Platform? platform = null, string? runtimeDirectory = null, IFileSystem? fileSystem = null)
    {
        // Use real file system if none provided
        fileSystem ??= new FileSystem();
        
        var targetPlatform = platform ?? GetCurrentPlatform();
        var runtimeId = GetRuntimeIdentifier(targetPlatform);
        var executableName = GetExecutableName(targetPlatform);

        if (string.IsNullOrEmpty(runtimeDirectory))
        {
            // No runtime directory provided - provide helpful error message
            var runtimePackageName = GetRuntimePackageName(targetPlatform);
            throw new FileNotFoundException(
                $"Bun runtime package not found.\n\n" +
                $"The {runtimePackageName} package must be installed for your platform.\n\n" +
                $"Add this to your project file:\n" +
                $"  <PackageReference Include=\"{runtimePackageName}\" Version=\"1.0.0\" />\n\n" +
                $"The runtime package will automatically provide its location via MSBuild properties.");
        }

        var bunPath = Path.GetFullPath(Path.Combine(runtimeDirectory, runtimeId, "native", executableName));

        if (!fileSystem.File.Exists(bunPath))
        {
            var runtimePackageName = GetRuntimePackageName(targetPlatform);
            throw new FileNotFoundException(
                $"Bun executable not found at: {bunPath}\n\n" +
                $"The {runtimePackageName} package appears to be installed but the executable is missing.\n" +
                $"Try cleaning and rebuilding your project.");
        }

        // On Unix systems, ensure the file is executable
        if (targetPlatform != Platform.WindowsX64)
        {
            try
            {
                // Use chmod to set execute permissions
                var chmodProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{bunPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                });
                
                if (chmodProcess != null)
                {
                    chmodProcess.WaitForExit();
                    if (chmodProcess.ExitCode != 0)
                    {
                        var error = chmodProcess.StandardError.ReadToEnd();
                        // Log but don't fail - permissions might already be set
                        System.Diagnostics.Debug.WriteLine($"chmod warning: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail - permissions might already be set or chmod not available
                System.Diagnostics.Debug.WriteLine($"Could not set execute permissions: {ex.Message}");
            }
        }

        return bunPath;
    }
}
