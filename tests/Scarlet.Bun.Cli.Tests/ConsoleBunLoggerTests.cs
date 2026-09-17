namespace Scarlet.Bun.Cli.Tests;

public class ConsoleBunLoggerTests
{
    [Fact]
    public void LogMessage_ShouldPrefixAndWriteToTheGivenWriter()
    {
        // Arrange - stderr, never stdout: `dotnet bun ... | jq` must not receive the tool's own chatter
        var stderr = new StringWriter();
        var logger = new ConsoleBunLogger(stderr);

        // Act
        logger.LogMessage("Another process is downloading the Bun runtime. Waiting...");

        // Assert
        Assert.Equal(
            "Scarlet.Bun: Another process is downloading the Bun runtime. Waiting..." + Environment.NewLine,
            stderr.ToString());
    }
}
