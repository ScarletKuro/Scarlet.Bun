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
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), BunRuntimeResolver.GetCurrentPlatform(), new NoOpBunLogger());

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
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), BunRuntimeResolver.GetCurrentPlatform(), new NoOpBunLogger());

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
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), BunRuntimeResolver.GetCurrentPlatform(), new NoOpBunLogger());

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
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform, new NoOpBunLogger());

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
        Assert.False(mockFileSystem.File.Exists(expectedPath + ".complete"));
        Assert.Equal(
            new[] { Path.GetFullPath(result) },
            mockFileSystem.Directory.GetFiles(Path.Combine(tempDir, runtimeId, "native")));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithSpecificVersion_ShouldDownloadThatVersion()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var version = "1.3.12";
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
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform, new NoOpBunLogger());

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
        var version = "1.3.12";
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
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform, new NoOpBunLogger());

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
    public async Task DownloadRuntimeAsync_ShouldKeepFinalExecutableHiddenUntilPublished()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When("https://github.com/oven-sh/bun/releases/latest/download/bun-linux-x64-baseline.zip")
                .Respond("application/zip", zipContent);

        var chmodProvider = new BlockingChmodProvider();
        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), chmodProvider, platform, new NoOpBunLogger());

        var downloadTask = downloader.DownloadRuntimeAsync(tempDir);

        Assert.True(chmodProvider.WaitForFirstCall(TimeSpan.FromSeconds(5)));
        Assert.NotNull(chmodProvider.LastPath);
        Assert.NotEqual(expectedPath, chmodProvider.LastPath);
        Assert.True(mockFileSystem.File.Exists(chmodProvider.LastPath!), "Expected staged executable to exist while publication is blocked");
        Assert.False(mockFileSystem.File.Exists(expectedPath), "Final executable should not be visible before publication");

        chmodProvider.Release();

        var result = await downloadTask;

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(expectedPath));
        Assert.False(mockFileSystem.File.Exists(chmodProvider.LastPath!));
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
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform, new NoOpBunLogger());

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
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeMissingExecutableZipArchiveProvider(), new NoOpChmodProvider(), platform, new NoOpBunLogger());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadRuntimeAsync(tempDir));

        Assert.Contains("did not contain expected executable", ex.Message);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenExtractionDoesNotCreateFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Content is irrelevant for this test because the ZIP provider is faked.
        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When("https://github.com/oven-sh/bun/releases/latest/download/bun-linux-x64-baseline.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeNoWriteZipArchiveProvider(), new NoOpChmodProvider(), platform, new NoOpBunLogger());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            downloader.DownloadRuntimeAsync(tempDir));

        Assert.Contains("not found after extraction", ex.Message);
        Assert.Contains(expectedPath, ex.Message);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenPublicationFails_ShouldCleanUpStagedExecutable()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var nativeDirectory = Path.Combine(tempDir, runtimeId, "native");
        var expectedPath = Path.Combine(nativeDirectory, executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When("https://github.com/oven-sh/bun/releases/latest/download/bun-linux-x64-baseline.zip")
                .Respond("application/zip", zipContent);

        var chmodProvider = new ThrowingChmodProvider();
        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), chmodProvider, platform, new NoOpBunLogger());

        await Assert.ThrowsAsync<IOException>(() => downloader.DownloadRuntimeAsync(tempDir));

        Assert.False(mockFileSystem.File.Exists(expectedPath));
        Assert.Empty(mockFileSystem.Directory.GetFiles(nativeDirectory));
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "bun.exe")]
    [InlineData(Platform.WindowsArm64, "bun.exe")]
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
        var downloadName = BunRuntimeResolver.GetDownloadName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Create a mock zip file with the Bun executable
        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/bun/releases/latest/download/{downloadName}.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform, new NoOpBunLogger());

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result));
        Assert.EndsWith(expectedExecutable, result);
    }

    #region DownloadRuntime (synchronous, mutex-protected)

    [Fact]
    public void DownloadRuntime_WithNullRuntimeDirectory_ShouldThrowArgumentException()
    {
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), BunRuntimeResolver.GetCurrentPlatform(), new NoOpBunLogger());

        Assert.Throws<ArgumentException>(() =>
            downloader.DownloadRuntime(null!));
    }

    [Fact]
    public void DownloadRuntime_WithEmptyRuntimeDirectory_ShouldThrowArgumentException()
    {
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), BunRuntimeResolver.GetCurrentPlatform(), new NoOpBunLogger());

        Assert.Throws<ArgumentException>(() =>
            downloader.DownloadRuntime(string.Empty));
    }

    [Fact]
    public void DownloadRuntime_WithWhitespaceRuntimeDirectory_ShouldThrowArgumentException()
    {
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), BunRuntimeResolver.GetCurrentPlatform(), new NoOpBunLogger());

        Assert.Throws<ArgumentException>(() =>
            downloader.DownloadRuntime("   "));
    }

    [Fact]
    public void DownloadRuntime_WithValidDirectory_ShouldReturnExecutablePath()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When("https://github.com/oven-sh/bun/releases/latest/download/bun-linux-x64-baseline.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform, new NoOpBunLogger());

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
        Assert.False(mockFileSystem.File.Exists(expectedPath + ".complete"));
        Assert.Equal(
            new[] { Path.GetFullPath(result) },
            mockFileSystem.Directory.GetFiles(Path.Combine(tempDir, runtimeId, "native")));
    }

    [Fact]
    public void DownloadRuntime_WithSpecificVersion_ShouldDownloadThatVersion()
    {
        var tempDir = "/test-runtime";
        var version = "1.3.12";
        var platform = Platform.LinuxX64;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/bun/releases/download/bun-v{version}/bun-linux-x64-baseline.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform, new NoOpBunLogger());

        var result = downloader.DownloadRuntime(tempDir, version);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
    }

    [Fact]
    public void DownloadRuntime_CalledTwice_ShouldReuseExistingRuntime()
    {
        var tempDir = "/test-runtime";
        var version = "1.3.12";
        var platform = Platform.LinuxX64;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var requestCount = 0;

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
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform, new NoOpBunLogger());

        var result1 = downloader.DownloadRuntime(tempDir, version);
        Assert.True(mockFileSystem.File.Exists(result1));

        var result2 = downloader.DownloadRuntime(tempDir, version);

        Assert.Equal(result1, result2);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public void DownloadRuntime_WhenArchiveDoesNotContainExecutable_ShouldThrowInvalidDataException()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockBunZip("bun");
        mockHttp.When("https://github.com/oven-sh/bun/releases/latest/download/bun-linux-x64-baseline.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeMissingExecutableZipArchiveProvider(), new NoOpChmodProvider(), platform, new NoOpBunLogger());

        var ex = Assert.Throws<InvalidDataException>(() =>
            downloader.DownloadRuntime(tempDir));

        Assert.Contains("did not contain expected executable", ex.Message);
    }

    [Fact]
    public void DownloadRuntime_WhenExtractionDoesNotCreateFile_ShouldThrowFileNotFoundException()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When("https://github.com/oven-sh/bun/releases/latest/download/bun-linux-x64-baseline.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeNoWriteZipArchiveProvider(), new NoOpChmodProvider(), platform, new NoOpBunLogger());

        var ex = Assert.Throws<FileNotFoundException>(() =>
            downloader.DownloadRuntime(tempDir));

        Assert.Contains("not found after extraction", ex.Message);
        Assert.Contains(expectedPath, ex.Message);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "bun.exe")]
    [InlineData(Platform.WindowsArm64, "bun.exe")]
    [InlineData(Platform.LinuxX64, "bun")]
    [InlineData(Platform.LinuxArm64, "bun")]
    [InlineData(Platform.MacOsX64, "bun")]
    [InlineData(Platform.MacOsArm64, "bun")]
    public void DownloadRuntime_ForAllPlatforms_ShouldDownloadCorrectExecutable(Platform platform, string expectedExecutable)
    {
        var tempDir = "/test-runtime";
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var downloadName = BunRuntimeResolver.GetDownloadName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockBunZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/bun/releases/latest/download/{downloadName}.zip")
                .Respond("application/zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new BunDownloader(httpClient, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), new NoOpChmodProvider(), platform, new NoOpBunLogger());

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result));
        Assert.EndsWith(expectedExecutable, result);
    }

    #endregion

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

    private sealed class BlockingChmodProvider : IChmodProvider
    {
        private readonly ManualResetEventSlim _entered = new(initialState: false);
        private readonly ManualResetEventSlim _release = new(initialState: false);

        public string? LastPath { get; private set; }

        public void EnsureExecutablePermissions(string filePath)
        {
            LastPath = filePath;
            _entered.Set();
            _release.Wait(TimeSpan.FromSeconds(5));
        }

        public void Release() => _release.Set();

        public bool WaitForFirstCall(TimeSpan timeout) => _entered.Wait(timeout);
    }

    private sealed class ThrowingChmodProvider : IChmodProvider
    {
        public void EnsureExecutablePermissions(string filePath)
        {
            throw new IOException($"chmod failed for {filePath}");
        }
    }
}
