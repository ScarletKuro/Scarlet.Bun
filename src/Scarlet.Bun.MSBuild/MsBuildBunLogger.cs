using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Scarlet.Bun.MSBuild;

internal sealed class MsBuildBunLogger : IBunLogger
{
    private readonly TaskLoggingHelper _log;

    public MsBuildBunLogger(TaskLoggingHelper log) => _log = log;

    public void LogMessage(string message) =>
        _log.LogMessage(MessageImportance.High, message);
}
