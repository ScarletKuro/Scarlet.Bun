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
