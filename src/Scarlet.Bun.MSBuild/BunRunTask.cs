using System;
using System.Diagnostics;
using System.IO.Abstractions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Scarlet.Bun.MSBuild.Providers;

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
    /// Optional path to the runtime directory. If not specified, uses the default NuGet package structure.
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
    /// Runtime package path for win-x64 (set by Scarlet.Bun.Runtime.windows-x64-baseline package).
    /// </summary>
    public string? BunRuntime_win_x64 { get; set; }

    /// <summary>
    /// Runtime package path for win-arm64 (set by Scarlet.Bun.Runtime.windows-aarch64 package).
    /// </summary>
    public string? BunRuntime_win_arm64 { get; set; }

    /// <summary>
    /// Runtime package path for linux-x64 (set by Scarlet.Bun.Runtime.linux-x64-baseline package).
    /// </summary>
    public string? BunRuntime_linux_x64 { get; set; }

    /// <summary>
    /// Runtime package path for linux-arm64 (set by Scarlet.Bun.Runtime.linux-aarch64 package).
    /// </summary>
    public string? BunRuntime_linux_arm64 { get; set; }

    /// <summary>
    /// Runtime package path for osx-x64 (set by Scarlet.Bun.Runtime.darwin-x64-baseline package).
    /// </summary>
    public string? BunRuntime_osx_x64 { get; set; }

    /// <summary>
    /// Runtime package path for osx-arm64 (set by Scarlet.Bun.Runtime.darwin-aarch64 package).
    /// </summary>
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
            // Uncomment for debugging purposes
            //Log.LogWarning($"Command: {Command}");
            //Log.LogWarning($"Arguments: {Arguments}");
            //Log.LogWarning($"WorkingDirectory: {WorkingDirectory}");
            //Log.LogWarning($"TimeoutMilliseconds: {TimeoutMilliseconds}");
            //Log.LogWarning($"ContinueOnError: {ContinueOnError}");
            //Log.LogWarning($"RuntimeDirectory: {RuntimeDirectory}");
            //Log.LogWarning($"BunRuntimeDownload: {BunRuntimeDownload}");
            //Log.LogWarning($"BunVersionDownload: {BunVersionDownload}");
            //Log.LogWarning($"BunRuntime_win_x64: {BunRuntime_win_x64}");
            //Log.LogWarning($"BunRuntime_linux_x64: {BunRuntime_linux_x64}");
            //Log.LogWarning($"BunRuntime_linux_arm64: {BunRuntime_linux_arm64}");
            //Log.LogWarning($"BunRuntime_osx_x64: {BunRuntime_osx_x64}");
            //Log.LogWarning($"BunRuntime_osx_arm64: {BunRuntime_osx_arm64}");
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
                    var downloader = new BunDownloader(httpClient, fileSystem, ZipArchiveProvider.Instance, chmodProvider, platform, new MsBuildBunLogger(Log));
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
                // Determine runtime directory from MSBuild properties if not explicitly set
                if (string.IsNullOrEmpty(RuntimeDirectory))
                {
                    var platform = BunRuntimeResolver.GetCurrentPlatform();
                    var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
                    
                    // Get the runtime package path based on current platform
                    var runtimePackagePath = GetRuntimePath(runtimeId);

                    if (!string.IsNullOrEmpty(runtimePackagePath))
                    {
                        RuntimeDirectory = System.IO.Path.Combine(runtimePackagePath, "runtimes");
                    }
                }
                
                bunPath = BunRuntimeResolver.ResolveBunExecutable(fileSystem, chmodProvider, runtimeDirectory: RuntimeDirectory);
                Log.LogMessage(MessageImportance.High, $"Platform: {BunRuntimeResolver.GetCurrentPlatform()}");
            }
            
            Log.LogMessage(MessageImportance.High, $"Using Bun at: {bunPath}");

            // Build the full command line
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

            bool exited;
            if (TimeoutMilliseconds > 0)
            {
                exited = process.WaitForExit(TimeoutMilliseconds);
                if (!exited)
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
                exited = true;
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
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            ExitCode = -1; // Set non-zero exit code to indicate failure
            return ContinueOnError;
        }
    }

    private string? GetRuntimePath(string runtimeId)
    {
        var runtimePackagePath = runtimeId switch
        {
            "win-x64" => BunRuntime_win_x64,
            "win-arm64" => BunRuntime_win_arm64,
            "linux-x64" => BunRuntime_linux_x64,
            "linux-arm64" => BunRuntime_linux_arm64,
            "osx-x64" => BunRuntime_osx_x64,
            "osx-arm64" => BunRuntime_osx_arm64,
            _ => null
        };

        return runtimePackagePath;
    }
}
