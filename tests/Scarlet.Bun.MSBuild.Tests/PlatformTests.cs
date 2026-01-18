namespace Scarlet.Bun.MSBuild.Tests;

public class PlatformTests
{
    [Fact]
    public void GetCurrentPlatform_ShouldReturnValidPlatform()
    {
        // Act
        var platform = BunRuntimeResolver.GetCurrentPlatform();

        // Assert
        Assert.True(Enum.IsDefined(platform));
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "win-x64")]
    [InlineData(Platform.LinuxX64, "linux-x64")]
    [InlineData(Platform.LinuxArm64, "linux-arm64")]
    [InlineData(Platform.MacOsX64, "osx-x64")]
    [InlineData(Platform.MacOsArm64, "osx-arm64")]
    public void GetRuntimeIdentifier_ShouldReturnCorrectIdentifier(Platform platform, string expected)
    {
        // Act
        var result = BunRuntimeResolver.GetRuntimeIdentifier(platform);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetRuntimeIdentifier_WithInvalidPlatform_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidPlatform = (Platform)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            BunRuntimeResolver.GetRuntimeIdentifier(invalidPlatform));
        Assert.Contains("Unknown platform", exception.Message);
        Assert.Equal("platform", exception.ParamName);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "bun-windows-x64-baseline")]
    [InlineData(Platform.LinuxX64, "bun-linux-x64-baseline")]
    [InlineData(Platform.LinuxArm64, "bun-linux-aarch64")]
    [InlineData(Platform.MacOsX64, "bun-darwin-x64-baseline")]
    [InlineData(Platform.MacOsArm64, "bun-darwin-aarch64")]
    public void GetRuntimeDirectoryName_ShouldReturnCorrectName(Platform platform, string expected)
    {
        // Act
        var result = BunRuntimeResolver.GetRuntimeDirectoryName(platform);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetRuntimeDirectoryName_WithInvalidPlatform_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidPlatform = (Platform)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            BunRuntimeResolver.GetRuntimeDirectoryName(invalidPlatform));
        Assert.Contains("Unknown platform", exception.Message);
        Assert.Equal("platform", exception.ParamName);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "Scarlet.Bun.Runtime.windows-x64-baseline")]
    [InlineData(Platform.LinuxX64, "Scarlet.Bun.Runtime.linux-x64-baseline")]
    [InlineData(Platform.LinuxArm64, "Scarlet.Bun.Runtime.linux-aarch64")]
    [InlineData(Platform.MacOsX64, "Scarlet.Bun.Runtime.darwin-x64-baseline")]
    [InlineData(Platform.MacOsArm64, "Scarlet.Bun.Runtime.darwin-aarch64")]
    public void GetRuntimePackageName_ShouldReturnCorrectName(Platform platform, string expected)
    {
        // Act
        var result = BunRuntimeResolver.GetRuntimePackageName(platform);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetRuntimePackageName_WithInvalidPlatform_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidPlatform = (Platform)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            BunRuntimeResolver.GetRuntimePackageName(invalidPlatform));
        Assert.Contains("Unknown platform", exception.Message);
        Assert.Equal("platform", exception.ParamName);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "bun.exe")]
    [InlineData(Platform.LinuxX64, "bun")]
    [InlineData(Platform.LinuxArm64, "bun")]
    [InlineData(Platform.MacOsX64, "bun")]
    [InlineData(Platform.MacOsArm64, "bun")]
    public void GetExecutableName_ShouldReturnCorrectName(Platform platform, string expected)
    {
        // Act
        var result = BunRuntimeResolver.GetExecutableName(platform);

        // Assert
        Assert.Equal(expected, result);
    }
}
