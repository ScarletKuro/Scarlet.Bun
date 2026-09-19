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