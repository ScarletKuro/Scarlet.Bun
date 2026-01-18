using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// Handles downloading Bun runtimes from GitHub releases.
/// </summary>
public static class BunDownloader
{
    private const string GithubReleasesUrl = "https://github.com/oven-sh/bun/releases";
    
    /// <summary>
    /// Downloads the Bun runtime for the current platform.
    /// </summary>
    /// <param name="runtimeDirectory">Directory where the runtime should be downloaded.</param>
    /// <param name="version">Specific version to download (e.g., "1.3.6"). If null or empty, downloads latest.</param>
    /// <param name="platform">Target platform. If null, uses current platform.</param>
    /// <param name="httpClient">HttpClient for making requests. If null, creates a new instance.</param>
    /// <param name="fileSystem">File system abstraction for testability. If null, uses the real file system.</param>
    /// <returns>Path to the downloaded Bun executable.</returns>
    public static async Task<string> DownloadRuntimeAsync(
        string runtimeDirectory, 
        string? version = null, 
        Platform? platform = null,
        HttpClient? httpClient = null,
        IFileSystem? fileSystem = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            throw new ArgumentException("Runtime directory must be specified when using BunRuntimeDownload", nameof(runtimeDirectory));
        }

        // Use real file system if none provided
        fileSystem ??= new FileSystem();
        
        // Use provided HttpClient or create disposable one
        bool disposeClient = httpClient == null;
        httpClient ??= CreateHttpClient();
        
        try
        {
            var targetPlatform = platform ?? BunRuntimeResolver.GetCurrentPlatform();
            var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(targetPlatform);
            var platformName = GetPlatformDownloadName(targetPlatform);
            var executableName = BunRuntimeResolver.GetExecutableName(targetPlatform);
            
            // Create the full runtime path: runtimeDirectory/runtimeId/native
            var fullRuntimePath = Path.Combine(runtimeDirectory, runtimeId, "native");
            var bunExecutablePath = Path.Combine(fullRuntimePath, executableName);
            
            // Check if runtime already exists
            if (fileSystem.File.Exists(bunExecutablePath))
            {
                // Verify it's executable on Unix
                Chmod.EnsureExecutablePermissions(bunExecutablePath);
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
            fileSystem.Directory.CreateDirectory(fullRuntimePath);
            await DownloadAndExtractAsync(downloadUrl, fullRuntimePath, platformName, executableName, httpClient, fileSystem);

            Chmod.EnsureExecutablePermissions(bunExecutablePath);

            return bunExecutablePath;
        }
        finally
        {
            if (disposeClient)
            {
                httpClient?.Dispose();
            }
        }
    }

    /// <summary>
    /// Creates an HttpClient configured for downloading Bun runtimes.
    /// </summary>
    private static HttpClient CreateHttpClient()
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
    /// Gets the platform-specific download archive name.
    /// </summary>
    private static string GetPlatformDownloadName(Platform platform)
    {
        return platform switch
        {
            Platform.WindowsX64 => "bun-windows-x64-baseline",
            Platform.LinuxX64 => "bun-linux-x64-baseline",
            Platform.LinuxArm64 => "bun-linux-aarch64",
            Platform.MacOsX64 => "bun-darwin-x64-baseline",
            Platform.MacOsArm64 => "bun-darwin-aarch64",
            _ => throw new ArgumentException($"Unknown platform: {platform}", nameof(platform))
        };
    }

    /// <summary>
    /// Downloads and extracts the Bun runtime archive.
    /// </summary>
    private static async Task DownloadAndExtractAsync(string downloadUrl, string extractPath, string platformName, string executableName, HttpClient httpClient, IFileSystem fileSystem)
    {
        // Download to temporary file
        var tempDir = Path.GetTempPath();
        var tempZipPath = Path.Combine(tempDir, $"bun-{Guid.NewGuid()}.zip");
        
        // Ensure temp directory exists (important for MockFileSystem)
        fileSystem.Directory.CreateDirectory(tempDir);
        
        try
        {
            var response = await httpClient.GetAsync(downloadUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = $"Failed to download Bun runtime from {downloadUrl}. Status: {response.StatusCode}";
                throw new HttpRequestException(error);
            }
            
            // Read ZIP content into memory stream for extraction
            using var zipStream = new MemoryStream();
            await response.Content.CopyToAsync(zipStream);
            zipStream.Position = 0;
            
            // Extract the zip file from memory
            // The zip contains a folder like "bun-windows-x64-baseline/bun.exe"
            // We need to extract just the executable to our target path
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                // Look for the bun executable in the archive
                if (entry.Name.Equals(executableName, StringComparison.OrdinalIgnoreCase))
                {
                    var destinationPath = Path.Combine(extractPath, executableName);
                    using var entryStream = entry.Open();
                    using var destinationStream = fileSystem.File.Create(destinationPath);
                    await entryStream.CopyToAsync(destinationStream);
                    break;
                }
            }
        }
        finally
        {
            // No temp file cleanup needed since we use memory stream
        }
    }
}
