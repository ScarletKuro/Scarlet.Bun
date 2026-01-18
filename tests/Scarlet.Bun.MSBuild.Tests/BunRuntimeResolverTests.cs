using System.IO.Abstractions.TestingHelpers;

namespace Scarlet.Bun.MSBuild.Tests;

public class BunRuntimeResolverTests
{
    [Fact]
    public void ResolveBunExecutable_WithNoRuntimeDirectory_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var assemblyPath = "/path/to/assembly.dll";

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
            BunRuntimeResolver.ResolveBunExecutable(assemblyPath, runtimeDirectory: null));
        Assert.Contains("Bun runtime package not found", exception.Message);
        Assert.Contains("Scarlet.Bun.Runtime", exception.Message);
    }

    [Fact]
    public void ResolveBunExecutable_WithMissingFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var assemblyPath = "/path/to/assembly.dll";
        var runtimeDirectory = "/runtime";
        var platform = Platform.LinuxX64;
        var mockFileSystem = new MockFileSystem();

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
            BunRuntimeResolver.ResolveBunExecutable(
                assemblyPath,
                platform,
                runtimeDirectory,
                mockFileSystem));
        
        Assert.Contains("Bun executable not found at", exception.Message);
        Assert.Contains("Scarlet.Bun.Runtime.linux-x64-baseline", exception.Message);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "win-x64", "bun.exe")]
    [InlineData(Platform.LinuxX64, "linux-x64", "bun")]
    [InlineData(Platform.LinuxArm64, "linux-arm64", "bun")]
    [InlineData(Platform.MacOsX64, "osx-x64", "bun")]
    [InlineData(Platform.MacOsArm64, "osx-arm64", "bun")]
    public void ResolveBunExecutable_WithValidFile_ShouldReturnPath(
        Platform platform,
        string runtimeId,
        string executableName)
    {
        // Arrange
        var assemblyPath = "/path/to/assembly.dll";
        var runtimeDirectory = "/runtime";
        var expectedPath = Path.GetFullPath(Path.Combine(runtimeDirectory, runtimeId, "native", executableName));
        
        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("fake executable"));

        // Act
        var result = BunRuntimeResolver.ResolveBunExecutable(
            assemblyPath,
            platform,
            runtimeDirectory,
            mockFileSystem);

        // Assert
        Assert.Equal(expectedPath, result);
    }

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
