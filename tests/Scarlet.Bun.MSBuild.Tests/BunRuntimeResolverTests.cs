namespace Scarlet.Bun.MSBuild.Tests;

public class BunRuntimeResolverTests
{
    [Fact]
    public void ResolveBunExecutable_WithInvalidPath_ShouldThrowException()
    {
        // Arrange
        var invalidPath = "/invalid/path/to/assembly.dll";

        // Act & Assert
        Assert.ThrowsAny<Exception>(() => BunRuntimeResolver.ResolveBunExecutable(invalidPath));
    }

    [Fact]
    public void ResolveBunExecutable_WithValidPath_ShouldReturnExecutablePath()
    {
        // Arrange - use the actual test assembly location
        var assemblyPath = typeof(BunRuntimeResolverTests).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(assemblyPath);
        
        // The runtime files might be copied to the test output or they might not be
        // We need to check if they exist first
        
        var platform = BunRuntimeResolver.GetCurrentPlatform();
        var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = BunRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(assemblyDir!, "runtimes", runtimeId, "native", executableName);

        if (File.Exists(expectedPath))
        {
            // Runtime files are present - verify the method returns the correct path
            var result = BunRuntimeResolver.ResolveBunExecutable(assemblyPath);
            Assert.Equal(expectedPath, result);
        }
        else
        {
            // Runtime files are not present - verify it throws FileNotFoundException
            var exception = Assert.Throws<FileNotFoundException>(() => 
                BunRuntimeResolver.ResolveBunExecutable(assemblyPath));
        }
    }
}
