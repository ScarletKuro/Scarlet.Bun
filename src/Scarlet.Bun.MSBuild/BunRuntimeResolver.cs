using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using Scarlet.Bun.MSBuild.Providers;

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
                downloadName: "bun-windows-x64-baseline",
                packageName: "Scarlet.Bun.Runtime.windows-x64-baseline",
                executableName: "bun.exe"
            ),
            [Platform.WindowsArm64] = new(
                rid: "win-arm64",
                directoryName: "bun-windows-aarch64",
                downloadName: "bun-windows-aarch64",
                packageName: "Scarlet.Bun.Runtime.windows-aarch64",
                executableName: "bun.exe"
            ),
            [Platform.LinuxX64] = new(
                rid: "linux-x64",
                directoryName: "bun-linux-x64-baseline",
                downloadName: "bun-linux-x64-baseline",
                packageName: "Scarlet.Bun.Runtime.linux-x64-baseline",
                executableName: "bun"
            ),
            [Platform.LinuxArm64] = new(
                rid: "linux-arm64",
                directoryName: "bun-linux-aarch64",
                downloadName: "bun-linux-aarch64",
                packageName: "Scarlet.Bun.Runtime.linux-aarch64",
                executableName: "bun"
            ),
            [Platform.MacOsX64] = new(
                rid: "osx-x64",
                directoryName: "bun-darwin-x64-baseline",
                downloadName: "bun-darwin-x64-baseline",
                packageName: "Scarlet.Bun.Runtime.darwin-x64-baseline",
                executableName: "bun"
            ),
            [Platform.MacOsArm64] = new(
                rid: "osx-arm64",
                directoryName: "bun-darwin-aarch64",
                downloadName: "bun-darwin-aarch64",
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
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? Platform.WindowsArm64
                : Platform.WindowsX64;
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
    /// Gets the GitHub release download archive name for the specified platform.
    /// </summary>
    public static string GetDownloadName(Platform platform) => GetInfo(platform).DownloadName;

    /// <summary>
    /// Resolves the full path to the Bun executable.
    /// </summary>
    /// <param name="fileSystem">File system abstraction.</param>
    /// <param name="chmodProvider">Provider for setting executable permissions.</param>
    /// <param name="platform">Target platform. If null, uses current platform.</param>
    /// <param name="runtimeDirectory">Optional path to the runtime directory. If not specified, throws an error.</param>
    /// <returns>Full path to the Bun executable.</returns>
    public static string ResolveBunExecutable(IFileSystem fileSystem, IChmodProvider chmodProvider, Platform? platform = null, string? runtimeDirectory = null)
    {
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

        chmodProvider.EnsureExecutablePermissions(bunPath);

        return bunPath;
    }

    private static PlatformInfo GetInfo(Platform platform)
    {
        return PlatformMap.TryGetValue(platform, out var info)
            ? info
            : throw new ArgumentException($"Unknown platform: {platform}", nameof(platform));
    }
}
