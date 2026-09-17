namespace Scarlet.Bun.MSBuild.Tests;

public class BunRuntimePackTests
{
    [Fact]
    public void Deduplicate_WithUnresolvablePath_ShouldNotThrow()
    {
        // Arrange - a path the OS cannot canonicalize must still be usable as a comparison key
        var packs = new[]
        {
            new BunRuntimePack("a", "linux-x64", "::invalid|path\0"),
            new BunRuntimePack("b", "linux-x64", "::invalid|path\0"),
            new BunRuntimePack("c", "linux-x64", "/other/runtimes")
        };

        // Act
        var result = BunRuntimePack.Deduplicate(packs);

        // Assert - the two identical unresolvable paths still collapse
        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { "a", "c" }, result.Select(pack => pack.Id));
    }

    [Fact]
    public void Deduplicate_WithDifferentRids_ShouldKeepBoth()
    {
        // Arrange
        var packs = new[]
        {
            new BunRuntimePack("a", "osx-arm64", "/packs/runtimes"),
            new BunRuntimePack("b", "linux-x64", "/packs/runtimes")
        };

        // Act
        var result = BunRuntimePack.Deduplicate(packs);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData("", "osx-arm64", "/runtimes")]
    [InlineData("id", "", "/runtimes")]
    [InlineData("id", "osx-arm64", "")]
    public void Constructor_WithEmptyRequiredValue_ShouldThrow(string id, string rid, string runtimesPath)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new BunRuntimePack(id, rid, runtimesPath));
    }

    [Fact]
    public void ToString_ShouldIncludeRidAndVariant()
    {
        // Assert
        Assert.Equal(
            "Scarlet.Bun.Runtime.linux-x64-baseline (linux-x64, baseline)",
            new BunRuntimePack("Scarlet.Bun.Runtime.linux-x64-baseline", "linux-x64", "/runtimes", "baseline").ToString());
        Assert.Equal(
            "Scarlet.Bun.Runtime.linux-aarch64 (linux-arm64)",
            new BunRuntimePack("Scarlet.Bun.Runtime.linux-aarch64", "linux-arm64", "/runtimes").ToString());
    }
}
