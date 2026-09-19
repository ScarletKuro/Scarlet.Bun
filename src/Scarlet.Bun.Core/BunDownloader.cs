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
        ILatestVersionResolver latestVersionResolver,
        IFileSystem fileSystem,
        IZipArchiveProvider zipProvider,
        IChmodProvider chmodProvider,
        Platform platform,
        IBunLogger log)
    {
        _platform = platform;
        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _zipProvider = zipProvider;
        _chmodProvider = chmodProvider;
        _log = log;
        _latestVersionResolver = latestVersionResolver;
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
    public string DownloadRuntime(string runtimeDirectory, string? version = null, int mutexTimeoutSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            throw new ArgumentException("Runtime directory must be specified when using BunRuntimeDownload", nameof(runtimeDirectory));
        }

        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(_platform);
        var platformName = BunRuntimeResolver.GetDownloadName(_platform);
        var executableName = BunRuntimeResolver.GetExecutableName(_platform);

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
                var (downloadUrl, checksumsUrl) = BuildDownloadUrls(platformName, version);
                DownloadAndExtractAsync(downloadUrl, checksumsUrl, stagedExecutablePath, platformName, executableName)
                    .GetAwaiter().GetResult();

                var publishedPath = PublishStagedExecutable(stagedExecutablePath, bunExecutablePath);
                WriteVersionMarker(versionMarkerPath, version!);
                return publishedPath;
            }

            var (latestDownloadUrl, latestChecksumsUrl) = BuildDownloadUrls(platformName, null);
            return ResolveAndDownloadLatestAsync(latestDownloadUrl, latestChecksumsUrl, bunExecutablePath, versionMarkerPath, stagedExecutablePath, platformName, executableName)
                .GetAwaiter().GetResult();
        }
        finally
        {
            // ReleaseMutex is thread-affine: it must run on the exact thread that called WaitOne. Everything
            // in this try block is a blocking .GetAwaiter().GetResult() rather than a real `await`, so this
            // method never yields its thread back to the pool - unlike an `async` method spanning the same
            // awaits, where a post-await continuation resuming on a different pooled thread would make this
            // throw ApplicationException instead of releasing the lock.
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Downloads the Bun runtime for the current platform, with the same cross-process mutex synchronization
    /// as <see cref="DownloadRuntime"/>.
    /// </summary>
    /// <param name="runtimeDirectory">Directory where the runtime should be downloaded.</param>
    /// <param name="version">Specific version to download (e.g., "1.3.6"). If null or empty, downloads latest.</param>
    /// <param name="mutexTimeoutSeconds">Maximum seconds to wait for the download mutex. Defaults to 300 (5 minutes).</param>
    /// <returns>Path to the downloaded Bun executable.</returns>
    /// <remarks>
    /// Delegates to <see cref="DownloadRuntime"/> on a dedicated pooled thread via <c>Task.Run</c>, rather than
    /// reimplementing it as a genuinely asynchronous method. The mutex this acquires is thread-affine - released
    /// only by the thread that acquired it - so the whole critical section must run start-to-finish on one
    /// thread. <c>Task.Run</c> gives it exactly that thread for the mutex's entire lifetime while still freeing
    /// the caller's thread, which a truly `async` version spanning real `await`s could not guarantee.
    /// </remarks>
    public Task<string> DownloadRuntimeAsync(string runtimeDirectory, string? version = null, int mutexTimeoutSeconds = 300)
        => Task.Run(() => DownloadRuntime(runtimeDirectory, version, mutexTimeoutSeconds));

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

    internal static string CreateMutexName(string executablePath)
    {
        var normalizedPath = Path.GetFullPath(executablePath).ToUpperInvariant();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
        var hashString = BitConverter.ToString(hash).Replace("-", "");
        return $"Global\\ScarletBun_{hashString}";
    }

    /// <summary>
    /// Builds the archive download URL and the matching upstream SHA-256 checksums URL for a Bun release.
    /// </summary>
    /// <remarks>
    /// Both URLs are always plain string formatting from a known version (or "latest"), never derived from
    /// an HTTP response. GitHub's release-asset redirect chain has two hops: the first
    /// (".../releases/download/bun-v1.4.2/asset.zip") carries the version; the second - a signed,
    /// time-limited "release-assets.githubusercontent.com" blob URL - does not, and has no sibling
    /// SHASUMS256.txt at all. A client that follows redirects automatically only ever observes that second
    /// hop, so deriving the checksums URL from the downloaded response's resolved URI (as opposed to
    /// building it here, before any request is made) would point at a checksums file that does not exist.
    /// See <see cref="GitHubLatestVersionResolver"/> for the same two-hop concern.
    /// </remarks>
    private static (string DownloadUrl, string ChecksumsUrl) BuildDownloadUrls(string platformName, string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return ($"{GithubReleasesUrl}/latest/download/{platformName}.zip",
                    $"{GithubReleasesUrl}/latest/download/SHASUMS256.txt");
        }

        return ($"{GithubReleasesUrl}/download/bun-v{version}/{platformName}.zip",
                $"{GithubReleasesUrl}/download/bun-v{version}/SHASUMS256.txt");
    }

    /// <summary>
    /// Downloads and extracts the Bun runtime archive.
    /// </summary>
    private async Task DownloadAndExtractAsync(string downloadUrl, string checksumsUrl, string stagedExecutablePath, string platformName, string executableName)
    {
        // ResponseHeadersRead avoids buffering the whole ~60-94 MB archive in memory before it can be
        // streamed to disk, which the default ResponseContentRead would do.
        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        await ExtractResponseToStagedExecutableAsync(response, downloadUrl, checksumsUrl, stagedExecutablePath, platformName, executableName);
    }

    /// <summary>
    /// Resolves the concrete version behind "latest" via <see cref="_latestVersionResolver"/> and downloads
    /// it only if it differs from what is already cached.
    /// </summary>
    private async Task<string> ResolveAndDownloadLatestAsync(
        string latestUrl,
        string latestChecksumsUrl,
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
        // following) client resolve "latest" itself; the checksums URL falls back the same way, which
        // means (rarely, only when resolution fails) it is re-resolved independently of the zip's own
        // "latest" redirect and could theoretically land on a different release cut in between the two
        // requests. Deriving it from the zip response instead is not an option - see BuildDownloadUrls.
        var (downloadUrl, checksumsUrl) = resolvedVersion is not null
            ? BuildDownloadUrls(platformName, resolvedVersion)
            : (latestUrl, latestChecksumsUrl);

        if (resolvedVersion is not null)
        {
            _log.LogMessage($"Bun 'latest' resolved to {resolvedVersion}.");
        }

        await DownloadAndExtractAsync(downloadUrl, checksumsUrl, stagedExecutablePath, platformName, executableName);
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
    private async Task ExtractResponseToStagedExecutableAsync(HttpResponseMessage response, string downloadUrl, string checksumsUrl, string stagedExecutablePath, string platformName, string executableName)
    {
        EnsureSuccessOrThrow(response, downloadUrl);

        // Download to temporary file
        var tempDir = Path.GetTempPath();
        var tempZipPath = Path.Combine(tempDir, $"bun-{Guid.NewGuid()}.zip");

        // Ensure temp directory exists (important for MockFileSystem)
        _fileSystem.Directory.CreateDirectory(tempDir);

        try
        {
            // Hashed in the same pass as the write, via CryptoStream, rather than reading the ~60-94 MB
            // archive back off disk afterward just to hash it. That second full read used to double the
            // disk I/O for every download.
            string actualHash;
            using (var sha256 = SHA256.Create())
            {
                using (var fileStream = _fileSystem.File.Create(tempZipPath))
                using (var hashingStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write))
                {
                    await response.Content.CopyToAsync(hashingStream);
                    hashingStream.FlushFinalBlock();
                }

                actualHash = BitConverter.ToString(sha256.Hash!).Replace("-", "");
            }

            // Verify against upstream's published SHA-256 sums before touching the archive. Bun publishes
            // SHASUMS256.txt alongside every release; checking it catches corrupt or mismatched archive
            // downloads before they are extracted into a consumer build.
            await VerifyChecksumAsync(actualHash, checksumsUrl, platformName);

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

    /// <summary>
    /// Verifies an already-computed archive hash against the SHA-256 sum GitHub publishes alongside each
    /// Bun release.
    /// </summary>
    private async Task VerifyChecksumAsync(string actualHash, string checksumsUrl, string platformName)
    {
        var archiveName = $"{platformName}.zip";
        string? expectedHash = null;
        try
        {
            using var response = await _httpClient.GetAsync(checksumsUrl, HttpCompletionOption.ResponseHeadersRead);
            EnsureSuccessOrThrow(response, checksumsUrl);

            using var checksumsStream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(checksumsStream);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[parts.Length - 1].Equals(archiveName, StringComparison.Ordinal))
                {
                    expectedHash = parts[0];
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidDataException($"Failed to download checksums from {checksumsUrl}.", ex);
        }

        if (expectedHash is null)
        {
            throw new InvalidDataException($"No checksum entry for '{archiveName}' in {checksumsUrl}.");
        }

        if (!expectedHash.Equals(actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Checksum mismatch for '{archiveName}'. Expected {expectedHash}, got {actualHash}.");
        }
    }

    internal string PublishStagedExecutable(string stagedExecutablePath, string bunExecutablePath)
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
                // Another concurrent caller already published a file at this path - e.g. a mutex abandoned
                // by a crashed process, or two hosts sharing a network path where the named mutex (which is
                // process/session scoped) does not reach across machines.
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
