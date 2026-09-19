using System.Diagnostics;
using System.Xml.Linq;

namespace Scarlet.Bun.MSBuild.IntegrationTests;

/// <summary>
/// Asserts that the Bun binary staged for this platform really is the version the repository claims.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a bug that shipped silently: <c>tools/download-bun.ps1</c> and
/// <c>download-bun.sh</c> used to extract the archive into the project directory and then search that same
/// directory for the executable. The search found the <em>existing</em> binary before the freshly extracted
/// one, concluded it was already in place, skipped the move - and still wrote the version marker. Every
/// version bump after the first download therefore kept the old binary and relabelled it, so
/// <c>Scarlet.Bun.Runtime.* 1.4.2</c> would have shipped Bun 1.3.14.
/// </para>
/// <para>
/// Nothing else catches that: the marker file agrees with itself, and a binary for another platform cannot
/// be executed to check. Only the host platform's binary can be asked, which is why this runs per CI leg.
/// </para>
/// </remarks>
public class BunBinaryVersionTests
{
    [Fact]
    public void StagedBunBinary_ShouldReportTheVersionTheRepositoryPinned()
    {
        // Arrange
        var expectedVersion = ReadPinnedBunVersion();
        var platform = BunRuntimeResolver.GetCurrentPlatform();
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        var bunPath = BunRuntimeResolver.GetExecutablePath(runtimesDirectory, platform);

        Assert.True(
            File.Exists(bunPath),
            $"No Bun binary staged for {platform} at '{bunPath}'. Build Scarlet.Bun.MSBuild first.");

        // Act
        var reportedVersion = RunBun(bunPath, "--version");

        // Assert
        Assert.Equal(expectedVersion, reportedVersion);
    }

    private static string ReadPinnedBunVersion()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Directory.Build.props");

            if (File.Exists(candidate))
            {
                var root = XDocument.Load(candidate).Root;
                Assert.NotNull(root);

                return Assert.Single(root.Descendants("BunVersion")).Value.Trim();
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate Directory.Build.props above '{AppContext.BaseDirectory}'.");
    }

    private static string RunBun(string bunPath, string argument)
    {
        var startInfo = new ProcessStartInfo(bunPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"'{bunPath} {argument}' exited with {process.ExitCode}.");

        return output.Trim();
    }
}
