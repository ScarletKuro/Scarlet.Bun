using Scarlet.Bun.Cli.Tests.Mock;

namespace Scarlet.Bun.Cli.Tests;

public class BunCliOptionsTests
{
    [Fact]
    public void FromEnvironment_WithNothingSet_ShouldUseThePinnedVersion()
    {
        // Act
        var options = BunCliOptions.FromEnvironment(new FakeEnvironmentProvider(), "1.4.2");

        // Assert
        Assert.Equal("1.4.2", options.RequestedVersion);
        Assert.False(options.UseLatest);
        Assert.Equal("1.4.2", options.DownloadVersion);
        Assert.Null(options.ExplicitBunPath);
        Assert.False(options.IgnoreEmbedded);
        Assert.False(options.PurePassthrough);
        Assert.Equal(300, options.DownloadTimeoutSeconds);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("LATEST")]
    public void FromEnvironment_WithLatest_ShouldAskTheDownloaderForTheLatestRelease(string requested)
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [BunCliOptions.VersionVariable] = requested
        });

        // Act
        var options = BunCliOptions.FromEnvironment(environment, "1.4.2");

        // Assert
        Assert.True(options.UseLatest);
        Assert.Null(options.DownloadVersion);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("", false)]
    public void FromEnvironment_ShouldReadBooleanVariablesForgivingly(string value, bool expected)
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [BunCliOptions.NoEmbeddedVariable] = value
        });

        // Act
        var options = BunCliOptions.FromEnvironment(environment, "1.4.2");

        // Assert
        Assert.Equal(expected, options.IgnoreEmbedded);
    }

    [Theory]
    [InlineData("600", 600)]
    [InlineData("not-a-number", 300)]
    [InlineData("0", 300)]
    [InlineData("-5", 300)]
    public void FromEnvironment_ShouldFallBackToTheDefaultTimeoutOnNonsense(string value, int expected)
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [BunCliOptions.DownloadTimeoutVariable] = value
        });

        // Act
        var options = BunCliOptions.FromEnvironment(environment, "1.4.2");

        // Assert
        Assert.Equal(expected, options.DownloadTimeoutSeconds);
    }

    [Fact]
    public void ResolveCacheRoot_OnWindows_ShouldUseLocalApplicationData()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(
            isWindows: true,
            home: @"C:\Users\tester",
            folders: new Dictionary<Environment.SpecialFolder, string>
            {
                [Environment.SpecialFolder.LocalApplicationData] = @"C:\Users\tester\AppData\Local"
            });

        // Act
        var root = BunCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine(@"C:\Users\tester\AppData\Local", "ScarletKuro", "Scarlet.Bun"), root);
    }

    [Fact]
    public void ResolveCacheRoot_OnMacOs_ShouldUseLibraryCaches()
    {
        // Arrange - .NET maps LocalApplicationData to ~/.local/share on macOS, which is not where a macOS
        // user expects a cache, so this path is written out explicitly
        var environment = new FakeEnvironmentProvider(home: "/Users/tester", isMacOs: true);

        // Act
        var root = BunCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine("/Users/tester", "Library", "Caches", "ScarletKuro", "Scarlet.Bun"), root);
    }

    [Fact]
    public void ResolveCacheRoot_OnLinuxWithXdg_ShouldHonourXdgCacheHome()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(
            new Dictionary<string, string> { ["XDG_CACHE_HOME"] = "/xdg" },
            home: "/home/tester");

        // Act
        var root = BunCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine("/xdg", "ScarletKuro", "Scarlet.Bun"), root);
    }

    [Fact]
    public void ResolveCacheRoot_OnLinuxWithoutXdg_ShouldFallBackToDotCache()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(home: "/home/tester");

        // Act
        var root = BunCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine("/home/tester", ".cache", "ScarletKuro", "Scarlet.Bun"), root);
    }

    [Fact]
    public void ResolveCacheRoot_WithNoHome_ShouldFallBackToTemp()
    {
        // Arrange - containers frequently run without HOME; failing on a path we could not build would be
        // worse than using temp
        var environment = new FakeEnvironmentProvider(home: null, tempDirectory: "/tmp");

        // Act
        var root = BunCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal(Path.Combine("/tmp", "ScarletKuro", "Scarlet.Bun"), root);
    }

    [Fact]
    public void ResolveCacheRoot_WithOverride_ShouldWinEverywhere()
    {
        // Arrange
        var environment = new FakeEnvironmentProvider(
            new Dictionary<string, string> { [BunCliOptions.CacheVariable] = "/explicit" },
            home: "/home/tester",
            isWindows: true);

        // Act
        var root = BunCliOptions.ResolveCacheRoot(environment);

        // Assert
        Assert.Equal("/explicit", root);
    }
}
