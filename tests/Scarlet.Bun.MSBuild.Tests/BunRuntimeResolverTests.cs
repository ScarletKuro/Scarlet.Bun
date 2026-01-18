using System.IO.Abstractions.TestingHelpers;

namespace Scarlet.Bun.MSBuild.Tests;

public class BunRuntimeResolverTests
{
    [Fact]
    public void ResolveBunExecutable_WithNoRuntimeDirectory_ShouldThrowFileNotFoundException()
    {
        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
            BunRuntimeResolver.ResolveBunExecutable(runtimeDirectory: null));
        Assert.Contains("Bun runtime package not found", exception.Message);
        Assert.Contains("Scarlet.Bun.Runtime", exception.Message);
    }

    [Fact]
    public void ResolveBunExecutable_WithMissingFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var runtimeDirectory = "/runtime";
        var platform = Platform.LinuxX64;
        var mockFileSystem = new MockFileSystem();

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
            BunRuntimeResolver.ResolveBunExecutable(
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
        var runtimeDirectory = "/runtime";
        var expectedPath = Path.GetFullPath(Path.Combine(runtimeDirectory, runtimeId, "native", executableName));
        
        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("fake executable"));

        // Act
        var result = BunRuntimeResolver.ResolveBunExecutable(
            platform,
            runtimeDirectory,
            mockFileSystem);

        // Assert
        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void ResolveBunExecutable_WithInvalidPath_ShouldThrowException()
    {
        // Act & Assert
        Assert.ThrowsAny<Exception>(() => BunRuntimeResolver.ResolveBunExecutable());
    }
}
