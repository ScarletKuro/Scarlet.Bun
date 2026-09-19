using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Scarlet.Bun.MSBuild.IntegrationTests;

/// <summary>
/// Verifies <see cref="GitHubLatestVersionResolver"/> against the real GitHub redirect chain. The
/// resolver's unit tests (in Scarlet.Bun.MSBuild.Tests) fake this response shape; this test exists
/// specifically to confirm the fake still matches reality.
/// </summary>
public class GitHubLatestVersionResolverIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public GitHubLatestVersionResolverIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TryResolveVersionAsync_AgainstRealGitHub_ShouldResolveAConcreteVersion()
    {
        var platform = BunRuntimeResolver.GetCurrentPlatform();
        var platformName = BunRuntimeResolver.GetDownloadName(platform);
        var latestUrl = $"https://github.com/oven-sh/bun/releases/latest/download/{platformName}.zip";

        var resolver = new GitHubLatestVersionResolver();

        var resolvedVersion = await resolver.TryResolveVersionAsync(latestUrl);

        _output.WriteLine($"Resolved '{latestUrl}' to version: {resolvedVersion}");

        Assert.NotNull(resolvedVersion);
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+"), resolvedVersion);
    }
}
