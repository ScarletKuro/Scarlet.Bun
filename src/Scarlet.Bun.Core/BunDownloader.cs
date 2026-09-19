using System;
using System.IO;
using System.IO.Abstractions;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Scarlet.Bun.Core.Providers;

namespace Scarlet.Bun.Core;

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
    private readonly ILatestVersionResolver _latestVersionResolver;

    public BunDownloader(
        HttpClient httpClient,
        IFileSystem fileSystem,
        IZipArchiveProvider zipProvider,
        IChmodProvider chmodProvider,
        Platform platform,
        IBunLogger log,
        ILatestVersionResolver latestVersionResolver)
    {
        _platform = platform;
        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _zipProvider = zipProvider;
        _chmodProvider = chmodProvider;
        _log = log;
        _latestVersionResolver = latestVersionResolver ?? throw new ArgumentNullException(nameof(latestVersionResolver));
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
        var versionMarkerPath = GetVersionMarkerPath(bunExecutablePath);
        var hasExplicitVersion = !string.IsNullOrWhiteSpace(version);

        // Fast path: only trustworthy for a pinned version - "latest" must always ask GitHub whether it moved.
        // The executable is published atomically only after extraction and chmod complete.
        if (hasExplicitVersion && IsCacheValidForVersion(bunExecutablePath, versionMarkerPath, version!))
        {
            _log.LogMessage($"Bun {version} is already cached at {bunExecutablePath}. Skipping download.");
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
            if (hasExplicitVersion && IsCacheValidForVersion(bunExecutablePath, versionMarkerPath, version!))
            {
                _log.LogMessage($"Bun {version} was downloaded by another process while waiting. Skipping download.");
                _chmodProvider.EnsureExecutablePermissions(bunExecutablePath);
                return bunExecutablePath;
            }

            var stagedExecutablePath = CreateStagedExecutablePath(fullRuntimePath, executableName);

            if (hasExplicitVersion)
            {
                var downloadUrl = $"{GithubReleasesUrl}/download/bun-v{version}/{platformName}.zip";
                DownloadAndExtractAsync(downloadUrl, stagedExecutablePath, platformName, executableName)
                    .GetAwaiter().GetResult();

                var publishedPath = PublishStagedExecutable(stagedExecutablePath, bunExecutablePath);
                WriteVersionMarker(versionMarkerPath, version!);
                return publishedPath;
            }

            var latestDownloadUrl = $"{GithubReleasesUrl}/latest/download/{platformName}.zip";
            return ResolveAndDownloadLatestAsync(latestDownloadUrl, bunExecutablePath, versionMarkerPath, stagedExecutablePath, platformName, executableName)
                .GetAwaiter().GetResult();
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
        var versionMarkerPath = GetVersionMarkerPath(bunExecutablePath);
        var hasExplicitVersion = !string.IsNullOrWhiteSpace(version);

        // Check if a runtime matching the requested version is already cached.
        // "latest" is never trusted here - it must always ask GitHub whether it moved.
        if (hasExplicitVersion && IsCacheValidForVersion(bunExecutablePath, versionMarkerPath, version!))
        {
            // Verify it's executable on Unix
            _log.LogMessage($"Bun {version} is already cached at {bunExecutablePath}. Skipping download.");
            _chmodProvider.EnsureExecutablePermissions(bunExecutablePath);
            return bunExecutablePath;
        }

        _fileSystem.Directory.CreateDirectory(fullRuntimePath);
        var stagedExecutablePath = CreateStagedExecutablePath(fullRuntimePath, executableName);

        if (hasExplicitVersion)
        {
            var downloadUrl = $"{GithubReleasesUrl}/download/bun-v{version}/{platformName}.zip";
            await DownloadAndExtractAsync(downloadUrl, stagedExecutablePath, platformName, executableName);

            var publishedPath = PublishStagedExecutable(stagedExecutablePath, bunExecutablePath);
            WriteVersionMarker(versionMarkerPath, version!);
            return publishedPath;
        }

        var latestDownloadUrl = $"{GithubReleasesUrl}/latest/download/{platformName}.zip";
        return await ResolveAndDownloadLatestAsync(latestDownloadUrl, bunExecutablePath, versionMarkerPath, stagedExecutablePath, platformName, executableName);
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
        client.DefaultRequestHeaders.Add("User-Agent", "Scarlet.Bun");
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
    /// Downloads and extracts the Bun runtime archive from a known URL.
    /// </summary>
    private async Task DownloadAndExtractAsync(string downloadUrl, string stagedExecutablePath, string platformName, string executableName)
    {
        using var response = await _httpClient.GetAsync(downloadUrl);
        await ExtractResponseToStagedExecutableAsync(response, downloadUrl, stagedExecutablePath, platformName, executableName);
    }

    /// <summary>
    /// Resolves the concrete version behind "latest" via <see cref="_latestVersionResolver"/> and downloads
    /// it only if it differs from what is already cached.
    /// </summary>
    private async Task<string> ResolveAndDownloadLatestAsync(
        string latestUrl,
        string bunExecutablePath,
        string versionMarkerPath,
        string stagedExecutablePath,
        string platformName,
        string executableName)
    {
        var resolvedVersion = await _latestVersionResolver.TryResolveVersionAsync(latestUrl);

        if (resolvedVersion is not null && IsCacheValidForVersion(bunExecutablePath, versionMarkerPath, resolvedVersion))
        {
            _log.LogMessage($"Bun 'latest' still resolves to {resolvedVersion}, which is already cached at {bunExecutablePath}. Skipping download.");
            _chmodProvider.EnsureExecutablePermissions(bunExecutablePath);
            return bunExecutablePath;
        }

        // A resolved version can be downloaded directly by its tag, skipping the redirect we already
        // followed once to resolve it. If resolution failed, fall back to letting the main (redirect
        // following) client resolve "latest" itself.
        var downloadUrl = resolvedVersion is not null
            ? $"{GithubReleasesUrl}/download/bun-v{resolvedVersion}/{platformName}.zip"
            : latestUrl;

        if (resolvedVersion is not null)
        {
            _log.LogMessage($"Bun 'latest' resolved to {resolvedVersion}.");
        }

        await DownloadAndExtractAsync(downloadUrl, stagedExecutablePath, platformName, executableName);
        var publishedPath = PublishStagedExecutable(stagedExecutablePath, bunExecutablePath);

        if (resolvedVersion is not null)
        {
            WriteVersionMarker(versionMarkerPath, resolvedVersion);
        }
        else
        {
            // Could not determine what version was just downloaded (e.g. GitHub changed the redirect
            // shape). Clear any stale marker rather than leave it pointing at a different version.
            DeleteFileIfExists(versionMarkerPath);
        }

        return publishedPath;
    }

    /// <summary>
    /// Reads an already-obtained response into a temp zip file and extracts the matching executable entry
    /// to <paramref name="stagedExecutablePath"/>.
    /// </summary>
    private async Task ExtractResponseToStagedExecutableAsync(HttpResponseMessage response, string downloadUrl, string stagedExecutablePath, string platformName, string executableName)
    {
        EnsureSuccessOrThrow(response, downloadUrl);

        // Download to temporary file
        var tempDir = Path.GetTempPath();
        var tempZipPath = Path.Combine(tempDir, $"bun-{Guid.NewGuid()}.zip");

        // Ensure temp directory exists (important for MockFileSystem)
        _fileSystem.Directory.CreateDirectory(tempDir);

        try
        {
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
                    _zipProvider.ExtractToFile(entry, stagedExecutablePath, overwrite: true);
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

    private static void EnsureSuccessOrThrow(HttpResponseMessage response, string downloadUrl)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to download Bun runtime from {downloadUrl}. Status: {response.StatusCode}");
        }
    }

    private static string GetVersionMarkerPath(string bunExecutablePath) => bunExecutablePath + ".version";

    private bool IsCacheValidForVersion(string bunExecutablePath, string versionMarkerPath, string expectedVersion)
    {
        if (!_fileSystem.File.Exists(bunExecutablePath) || !_fileSystem.File.Exists(versionMarkerPath))
        {
            return false;
        }

        var storedVersion = _fileSystem.File.ReadAllText(versionMarkerPath).Trim();
        return string.Equals(storedVersion, expectedVersion, StringComparison.Ordinal);
    }

    private void WriteVersionMarker(string versionMarkerPath, string version)
    {
        _fileSystem.File.WriteAllText(versionMarkerPath, version);
    }

    private string PublishStagedExecutable(string stagedExecutablePath, string bunExecutablePath)
    {
        try
        {
            if (!_fileSystem.File.Exists(stagedExecutablePath))
            {
                throw new FileNotFoundException(
                    $"Bun executable was not found after extraction before publication. Final path: {bunExecutablePath}. Staging path: {stagedExecutablePath}");
            }

            _chmodProvider.EnsureExecutablePermissions(stagedExecutablePath);

            // Reaching this point means the caller already determined any existing cache is stale
            // (missing/mismatched version marker) or absent, so a pre-existing file here is leftover
            // content that must be replaced, not a valid cache hit to preserve. Unlike the staged-file
            // cleanup below, a failed delete here must not be swallowed: silently keeping the stale file
            // while the caller goes on to write the new version marker would make the marker lie about
            // what is actually on disk.
            if (_fileSystem.File.Exists(bunExecutablePath))
            {
                _fileSystem.File.Delete(bunExecutablePath);
            }

            try
            {
                _fileSystem.File.Move(stagedExecutablePath, bunExecutablePath);
            }
            catch (IOException) when (_fileSystem.File.Exists(bunExecutablePath))
            {
                // Another concurrent caller (e.g. two unsynchronized DownloadRuntimeAsync calls for the
                // same version) already published a file at this path.
                return bunExecutablePath;
            }

            if (!_fileSystem.File.Exists(bunExecutablePath))
            {
                throw new FileNotFoundException(
                    $"Bun executable was not found after publication at expected path: {bunExecutablePath}");
            }

            return bunExecutablePath;
        }
        finally
        {
            DeleteFileIfExists(stagedExecutablePath);
        }
    }

    private static string CreateStagedExecutablePath(string directoryPath, string executableName)
    {
        return Path.Combine(directoryPath, $".{executableName}.{Guid.NewGuid():N}.tmp");
    }

    private void DeleteFileIfExists(string path)
    {
        if (!_fileSystem.File.Exists(path))
        {
            return;
        }

        try
        {
            _fileSystem.File.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
