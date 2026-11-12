using System.Xml.Linq;

namespace Scarlet.Bun.Testing;

/// <summary>
/// The Bun version the repository pins as <c>$(BunVersion)</c> in <c>Directory.Build.props</c>.
/// </summary>
/// <remarks>
/// Tests that download a real Bun use this rather than a literal. The Bun version bump workflow only rewrites
/// <c>Directory.Build.props</c>, so a literal silently falls behind with every release - and then the download
/// tests exercise a Bun the packages no longer ship.
/// </remarks>
internal static class PinnedBunVersion
{
    private static readonly Lazy<string> LazyValue = new(Read);

    public static string Value => LazyValue.Value;

    private static string Read()
    {
        var propsPath = System.IO.Path.Combine(RepositoryRoot.Path, "Directory.Build.props");
        var versions = XDocument.Load(propsPath).Descendants("BunVersion").ToList();

        if (versions.Count != 1 || string.IsNullOrWhiteSpace(versions[0].Value))
        {
            throw new InvalidOperationException(
                $"Expected exactly one non-empty <BunVersion> in '{propsPath}', found {versions.Count}.");
        }

        return versions[0].Value.Trim();
    }
}
