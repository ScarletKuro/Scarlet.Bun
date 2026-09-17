using System.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Scarlet.Bun.MSBuild.IntegrationTests;

/// <summary>
/// Covers how <see cref="BunRunTask"/> merges the <c>BunRuntimePack</c> item contract with the older
/// <c>BunRuntime_&lt;rid&gt;</c> property contract, which is what keeps mixed package versions working.
/// </summary>
public class BunRuntimePackDiscoveryTests
{
    private static BunRunTask CreateTask() => new() { Command = "--version", BuildEngine = new MockBuildEngine() };

    private static ITaskItem CreatePackItem(string id, string rid, string runtimesPath, string? variant = null)
    {
        var metadata = new Hashtable
        {
            { BunRuntimePack.RidMetadataName, rid },
            { BunRuntimePack.RuntimesPathMetadataName, runtimesPath }
        };

        if (variant is not null)
        {
            metadata[BunRuntimePack.VariantMetadataName] = variant;
        }

        return new TaskItem(id, metadata);
    }

    [Fact]
    public void CollectRuntimePacks_WithNothingConfigured_ShouldReturnEmpty()
    {
        // Act & Assert
        Assert.Empty(CreateTask().CollectRuntimePacks());
    }

    [Fact]
    public void CollectRuntimePacks_WithOnlyLegacyProperty_ShouldTreatItAsAPack()
    {
        // Arrange - a runtime package older than the item contract only sets the property, and it points
        // at the package root rather than at the runtimes folder
        var task = CreateTask();
        task.BunRuntime_osx_arm64 = Path.Combine("packages", "scarlet.bun.runtime.darwin-aarch64", "1.3.14");

        // Act
        var pack = Assert.Single(task.CollectRuntimePacks());

        // Assert
        Assert.Equal("Scarlet.Bun.Runtime.darwin-aarch64", pack.Id);
        Assert.Equal("osx-arm64", pack.Rid);
        Assert.Equal(Path.Combine(task.BunRuntime_osx_arm64, "runtimes"), pack.RuntimesPath);
        Assert.Equal(BunRuntimePackSource.LegacyProperty, pack.Source);
    }

    [Fact]
    public void CollectRuntimePacks_WithOnlyLegacyProperty_ShouldReportTheDeprecatedContract()
    {
        // Arrange
        var engine = new MockBuildEngine();
        var task = new BunRunTask
        {
            Command = "--version",
            BuildEngine = engine,
            BunRuntime_osx_arm64 = Path.Combine("packages", "scarlet.bun.runtime.darwin-aarch64", "1.3.14")
        };

        // Act
        task.CollectRuntimePacks();

        // Assert - a message, not a warning, so that pinning an old Bun version cannot fail a build
        Assert.Empty(engine.Warnings);
        Assert.Contains(engine.Messages, message =>
            message.Message is not null
            && message.Message.Contains("BunRuntime_osx_arm64")
            && message.Message.Contains("deprecated")
            && message.Message.Contains(BunRunTask.FirstItemAwareRuntimeVersion));
    }

    [Fact]
    public void CollectRuntimePacks_WithBothContractsFromOnePackage_ShouldReturnOnePackAndStaySilent()
    {
        // Arrange - current runtime packages set both contracts so that they work with any task version
        var packageRoot = Path.Combine("packages", "scarlet.bun.runtime.darwin-aarch64", "1.3.14");
        var task = CreateTask();
        task.BunRuntime_osx_arm64 = packageRoot;
        task.RuntimePacks =
        [
            CreatePackItem("Scarlet.Bun.Runtime.darwin-aarch64", "osx-arm64", Path.Combine(packageRoot, "runtimes"))
        ];

        // Act
        var pack = Assert.Single(task.CollectRuntimePacks());

        // Assert - the item wins de-duplication, so an up-to-date package is never called deprecated
        Assert.Equal("Scarlet.Bun.Runtime.darwin-aarch64", pack.Id);
        Assert.Equal(BunRuntimePackSource.Item, pack.Source);
        Assert.DoesNotContain(((MockBuildEngine)task.BuildEngine).Messages, message =>
            message.Message is not null && message.Message.Contains("deprecated"));
    }

    [Fact]
    public void CollectRuntimePacks_WithPacksAndLegacyProperties_ShouldReturnBoth()
    {
        // Arrange
        var task = CreateTask();
        task.BunRuntime_linux_x64 = Path.Combine("packages", "scarlet.bun.runtime.linux-x64-baseline", "1.3.14");
        task.RuntimePacks =
        [
            CreatePackItem("Contoso.Bun.linux-x64-musl", "linux-musl-x64", Path.Combine("bun", "runtimes"), "musl")
        ];

        // Act
        var packs = task.CollectRuntimePacks();

        // Assert - a RID the task has never heard of still flows through untouched
        Assert.Equal(2, packs.Count);
        Assert.Contains(packs, pack => pack.Rid == "linux-musl-x64" && pack.Variant == "musl");
        Assert.Contains(packs, pack => pack.Id == "Scarlet.Bun.Runtime.linux-x64-baseline");
    }

    [Fact]
    public void Execute_WithNoRuntimeAtAll_ShouldFailWithTheResolverMessageAndNoStackTrace()
    {
        // Arrange - no packs, no legacy property, no explicit directory
        var engine = new MockBuildEngine();
        var task = new BunRunTask { Command = "--version", BuildEngine = engine };

        // Act
        var result = task.Execute();

        // Assert
        Assert.False(result);
        Assert.Equal(-1, task.ExitCode);

        var error = Assert.Single(engine.Errors).Message;
        Assert.NotNull(error);
        Assert.Contains("Bun runtime package not found", error);
        Assert.Contains("Runtime packs visible to this project: (none)", error);

        // The actionable text is the whole point of that error; a stack trace would bury it
        Assert.DoesNotContain("at Scarlet.Bun.MSBuild.", error);
    }

    [Fact]
    public void Execute_WithNoRuntimeAndContinueOnError_ShouldStillReportButNotFailTheTask()
    {
        // Arrange
        var engine = new MockBuildEngine();
        var task = new BunRunTask { Command = "--version", BuildEngine = engine, ContinueOnError = true };

        // Act
        var result = task.Execute();

        // Assert
        Assert.True(result);
        Assert.Equal(-1, task.ExitCode);
        Assert.Single(engine.Errors);
    }

    [Fact]
    public void CollectRuntimePacks_WithMalformedItem_ShouldWarnAndSkip()
    {
        // Arrange
        var engine = new MockBuildEngine();
        var task = new BunRunTask
        {
            Command = "--version",
            BuildEngine = engine,
            RuntimePacks = [new TaskItem("Broken.Pack")]
        };

        // Act
        var packs = task.CollectRuntimePacks();

        // Assert
        Assert.Empty(packs);
        Assert.Contains(engine.Warnings, warning => warning.Message is not null && warning.Message.Contains("Broken.Pack"));
    }
}
