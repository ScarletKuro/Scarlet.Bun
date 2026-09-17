using System.IO.Abstractions.TestingHelpers;
using RichardSzalay.MockHttp;
using Scarlet.Bun.Cli.Tests.Mock;

namespace Scarlet.Bun.Cli.Tests;

/// <summary>
/// Covers the resolver's download path, which is the portable <c>any</c> package's only route to a Bun.
/// </summary>
/// <remarks>
/// Driven through a real <c>BunDownloader</c> over a mocked transport rather than a stubbed one, because
/// what is worth checking is that the resolver hands it the right directory and version - and the
/// directory is version-scoped precisely so a later version request is not served the earlier binary.
/// </remarks>
public class BunCliDownloadTests
{
    private const string ToolDirectory = "/tool";
    private const string CacheRoot = "/cache";

    [Fact]
    public void Resolve_WithNothingCached_ShouldDownloadAndReportItAsDownloaded()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        handler.When("https://github.com/oven-sh/bun/releases/download/bun-v1.4.2/*")
            .Respond("application/zip", new MemoryStream([1, 2, 3]));

        // Act
        var resolution = Resolve(fileSystem, handler, version: "1.4.2");

        // Assert
        Assert.Equal(BunSource.Downloaded, resolution.Source);
        Assert.Equal(
            BunRuntimeResolver.GetExecutablePath("/cache/runtimes/1.4.2", Platform.LinuxX64),
            resolution.ExecutablePath);
        Assert.True(fileSystem.File.Exists(resolution.ExecutablePath!));
    }

    [Fact]
    public void Resolve_ShouldRequestTheVersionScopedDirectory()
    {
        // Arrange - the regression guard for BunDownloader caching on file existence alone: two versions
        // must not share a directory, or the second request would silently be served the first binary
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        handler.When("https://github.com/oven-sh/bun/releases/download/bun-v1.3.6/*")
            .Respond("application/zip", new MemoryStream([1, 2, 3]));

        // Act
        var resolution = Resolve(fileSystem, handler, version: "1.3.6");

        // Assert
        Assert.Contains("1.3.6", resolution.RuntimeDirectory);
        Assert.DoesNotContain("1.4.2", resolution.RuntimeDirectory);
        Assert.Equal(BunSource.Downloaded, resolution.Source);
    }

    [Fact]
    public void Resolve_WithLatestRequested_ShouldAskForTheLatestRelease()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        // Expect, not When: this asserts the URL shape rather than merely tolerating it
        handler.Expect("https://github.com/oven-sh/bun/releases/latest/download/bun-linux-x64-baseline.zip")
            .Respond("application/zip", new MemoryStream([1, 2, 3]));

        // Act
        var resolution = Resolve(fileSystem, handler, version: BunCliOptions.LatestVersion);

        // Assert
        handler.VerifyNoOutstandingExpectation();
        Assert.Equal(BunSource.Downloaded, resolution.Source);
    }

    [Fact]
    public void Resolve_WhenTheDownloadFails_ShouldSurfaceTheFailure()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        handler.When("https://github.com/oven-sh/bun/releases/download/*")
            .Respond(System.Net.HttpStatusCode.NotFound);

        // Act & Assert - the application turns this into exit code 127 with the message attached
        var exception = Assert.ThrowsAny<Exception>(() => Resolve(fileSystem, handler, version: "9.9.9"));
        Assert.Contains("9.9.9", exception.Message);
    }

    private static BunResolution Resolve(MockFileSystem fileSystem, MockHttpMessageHandler handler, string version)
    {
        var options = BunCliOptions.FromEnvironment(
            new FakeEnvironmentProvider(new Dictionary<string, string>
            {
                [BunCliOptions.CacheVariable] = CacheRoot,
                [BunCliOptions.VersionVariable] = version
            }),
            BunBuildInfo.PinnedBunVersion);

        var resolver = new BunCliResolver(
            fileSystem,
            NoOpChmodProvider.Instance,
            Platform.LinuxX64,
            ToolDirectory,
            (platform, log) => new BunDownloader(
                new HttpClient(handler),
                fileSystem,
                new FakeZipArchiveProvider(fileSystem),
                NoOpChmodProvider.Instance,
                platform,
                log));

        return resolver.Resolve(options, allowDownload: true, new RecordingBunLogger());
    }

    private sealed class RecordingBunLogger : IBunLogger
    {
        public void LogMessage(string message)
        {
        }
    }
}
