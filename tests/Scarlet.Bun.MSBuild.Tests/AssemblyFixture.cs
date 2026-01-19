using Scarlet.Bun.MSBuild.Providers;
using Scarlet.Bun.MSBuild.Tests;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework($"Scarlet.Bun.MSBuild.Tests.{nameof(AssemblyFixture)}", "Scarlet.Bun.MSBuild.Tests")]
namespace Scarlet.Bun.MSBuild.Tests;

public sealed class AssemblyFixture : XunitTestFramework
{
    public AssemblyFixture(IMessageSink messageSink)
        : base(messageSink)
    {
        Chmod.Provider = new NoOpChmodProvider();
    }
}