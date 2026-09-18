using System.Xml.Linq;

namespace Scarlet.Bun.MSBuild.Tests;

public class BunTargetsTests
{
    [Theory]
    [InlineData("Bun")]
    [InlineData("RunBunBeforeStaticWebAssets")]
    public void DevelopmentTargets_ShouldStayInSyncWithPackagedTargets(string targetName)
    {
        // Arrange
        var packagedTarget = LoadTarget(Path.Combine("build", "Scarlet.Bun.MSBuild.targets"), targetName);
        var developmentTarget = LoadTarget("Scarlet.Bun.MSBuild.targets", targetName);

        if (targetName == "RunBunBeforeStaticWebAssets")
        {
            Assert.Equal("ResolveProjectReferences", developmentTarget.Attribute("DependsOnTargets")?.Value);
            Assert.Null(packagedTarget.Attribute("DependsOnTargets"));

            developmentTarget.Attribute("DependsOnTargets")?.Remove();
        }

        // Act & Assert
        Assert.True(
            XNode.DeepEquals(packagedTarget, developmentTarget),
            $"The '{targetName}' target differs between the packaged and development targets files.");
    }

    private static XElement LoadTarget(string targetsRelativePath, string targetName)
    {
        var targetsPath = Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Bun.MSBuild", targetsRelativePath);

        Assert.True(File.Exists(targetsPath), $"Targets file not found: {targetsPath}");

        var project = XDocument.Load(targetsPath).Root;
        Assert.NotNull(project);

        return new XElement(Assert.Single(
            project.Elements("Target"),
            target => string.Equals(target.Attribute("Name")?.Value, targetName, StringComparison.Ordinal)));
    }
}
