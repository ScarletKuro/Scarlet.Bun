using System;
using System.IO;
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
    /// <returns>Path to the downloaded Bun executable.</returns>
    public static async Task<string> DownloadRuntimeAsync(string runtimeDirectory, string? version = null, Platform? platform = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            throw new ArgumentException("Runtime directory must be specified when using BunRuntimeDownload", nameof(runtimeDirectory));
        }

        var targetPlatform = platform ?? BunRuntimeResolver.GetCurrentPlatform();
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(targetPlatform);
        var platformName = GetPlatformDownloadName(targetPlatform);
        var executableName = BunRuntimeResolver.GetExecutableName(targetPlatform);
        
        // Create the full runtime path: runtimeDirectory/runtimeId/native
        var fullRuntimePath = Path.Combine(runtimeDirectory, runtimeId, "native");
        var bunExecutablePath = Path.Combine(fullRuntimePath, executableName);
        
        // Check if runtime already exists
        if (File.Exists(bunExecutablePath))
        {
            // Verify it's executable on Unix
            if (targetPlatform != Platform.WindowsX64)
            {
                EnsureExecutablePermissions(bunExecutablePath);
            }
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
        Directory.CreateDirectory(fullRuntimePath);
        await DownloadAndExtractAsync(downloadUrl, fullRuntimePath, platformName, executableName);
        
        // Ensure executable permissions on Unix
        if (targetPlatform != Platform.WindowsX64)
        {
            EnsureExecutablePermissions(bunExecutablePath);
        }
        
        return bunExecutablePath;
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
    private static async Task DownloadAndExtractAsync(string downloadUrl, string extractPath, string platformName, string executableName)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        };
        using var httpClient = new HttpClient(handler);
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Scarlet.Bun.MSBuild");
        
        // Download to temporary file
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"bun-{Guid.NewGuid()}.zip");
        try
        {
            var response = await httpClient.GetAsync(downloadUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = $"Failed to download Bun runtime from {downloadUrl}. Status: {response.StatusCode}";
                throw new HttpRequestException(error);
            }
            
            using (var fileStream = File.Create(tempZipPath))
            {
                await response.Content.CopyToAsync(fileStream);
            }
            
            // Extract the zip file
            // The zip contains a folder like "bun-windows-x64-baseline/bun.exe"
            // We need to extract just the executable to our target path
            using var archive = ZipFile.OpenRead(tempZipPath);
            foreach (var entry in archive.Entries)
            {
                // Look for the bun executable in the archive
                if (entry.Name.Equals(executableName, StringComparison.OrdinalIgnoreCase))
                {
                    var destinationPath = Path.Combine(extractPath, executableName);
                    entry.ExtractToFile(destinationPath, overwrite: true);
                    break;
                }
            }
        }
        finally
        {
            // Clean up temporary file
            if (File.Exists(tempZipPath))
            {
                try
                {
                    File.Delete(tempZipPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    /// <summary>
    /// Ensures the file has executable permissions on Unix systems.
    /// </summary>
    private static void EnsureExecutablePermissions(string filePath)
    {
        try
        {
            var chmodProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            });
            
            if (chmodProcess != null)
            {
                chmodProcess.WaitForExit();
            }
        }
        catch
        {
            // Ignore errors - permissions might already be set
        }
    }
}
