using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Scarlet.Bun.Core;
using Scarlet.Bun.Core.Providers;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// MSBuild task to run Bun commands.
/// </summary>
public class BunRunTask : Task
{
    /// <summary>
    /// The Bun command to execute (e.g., "run", "install", "build").
    /// </summary>
    [Required]
    public string? Command { get; set; }

    /// <summary>
    /// Arguments to pass to the Bun command.
    /// </summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// Working directory for the command execution.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Timeout in milliseconds for the command execution. 0 means no timeout.
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 0;

    /// <summary>
    /// Whether to continue the build if the command fails.
    /// </summary>
    public bool ContinueOnError { get; set; } = false;

    /// <summary>
    /// Optional path to the runtime directory. When set it overrides <see cref="RuntimePacks"/>.
    /// Required when BunRuntimeDownload is true.
    /// </summary>
    public string? RuntimeDirectory { get; set; }

    /// <summary>
    /// When true, downloads the Bun runtime from GitHub releases instead of using embedded runtimes.
    /// Requires RuntimeDirectory to be specified.
    /// </summary>
    public bool BunRuntimeDownload { get; set; } = false;

    /// <summary>
    /// Specific Bun version to download (e.g., "1.3.6"). If not specified, downloads latest version.
    /// Only used when BunRuntimeDownload is true.
    /// </summary>
    public string? BunVersionDownload { get; set; }

    /// <summary>
    /// Maximum seconds to wait for the download mutex when another process is already downloading.
    /// Only used when BunRuntimeDownload is true. Defaults to 300 (5 minutes).
    /// </summary>
    public int DownloadMutexTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// The Bun runtimes available to this build, normally <c>@(BunRuntimePack)</c>.
    /// </summary>
    /// <remarks>
    /// Each referenced <c>Scarlet.Bun.Runtime.*</c> package contributes one item; see <see cref="BunRuntimePack"/>
    /// for the metadata contract. This is the supported way to make a Bun build discoverable - a new runtime
    /// identifier needs no change here.
    /// </remarks>
    public ITaskItem[]? RuntimePacks { get; set; }

    /// <summary>
    /// Runtime package path for win-x64 (set by Scarlet.Bun.Runtime.windows-x64-baseline package).
    /// </summary>
    /// <remarks>Legacy contract, superseded by <see cref="RuntimePacks"/>. Kept so that runtime packages
    /// published before the item contract existed keep working with this task.</remarks>
    public string? BunRuntime_win_x64 { get; set; }

    /// <summary>
    /// Runtime package path for win-arm64 (set by Scarlet.Bun.Runtime.windows-aarch64 package).
    /// </summary>
    /// <remarks>Legacy contract, superseded by <see cref="RuntimePacks"/>.</remarks>
    public string? BunRuntime_win_arm64 { get; set; }

    /// <summary>
    /// Runtime package path for linux-x64 (set by Scarlet.Bun.Runtime.linux-x64-baseline package).
    /// </summary>
    /// <remarks>Legacy contract, superseded by <see cref="RuntimePacks"/>.</remarks>
    public string? BunRuntime_linux_x64 { get; set; }

    /// <summary>
    /// Runtime package path for linux-arm64 (set by Scarlet.Bun.Runtime.linux-aarch64 package).
    /// </summary>
    /// <remarks>Legacy contract, superseded by <see cref="RuntimePacks"/>.</remarks>
    public string? BunRuntime_linux_arm64 { get; set; }

    /// <summary>
    /// Runtime package path for osx-x64 (set by Scarlet.Bun.Runtime.darwin-x64-baseline package).
    /// </summary>
    /// <remarks>Legacy contract, superseded by <see cref="RuntimePacks"/>.</remarks>
    public string? BunRuntime_osx_x64 { get; set; }

    /// <summary>
    /// Runtime package path for osx-arm64 (set by Scarlet.Bun.Runtime.darwin-aarch64 package).
    /// </summary>
    /// <remarks>Legacy contract, superseded by <see cref="RuntimePacks"/>.</remarks>
    public string? BunRuntime_osx_arm64 { get; set; }

    /// <summary>
    /// The exit code of the executed command.
    /// </summary>
    [Output]
    public int ExitCode { get; set; }

    /// <summary>
    /// Standard output from the executed command.
    /// </summary>
    [Output]
    public string? StandardOutput { get; set; }

    /// <summary>
    /// Standard error from the executed command.
    /// </summary>
    [Output]
    public string? StandardError { get; set; }

    public override bool Execute()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Command))
            {
                Log.LogError("Command parameter is required");
                return false;
            }

            var fileSystem = new FileSystem();
            var chmodProvider = Chmod.CreateProvider();

            string bunPath;

            // Handle runtime download mode
            if (BunRuntimeDownload)
            {
                if (string.IsNullOrWhiteSpace(RuntimeDirectory))
                {
                    Log.LogError("RuntimeDirectory parameter is required when BunRuntimeDownload is true");
                    return false;
                }

                Log.LogMessage(MessageImportance.High, "BunRuntimeDownload mode enabled");

                var platform = BunRuntimeResolver.GetCurrentPlatform();

                if (!string.IsNullOrWhiteSpace(BunVersionDownload))
                {
                    Log.LogMessage(MessageImportance.High, $"Downloading Bun runtime version {BunVersionDownload} for {platform}...");
                }
                else
                {
                    Log.LogMessage(MessageImportance.High, $"Downloading latest Bun runtime for {platform}...");
                }

                try
                {
                    // Download runtime asynchronously (RuntimeDirectory is already validated above)
                    using var httpClient = BunDownloader.CreateHttpClient();
                    var downloader = new BunDownloader(
                        httpClient,
                        new GitHubLatestVersionResolver(),
                        fileSystem,
                        ZipArchiveProvider.Instance,
                        chmodProvider,
                        platform,
                        new MsBuildBunLogger(Log));
                    bunPath = downloader.DownloadRuntime(RuntimeDirectory!, BunVersionDownload, DownloadMutexTimeoutSeconds);

                    Log.LogMessage(MessageImportance.High, $"Bun runtime ready at: {bunPath}");
                }
                catch (Exception ex)
                {
                    Log.LogError($"Failed to download Bun runtime: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Log.LogError($"Inner exception: {ex.InnerException.Message}");
                    }
                    return false;
                }
            }
            else
            {
                var packs = CollectRuntimePacks();

                Log.LogMessage(MessageImportance.Low, $"Runtime packs: {(packs.Count == 0 ? "(none)" : string.Join(", ", packs))}");

                // Resolved once and reused for the log line below - on Linux, GetCurrentPlatform() probes
                // the filesystem for a musl loader, and calling it twice would do that walk twice for no
                // reason.
                var currentPlatform = BunRuntimeResolver.GetCurrentPlatform();

                bunPath = BunRuntimeResolver.ResolveBunExecutable(
                    fileSystem,
                    chmodProvider,
                    platform: currentPlatform,
                    runtimeDirectory: RuntimeDirectory,
                    runtimePacks: packs,
                    log: message => Log.LogMessage(MessageImportance.Normal, message));

                Log.LogMessage(MessageImportance.High, $"Platform: {currentPlatform}");
            }

            Log.LogMessage(MessageImportance.High, $"Using Bun at: {bunPath}");

            // Deliberately a single command-line string, not ProcessStartInfo.ArgumentList: that property
            // isn't part of the netstandard2.0 surface this task targets. It also wouldn't fix anything -
            // Arguments here is one flat MSBuild-authored string (like MSBuild's own <Exec Command="...">),
            // not a pre-split argv array like Scarlet.Bun.Cli forwards, and .NET does not re-parse this
            // string before Bun's own argv parser sees it (on Windows it's passed through as the literal
            // command line; on Unix .NET splits it once, using the same convention, to build argv).
            var fullArguments = $"{Command}";
            if (!string.IsNullOrWhiteSpace(Arguments))
            {
                fullArguments += $" {Arguments}";
            }

            Log.LogMessage(MessageImportance.High, $"Executing: bun {fullArguments}");

            // Prepare the process
            var processStartInfo = new ProcessStartInfo
            {
                FileName = bunPath,
                Arguments = fullArguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            if (!string.IsNullOrWhiteSpace(WorkingDirectory))
            {
                processStartInfo.WorkingDirectory = WorkingDirectory!;
                Log.LogMessage(MessageImportance.Normal, $"Working directory: {WorkingDirectory}");
            }

            // Execute the process
            using var process = new Process();
            process.StartInfo = processStartInfo;

            var outputData = new System.Text.StringBuilder();
            var errorData = new System.Text.StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    outputData.AppendLine(e.Data);
                    Log.LogMessage(MessageImportance.Normal, e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    errorData.AppendLine(e.Data);
                    Log.LogMessage(MessageImportance.High, e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (TimeoutMilliseconds > 0)
            {
                if (!process.WaitForExit(TimeoutMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Ignore if process already exited
                    }
                    Log.LogError($"Command timed out after {TimeoutMilliseconds}ms");
                    return false;
                }
            }
            else
            {
                process.WaitForExit();
            }

            ExitCode = process.ExitCode;
            StandardOutput = outputData.ToString();
            StandardError = errorData.ToString();

            if (ExitCode != 0)
            {
                Log.LogError($"Bun command failed with exit code {ExitCode}");
                if (!string.IsNullOrWhiteSpace(StandardError))
                {
                    Log.LogError($"Error output: {StandardError}");
                }
                return ContinueOnError;
            }

            Log.LogMessage(MessageImportance.High, "Bun command completed successfully");
            return true;
        }
        catch (FileNotFoundException ex)
        {
            // The resolver already explains what is missing and how to fix it; a stack trace only buries that.
            Log.LogError(ex.Message);
            ExitCode = -1; // Set non-zero exit code to indicate failure
            return ContinueOnError;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            ExitCode = -1; // Set non-zero exit code to indicate failure
            return ContinueOnError;
        }
    }

    /// <summary>
    /// Gathers every runtime pack the build knows about, from both the item and the legacy property contract.
    /// </summary>
    /// <returns>The distinct packs available to this build.</returns>
    internal IReadOnlyList<BunRuntimePack> CollectRuntimePacks()
    {
        var packs = new List<BunRuntimePack>(BunRuntimePackFactory.FromTaskItems(RuntimePacks, warning => Log.LogWarning(warning)));
        packs.AddRange(CreateLegacyPacks());

        // A runtime package sets both contracts, so the same pack usually arrives twice. Items are added first
        // and Deduplicate keeps the first occurrence, so anything still marked LegacyProperty afterwards came
        // from a runtime package too old to declare the item.
        var distinct = BunRuntimePack.Deduplicate(packs);

        ReportDeprecatedPacks(distinct);

        return distinct;
    }

    /// <summary>
    /// First <c>Scarlet.Bun.Runtime.*</c> version that declares a <see cref="BunRuntimePack"/> item.
    /// </summary>
    /// <remarks>
    /// A fixed historical fact rather than a moving target - every release from this one on carries the item, so
    /// "update to this or later" stays correct indefinitely. Remove it with the rest of the legacy contract.
    /// </remarks>
    internal const string FirstItemAwareRuntimeVersion = "1.4.2";

    /// <summary>
    /// Reports packs that only the deprecated property contract knows about.
    /// </summary>
    /// <remarks>
    /// Deliberately a message rather than a warning: pinning an older runtime package is how you pin a Bun
    /// version, so this must not break builds that set TreatWarningsAsErrors. Promote it to
    /// <c>Log.LogWarning</c> one release before the property contract is removed.
    /// </remarks>
    /// <param name="packs">The de-duplicated packs available to this build.</param>
    private void ReportDeprecatedPacks(IReadOnlyList<BunRuntimePack> packs)
    {
        foreach (var pack in packs)
        {
            if (pack.Source != BunRuntimePackSource.LegacyProperty)
            {
                continue;
            }

            Log.LogMessage(
                MessageImportance.Normal,
                $"Bun runtime pack {pack} was discovered through the deprecated {GetLegacyPropertyName(pack.Rid)} property. " +
                $"Update that runtime package to {FirstItemAwareRuntimeVersion} or later, which declares a {BunRuntimePack.ItemName} item instead. " +
                "The property contract will be removed in a future major version of Scarlet.Bun.MSBuild.");
        }
    }

    /// <summary>
    /// Maps a runtime identifier back to its legacy property name.
    /// </summary>
    /// <remarks>Only valid for the six RIDs the legacy contract ever covered; it is frozen at those.</remarks>
    /// <param name="rid">The runtime identifier, for example <c>osx-arm64</c>.</param>
    /// <returns>The property name, for example <c>BunRuntime_osx_arm64</c>.</returns>
    private static string GetLegacyPropertyName(string rid) => "BunRuntime_" + rid.Replace('-', '_');

    /// <summary>
    /// Translates the legacy <c>BunRuntime_&lt;rid&gt;</c> properties into packs.
    /// </summary>
    /// <remarks>
    /// Runtime packages published before the <c>BunRuntimePack</c> item existed only set these properties, and they
    /// point at the package root rather than at its runtimes folder.
    /// </remarks>
    /// <returns>A pack for every legacy property that carries a value.</returns>
    private IEnumerable<BunRuntimePack> CreateLegacyPacks()
    {
        var legacyProperties = new[]
        {
            (Platform: Platform.WindowsX64, PackageRoot: BunRuntime_win_x64),
            (Platform: Platform.WindowsArm64, PackageRoot: BunRuntime_win_arm64),
            (Platform: Platform.LinuxX64, PackageRoot: BunRuntime_linux_x64),
            (Platform: Platform.LinuxArm64, PackageRoot: BunRuntime_linux_arm64),
            (Platform: Platform.MacOsX64, PackageRoot: BunRuntime_osx_x64),
            (Platform: Platform.MacOsArm64, PackageRoot: BunRuntime_osx_arm64)
        };

        foreach (var (platform, packageRoot) in legacyProperties)
        {
            if (string.IsNullOrWhiteSpace(packageRoot))
            {
                continue;
            }

            yield return new BunRuntimePack(
                BunRuntimeResolver.GetRuntimePackageName(platform),
                BunRuntimeResolver.GetRuntimeIdentifier(platform),
                Path.Combine(packageRoot!.Trim(), "runtimes"),
                source: BunRuntimePackSource.LegacyProperty);
        }
    }
}
