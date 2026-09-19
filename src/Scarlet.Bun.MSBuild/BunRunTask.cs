using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
    /// Linux errno for "Text file busy", surfaced by <see cref="Win32Exception.NativeErrorCode"/> when
    /// <see cref="Process.Start()"/> fails on Unix.
    /// </summary>
    private const int TextFileBusyErrorCode = 26;

    private const int MaxProcessStartAttempts = 5;

    private const int ProcessStartRetryBaseDelayMilliseconds = 25;

    /// <summary>
    /// Lines of each stream kept for the failure message even when <see cref="CaptureOutput"/> is off.
    /// </summary>
    /// <remarks>
    /// Streamed output is logged as messages - stderr at High, which quiet verbosity drops, and stdout at
    /// Normal, which the default minimal verbosity drops - so without this a failing step reports only its
    /// exit code. A bounded tail keeps the failure readable without holding a whole transcript in memory.
    /// </remarks>
    private const int DiagnosticTailLineCount = 50;

    /// <summary>
    /// How long a timed run waits for redirected output to drain after the process itself has exited.
    /// </summary>
    private const int OutputDrainGraceMilliseconds = 5000;

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
    /// Whether stdout and stderr should be retained in <see cref="StandardOutput"/> and <see cref="StandardError"/>.
    /// Output is still logged while the process runs.
    /// </summary>
    public bool CaptureOutput { get; set; } = true;

    /// <summary>
    /// Optional semicolon-separated list of input files or directories for timestamp-based skipping.
    /// A directory is walked recursively, so editing a file in place invalidates the step.
    /// </summary>
    public string? Inputs { get; set; }

    /// <summary>
    /// Optional semicolon-separated list of output files or directories for timestamp-based skipping.
    /// </summary>
    public string? Outputs { get; set; }

    /// <summary>
    /// Optional file that records a successful incremental run. When omitted, a generated name under
    /// <see cref="StampDirectory"/> is used.
    /// </summary>
    public string? StampFile { get; set; }

    /// <summary>
    /// Optional directory for the generated stamp, normally the project's <c>$(IntermediateOutputPath)</c>.
    /// Ignored when <see cref="StampFile"/> is set.
    /// </summary>
    public string? StampDirectory { get; set; }

    /// <summary>
    /// The project's directory, which relative <see cref="Inputs"/>, <see cref="Outputs"/> and
    /// <see cref="StampFile"/> resolve against. Normally <c>$(MSBuildProjectDirectory)</c>.
    /// </summary>
    /// <remarks>
    /// Passed explicitly rather than read from <c>IBuildEngine.ProjectFileOfTaskNode</c>: that reports the
    /// file containing the task invocation, which for this package is the imported .targets, not the project.
    /// </remarks>
    public string? ProjectDirectory { get; set; }

    /// <summary>
    /// The stamp this run used, so the build can record it in <c>@(FileWrites)</c> and clean it.
    /// Empty when incremental skipping is not configured.
    /// </summary>
    [Output]
    public string? StampFilePath { get; set; }

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
        // Closed on every exit path. A handler firing after the task has returned - possible whenever the drain
        // grace expires - would otherwise log into a finished task, which MSBuild turns into an exception that
        // AsyncStreamReader rethrows on a thread-pool thread, killing the build process.
        var gate = new TaskLifetimeGate();

        // Declared out here, not beside the handlers that use them: a `using` inside the try disposes as
        // control leaves the try, which is before the finally closes the gate - leaving a window where a late
        // handler could pass the gate and signal a disposed event. At method scope they outlive the gate.
        using var outputClosed = new ManualResetEventSlim(false);
        using var errorClosed = new ManualResetEventSlim(false);

        try
        {
            if (string.IsNullOrWhiteSpace(Command))
            {
                Log.LogError("Command parameter is required");
                return false;
            }

            var fileSystem = new FileSystem();
            var chmodProvider = Chmod.CreateProvider();
            var incrementalState = CreateIncrementalState(fileSystem);
            StampFilePath = incrementalState?.StampPath;

            if (IsUpToDate(fileSystem, incrementalState))
            {
                Log.LogMessage(MessageImportance.Normal, $"Skipping Bun {Command}: outputs are up-to-date.");
                ExitCode = 0;
                StandardOutput = CaptureOutput ? string.Empty : null;
                StandardError = CaptureOutput ? string.Empty : null;
                return true;
            }

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
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrWhiteSpace(WorkingDirectory))
            {
                processStartInfo.WorkingDirectory = WorkingDirectory!;
                Log.LogMessage(MessageImportance.Normal, $"Working directory: {WorkingDirectory}");
            }

            // Execute the process
            using var process = new Process();
            process.StartInfo = processStartInfo;

            // Both streams keep a bounded tail for the failure message, and the full text when asked to.
            // stdout needs a tail as much as stderr: it is logged at Normal importance, which `dotnet build`'s
            // default minimal verbosity drops, and plenty of tools report what went wrong there.
            var output = new OutputCollector(DiagnosticTailLineCount, CaptureOutput);
            var error = new OutputCollector(DiagnosticTailLineCount, CaptureOutput);

            // A null Data is how the framework signals end-of-stream, which is what the drain below waits on.
            // Accumulating stays outside the gate: it is independently thread-safe, and a late line landing in
            // a buffer nobody reads is harmless. Only the two things that must not outlive the task - signalling
            // the events and logging - go through it.
            //
            // Neither handler may touch `process`. It is disposed as control leaves the try, while these can
            // still fire, so reaching for something like process.Id here would hit a disposed object on a
            // thread-pool thread - which terminates the build. Nothing tests this; it only holds by inspection.
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    // ReSharper disable once AccessToDisposedClosure
                    gate.TryRun(outputClosed.Set);
                    return;
                }

                output.Add(e.Data);
                gate.TryRun(() => Log.LogMessage(MessageImportance.Normal, e.Data));
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    // ReSharper disable once AccessToDisposedClosure
                    gate.TryRun(errorClosed.Set);
                    return;
                }

                error.Add(e.Data);
                gate.TryRun(() => Log.LogMessage(MessageImportance.High, e.Data));
            };

            StartProcessWithRetry(process);
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

                // The process is gone, but the handlers may not have drained. Waiting on the end-of-stream
                // signals rather than the parameterless WaitForExit() keeps that wait bounded - a detached
                // grandchild holding the write end can withhold EOF forever, outliving the timeout the caller
                // asked for - and costs no extra thread to abandon when it does.
                if (!(outputClosed.Wait(OutputDrainGraceMilliseconds) && errorClosed.Wait(OutputDrainGraceMilliseconds)))
                {
                    Log.LogMessage(
                        MessageImportance.Normal,
                        $"Bun exited but its output was still open after {OutputDrainGraceMilliseconds}ms; some output may be missing.");
                }
            }
            else
            {
                process.WaitForExit();
            }

            ExitCode = process.ExitCode;
            StandardOutput = output.All;
            StandardError = error.All;

            if (ExitCode != 0)
            {
                Log.LogError($"Bun command failed with exit code {ExitCode}");

                var errorDetail = CaptureOutput && !string.IsNullOrWhiteSpace(StandardError)
                    ? StandardError!
                    : error.Tail;

                if (!string.IsNullOrWhiteSpace(errorDetail))
                {
                    Log.LogError($"Error output: {errorDetail}");
                }

                // Always the bounded tail, even when capturing: stdout is context for the failure rather than
                // the failure itself, and a full `bun install` transcript repeated into an error helps nobody.
                var standardDetail = output.Tail;

                if (!string.IsNullOrWhiteSpace(standardDetail))
                {
                    Log.LogError($"Standard output: {standardDetail}");
                }

                return ContinueOnError;
            }

            WriteIncrementalStamp(fileSystem, incrementalState);

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
        finally
        {
            gate.Close();
        }
    }


    /// <summary>
    /// Starts <paramref name="process"/>, retrying a handful of times on Linux/macOS if the kernel reports
    /// the executable as busy (ETXTBSY). This shows up when a just-downloaded or just-run Bun binary is
    /// exec'd again within milliseconds - e.g. an <c>install</c> step immediately followed by a <c>run</c>
    /// step against the same cached binary - and a grandchild process Bun spawned for the first run hasn't
    /// fully released the executable yet even though the process .NET waited on has already exited.
    /// </summary>
    [ExcludeFromCodeCoverage]
    private void StartProcessWithRetry(Process process)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                process.Start();
                return;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == TextFileBusyErrorCode
                && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && attempt < MaxProcessStartAttempts)
            {
                var delayMilliseconds = ProcessStartRetryBaseDelayMilliseconds * (1 << (attempt - 1));
                Log.LogMessage(
                    MessageImportance.Normal,
                    $"Bun executable was busy (ETXTBSY); retrying in {delayMilliseconds}ms (attempt {attempt}/{MaxProcessStartAttempts}).");
                Thread.Sleep(delayMilliseconds);
            }
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

    private IncrementalState? CreateIncrementalState(IFileSystem fileSystem)
    {
        var inputPaths = SplitPaths(Inputs);
        var outputPaths = SplitPaths(Outputs);

        if (inputPaths.Count == 0 || outputPaths.Count == 0)
        {
            return null;
        }

        var baseDirectory = ResolveIncrementalBaseDirectory();

        // Captured before the inputs are read, so an edit that lands during the run is newer than the stamp.
        var probedAtUtc = DateTime.UtcNow;

        DateTime? newestInput = null;
        var resolvedInputs = new List<string>(inputPaths.Count);
        foreach (var input in inputPaths)
        {
            var resolvedInput = ResolveIncrementalPath(baseDirectory, input);
            if (!TryGetNewestWriteTimeUtc(fileSystem, resolvedInput, out var timestamp))
            {
                return null;
            }

            resolvedInputs.Add(resolvedInput);
            newestInput = newestInput is null || timestamp > newestInput.Value
                ? timestamp
                : newestInput;
        }

        var resolvedOutputs = new List<string>(outputPaths.Count);
        foreach (var output in outputPaths)
        {
            resolvedOutputs.Add(ResolveIncrementalPath(baseDirectory, output));
        }

        var stampContent = CreateIncrementalStampContent(resolvedInputs, resolvedOutputs);
        var stampPath = string.IsNullOrWhiteSpace(StampFile)
            ? CreateDefaultStampPath(baseDirectory, stampContent)
            : ResolveIncrementalPath(baseDirectory, StampFile!);

        return new IncrementalState(
            resolvedOutputs,
            stampPath,
            newestInput!.Value,
            probedAtUtc,
            stampContent);
    }

    private static bool IsUpToDate(IFileSystem fileSystem, IncrementalState? state)
    {
        if (state is null || !fileSystem.File.Exists(state.StampPath))
        {
            return false;
        }

        foreach (var output in state.Outputs)
        {
            if (!fileSystem.File.Exists(output) && !fileSystem.Directory.Exists(output))
            {
                return false;
            }
        }

        if (fileSystem.File.GetLastWriteTimeUtc(state.StampPath) < state.NewestInput)
        {
            return false;
        }

        try
        {
            return string.Equals(fileSystem.File.ReadAllText(state.StampPath), state.StampContent, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void WriteIncrementalStamp(IFileSystem fileSystem, IncrementalState? state)
    {
        if (state is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(state.StampPath);
            if (!string.IsNullOrEmpty(directory))
            {
                fileSystem.Directory.CreateDirectory(directory);
            }

            fileSystem.File.WriteAllText(state.StampPath, state.StampContent);

            // Back-date the stamp to when the inputs were read, not to now. Inputs are stat'd before the
            // process starts, so a file edited while a long run is in flight ends up older than a stamp
            // written afterwards - and would never invalidate. Dating the stamp from the probe means such an
            // edit is newer than the stamp and the next build picks it up.
            fileSystem.File.SetLastWriteTimeUtc(state.StampPath, state.ProbedAtUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.LogMessage(
                MessageImportance.Normal,
                $"Could not write Bun incremental stamp '{state.StampPath}'. The command succeeded, but this step may run again next build. {ex.Message}");
        }
    }

    private static IReadOnlyList<string> SplitPaths(string? paths)
    {
        if (string.IsNullOrWhiteSpace(paths))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var path in paths!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = path.Trim();
            if (trimmed.Length > 0)
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private string CreateIncrementalStampContent(
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs)
    {
        var content = new StringBuilder();
        content.AppendLine("Scarlet.Bun.MSBuild incremental stamp");
        content.AppendLine($"Command={Command}");
        content.AppendLine($"Arguments={Arguments}");
        // The directory the command ran in, which can change its behaviour. The base the paths were resolved
        // against is already implicit: the inputs and outputs below are recorded absolute.
        content.AppendLine($"WorkingDirectory={WorkingDirectory}");

        // How the runtime is selected, not the resolved binary: keeps the up-to-date check ahead of
        // resolution, so a skipped step still costs no download.
        content.AppendLine($"RuntimeDirectory={RuntimeDirectory}");
        content.AppendLine($"RuntimeDownload={BunRuntimeDownload}");
        content.AppendLine($"VersionDownload={BunVersionDownload}");
        content.AppendLine("RuntimePacks=");

        foreach (var pack in DescribeRuntimePacksForStamp())
        {
            content.AppendLine(pack);
        }

        content.AppendLine("Inputs=");

        foreach (var input in inputs)
        {
            content.AppendLine(input);
        }

        content.AppendLine("Outputs=");

        foreach (var output in outputs)
        {
            content.AppendLine(output);
        }

        return content.ToString();
    }

    /// <summary>
    /// Describes the runtime packs for the stamp, sorted so the text never depends on restore order.
    /// </summary>
    /// <remarks>
    /// Deliberately reads the raw inputs instead of calling <see cref="CollectRuntimePacks"/>: that path
    /// reports deprecation warnings, and building a stamp is not the moment to emit them a second time.
    /// </remarks>
    private IEnumerable<string> DescribeRuntimePacksForStamp()
    {
        var descriptions = new List<string>();

        foreach (var pack in RuntimePacks ?? Array.Empty<ITaskItem>())
        {
            descriptions.Add(string.Join(
                "|",
                pack.ItemSpec,
                pack.GetMetadata(BunRuntimePack.RidMetadataName),
                pack.GetMetadata(BunRuntimePack.RuntimesPathMetadataName),
                pack.GetMetadata(BunRuntimePack.PriorityMetadataName)));
        }

        foreach (var legacy in new[]
                 {
                     ("win-x64", BunRuntime_win_x64),
                     ("win-arm64", BunRuntime_win_arm64),
                     ("linux-x64", BunRuntime_linux_x64),
                     ("linux-arm64", BunRuntime_linux_arm64),
                     ("osx-x64", BunRuntime_osx_x64),
                     ("osx-arm64", BunRuntime_osx_arm64)
                 })
        {
            if (!string.IsNullOrWhiteSpace(legacy.Item2))
            {
                descriptions.Add($"{legacy.Item1}|{legacy.Item2}");
            }
        }

        descriptions.Sort(StringComparer.Ordinal);

        return descriptions;
    }

    private string CreateDefaultStampPath(string baseDirectory, string stampContent)
    {
        using var sha256 = SHA256.Create();
        var stampName = BitConverter
            .ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(stampContent)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();

        // StampDirectory is the project's real intermediate output, passed by the targets. Falling back to
        // "obj" under the working directory would ignore a relocated BaseIntermediateOutputPath or artifacts
        // layout, and would scatter stray obj folders when a step sets WorkingDirectory to a subfolder.
        var stampDirectory = string.IsNullOrWhiteSpace(StampDirectory)
            ? Path.Combine(baseDirectory, "obj", "Scarlet.Bun")
            : ResolveIncrementalPath(baseDirectory, StampDirectory!);

        return Path.Combine(stampDirectory, $"{stampName}.stamp");
    }

    /// <summary>
    /// The directory relative <see cref="Inputs"/>, <see cref="Outputs"/> and <see cref="StampFile"/> are
    /// resolved against: the project's, as every other relative path in a project file is.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="WorkingDirectory"/>. That says where the command runs, and letting it move
    /// what the paths mean would make <c>&lt;Inputs&gt;assets/app.js&lt;/Inputs&gt;</c> point somewhere else
    /// the moment a step also set a working directory - silently, and differently from every other item in
    /// the file. <see cref="Environment.CurrentDirectory"/> is only the last resort, for a task constructed
    /// outside a real build.
    /// </remarks>
    private string ResolveIncrementalBaseDirectory()
    {
        if (!string.IsNullOrWhiteSpace(ProjectDirectory))
        {
            return ProjectDirectory!;
        }

        return string.IsNullOrWhiteSpace(WorkingDirectory)
            ? Environment.CurrentDirectory
            : WorkingDirectory!;
    }

    private static string ResolveIncrementalPath(string baseDirectory, string path)
    {
        try
        {
            return Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(baseDirectory, path));
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>
    /// Newest write time under <paramref name="path"/>, which may be a file or a directory.
    /// </summary>
    /// <remarks>
    /// A directory is walked recursively rather than read with <see cref="IDirectory.GetLastWriteTimeUtc"/>.
    /// A directory's own timestamp only moves when an entry is added to or removed from that one directory,
    /// so editing a file in place - the common case for a source tree handed to Bun - leaves it untouched and
    /// the step would be skipped with stale outputs. Entries are enumerated as <see cref="IFileSystemInfo"/>
    /// so each timestamp comes from the walk rather than a second stat per entry. The directory itself is
    /// included so deletions, which move the parent's timestamp but leave nothing behind to observe, still
    /// invalidate.
    /// </remarks>
    private static bool TryGetNewestWriteTimeUtc(IFileSystem fileSystem, string path, out DateTime timestamp)
    {
        if (fileSystem.File.Exists(path))
        {
            timestamp = fileSystem.File.GetLastWriteTimeUtc(path);
            return true;
        }

        if (!fileSystem.Directory.Exists(path))
        {
            timestamp = default;
            return false;
        }

        // The (pattern, SearchOption) overload maps to EnumerationOptions.CompatibleRecursive, which sets
        // IgnoreInaccessible = false - so an unreadable subdirectory throws, as does one deleted mid-walk.
        // Probing freshness must never be the thing that fails a build: if the tree cannot be read, say so
        // and let the step run.
        try
        {
            var directory = fileSystem.DirectoryInfo.New(path);
            var newest = directory.LastWriteTimeUtc;

            foreach (var entry in directory.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                if (entry.LastWriteTimeUtc > newest)
                {
                    newest = entry.LastWriteTimeUtc;
                }
            }

            timestamp = newest;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            timestamp = default;
            return false;
        }
    }

    private sealed class IncrementalState
    {
        public IncrementalState(
            IReadOnlyList<string> outputs,
            string stampPath,
            DateTime newestInput,
            DateTime probedAtUtc,
            string stampContent)
        {
            Outputs = outputs;
            StampPath = stampPath;
            NewestInput = newestInput;
            ProbedAtUtc = probedAtUtc;
            StampContent = stampContent;
        }

        public IReadOnlyList<string> Outputs { get; }
        public string StampPath { get; }
        public DateTime NewestInput { get; }

        /// <summary>When the inputs were read, which is what the written stamp is dated from.</summary>
        public DateTime ProbedAtUtc { get; }

        public string StampContent { get; }
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
