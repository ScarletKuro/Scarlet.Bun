namespace Scarlet.Bun.Cli;

/// <summary>
/// A request to run Bun.
/// </summary>
/// <param name="ExecutablePath">The Bun executable to start.</param>
/// <param name="Arguments">The arguments, forwarded verbatim and in order.</param>
internal readonly record struct BunLaunchRequest(string ExecutablePath, IReadOnlyList<string> Arguments);