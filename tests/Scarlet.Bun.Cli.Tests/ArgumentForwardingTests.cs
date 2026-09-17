using System.IO.Abstractions.TestingHelpers;
using Scarlet.Bun.Cli.Tests.Mock;

namespace Scarlet.Bun.Cli.Tests;

/// <summary>
/// The contract that matters most: every argument reaches Bun unchanged, in order.
/// </summary>
/// <remarks>
/// <c>dotnet bun --version</c> has to print Bun's version, not the tool's, and a flag Bun adds tomorrow has
/// to work without a release of this package. Anything that parses, reorders, trims or re-quotes arguments
/// breaks that.
/// </remarks>
public class ArgumentForwardingTests
{
    public static TheoryData<string[]> Arguments => new()
    {
        Array.Empty<string>(),
        new[] { "--version" },
        new[] { "--help" },
        new[] { "install" },
        new[] { "run", "build.mjs" },
        new[] { "-e", "console.log('a b')" },
        new[] { "run", "x", "--", "--watch" },
        new[] { "--" },
        new[] { "--define", "X=\"y\"" },
        new[] { "a b" },
        new[] { string.Empty },
        new[] { "trailing\\" },
        new[] { "日本語" },
        new[] { "--flag=va lue", "second", string.Empty, "-" }
    };

    [Theory]
    [MemberData(nameof(Arguments))]
    public void Run_ShouldForwardEveryArgumentVerbatim(string[] args)
    {
        // Arrange
        var launcher = new RecordingProcessLauncher();
        var application = CreateApplication(launcher, out _);

        // Act
        application.Run(args);

        // Assert
        Assert.Equal(args, launcher.ReceivedArguments);
    }

    [Fact]
    public void Run_ShouldReturnBunsExitCodeUnchanged()
    {
        // Arrange - 130 is the shell's "terminated by SIGINT"; it must survive as-is
        foreach (var exitCode in new[] { 0, 1, 2, 3, 130, 255 })
        {
            var application = CreateApplication(new RecordingProcessLauncher(exitCode), out _);

            // Act
            var result = application.Run(new[] { "run", "build.mjs" });

            // Assert
            Assert.Equal(exitCode, result);
        }
    }

    [Fact]
    public void Run_ShouldPassTheResolvedExecutableToTheLauncher()
    {
        // Arrange
        var launcher = new RecordingProcessLauncher();
        var application = CreateApplication(launcher, out var embeddedPath);

        // Act
        application.Run(new[] { "--version" });

        // Assert
        Assert.NotNull(launcher.Received);
        Assert.Equal(embeddedPath, launcher.Received!.Value.ExecutablePath);
    }

    [Fact]
    public void Run_WhenNothingCanBeResolved_ShouldReportAndNotLaunch()
    {
        // Arrange - no embedded binary, and a downloader that would fail the test if it were used
        var fileSystem = new MockFileSystem();
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [BunCliOptions.CacheVariable] = "/cache"
        });
        var launcher = new RecordingProcessLauncher();
        var stderr = new StringWriter();

        var resolver = new BunCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            "/tool",
            (_, _) => throw new InvalidOperationException("boom"));

        var application = new BunCliApplication(
            resolver,
            launcher,
            BunCliOptions.FromEnvironment(environment, BunBuildInfo.PinnedBunVersion),
            new StringWriter(),
            stderr);

        // Act
        var result = application.Run(new[] { "--version" });

        // Assert
        Assert.Equal(127, result);
        Assert.Null(launcher.Received);
        Assert.Contains("Scarlet.Bun:", stderr.ToString());
    }

    private static BunCliApplication CreateApplication(IProcessLauncher launcher, out string embeddedPath)
    {
        const string toolDirectory = "/tool";
        embeddedPath = Path.Combine(toolDirectory, "bun");

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(embeddedPath, new MockFileData("fake bun"));

        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [BunCliOptions.CacheVariable] = "/cache"
        });

        var resolver = new BunCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            toolDirectory,
            (_, _) => throw new InvalidOperationException("The embedded binary must be used without downloading."));

        return new BunCliApplication(
            resolver,
            launcher,
            BunCliOptions.FromEnvironment(environment, BunBuildInfo.PinnedBunVersion),
            new StringWriter(),
            new StringWriter());
    }
}
