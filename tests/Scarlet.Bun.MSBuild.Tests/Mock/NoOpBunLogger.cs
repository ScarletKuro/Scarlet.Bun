namespace Scarlet.Bun.MSBuild.Tests.Mock;

internal sealed class NoOpBunLogger : IBunLogger
{
    public void LogMessage(string message) { }
}
