using System.Text.Json;
using Scarlet.Bun.Cli.Tests.Mock;

namespace Scarlet.Bun.Cli.Tests;

/// <summary>
/// Covers what <c>--scarlet-info</c> prints for each way Bun can be resolved.
/// </summary>
/// <remarks>
/// This is the output people paste into issues, and <c>tests/e2e/cli-tool/verify.sh</c> greps it to prove
/// nothing was downloaded, so the wording is a contract rather than decoration.
/// </remarks>
public class DiagnosticsReportTests
{
    // BunSource is internal, so the source is named rather than passed: an internal type cannot appear in
    // the signature of a public xunit test method.
    [Theory]
    [InlineData(nameof(BunSource.Embedded), "embedded")]
    [InlineData(nameof(BunSource.Cache), "cache")]
    [InlineData(nameof(BunSource.Downloaded), "downloaded")]
    [InlineData(nameof(BunSource.Explicit), "explicit")]
    [InlineData(nameof(BunSource.NotFound), "not found")]
    public void ToText_ShouldDescribeEverySource(string sourceName, string expected)
    {
        // Arrange
        var resolution = CreateResolution(Enum.Parse<BunSource>(sourceName));

        // Act
        var report = DiagnosticsReport.ToText(resolution, CreateOptions());

        // Assert
        Assert.Contains($"Source", report);
        Assert.Contains(expected, report);
    }

    [Fact]
    public void ToText_WhenResolved_ShouldNotOfferADownloadUrl()
    {
        // Act
        var report = DiagnosticsReport.ToText(CreateResolution(BunSource.Embedded), CreateOptions());

        // Assert
        Assert.Contains("(not needed)", report);
        Assert.DoesNotContain("https://github.com/oven-sh/bun/releases", report);
    }

    [Fact]
    public void ToText_WhenNotResolved_ShouldNameTheExactArchiveItWouldFetch()
    {
        // Arrange
        var resolution = CreateResolution(BunSource.NotFound, executablePath: null, version: "1.4.2");

        // Act
        var report = DiagnosticsReport.ToText(resolution, CreateOptions());

        // Assert
        Assert.Contains("https://github.com/oven-sh/bun/releases/download/bun-v1.4.2/bun-linux-x64-baseline.zip", report);
    }

    [Fact]
    public void ToText_WhenLatestIsRequested_ShouldPointAtTheLatestRelease()
    {
        // Arrange
        var resolution = CreateResolution(BunSource.NotFound, executablePath: null, version: BunCliOptions.LatestVersion);

        // Act
        var report = DiagnosticsReport.ToText(resolution, CreateOptions());

        // Assert
        Assert.Contains("releases/latest/download/bun-linux-x64-baseline.zip", report);
    }

    [Fact]
    public void ToText_ShouldReportWhichEnvironmentVariablesAreSet()
    {
        // Arrange - the pinned version is 1.4.2, so setting it here proves the line echoes the override
        // rather than falling back to the pin
        var options = CreateOptions(new Dictionary<string, string>
        {
            [BunCliOptions.VersionVariable] = "1.2.3",
            [BunCliOptions.CacheVariable] = "/cache",
            [BunCliOptions.NoEmbeddedVariable] = "1",
            [BunCliOptions.PassthroughVariable] = "1",
            [BunCliOptions.DiagnosticsVariable] = "1",
            [BunCliOptions.DownloadTimeoutVariable] = "42"
        });

        // Act
        var report = DiagnosticsReport.ToText(CreateResolution(BunSource.Embedded), options);

        // Assert
        Assert.Contains($"{BunCliOptions.VersionVariable,-28}1.2.3", report);
        Assert.Contains($"{BunCliOptions.NoEmbeddedVariable,-28}enabled", report);
        Assert.Contains($"{BunCliOptions.PassthroughVariable,-28}enabled", report);
        Assert.Contains($"{BunCliOptions.DiagnosticsVariable,-28}enabled", report);
        Assert.Contains($"{BunCliOptions.CacheVariable,-28}/cache", report);
        Assert.Contains($"{BunCliOptions.DownloadTimeoutVariable,-28}42", report);
    }

    [Fact]
    public void ToText_WithNothingSet_ShouldSayUnset()
    {
        // Arrange - a genuinely empty environment, rather than CreateOptions()'s default, which pins
        // SCARLET_BUN_CACHE so unrelated tests don't depend on the host's filesystem layout
        var options = BunCliOptions.FromEnvironment(new FakeEnvironmentProvider(), "1.4.2");

        // Act
        var report = DiagnosticsReport.ToText(CreateResolution(BunSource.Embedded), options);

        // Assert
        Assert.Contains($"{BunCliOptions.NoEmbeddedVariable,-28}(unset)", report);
        Assert.Contains($"{BunCliOptions.PathVariable,-28}(unset)", report);
        Assert.Contains($"{BunCliOptions.VersionVariable,-28}(unset)", report);
        Assert.Contains($"{BunCliOptions.CacheVariable,-28}(unset)", report);
        Assert.Contains($"{BunCliOptions.DownloadTimeoutVariable,-28}(unset)", report);
    }

    [Theory]
    [InlineData("", "Scarlet.Bun.Cli (portable)")]
    [InlineData("win-x64", "Scarlet.Bun.Cli.win-x64")]
    [InlineData("linux-arm64", "Scarlet.Bun.Cli.linux-arm64")]
    public void DescribePackage_ShouldNameThePackageThisBuildCameFrom(string rid, string expected)
    {
        // Act & Assert - the portable package reports itself differently, and only one of the two can ever
        // be reached from a test build, which is why the identifier is a parameter
        Assert.Equal(expected, DiagnosticsReport.DescribePackage(rid));
    }

    [Fact]
    public void ToJson_ShouldEmitTheSameFactsAsParseableJson()
    {
        // Act
        var json = DiagnosticsReport.ToJson(CreateResolution(BunSource.Embedded), CreateOptions());

        // Assert
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("embedded", root.GetProperty("source").GetString());
        Assert.Equal("linux-x64", root.GetProperty("runtimeIdentifier").GetString());
        Assert.Equal("/tool/bun", root.GetProperty("bunExecutable").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("downloadUrl").ValueKind);
    }

    [Fact]
    public void ToJson_WhenNotResolved_ShouldCarryTheDownloadUrlAndFailureReason()
    {
        // Arrange
        var resolution = CreateResolution(BunSource.NotFound, executablePath: null, failureReason: "nothing yet");

        // Act
        var json = DiagnosticsReport.ToJson(resolution, CreateOptions());

        // Assert
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("bunExecutable").ValueKind);
        Assert.Contains("bun-v1.4.2", root.GetProperty("downloadUrl").GetString()!);
        Assert.Equal("nothing yet", root.GetProperty("failureReason").GetString());
    }

    private static BunResolution CreateResolution(
        BunSource source,
        string? executablePath = "/tool/bun",
        string version = "1.4.2",
        string? failureReason = null)
    {
        return new BunResolution(
            executablePath,
            source,
            Platform.LinuxX64,
            "linux-x64",
            version,
            "/cache",
            $"/cache/runtimes/{version}",
            "/tool/bun",
            failureReason);
    }

    private static BunCliOptions CreateOptions(IDictionary<string, string>? variables = null)
    {
        variables ??= new Dictionary<string, string> { [BunCliOptions.CacheVariable] = "/cache" };

        return BunCliOptions.FromEnvironment(new FakeEnvironmentProvider(variables), "1.4.2");
    }
}
