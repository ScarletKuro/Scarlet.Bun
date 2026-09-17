namespace Scarlet.Bun.Cli;

/// <summary>
/// A request to run Bun.
/// </summary>
/// <param name="ExecutablePath">The Bun executable to start.</param>
/// <param name="Arguments">The arguments, forwarded verbatim and in order.</param>
internal readonly record struct BunLaunchRequest(string ExecutablePath, IReadOnlyList<string> Arguments);

/// <summary>
/// Runs Bun and returns its exit code.
/// </summary>
/// <remarks>
/// The seam that keeps everything else unit-testable: tests substitute a recorder and assert the exact
/// argument list that would have been handed to Bun, without starting a process.
/// </remarks>
internal interface IProcessLauncher
{
    /// <summary>
    /// Runs the request to completion.
    /// </summary>
    /// <param name="request">What to run.</param>
    /// <returns>Bun's exit code.</returns>
    int Run(BunLaunchRequest request);
}
