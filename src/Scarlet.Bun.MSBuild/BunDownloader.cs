using System;
using System.IO;
using System.IO.Abstractions;
using System.Net.Http;
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

    public BunDownloader(HttpClient httpClient, IFileSystem fileSystem, IZipArchiveProvider zipProvider, IChmodProvider chmodProvider, Platform platform)
    {
        _platform = platform;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _zipProvider = zipProvider ?? throw new ArgumentNullException(nameof(zipProvider));
        _chmodProvider = chmodProvider ?? throw new ArgumentNullException(nameof(chmodProvider));
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
        var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Add("User-Agent", "Scarlet.Bun.MSBuild");
        return client;
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
            foreach (var entry in archive.Entries)
            {
                // Look for the bun executable in the archive
                if (entry.Name.Equals(executableName, StringComparison.OrdinalIgnoreCase))
                {
                    var destinationPath = Path.Combine(extractPath, executableName);
                    _zipProvider.ExtractToFile(entry, destinationPath, overwrite: true);
                    break;
                }
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
