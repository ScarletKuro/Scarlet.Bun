using System;
using System.IO;
using System.IO.Abstractions;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Scarlet.Bun.MSBuild.Providers;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// Handles downloading Bun runtimes from GitHub releases.
/// </summary>
public sealed class BunDownloader
{
    private const string GithubReleasesUrl = "https://github.com/oven-sh/bun/releases";
    private readonly Platform _platform;
    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fileSystem;
    private readonly IChmodProvider _chmodProvider;
    private readonly IZipArchiveProvider _zipProvider;
    private readonly IBunLogger _log;

    public BunDownloader(HttpClient httpClient, IFileSystem fileSystem, IZipArchiveProvider zipProvider, IChmodProvider chmodProvider, Platform platform, IBunLogger log)
    {
        _platform = platform;
        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _zipProvider = zipProvider;
        _chmodProvider = chmodProvider;
        _log = log;
    }

    /// <summary>
    /// Downloads the Bun runtime with cross-process synchronization.
    /// Uses a named mutex to prevent concurrent downloads when multiple MSBuild projects
    /// target the same runtime directory (e.g., in monorepo scenarios).
    /// </summary>
    /// <param name="runtimeDirectory">Directory where the runtime should be downloaded.</param>
    /// <param name="version">Specific version to download (e.g., "1.3.6"). If null or empty, downloads latest.</param>
    /// <param name="mutexTimeoutSeconds">Maximum seconds to wait for the download mutex. Defaults to 300 (5 minutes).</param>
    /// <returns>Path to the downloaded Bun executable.</returns>
    public string DownloadRuntime(
        string runtimeDirectory,
        string? version = null,
        int mutexTimeoutSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            throw new ArgumentException("Runtime directory must be specified when using BunRuntimeDownload", nameof(runtimeDirectory));
        }

        var targetPlatform = _platform;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(targetPlatform);
        var platformName = BunRuntimeResolver.GetDownloadName(targetPlatform);
        var executableName = BunRuntimeResolver.GetExecutableName(targetPlatform);

        var fullRuntimePath = Path.Combine(runtimeDirectory, runtimeId, "native");
        var bunExecutablePath = Path.Combine(fullRuntimePath, executableName);

        // Fast path: runtime already exists, no synchronization needed
        if (_fileSystem.File.Exists(bunExecutablePath))
        {
            _chmodProvider.EnsureExecutablePermissions(bunExecutablePath);
            return bunExecutablePath;
        }

        _fileSystem.Directory.CreateDirectory(fullRuntimePath);

        var mutexName = CreateMutexName(bunExecutablePath);
        using var mutex = new Mutex(false, mutexName, out var createdNew);

        if (!createdNew)
        {
            _log.LogMessage("Another process is downloading the Bun runtime. Waiting...");
        }

        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(mutexTimeoutSeconds));
        }
        catch (AbandonedMutexException)
        {
            // Previous owner crashed — we now own the mutex, proceed normally
            acquired = true;
        }

        if (!acquired)
        {
            throw new TimeoutException(
                "Timed out waiting for another process to finish downloading the Bun runtime.");
        }

        if (!createdNew)
        {
            _log.LogMessage("Finished waiting. Resuming Bun runtime setup.");
        }

        try
        {
            // Double-check: another process may have completed the download while we waited
            if (_fileSystem.File.Exists(bunExecutablePath))
            {
                _chmodProvider.EnsureExecutablePermissions(bunExecutablePath);
                return bunExecutablePath;
            }

            // Construct download URL
            string downloadUrl;
            if (string.IsNullOrWhiteSpace(version))
            {
                downloadUrl = $"{GithubReleasesUrl}/latest/download/{platformName}.zip";
            }
            else
            {
                downloadUrl = $"{GithubReleasesUrl}/download/bun-v{version}/{platformName}.zip";
            }

            DownloadAndExtractAsync(downloadUrl, fullRuntimePath, platformName, executableName)
                .GetAwaiter().GetResult();

            if (!_fileSystem.File.Exists(bunExecutablePath))
            {
                throw new FileNotFoundException(
                    $"Bun executable was not found after extraction at expected path: {bunExecutablePath}");
            }

            _chmodProvider.EnsureExecutablePermissions(bunExecutablePath);

            return bunExecutablePath;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Downloads the Bun runtime for the current platform.
    /// </summary>
    /// <param name="runtimeDirectory">Directory where the runtime should be downloaded.</param>
    /// <param name="version">Specific version to download (e.g., "1.3.6"). If null or empty, downloads latest.</param>
    /// <returns>Path to the downloaded Bun executable.</returns>
    public async Task<string> DownloadRuntimeAsync(
        string runtimeDirectory,
        string? version = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            throw new ArgumentException("Runtime directory must be specified when using BunRuntimeDownload", nameof(runtimeDirectory));
        }

        var targetPlatform = _platform;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(targetPlatform);
        var platformName = BunRuntimeResolver.GetDownloadName(targetPlatform);
        var executableName = BunRuntimeResolver.GetExecutableName(targetPlatform);

        // Create the full runtime path: runtimeDirectory/runtimeId/native
        var fullRuntimePath = Path.Combine(runtimeDirectory, runtimeId, "native");
        var bunExecutablePath = Path.Combine(fullRuntimePath, executableName);

        // Check if runtime already exists
        if (_fileSystem.File.Exists(bunExecutablePath))
        {
            // Verify it's executable on Unix
            _chmodProvider.EnsureExecutablePermissions(bunExecutablePath);
            return bunExecutablePath;
        }

        // Construct download URL
        string downloadUrl;
        if (string.IsNullOrWhiteSpace(version))
        {
            downloadUrl = $"{GithubReleasesUrl}/latest/download/{platformName}.zip";
        }
        else
        {
            downloadUrl = $"{GithubReleasesUrl}/download/bun-v{version}/{platformName}.zip";
        }

        // Download and extract
        _fileSystem.Directory.CreateDirectory(fullRuntimePath);
        await DownloadAndExtractAsync(downloadUrl, fullRuntimePath, platformName, executableName);

        if (!_fileSystem.File.Exists(bunExecutablePath))
        {
            throw new FileNotFoundException(
                $"Bun executable was not found after extraction at expected path: {bunExecutablePath}");
        }

        _chmodProvider.EnsureExecutablePermissions(bunExecutablePath);

        return bunExecutablePath;
    }

    /// <summary>
    /// Creates an HttpClient configured for downloading Bun runtimes.
    /// </summary>
    public static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "Scarlet.Bun.MSBuild");
        return client;
    }

    private static string CreateMutexName(string executablePath)
    {
        var normalizedPath = Path.GetFullPath(executablePath).ToUpperInvariant();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
        var hashString = BitConverter.ToString(hash).Replace("-", "");
        return $"Global\\ScarletBun_{hashString}";
    }

    /// <summary>
    /// Downloads and extracts the Bun runtime archive.
    /// </summary>
    private async Task DownloadAndExtractAsync(string downloadUrl, string extractPath, string platformName, string executableName)
    {
        // Download to temporary file
        var tempDir = Path.GetTempPath();
        var tempZipPath = Path.Combine(tempDir, $"bun-{Guid.NewGuid()}.zip");

        // Ensure temp directory exists (important for MockFileSystem)
        _fileSystem.Directory.CreateDirectory(tempDir);

        try
        {
            var response = await _httpClient.GetAsync(downloadUrl);

            if (!response.IsSuccessStatusCode)
            {
                var error = $"Failed to download Bun runtime from {downloadUrl}. Status: {response.StatusCode}";
                throw new HttpRequestException(error);
            }

            using (var fileStream = _fileSystem.File.Create(tempZipPath))
            {
                await response.Content.CopyToAsync(fileStream);
            }

            // Extract the zip file
            // The zip contains a folder like "bun-windows-x64-baseline/bun.exe"
            // We need to extract just the executable to our target path
            using var archive = _zipProvider.OpenRead(tempZipPath);
            var extracted = false;
            foreach (var entry in archive.Entries)
            {
                // Look for the bun executable in the archive
                if (entry.Name.Equals(executableName, StringComparison.OrdinalIgnoreCase))
                {
                    var destinationPath = Path.Combine(extractPath, executableName);
                    _zipProvider.ExtractToFile(entry, destinationPath, overwrite: true);
                    extracted = true;
                    break;
                }
            }

            if (!extracted)
            {
                throw new InvalidDataException(
                    $"Downloaded Bun archive for '{platformName}' from '{downloadUrl}' did not contain expected executable '{executableName}'.");
            }
        }
        finally
        {
            if (_fileSystem.File.Exists(tempZipPath))
            {
                try
                {
                    _fileSystem.File.Delete(tempZipPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
