namespace Scarlet.Bun.MSBuild.Tests;

public class BunDownloaderTests
{
    [Fact]
    public async Task DownloadRuntimeAsync_WithNullRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => BunDownloader.DownloadRuntimeAsync(null!));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithEmptyRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => BunDownloader.DownloadRuntimeAsync(string.Empty));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithWhitespaceRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => BunDownloader.DownloadRuntimeAsync("   "));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithValidDirectory_ShouldReturnExecutablePath()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"bun-test-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var platform = BunRuntimeResolver.GetCurrentPlatform();
            var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
            var executableName = BunRuntimeResolver.GetExecutableName(platform);
            var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

            // Act - download latest version (this is a real download, might take time)
            var result = await BunDownloader.DownloadRuntimeAsync(tempDir);

            // Assert
            Assert.Equal(expectedPath, result);
            Assert.True(File.Exists(result), $"Expected executable to exist at {result}");

            // Verify it's executable on Unix
            if (platform != Platform.WindowsX64)
            {
                var fileInfo = new FileInfo(result);
                Assert.True(fileInfo.Exists);
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithSpecificVersion_ShouldDownloadThatVersion()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"bun-test-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var version = "1.3.6"; // Use a known stable version
            var platform = BunRuntimeResolver.GetCurrentPlatform();
            var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
            var executableName = BunRuntimeResolver.GetExecutableName(platform);
            var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

            // Act
            var result = await BunDownloader.DownloadRuntimeAsync(tempDir, version);

            // Assert
            Assert.Equal(expectedPath, result);
            Assert.True(File.Exists(result), $"Expected executable to exist at {result}");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    [Fact]
    public async Task DownloadRuntimeAsync_CalledTwice_ShouldReuseExistingRuntime()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"bun-test-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var version = "1.3.6";

            // Act - first download
            var result1 = await BunDownloader.DownloadRuntimeAsync(tempDir, version);
            var fileInfo1 = new FileInfo(result1);
            var firstWriteTime = fileInfo1.LastWriteTimeUtc;

            // Wait a bit to ensure timestamps would be different if file was rewritten
            await Task.Delay(100);

            // Act - second download (should reuse)
            var result2 = await BunDownloader.DownloadRuntimeAsync(tempDir, version);
            var fileInfo2 = new FileInfo(result2);
            var secondWriteTime = fileInfo2.LastWriteTimeUtc;

            // Assert
            Assert.Equal(result1, result2);
            Assert.Equal(firstWriteTime, secondWriteTime); // File was not rewritten
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithInvalidVersion_ShouldThrowException()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"bun-test-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var invalidVersion = "invalid_version"; // This version doesn't exist

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() => BunDownloader.DownloadRuntimeAsync(tempDir, invalidVersion));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
