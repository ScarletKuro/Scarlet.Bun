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

    [Fact]
    public void Acquire_WhileAnotherRunHoldsTheLock_WaitsForIt()
    {
        var directory = NewDirectory();
        var waited = false;
        var acquired = new ManualResetEventSlim();

        using (BunRunLock.Acquire(directory, () => Assert.Fail("Nothing else holds this lock yet.")))
        {
            var contender = new Thread(() =>
            {
                using (BunRunLock.Acquire(directory, () => waited = true))
                {
                    acquired.Set();
                }
            });
            contender.Start();

            Assert.False(acquired.Wait(TimeSpan.FromMilliseconds(300)), "The second run got the lock while the first held it.");
            Assert.True(waited);
        }

        Assert.True(acquired.Wait(TimeSpan.FromSeconds(10)), "The second run never got the lock after it was released.");
    }

    [Fact]
    public void Acquire_AfterAHolderDiedWithoutReleasing_TakesTheLock()
    {
        // A build process that crashes mid-run abandons the mutex rather than releasing it.
        var directory = NewDirectory();
        using var held = new Mutex(false, BunRunLock.CreateMutexName(directory));
        var holder = new Thread(() => held.WaitOne());
        holder.Start();
        holder.Join();

        using (BunRunLock.Acquire(directory, () => { }))
        {
            // Owning it is the point: the next run must not wait on a holder that will never return.
        }

        using var next = BunRunLock.Acquire(directory, () => Assert.Fail("The lock was not released after the abandoned owner."));
    }

    [Fact]
    public void Acquire_WhenWaitingFails_PropagatesTheErrorAndHoldsNothing()
    {
        var directory = NewDirectory();
        var holding = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using (BunRunLock.Acquire(directory, () => { }))
            {
                holding.Set();
                release.Wait();
            }
        });
        holder.Start();
        holding.Wait();

        var failure = Assert.Throws<InvalidOperationException>(() =>
            BunRunLock.Acquire(directory, () => throw new InvalidOperationException("logging failed")));
        Assert.Equal("logging failed", failure.Message);

        release.Set();
        holder.Join();

        // The failed attempt never owned the mutex, so nothing is left held once the holder lets go.
        using var next = BunRunLock.Acquire(directory, () => Assert.Fail("A failed attempt left the lock held."));
    }

    private static string NewDirectory() =>
        BunRunLock.ResolveDirectory(Path.Combine(Root, Guid.NewGuid().ToString("N")), projectDirectory: null);

    private static string MutexNameFor(string workingDirectory) =>
        BunRunLock.CreateMutexName(BunRunLock.ResolveDirectory(workingDirectory, projectDirectory: null));
}
