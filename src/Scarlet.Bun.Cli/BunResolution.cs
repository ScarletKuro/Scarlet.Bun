using Scarlet.Bun.Core;

namespace Scarlet.Bun.Cli;

/// <summary>
/// Where the Bun executable that is about to run came from.
/// </summary>
internal enum BunSource
{
    /// <summary>No Bun executable could be resolved.</summary>
    NotFound,

    /// <summary>Supplied by the user through <c>SCARLET_BUN_PATH</c>.</summary>
    Explicit,

    /// <summary>Shipped inside this package. No network was involved, ever.</summary>
    Embedded,

    /// <summary>Found in the per-user download cache from an earlier run.</summary>
    Cache,

    /// <summary>Downloaded from GitHub during this run.</summary>
    Downloaded
}

/// <summary>
/// The outcome of resolving a Bun executable, including the context needed to explain it.
/// </summary>
/// <param name="ExecutablePath">The resolved executable, or <see langword="null"/> when none was found.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="Platform">The detected host platform.</param>
/// <param name="RuntimeIdentifier">The runtime identifier for <paramref name="Platform"/>.</param>
/// <param name="RequestedVersion">The version that was asked for.</param>
/// <param name="CacheRoot">The per-user cache root in effect.</param>
/// <param name="RuntimeDirectory">The version-scoped directory downloads go to.</param>
/// <param name="EmbeddedProbePath">Where an embedded binary would have been, for diagnostics.</param>
/// <param name="FailureReason">Why resolution failed, when it did.</param>
internal sealed record BunResolution(
    string? ExecutablePath,
    BunSource Source,
    Platform Platform,
    string RuntimeIdentifier,
    string RequestedVersion,
    string CacheRoot,
    string RuntimeDirectory,
    string EmbeddedProbePath,
    string? FailureReason)
{
    /// <summary>Whether a usable Bun executable was resolved.</summary>
    public bool IsResolved => ExecutablePath is not null;
}
