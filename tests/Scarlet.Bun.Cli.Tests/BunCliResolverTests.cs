using System.IO.Abstractions.TestingHelpers;
using Scarlet.Bun.Cli.Tests.Mock;

namespace Scarlet.Bun.Cli.Tests;

public class BunCliResolverTests
{
    private const string ToolDirectory = "/tool";
    private const string CacheRoot = "/cache";

    [Fact]
    public void Resolve_WithEmbeddedBinary_ShouldUseItWithoutDownloading()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var embedded = Path.Combine(ToolDirectory, "bun");
        fileSystem.AddFile(embedded, new MockFileData("bun"));

        // Act
        var resolution = Resolve(fileSystem, out _);

        // Assert
        Assert.Equal(BunSource.Embedded, resolution.Source);
        Assert.Equal(embedded, resolution.ExecutablePath);
    }

    [Fact]
    public void Resolve_WithEmbeddedBinary_ShouldMakeItExecutable()
    {
        // Arrange - NuGet packages carry no Unix permission bits, so without this the first run on Linux or
        // macOS fails with EACCES
        var fileSystem = new MockFileSystem();
        var embedded = Path.Combine(ToolDirectory, "bun");
        fileSystem.AddFile(embedded, new MockFileData("bun"));

        // Act
        Resolve(fileSystem, out var chmod);

        // Assert
        Assert.Equal([embedded], chmod.Paths);
    }

    [Fact]
    public void Resolve_WithCachedBinary_ShouldUseItWithoutDownloading()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var options = CreateOptions();
        var cached = BunRuntimeResolver.GetExecutablePath(options.RuntimeDirectory, Platform.LinuxX64);
        fileSystem.AddFile(cached, new MockFileData("bun"));
        fileSystem.AddFile(BunDownloader.GetVersionMarkerPath(cached), new MockFileData("1.4.2"));

        // Act
        var resolution = Resolve(fileSystem, out _, options);

        // Assert
        Assert.Equal(BunSource.Cache, resolution.Source);
        Assert.Equal(cached, resolution.ExecutablePath);
    }

    [Fact]
    public void Resolve_WithCachedExecutableButNoVersionMarkerAndDownloadsDisabled_ShouldNotReportItAsUsable()
    {
        // Arrange - --scarlet-info must not tell the user an incompletely published cache entry is ready.
        var fileSystem = new MockFileSystem();
        var options = CreateOptions();
        var cached = BunRuntimeResolver.GetExecutablePath(options.RuntimeDirectory, Platform.LinuxX64);
        fileSystem.AddFile(cached, new MockFileData("partially published"));

        // Act
        var resolution = Resolve(fileSystem, out _, options, allowDownload: false);

        // Assert
        Assert.Equal(BunSource.NotFound, resolution.Source);
    }

    [Fact]
    public void Resolve_WithExplicitPath_ShouldWinOverTheEmbeddedBinary()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(ToolDirectory, "bun"), new MockFileData("embedded"));
        fileSystem.AddFile("/elsewhere/bun", new MockFileData("explicit"));

        var options = CreateOptions(new Dictionary<string, string>
        {
            [BunCliOptions.CacheVariable] = CacheRoot,
            [BunCliOptions.PathVariable] = "/elsewhere/bun"
        });

        // Act
        var resolution = Resolve(fileSystem, out _, options);

        // Assert
        Assert.Equal(BunSource.Explicit, resolution.Source);
        Assert.Equal("/elsewhere/bun", resolution.ExecutablePath);
    }

    [Fact]
    public void Resolve_WithExplicitPathThatDoesNotExist_ShouldFailRatherThanFallBack()
    {
        // Arrange - an explicit instruction that silently did something else would be worse than an error
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(ToolDirectory, "bun"), new MockFileData("embedded"));

        var options = CreateOptions(new Dictionary<string, string>
        {
            [BunCliOptions.CacheVariable] = CacheRoot,
            [BunCliOptions.PathVariable] = "/missing/bun"
        });

        // Act
        var resolution = Resolve(fileSystem, out _, options);

        // Assert
        Assert.False(resolution.IsResolved);
        Assert.Contains(BunCliOptions.PathVariable, resolution.FailureReason);
    }

    [Fact]
    public void Resolve_WithNoEmbeddedOptOut_ShouldSkipTheEmbeddedBinary()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(ToolDirectory, "bun"), new MockFileData("embedded"));

        var options = CreateOptions(new Dictionary<string, string>
        {
            [BunCliOptions.CacheVariable] = CacheRoot,
            [BunCliOptions.NoEmbeddedVariable] = "1"
        });

        // Act
        var resolution = Resolve(fileSystem, out _, options, allowDownload: false);

        // Assert
        Assert.NotEqual(BunSource.Embedded, resolution.Source);
    }

    [Fact]
    public void Resolve_WhenADifferentVersionIsRequested_ShouldNotUseTheEmbeddedBinary()
    {
        // Arrange - the embedded binary IS the pinned version, so honouring a different request means
        // bypassing it rather than silently ignoring the request
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(ToolDirectory, "bun"), new MockFileData("embedded"));

        var options = CreateOptions(new Dictionary<string, string>
        {
            [BunCliOptions.CacheVariable] = CacheRoot,
            [BunCliOptions.VersionVariable] = "1.0.0"
        });

        // Act
        var resolution = Resolve(fileSystem, out _, options, allowDownload: false);

        // Assert
        Assert.NotEqual(BunSource.Embedded, resolution.Source);
        Assert.Equal("1.0.0", resolution.RequestedVersion);
    }

    [Fact]
    public void Resolve_WithDownloadDisallowed_ShouldNeverConstructTheDownloader()
    {
        // Arrange - diagnostics must be able to report what would happen without fetching 90 MB
        var fileSystem = new MockFileSystem();

        // Act
        var resolution = Resolve(fileSystem, out _, allowDownload: false);

        // Assert
        Assert.False(resolution.IsResolved);
        Assert.Equal(BunSource.NotFound, resolution.Source);
    }

    [Fact]
    public void Resolve_ShouldScopeTheRuntimeDirectoryByVersion()
    {
        // Arrange - each requested CLI version gets a distinct cache directory so old and new tool versions
        // can coexist without sharing downloaded runtimes.
        var first = CreateOptions(new Dictionary<string, string>
        {
            [BunCliOptions.CacheVariable] = CacheRoot,
            [BunCliOptions.VersionVariable] = "1.2.3"
        });
        var second = CreateOptions(new Dictionary<string, string>
        {
            [BunCliOptions.CacheVariable] = CacheRoot,
            [BunCliOptions.VersionVariable] = "4.5.6"
        });

        // Act & Assert
        Assert.NotEqual(first.RuntimeDirectory, second.RuntimeDirectory);
        Assert.Contains("1.2.3", first.RuntimeDirectory);
        Assert.Contains("4.5.6", second.RuntimeDirectory);
    }

    private static BunCliOptions CreateOptions(IDictionary<string, string>? variables = null)
    {
        variables ??= new Dictionary<string, string> { [BunCliOptions.CacheVariable] = CacheRoot };

        return BunCliOptions.FromEnvironment(new FakeEnvironmentProvider(variables), BunBuildInfo.PinnedBunVersion);
    }

    private static BunResolution Resolve(
        MockFileSystem fileSystem,
        out RecordingChmodProvider chmod,
        BunCliOptions? options = null,
        bool allowDownload = true)
    {
        chmod = new RecordingChmodProvider();

        var resolver = new BunCliResolver(
            fileSystem,
            chmod,
            Platform.LinuxX64,
            ToolDirectory,
            (_, _) => throw new InvalidOperationException("The downloader must not be used in this test."));

        return resolver.Resolve(options ?? CreateOptions(), allowDownload, new NoOpLogger());
    }

    private sealed class NoOpLogger : IBunLogger
    {
        public void LogMessage(string message)
        {
        }
    }
}
