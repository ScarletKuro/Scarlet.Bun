using System.IO.Abstractions.TestingHelpers;
using Scarlet.Bun.Cli.Tests.Mock;

namespace Scarlet.Bun.Cli.Tests;

/// <summary>
/// The tool reserves exactly one token. These tests pin down how little it takes.
/// </summary>
public class ReservedFlagTests
{
    [Fact]
    public void Run_WithInfoFlagFirst_ShouldReportInsteadOfLaunching()
    {
        // Arrange
        var launcher = new RecordingProcessLauncher();
        var stdout = new StringWriter();
        var application = Create(launcher, stdout);

        // Act
        var result = application.Run([BunCliApplication.InfoFlag]);

        // Assert
        Assert.Equal(0, result);
        Assert.Null(launcher.Received);
        Assert.Contains("Pinned Bun version", stdout.ToString());
    }

    public static TheoryData<string[]> InfoFlagNotFirst =>
    [
        ["run", "--scarlet-info"],
        ["--version", "--scarlet-info"],
        ["run", "build.mjs", "--", "--scarlet-info"]
    ];

    [Theory]
    [MemberData(nameof(InfoFlagNotFirst))]
    public void Run_WithInfoFlagAnywhereButFirst_ShouldForwardItToBun(string[] args)
    {
        // Arrange - restricting recognition to position 0 keeps the reserved surface as small as possible
        var launcher = new RecordingProcessLauncher();
        var application = Create(launcher, new StringWriter());

        // Act
        application.Run(args);

        // Assert
        Assert.Equal(args, launcher.ReceivedArguments);
    }

    [Fact]
    public void Run_WithPassthroughEnabled_ShouldForwardTheInfoFlagEvenWhenFirst()
    {
        // Arrange - the permanent escape hatch: absolute passthrough, no reserved tokens at all
        var launcher = new RecordingProcessLauncher();
        var application = Create(
            launcher,
            new StringWriter(),
            new Dictionary<string, string>
            {
                [BunCliOptions.CacheVariable] = "/cache",
                [BunCliOptions.PassthroughVariable] = "1"
            });

        var args = new[] { BunCliApplication.InfoFlag, "extra" };

        // Act
        application.Run(args);

        // Assert
        Assert.Equal(args, launcher.ReceivedArguments);
    }

    [Fact]
    public void Run_WithInfoFlagAndJson_ShouldEmitParseableJson()
    {
        // Arrange
        var stdout = new StringWriter();
        var application = Create(new RecordingProcessLauncher(), stdout);

        // Act
        var result = application.Run([BunCliApplication.InfoFlag, "--json"]);

        // Assert
        Assert.Equal(0, result);
        using var document = System.Text.Json.JsonDocument.Parse(stdout.ToString());
        Assert.Equal("embedded", document.RootElement.GetProperty("source").GetString());
        Assert.Equal(BunBuildInfo.PinnedBunVersion, document.RootElement.GetProperty("pinnedBunVersion").GetString());
    }

    [Fact]
    public void Run_WithUnknownOptionAfterInfoFlag_ShouldFailWithAUsageError()
    {
        // Arrange
        var stderr = new StringWriter();
        var application = Create(new RecordingProcessLauncher(), new StringWriter(), stderr: stderr);

        // Act
        var result = application.Run([BunCliApplication.InfoFlag, "--nope"]);

        // Assert
        Assert.Equal(64, result);
        Assert.Contains("--nope", stderr.ToString());
    }

    [Fact]
    public void Run_WithInfoFlag_ShouldNotDownload()
    {
        // Arrange - asking the tool what it would do must never itself fetch 90 MB
        var fileSystem = new MockFileSystem();
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [BunCliOptions.CacheVariable] = "/cache"
        });

        var resolver = new BunCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            "/tool",
            (_, _) => throw new InvalidOperationException("Diagnostics must not download."));

        var stdout = new StringWriter();
        var application = new BunCliApplication(
            resolver,
            new RecordingProcessLauncher(),
            BunCliOptions.FromEnvironment(environment, BunBuildInfo.PinnedBunVersion),
            stdout,
            new StringWriter());

        // Act
        var result = application.Run([BunCliApplication.InfoFlag]);

        // Assert - it reports the URL it *would* use rather than fetching it
        Assert.Equal(0, result);
        Assert.Contains("https://github.com/oven-sh/bun/releases", stdout.ToString());
    }

    private static BunCliApplication Create(
        IProcessLauncher launcher,
        TextWriter stdout,
        IDictionary<string, string>? variables = null,
        TextWriter? stderr = null)
    {
        const string toolDirectory = "/tool";

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(toolDirectory, "bun"), new MockFileData("bun"));

        variables ??= new Dictionary<string, string> { [BunCliOptions.CacheVariable] = "/cache" };

        var resolver = new BunCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            toolDirectory,
            (_, _) => throw new InvalidOperationException("The downloader must not be used in this test."));

        return new BunCliApplication(
            resolver,
            launcher,
            BunCliOptions.FromEnvironment(new FakeEnvironmentProvider(variables), BunBuildInfo.PinnedBunVersion),
            stdout,
            stderr ?? new StringWriter());
    }
}
