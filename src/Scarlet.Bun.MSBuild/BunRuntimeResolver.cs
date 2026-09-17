using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text;
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
    /// Gets the full path the Bun executable is expected at inside a runtimes directory.
    /// </summary>
    /// <param name="runtimesPath">Directory containing <c>&lt;rid&gt;/native/&lt;executable&gt;</c>.</param>
    /// <param name="platform">The platform to build the path for.</param>
    /// <returns>The full path to the Bun executable. The file is not required to exist.</returns>
    public static string GetExecutablePath(string runtimesPath, Platform platform)
    {
        var info = GetInfo(platform);

        return Path.GetFullPath(Path.Combine(runtimesPath, info.Rid, "native", info.ExecutableName));
    }

    /// <summary>
    /// Selects the runtime packs that can serve the given platform, best candidate first.
    /// </summary>
    /// <param name="packs">All packs contributed to the build. May be <see langword="null"/>.</param>
    /// <param name="platform">The platform that has to be served.</param>
    /// <returns>
    /// The matching packs ordered by descending <see cref="BunRuntimePack.Priority"/>, then by pack id and path so
    /// that the outcome does not depend on the order NuGet happened to import the runtime packages in.
    /// </returns>
    public static IReadOnlyList<BunRuntimePack> SelectPacks(IEnumerable<BunRuntimePack>? packs, Platform platform)
    {
        if (packs is null)
        {
            return Array.Empty<BunRuntimePack>();
        }

        var rid = GetRuntimeIdentifier(platform);
        var matches = new List<BunRuntimePack>();

        foreach (var pack in packs)
        {
            if (string.Equals(pack.Rid, rid, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(pack);
            }
        }

        matches.Sort(static (left, right) =>
        {
            var byPriority = right.Priority.CompareTo(left.Priority);
            if (byPriority != 0)
            {
                return byPriority;
            }

            var byId = string.CompareOrdinal(left.Id, right.Id);

            return byId != 0 ? byId : string.CompareOrdinal(left.RuntimesPath, right.RuntimesPath);
        });

        return matches;
    }

    /// <summary>
    /// Resolves the full path to the Bun executable.
    /// </summary>
    /// <param name="fileSystem">File system abstraction.</param>
    /// <param name="chmodProvider">Provider for setting executable permissions.</param>
    /// <param name="platform">Target platform. If null, uses current platform.</param>
    /// <param name="runtimeDirectory">Optional explicit runtime directory. When set, it wins over <paramref name="runtimePacks"/>.</param>
    /// <param name="runtimePacks">Runtime packs contributed by the referenced runtime packages.</param>
    /// <param name="log">Optional sink for diagnostic messages about the selection.</param>
    /// <returns>Full path to the Bun executable.</returns>
    /// <exception cref="FileNotFoundException">No usable Bun executable could be found.</exception>
    public static string ResolveBunExecutable(
        IFileSystem fileSystem,
        IChmodProvider chmodProvider,
        Platform? platform = null,
        string? runtimeDirectory = null,
        IReadOnlyList<BunRuntimePack>? runtimePacks = null,
        Action<string>? log = null)
    {
        var targetPlatform = platform ?? GetCurrentPlatform();

        // An explicit directory is a deliberate override, so it is never second-guessed against the packs.
        if (!string.IsNullOrEmpty(runtimeDirectory))
        {
            return ResolveFromDirectory(fileSystem, chmodProvider, targetPlatform, runtimeDirectory!);
        }

        var candidates = SelectPacks(runtimePacks, targetPlatform);
        var searched = new List<string>();

        foreach (var candidate in candidates)
        {
            var candidatePath = GetExecutablePath(candidate.RuntimesPath, targetPlatform);

            if (fileSystem.File.Exists(candidatePath))
            {
                if (candidates.Count > 1)
                {
                    log?.Invoke($"Selected Bun runtime pack {candidate} out of {candidates.Count} candidates for {GetRuntimeIdentifier(targetPlatform)}.");
                }
                else
                {
                    log?.Invoke($"Using Bun runtime pack {candidate}.");
                }

                chmodProvider.EnsureExecutablePermissions(candidatePath);

                return candidatePath;
            }

            searched.Add(candidatePath);
        }

        throw new FileNotFoundException(candidates.Count > 0
            ? BuildIncompletePackMessage(targetPlatform, candidates, searched)
            : BuildMissingPackMessage(targetPlatform, runtimePacks));
    }

    /// <summary>
    /// Resolves the Bun executable inside an explicitly configured runtimes directory.
    /// </summary>
    private static string ResolveFromDirectory(
        IFileSystem fileSystem,
        IChmodProvider chmodProvider,
        Platform platform,
        string runtimeDirectory)
    {
        var bunPath = GetExecutablePath(runtimeDirectory, platform);

        if (!fileSystem.File.Exists(bunPath))
        {
            var runtimePackageName = GetRuntimePackageName(platform);

            throw new FileNotFoundException(
                $"Bun executable not found at: {bunPath}\n\n" +
                $"BunRuntimeDirectory points at '{runtimeDirectory}', which does not contain a Bun build for {GetRuntimeIdentifier(platform)}.\n" +
                $"Either clear that property and reference the {runtimePackageName} package, or make sure the directory " +
                $"contains '{GetRuntimeIdentifier(platform)}/native/{GetExecutableName(platform)}'.");
        }

        chmodProvider.EnsureExecutablePermissions(bunPath);

        return bunPath;
    }

    /// <summary>
    /// Builds the error shown when no runtime pack targets the build host.
    /// </summary>
    private static string BuildMissingPackMessage(Platform platform, IEnumerable<BunRuntimePack>? allPacks)
    {
        var runtimePackageName = GetRuntimePackageName(platform);
        var rid = GetRuntimeIdentifier(platform);

        var message = new StringBuilder();
        message.Append("Bun runtime package not found.\n\n");
        message.Append($"No Bun runtime is available for this build host ({rid}).\n\n");
        message.Append("Add the matching runtime package to your project:\n");
        message.Append($"  <PackageReference Include=\"{runtimePackageName}\" Version=\"<bun-version>\" PrivateAssets=\"all\" />\n\n");
        message.Append("...or let the build download Bun on demand:\n");
        message.Append("  <PropertyGroup>\n");
        message.Append("    <BunRuntimeDownload>true</BunRuntimeDownload>\n");
        message.Append("    <BunRuntimeDirectory>$(MSBuildProjectDirectory)/runtimes</BunRuntimeDirectory>\n");
        message.Append("  </PropertyGroup>\n\n");
        message.Append(DescribeVisiblePacks(allPacks));

        return message.ToString();
    }

    /// <summary>
    /// Builds the error shown when a matching runtime pack is referenced but its binary is missing.
    /// </summary>
    private static string BuildIncompletePackMessage(
        Platform platform,
        IReadOnlyList<BunRuntimePack> candidates,
        IReadOnlyList<string> searched)
    {
        var message = new StringBuilder();
        message.Append($"Bun executable not found at: {searched[0]}\n\n");
        message.Append(candidates.Count == 1
            ? $"The runtime pack {candidates[0]} is referenced but its Bun executable is missing.\n"
            : $"{candidates.Count} runtime packs target {GetRuntimeIdentifier(platform)} but none of them contains a Bun executable.\n");
        message.Append("Try clearing the NuGet cache for the runtime package and rebuilding.\n\n");
        message.Append("Locations searched:\n");

        foreach (var path in searched)
        {
            message.Append($"  - {path}\n");
        }

        return message.ToString();
    }

    /// <summary>
    /// Renders the packs the build can see, which is the fastest way to spot a host/pack mismatch.
    /// </summary>
    private static string DescribeVisiblePacks(IEnumerable<BunRuntimePack>? allPacks)
    {
        var message = new StringBuilder("Runtime packs visible to this project:");
        var any = false;

        if (allPacks is not null)
        {
            foreach (var pack in allPacks)
            {
                message.Append($"\n  - {pack}");
                any = true;
            }
        }

        if (!any)
        {
            message.Append(" (none)");
        }

        return message.ToString();
    }

    private static PlatformInfo GetInfo(Platform platform)
    {
        return PlatformMap.TryGetValue(platform, out var info)
            ? info
            : throw new ArgumentException($"Unknown platform: {platform}", nameof(platform));
    }
}
