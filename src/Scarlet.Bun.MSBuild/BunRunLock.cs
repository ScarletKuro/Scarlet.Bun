using System.IO;
using Scarlet.Bun.Core;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// Names the cross-process lock behind <see cref="BunRunTask.RunExclusively"/>. Kept apart from the task so the
/// naming rules can be tested without MSBuild.
/// </summary>
internal static class BunRunLock
{
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
