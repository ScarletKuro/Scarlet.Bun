using System.IO.Abstractions.TestingHelpers;
using Scarlet.Bun.MSBuild.Providers;

namespace Scarlet.Bun.MSBuild.Tests;

public class BunRuntimeResolverTests
{
    private static MockFileSystem FileSystemWithBun(string runtimesPath, Platform platform)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(BunRuntimeResolver.GetExecutablePath(runtimesPath, platform), new MockFileData("fake executable"));

        return fileSystem;
    }

    [Fact]
    public void ResolveBunExecutable_WithNoRuntimeDirectory_ShouldThrowFileNotFoundException()
    {
        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
            BunRuntimeResolver.ResolveBunExecutable(new MockFileSystem(), NoOpChmodProvider.Instance, runtimeDirectory: null));
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
                mockFileSystem,
                NoOpChmodProvider.Instance,
                platform,
                runtimeDirectory));

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
            mockFileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory);

        // Assert
        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void ResolveBunExecutable_WithInvalidPath_ShouldThrowException()
    {
        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            BunRuntimeResolver.ResolveBunExecutable(new MockFileSystem(), NoOpChmodProvider.Instance));
    }

    [Fact]
    public void ResolveBunExecutable_WithMatchingPack_ShouldReturnPathFromPack()
    {
        // Arrange
        var platform = Platform.LinuxArm64;
        var packs = new[]
        {
            new BunRuntimePack("Scarlet.Bun.Runtime.linux-x64-baseline", "linux-x64", "/packs/linux-x64/runtimes"),
            new BunRuntimePack("Scarlet.Bun.Runtime.linux-aarch64", "linux-arm64", "/packs/linux-arm64/runtimes")
        };
        var fileSystem = FileSystemWithBun("/packs/linux-arm64/runtimes", platform);

        // Act
        var result = BunRuntimeResolver.ResolveBunExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs);

        // Assert
        Assert.Equal(BunRuntimeResolver.GetExecutablePath("/packs/linux-arm64/runtimes", platform), result);
    }

    [Fact]
    public void ResolveBunExecutable_WithExplicitDirectory_ShouldIgnorePacks()
    {
        // Arrange - an explicit BunRuntimeDirectory is a deliberate override
        var platform = Platform.LinuxArm64;
        var packs = new[] { new BunRuntimePack("pack", "linux-arm64", "/packs/linux-arm64/runtimes") };
        var fileSystem = FileSystemWithBun("/explicit", platform);

        // Act
        var result = BunRuntimeResolver.ResolveBunExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: "/explicit",
            runtimePacks: packs);

        // Assert
        Assert.Equal(BunRuntimeResolver.GetExecutablePath("/explicit", platform), result);
    }

    [Fact]
    public void ResolveBunExecutable_WithHigherPriorityPack_ShouldPreferIt()
    {
        // Arrange
        var platform = Platform.LinuxX64;
        var packs = new[]
        {
            new BunRuntimePack("Scarlet.Bun.Runtime.linux-x64-baseline", "linux-x64", "/packs/baseline/runtimes", "baseline"),
            new BunRuntimePack("Contoso.Bun.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100)
        };

        var fileSystem = FileSystemWithBun("/packs/baseline/runtimes", platform);
        fileSystem.AddFile(BunRuntimeResolver.GetExecutablePath("/packs/custom/runtimes", platform), new MockFileData("fake executable"));

        // Act
        var result = BunRuntimeResolver.ResolveBunExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs);

        // Assert
        Assert.Equal(BunRuntimeResolver.GetExecutablePath("/packs/custom/runtimes", platform), result);
    }

    [Fact]
    public void ResolveBunExecutable_WithBestPackMissingItsBinary_ShouldFallBackToNextCandidate()
    {
        // Arrange
        var platform = Platform.LinuxX64;
        var packs = new[]
        {
            new BunRuntimePack("Contoso.Bun.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100),
            new BunRuntimePack("Scarlet.Bun.Runtime.linux-x64-baseline", "linux-x64", "/packs/baseline/runtimes", "baseline")
        };
        var fileSystem = FileSystemWithBun("/packs/baseline/runtimes", platform);

        // Act
        var result = BunRuntimeResolver.ResolveBunExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs);

        // Assert
        Assert.Equal(BunRuntimeResolver.GetExecutablePath("/packs/baseline/runtimes", platform), result);
    }

    [Fact]
    public void ResolveBunExecutable_WithPackForAnotherHost_ShouldNameWhatIsInstalled()
    {
        // Arrange - the classic "wrong runtime package referenced" mistake
        var packs = new[] { new BunRuntimePack("Scarlet.Bun.Runtime.linux-x64-baseline", "linux-x64", "/packs/linux-x64/runtimes") };

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            BunRuntimeResolver.ResolveBunExecutable(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                Platform.MacOsArm64,
                runtimeDirectory: null,
                runtimePacks: packs));

        // Assert
        Assert.Contains("Bun runtime package not found", exception.Message);
        Assert.Contains("osx-arm64", exception.Message);
        Assert.Contains("Scarlet.Bun.Runtime.darwin-aarch64", exception.Message);
        Assert.Contains("Scarlet.Bun.Runtime.linux-x64-baseline (linux-x64)", exception.Message);
    }

    [Fact]
    public void ResolveBunExecutable_WithMatchingPackButNoBinary_ShouldListSearchedLocations()
    {
        // Arrange
        var packs = new[] { new BunRuntimePack("Scarlet.Bun.Runtime.darwin-aarch64", "osx-arm64", "/packs/osx-arm64/runtimes") };

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            BunRuntimeResolver.ResolveBunExecutable(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                Platform.MacOsArm64,
                runtimeDirectory: null,
                runtimePacks: packs));

        // Assert
        Assert.Contains("Bun executable not found at", exception.Message);
        Assert.Contains("Scarlet.Bun.Runtime.darwin-aarch64 (osx-arm64)", exception.Message);
        Assert.Contains(BunRuntimeResolver.GetExecutablePath("/packs/osx-arm64/runtimes", Platform.MacOsArm64), exception.Message);
    }

    [Fact]
    public void ResolveBunExecutable_WithMatchingPack_ShouldLogTheSelection()
    {
        // Arrange
        var platform = Platform.WindowsX64;
        var packs = new[] { new BunRuntimePack("Scarlet.Bun.Runtime.windows-x64-baseline", "win-x64", "/packs/win-x64/runtimes", "baseline") };
        var fileSystem = FileSystemWithBun("/packs/win-x64/runtimes", platform);
        var messages = new List<string>();

        // Act
        BunRuntimeResolver.ResolveBunExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs,
            log: messages.Add);

        // Assert
        Assert.Contains(messages, message => message.Contains("Scarlet.Bun.Runtime.windows-x64-baseline (win-x64, baseline)"));
    }

    [Fact]
    public void SelectPacks_ShouldOrderByPriorityThenIdIndependentlyOfInputOrder()
    {
        // Arrange
        var low = new BunRuntimePack("z-pack", "linux-x64", "/z/runtimes");
        var alsoLow = new BunRuntimePack("a-pack", "linux-x64", "/a/runtimes");
        var high = new BunRuntimePack("m-pack", "linux-x64", "/m/runtimes", priority: 5);
        var other = new BunRuntimePack("other", "osx-arm64", "/o/runtimes", priority: 99);

        // Act
        var forward = BunRuntimeResolver.SelectPacks(new[] { low, alsoLow, high, other }, Platform.LinuxX64);
        var reversed = BunRuntimeResolver.SelectPacks(new[] { other, high, alsoLow, low }, Platform.LinuxX64);

        // Assert
        Assert.Equal(new[] { "m-pack", "a-pack", "z-pack" }, forward.Select(pack => pack.Id));
        Assert.Equal(forward.Select(pack => pack.Id), reversed.Select(pack => pack.Id));
    }

    [Fact]
    public void SelectPacks_WithSameIdAndPriority_ShouldBreakTheTieOnPath()
    {
        // Arrange - two packs indistinguishable except for where they live
        var second = new BunRuntimePack("same-id", "linux-x64", "/b/runtimes");
        var first = new BunRuntimePack("same-id", "linux-x64", "/a/runtimes");

        // Act
        var forward = BunRuntimeResolver.SelectPacks(new[] { second, first }, Platform.LinuxX64);
        var reversed = BunRuntimeResolver.SelectPacks(new[] { first, second }, Platform.LinuxX64);

        // Assert
        Assert.Equal(new[] { "/a/runtimes", "/b/runtimes" }, forward.Select(pack => pack.RuntimesPath));
        Assert.Equal(forward.Select(pack => pack.RuntimesPath), reversed.Select(pack => pack.RuntimesPath));
    }

    [Fact]
    public void ResolveBunExecutable_WithSeveralCandidates_ShouldLogHowManyWereConsidered()
    {
        // Arrange
        var platform = Platform.LinuxX64;
        var packs = new[]
        {
            new BunRuntimePack("Contoso.Bun.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100),
            new BunRuntimePack("Scarlet.Bun.Runtime.linux-x64-baseline", "linux-x64", "/packs/baseline/runtimes", "baseline")
        };
        var fileSystem = FileSystemWithBun("/packs/custom/runtimes", platform);
        var messages = new List<string>();

        // Act
        BunRuntimeResolver.ResolveBunExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs,
            log: messages.Add);

        // Assert
        Assert.Contains(messages, message =>
            message.Contains("Contoso.Bun.linux-x64") && message.Contains("out of 2 candidates"));
    }

    [Fact]
    public void ResolveBunExecutable_WithSeveralCandidatesAndNoBinary_ShouldListEveryLocation()
    {
        // Arrange
        var packs = new[]
        {
            new BunRuntimePack("Contoso.Bun.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100),
            new BunRuntimePack("Scarlet.Bun.Runtime.linux-x64-baseline", "linux-x64", "/packs/baseline/runtimes", "baseline")
        };

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            BunRuntimeResolver.ResolveBunExecutable(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                Platform.LinuxX64,
                runtimeDirectory: null,
                runtimePacks: packs));

        // Assert
        Assert.Contains("2 runtime packs target linux-x64", exception.Message);
        Assert.Contains(BunRuntimeResolver.GetExecutablePath("/packs/custom/runtimes", Platform.LinuxX64), exception.Message);
        Assert.Contains(BunRuntimeResolver.GetExecutablePath("/packs/baseline/runtimes", Platform.LinuxX64), exception.Message);
    }

    [Fact]
    public void SelectPacks_WithNull_ShouldReturnEmpty()
    {
        // Act & Assert
        Assert.Empty(BunRuntimeResolver.SelectPacks(null, Platform.LinuxX64));
    }

    [Theory]
    [InlineData(Platform.WindowsArm64, "win-arm64", "bun.exe")]
    [InlineData(Platform.MacOsX64, "osx-x64", "bun")]
    public void GetExecutablePath_ShouldFollowTheRuntimePackLayout(Platform platform, string rid, string executableName)
    {
        // Act
        var result = BunRuntimeResolver.GetExecutablePath("/packs/runtimes", platform);

        // Assert
        Assert.Equal(Path.GetFullPath(Path.Combine("/packs/runtimes", rid, "native", executableName)), result);
    }
}
