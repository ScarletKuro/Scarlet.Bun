using Microsoft.Build.Framework;
using Scarlet.Bun.MSBuild.Tests.Mock;

namespace Scarlet.Bun.MSBuild.Tests;

public class BunRuntimePackTests
{
    private static FakeTaskItem Pack(
        string id,
        string? rid = "osx-arm64",
        string? runtimesPath = "/packs/osx-arm64/runtimes",
        string? variant = null,
        string? priority = null)
    {
        var metadata = new Dictionary<string, string>();

        if (rid is not null)
        {
            metadata[BunRuntimePack.RidMetadataName] = rid;
        }

        if (runtimesPath is not null)
        {
            metadata[BunRuntimePack.RuntimesPathMetadataName] = runtimesPath;
        }

        if (variant is not null)
        {
            metadata[BunRuntimePack.VariantMetadataName] = variant;
        }

        if (priority is not null)
        {
            metadata[BunRuntimePack.PriorityMetadataName] = priority;
        }

        return new FakeTaskItem(id, metadata);
    }

    [Fact]
    public void FromTaskItems_WithNull_ShouldReturnEmpty()
    {
        // Act
        var packs = BunRuntimePack.FromTaskItems(null);

        // Assert
        Assert.Empty(packs);
    }

    [Fact]
    public void FromTaskItems_ShouldReadAllMetadata()
    {
        // Arrange
        var items = new ITaskItem[]
        {
            Pack("Scarlet.Bun.Runtime.darwin-aarch64", variant: "default", priority: "7")
        };

        // Act
        var pack = Assert.Single(BunRuntimePack.FromTaskItems(items));

        // Assert
        Assert.Equal("Scarlet.Bun.Runtime.darwin-aarch64", pack.Id);
        Assert.Equal("osx-arm64", pack.Rid);
        Assert.Equal("/packs/osx-arm64/runtimes", pack.RuntimesPath);
        Assert.Equal("default", pack.Variant);
        Assert.Equal(7, pack.Priority);
    }

    [Fact]
    public void FromTaskItems_WithoutPriority_ShouldDefaultToZero()
    {
        // Act
        var pack = Assert.Single(BunRuntimePack.FromTaskItems(new ITaskItem[] { Pack("pack") }));

        // Assert
        Assert.Equal(0, pack.Priority);
        Assert.Null(pack.Variant);
    }

    [Theory]
    [InlineData(null, "/packs/runtimes", BunRuntimePack.RidMetadataName)]
    [InlineData("osx-arm64", null, BunRuntimePack.RuntimesPathMetadataName)]
    public void FromTaskItems_WithMissingRequiredMetadata_ShouldSkipAndReport(string? rid, string? runtimesPath, string expectedMetadataName)
    {
        // Arrange
        var reported = new List<string>();
        var items = new ITaskItem[] { Pack("Incomplete.Pack", rid, runtimesPath) };

        // Act
        var packs = BunRuntimePack.FromTaskItems(items, reported.Add);

        // Assert
        Assert.Empty(packs);
        var message = Assert.Single(reported);
        Assert.Contains("Incomplete.Pack", message);
        Assert.Contains(expectedMetadataName, message);
    }

    [Fact]
    public void FromTaskItems_WithNonNumericPriority_ShouldFallBackToZeroAndReport()
    {
        // Arrange
        var reported = new List<string>();
        var items = new ITaskItem[] { Pack("Odd.Pack", priority: "highest") };

        // Act
        var pack = Assert.Single(BunRuntimePack.FromTaskItems(items, reported.Add));

        // Assert
        Assert.Equal(0, pack.Priority);
        Assert.Contains(reported, message => message.Contains("Odd.Pack") && message.Contains("highest"));
    }

    [Fact]
    public void FromTaskItems_WithSamePackTwice_ShouldKeepOne()
    {
        // Arrange - a package's props can be imported more than once, and items are additive
        var items = new ITaskItem[]
        {
            Pack("Scarlet.Bun.Runtime.darwin-aarch64", runtimesPath: "/packs/osx-arm64/runtimes"),
            Pack("Scarlet.Bun.Runtime.darwin-aarch64", runtimesPath: "/packs/osx-arm64/other/../runtimes")
        };

        // Act
        var pack = Assert.Single(BunRuntimePack.FromTaskItems(items));

        // Assert
        Assert.Equal("/packs/osx-arm64/runtimes", pack.RuntimesPath);
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
