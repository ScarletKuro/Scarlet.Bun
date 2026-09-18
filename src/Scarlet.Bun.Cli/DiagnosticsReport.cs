using System.Text;
using System.Text.Json;
using Scarlet.Bun.Core;

namespace Scarlet.Bun.Cli;

/// <summary>
/// Renders what the tool resolved and why.
/// </summary>
internal static class DiagnosticsReport
{
    /// <summary>
    /// Renders the human-readable report.
    /// </summary>
    /// <param name="resolution">The resolution to describe.</param>
    /// <param name="options">The configuration in effect.</param>
    /// <returns>The report, ending in a newline.</returns>
    public static string ToText(BunResolution resolution, BunCliOptions options)
    {
        var report = new StringBuilder();

        report.AppendLine();
        Append(report, "Scarlet.Bun.Cli", DescribePackage());
        Append(report, "Pinned Bun version", BunBuildInfo.PinnedBunVersion);
        Append(report, "Host platform", $"{resolution.Platform} ({resolution.RuntimeIdentifier})");
        Append(report, "Tool directory", AppContext.BaseDirectory);
        report.AppendLine();

        Append(report, "Bun executable", resolution.ExecutablePath ?? "(not present)");
        Append(report, "Source", DescribeSource(resolution));
        Append(report, "Requested version", resolution.RequestedVersion);
        Append(report, "Embedded probe path", resolution.EmbeddedProbePath);
        Append(report, "Cache root", resolution.CacheRoot);
        Append(report, "Runtime directory", resolution.RuntimeDirectory);
        Append(report, "Download URL", DescribeDownloadUrl(resolution));
        report.AppendLine();

        report.AppendLine("Environment");
        foreach (var (name, value) in DescribeEnvironment(options))
        {
            report.AppendLine($"  {name,-28}{value}");
        }

        return report.ToString();
    }

    /// <summary>
    /// Renders the same facts as a single JSON object, for scripting.
    /// </summary>
    /// <param name="resolution">The resolution to describe.</param>
    /// <param name="options">The configuration in effect.</param>
    /// <returns>Indented JSON, ending in a newline.</returns>
    public static string ToJson(BunResolution resolution, BunCliOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["package"] = DescribePackage(),
            ["pinnedBunVersion"] = BunBuildInfo.PinnedBunVersion,
            ["platform"] = resolution.Platform.ToString(),
            ["runtimeIdentifier"] = resolution.RuntimeIdentifier,
            ["toolDirectory"] = AppContext.BaseDirectory,
            ["bunExecutable"] = resolution.ExecutablePath,
            ["source"] = resolution.Source.ToString().ToLowerInvariant(),
            ["requestedVersion"] = resolution.RequestedVersion,
            ["embeddedProbePath"] = resolution.EmbeddedProbePath,
            ["cacheRoot"] = resolution.CacheRoot,
            ["runtimeDirectory"] = resolution.RuntimeDirectory,
            ["downloadUrl"] = resolution.IsResolved ? null : BuildDownloadUrl(resolution),
            ["failureReason"] = resolution.FailureReason,
            ["environment"] = new Dictionary<string, object?>
            {
                [BunCliOptions.PathVariable] = options.ExplicitBunPath,
                [BunCliOptions.VersionVariable] = options.RequestedVersionOverride,
                [BunCliOptions.CacheVariable] = options.CacheRootOverride,
                [BunCliOptions.NoEmbeddedVariable] = options.IgnoreEmbedded,
                [BunCliOptions.PassthroughVariable] = options.PurePassthrough,
                [BunCliOptions.DiagnosticsVariable] = options.Diagnostics,
                [BunCliOptions.DownloadTimeoutVariable] = options.DownloadTimeoutOverride
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static string DescribePackage() => DescribePackage(BunBuildInfo.PackagedRuntimeIdentifier);

    /// <summary>
    /// Names the package this build came from.
    /// </summary>
    /// <param name="packagedRuntimeIdentifier">The RID baked in at build time, empty for the portable package.</param>
    /// <returns>For example <c>Scarlet.Bun.Cli.win-x64</c>.</returns>
    /// <remarks>
    /// Takes the identifier rather than reading the constant so both branches are reachable from a test:
    /// the test build has no runtime identifier, so it could only ever exercise the portable one.
    /// </remarks>
    internal static string DescribePackage(string packagedRuntimeIdentifier)
    {
        return string.IsNullOrEmpty(packagedRuntimeIdentifier)
            ? "Scarlet.Bun.Cli (portable)"
            : $"Scarlet.Bun.Cli.{packagedRuntimeIdentifier}";
    }

    private static string DescribeSource(BunResolution resolution)
    {
        return resolution.Source switch
        {
            BunSource.Embedded => "embedded - shipped in this package, no network required",
            BunSource.Cache => "cache - downloaded by an earlier run",
            BunSource.Downloaded => "downloaded - fetched during this run",
            BunSource.Explicit => $"explicit - {BunCliOptions.PathVariable}",
            _ => "not found - would download on the next run"
        };
    }

    private static string DescribeDownloadUrl(BunResolution resolution)
    {
        return resolution.IsResolved ? "(not needed)" : BuildDownloadUrl(resolution);
    }

    private static string BuildDownloadUrl(BunResolution resolution)
    {
        var archive = BunRuntimeResolver.GetDownloadName(resolution.Platform);

        return string.Equals(resolution.RequestedVersion, BunCliOptions.LatestVersion, StringComparison.OrdinalIgnoreCase)
            ? $"https://github.com/oven-sh/bun/releases/latest/download/{archive}.zip"
            : $"https://github.com/oven-sh/bun/releases/download/bun-v{resolution.RequestedVersion}/{archive}.zip";
    }

    private static IEnumerable<(string Name, string Value)> DescribeEnvironment(BunCliOptions options)
    {
        yield return (BunCliOptions.PathVariable, options.ExplicitBunPath ?? "(unset)");
        yield return (BunCliOptions.VersionVariable, options.RequestedVersionOverride ?? "(unset)");
        yield return (BunCliOptions.CacheVariable, options.CacheRootOverride ?? "(unset)");
        yield return (BunCliOptions.NoEmbeddedVariable, options.IgnoreEmbedded ? "enabled" : "(unset)");
        yield return (BunCliOptions.PassthroughVariable, options.PurePassthrough ? "enabled" : "(unset)");
        yield return (BunCliOptions.DiagnosticsVariable, options.Diagnostics ? "enabled" : "(unset)");
        yield return (BunCliOptions.DownloadTimeoutVariable, options.DownloadTimeoutOverride ?? "(unset)");
    }

    private static void Append(StringBuilder report, string label, string value) =>
        report.AppendLine($"{label,-22}{value}");
}
