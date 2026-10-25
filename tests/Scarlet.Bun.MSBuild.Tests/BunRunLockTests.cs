namespace Scarlet.Bun.MSBuild.Tests;

/// <summary>
/// <c>BunRunTask.RunExclusively</c> only works if every build that runs Bun in a directory arrives at the same
/// mutex name, however that directory was spelled - and builds in different directories never share one.
/// </summary>
public class BunRunLockTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "scarlet-bun-lock");

    [Fact]
    public void ResolveDirectory_PrefersWorkingDirectory()
    {
        Assert.Equal(
            Path.Combine(Root, "client"),
            BunRunLock.ResolveDirectory(Path.Combine(Root, "client"), Path.Combine(Root, "project")));
    }

    [Fact]
    public void ResolveDirectory_FallsBackToProjectDirectory()
    {
        Assert.Equal(Path.Combine(Root, "project"), BunRunLock.ResolveDirectory(" ", Path.Combine(Root, "project")));
    }

    [Fact]
    public void ResolveDirectory_FallsBackToCurrentDirectory()
    {
        var expected = Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        Assert.Equal(expected, BunRunLock.ResolveDirectory(null, null));
    }

    [Fact]
    public void MutexName_IgnoresTrailingSeparatorAndRelativeSegments()
    {
        var plain = MutexNameFor(Path.Combine(Root, "project"));

        Assert.Equal(plain, MutexNameFor(Path.Combine(Root, "project") + Path.DirectorySeparatorChar));
        Assert.Equal(plain, MutexNameFor(Path.Combine(Root, "other", "..", "project")));
    }

    [Fact]
    public void MutexName_IgnoresCase()
    {
        // Windows paths are case-insensitive, so two builds can spell the same directory differently.
        Assert.Equal(MutexNameFor(Path.Combine(Root, "Project")), MutexNameFor(Path.Combine(Root, "project")));
    }

    [Fact]
    public void MutexName_DiffersBetweenDirectories()
    {
        Assert.NotEqual(MutexNameFor(Path.Combine(Root, "a")), MutexNameFor(Path.Combine(Root, "b")));
    }

    [Fact]
    public void MutexName_DoesNotCollideWithTheDownloadMutex()
    {
        // Both are keyed on a hashed path. If a run's name could equal the download's, a step running in the
        // runtime directory would deadlock against the download it is waiting on.
        var directory = Path.Combine(Root, "runtimes");

        Assert.NotEqual(BunDownloader.CreateMutexName(directory), MutexNameFor(directory));
    }

    private static string MutexNameFor(string workingDirectory) =>
        BunRunLock.CreateMutexName(BunRunLock.ResolveDirectory(workingDirectory, projectDirectory: null));
}
