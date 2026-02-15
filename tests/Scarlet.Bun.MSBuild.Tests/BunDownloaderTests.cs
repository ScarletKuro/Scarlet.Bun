using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using RichardSzalay.MockHttp;
using Scarlet.Bun.MSBuild.Providers;
using Scarlet.Bun.MSBuild.Tests.Mock;

namespace Scarlet.Bun.MSBuild.Tests;

public class BunDownloaderTests
{
    [Fact]
    public async Task DownloadRuntimeAsync_WithNullRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), BunRuntimeResolver.GetCurrentPlatform());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadRuntimeAsync(null!));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithEmptyRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), BunRuntimeResolver.GetCurrentPlatform());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadRuntimeAsync(string.Empty));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithWhitespaceRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), BunRuntimeResolver.GetCurrentPlatform());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadRuntimeAsync("   "));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithValidDirectory_ShouldReturnExecutablePath()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Create a mock zip file with the Bun executable
        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When("https://github.com/oven-sh/bun/releases/latest/download/bun-linux-x64-baseline.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithSpecificVersion_ShouldDownloadThatVersion()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var version = "1.3.6";
        var platform = Platform.LinuxX64;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Create a mock zip file with the Bun executable
        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/bun/releases/download/bun-v{version}/bun-linux-x64-baseline.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir, version);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
    }

    [Fact]
    public async Task DownloadRuntimeAsync_CalledTwice_ShouldReuseExistingRuntime()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var version = "1.3.6";
        var platform = Platform.LinuxX64;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Track request count
        var requestCount = 0;

        // Create a mock zip file with the Bun executable
        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/bun/releases/download/bun-v{version}/bun-linux-x64-baseline.zip")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(zipContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                    return Task.FromResult(response);
                });

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform);

        // Act - first download
        var result1 = await downloader.DownloadRuntimeAsync(tempDir, version);

        // Verify file was created
        Assert.True(mockFileSystem.File.Exists(result1));

        // Act - second download (should reuse without downloading)
        var result2 = await downloader.DownloadRuntimeAsync(tempDir, version);

        // Assert
        Assert.Equal(result1, result2);

        // Verify HTTP was called only once (file was reused on second call)
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithInvalidVersion_ShouldThrowException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var invalidVersion = "invalid_version";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Mock a 404 response for invalid version
        mockHttp.When($"https://github.com/oven-sh/bun/releases/download/bun-v{invalidVersion}/bun-linux-x64-baseline.zip")
                .Respond(System.Net.HttpStatusCode.NotFound);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            downloader.DownloadRuntimeAsync(tempDir, invalidVersion));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenArchiveDoesNotContainExecutable_ShouldThrowInvalidDataException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Content is irrelevant for this test because the ZIP provider is faked.
        var zipContent = CreateMockBunZip("bun");
        mockHttp.When("https://github.com/oven-sh/bun/releases/latest/download/bun-linux-x64-baseline.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new MissingExecutableZipArchiveProvider(), new NoOpChmodProvider());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadRuntimeAsync(tempDir, platform: platform));

        Assert.Contains("did not contain expected executable", ex.Message);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "bun.exe")]
    [InlineData(Platform.LinuxX64, "bun")]
    [InlineData(Platform.LinuxArm64, "bun")]
    [InlineData(Platform.MacOsX64, "bun")]
    [InlineData(Platform.MacOsArm64, "bun")]
    public async Task DownloadRuntimeAsync_ForAllPlatforms_ShouldDownloadCorrectExecutable(Platform platform, string expectedExecutable)
    {
        // Arrange
        var tempDir = "/test-runtime";
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Create a mock zip file with the Bun executable
        var zipContent = CreateMockBunZip(executableName);
        var platformName = GetPlatformDownloadName(platform);
        mockHttp.When($"https://github.com/oven-sh/bun/releases/latest/download/{platformName}.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result));
        Assert.EndsWith(expectedExecutable, result);
    }

    /// <summary>
    /// Creates a mock ZIP file containing a Bun executable.
    /// </summary>
    private static MemoryStream CreateMockBunZip(string executableName)
    {
        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // Create entry with platform folder structure (e.g., "bun-linux-x64-baseline/bun")
            var entry = archive.CreateEntry($"bun-linux-x64-baseline/{executableName}");
            using var entryStream = entry.Open();
            var content = "mock bun executable"u8.ToArray();
            entryStream.Write(content, 0, content.Length);
        }
        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Gets the platform-specific download archive name (same logic as in BunDownloader).
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

    private sealed class MissingExecutableZipArchiveProvider : IZipArchiveProvider
    {
        public ZipArchive OpenRead(string archiveFileName)
        {
            var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("bun-linux-x64-baseline/not-bun");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("not bun");
            }

            stream.Position = 0;
            return new ZipArchive(stream, ZipArchiveMode.Read);
        }

        public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite)
        {
            source.ExtractToFile(destinationFileName, overwrite);
        }
    }
}
