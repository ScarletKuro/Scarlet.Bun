using System;
using System.IO;
using System.Threading;
using Scarlet.Bun.Core;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// The cross-process lock behind <see cref="BunRunTask.RunExclusively"/>: runs that share a directory take turns.
/// Kept apart from the task so both the naming rules and the locking can be tested without MSBuild.
/// </summary>
internal sealed class BunRunLock : IDisposable
{
    private readonly Mutex _mutex;

    private BunRunLock(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>
    /// Blocks until no other run in <paramref name="directory"/> holds the lock.
    /// </summary>
    /// <param name="directory">The directory to lock, as returned by <see cref="ResolveDirectory"/>.</param>
    /// <param name="onWait">Called when another run holds the lock, before waiting for it.</param>
    internal static BunRunLock Acquire(string directory, Action onWait)
    {
        var mutex = new Mutex(false, CreateMutexName(directory));

        try
        {
            if (!mutex.WaitOne(0))
            {
                onWait();

                // No timeout of its own: the holder is a Bun process bounded by its own TimeoutMilliseconds, and a
                // holder that dies abandons the mutex rather than keeping it.
                mutex.WaitOne();
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died mid-run. We own the mutex now; whatever it left half-written is what the
            // up-to-date check inside the task is there to catch.
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        return new BunRunLock(mutex);
    }

    /// <summary>
    /// Releases the lock. Thread-affine, like the download mutex: dispose it on the thread that acquired it, which
    /// the task does because its run blocks rather than awaiting.
    /// </summary>
    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    /// <summary>
    /// The directory Bun runs in, which is what concurrent runs actually collide on.
    /// </summary>
    internal static string ResolveDirectory(string? workingDirectory, string? projectDirectory)
    {
        var directory = !string.IsNullOrWhiteSpace(workingDirectory) ? workingDirectory!
            : !string.IsNullOrWhiteSpace(projectDirectory) ? projectDirectory!
            : Directory.GetCurrentDirectory();

        return Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static string CreateMutexName(string directory)
    {
        // Upper-cased so that "C:\App" and "c:\app" share a lock on Windows. On a case-sensitive file system two
        // directories differing only in case then share one too, which costs at most some parallelism.
        var hashString = HashUtilities.ComputeSha256Hex(directory.ToUpperInvariant()).ToUpperInvariant();
        return $"Global\\ScarletBun_Run_{hashString}";
    }
}
