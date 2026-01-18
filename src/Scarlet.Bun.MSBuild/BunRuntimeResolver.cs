using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.InteropServices;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// Helper class for detecting and resolving Bun runtime paths.
/// </summary>
public static class BunRuntimeResolver
{
    private static readonly IReadOnlyDictionary<Platform, PlatformInfo> PlatformMap =
        new Dictionary<Platform, PlatformInfo>
        {
            [Platform.WindowsX64] = new(
                rid: "win-x64",
                directoryName: "bun-windows-x64-baseline",
                packageName: "Scarlet.Bun.Runtime.windows-x64-baseline",
                executableName: "bun.exe"
            ),
            [Platform.LinuxX64] = new(
                rid: "linux-x64",
                directoryName: "bun-linux-x64-baseline",
                packageName: "Scarlet.Bun.Runtime.linux-x64-baseline",
                executableName: "bun"
            ),
            [Platform.LinuxArm64] = new(
                rid: "linux-arm64",
                directoryName: "bun-linux-aarch64",
                packageName: "Scarlet.Bun.Runtime.linux-aarch64",
                executableName: "bun"
            ),
            [Platform.MacOsX64] = new(
                rid: "osx-x64",
                directoryName: "bun-darwin-x64-baseline",
                packageName: "Scarlet.Bun.Runtime.darwin-x64-baseline",
                executableName: "bun"
            ),
            [Platform.MacOsArm64] = new(
                rid: "osx-arm64",
                directoryName: "bun-darwin-aarch64",
                packageName: "Scarlet.Bun.Runtime.darwin-aarch64",
                executableName: "bun"
            )
        };


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
    public static string GetRuntimeIdentifier(Platform platform) => GetInfo(platform).Rid;

    /// <summary>
    /// Gets the runtime directory name for the specified platform (for backwards compatibility).
    /// </summary>
    public static string GetRuntimeDirectoryName(Platform platform) => GetInfo(platform).DirectoryName;

    /// <summary>
    /// Gets the Bun executable name for the specified platform.
    /// </summary>
    public static string GetExecutableName(Platform platform) => GetInfo(platform).ExecutableName;

    /// <summary>
    /// Gets the runtime package name for the specified platform.
    /// </summary>
    public static string GetRuntimePackageName(Platform platform) => GetInfo(platform).PackageName;

    /// <summary>
    /// Resolves the full path to the Bun executable.
    /// </summary>
    /// <param name="platform">Target platform.</param>
    /// <param name="runtimeDirectory">Optional path to the runtime directory. If not specified, throws an error.</param>
    /// <param name="fileSystem">File system abstraction for testability. If null, uses the real file system.</param>
    /// <returns>Full path to the Bun executable.</returns>
    public static string ResolveBunExecutable(Platform? platform = null, string? runtimeDirectory = null, IFileSystem? fileSystem = null)
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
            EnsureExecutablePermission(bunPath);
        }

        return bunPath;
    }

    private static void EnsureExecutablePermission(string path)
    {
        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process is not null)
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    System.Diagnostics.Debug.WriteLine($"chmod warning: {error}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not set execute permissions: {ex.Message}");
        }
    }

    private static PlatformInfo GetInfo(Platform platform)
    {
        return PlatformMap.TryGetValue(platform, out var info)
            ? info
            : throw new ArgumentException($"Unknown platform: {platform}", nameof(platform));
    }
}
