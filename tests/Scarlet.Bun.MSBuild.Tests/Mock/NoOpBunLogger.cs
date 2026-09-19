namespace Scarlet.Bun.MSBuild.Tests.Mock;

internal sealed class NoOpBunLogger : IBunLogger
{
    public void LogMessage(string message) { }

    public static NoOpBunLogger Instance { get; } = new NoOpBunLogger();
}
