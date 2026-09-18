using System.Xml.Linq;

namespace Scarlet.Bun.MSBuild.Tests;

/// <summary>
/// The package ships <c>build/Scarlet.Bun.MSBuild.targets</c>, while the samples and integration tests
/// import <c>Scarlet.Bun.MSBuild.targets</c> from the source tree. The two are hand-maintained copies, so
/// anything proven against the development copy only holds for real consumers while they agree.
/// </summary>
public class BunTargetsTests
{
    private const string PackagedTargets = "build/Scarlet.Bun.MSBuild.targets";
    private const string DevelopmentTargets = "Scarlet.Bun.MSBuild.targets";

    [Fact]
    public void BothTargetsFiles_ShouldDefineTheSameTargets()
    {
        // Comparing the target bodies below only covers targets that exist in both files; without this, a
        // target added to one copy alone would go unnoticed.
        var packaged = LoadTargetNames(PackagedTargets);
        var development = LoadTargetNames(DevelopmentTargets);

        Assert.Equal(packaged, development);
    }

    [Theory]
    // The development copy has to build the task assembly before it can call into it; the packaged copy
    // ships that assembly, so it must not carry the dependency.
    [InlineData("Bun", null)]
    [InlineData("RunBunBeforeStaticWebAssets", "ResolveProjectReferences")]
    public void DevelopmentTargets_ShouldStayInSyncWithPackagedTargets(string targetName, string? developmentOnlyDependsOnTargets)
    {
        // Arrange
        var packagedTarget = LoadTarget(PackagedTargets, targetName);
        var developmentTarget = LoadTarget(DevelopmentTargets, targetName);

        Assert.Equal(developmentOnlyDependsOnTargets, developmentTarget.Attribute("DependsOnTargets")?.Value);
        Assert.Null(packagedTarget.Attribute("DependsOnTargets"));

        developmentTarget.Attribute("DependsOnTargets")?.Remove();

        // Act & Assert
        Assert.True(
            XNode.DeepEquals(packagedTarget, developmentTarget),
            $"""
             The '{targetName}' target differs between the packaged and development targets files.

             {PackagedTargets}:
             {packagedTarget}

             {DevelopmentTargets}:
             {developmentTarget}
             """);
    }

    private static IReadOnlyList<string> LoadTargetNames(string targetsRelativePath) =>
        LoadProject(targetsRelativePath)
            .Elements("Target")
            .Select(target => target.Attribute("Name")?.Value ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static XElement LoadTarget(string targetsRelativePath, string targetName) =>
        // Detached from its document so the caller can strip the expected differences before comparing.
        new(Assert.Single(
            LoadProject(targetsRelativePath).Elements("Target"),
            target => string.Equals(target.Attribute("Name")?.Value, targetName, StringComparison.Ordinal)));

    private static XElement LoadProject(string targetsRelativePath)
    {
        var targetsPath = Path.Combine(
            RepositoryRoot.Path, "src", "Scarlet.Bun.MSBuild", targetsRelativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(targetsPath), $"Targets file not found: {targetsPath}");

        var project = XDocument.Load(targetsPath).Root;
        Assert.NotNull(project);

        return project;
    }
}
