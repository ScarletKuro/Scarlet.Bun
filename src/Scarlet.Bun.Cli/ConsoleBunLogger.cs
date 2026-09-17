using Scarlet.Bun.Core;

namespace Scarlet.Bun.Cli;

/// <summary>
/// Writes the downloader's progress messages to stderr.
/// </summary>
/// <remarks>
/// stderr rather than stdout on purpose: <c>dotnet bun ... | jq</c> has to keep working, so nothing this
/// tool says may ever appear in Bun's output stream.
/// </remarks>
internal sealed class ConsoleBunLogger : IBunLogger
{
    private readonly TextWriter _stderr;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleBunLogger"/> class.
    /// </summary>
    /// <param name="stderr">The stream to write to.</param>
    public ConsoleBunLogger(TextWriter stderr) => _stderr = stderr;

    /// <inheritdoc />
    public void LogMessage(string message) => _stderr.WriteLine($"Scarlet.Bun: {message}");
}
