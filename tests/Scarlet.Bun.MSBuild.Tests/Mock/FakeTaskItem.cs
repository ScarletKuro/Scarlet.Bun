using System.Collections;
using Microsoft.Build.Framework;

namespace Scarlet.Bun.MSBuild.Tests.Mock;

/// <summary>
/// Minimal <see cref="ITaskItem"/> so pack parsing can be tested without Microsoft.Build.Utilities.
/// </summary>
internal sealed class FakeTaskItem : ITaskItem
{
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);

    public FakeTaskItem(string itemSpec, IDictionary<string, string>? metadata = null)
    {
        ItemSpec = itemSpec;

        if (metadata is null)
        {
            return;
        }

        foreach (var pair in metadata)
        {
            _metadata[pair.Key] = pair.Value;
        }
    }

    public string ItemSpec { get; set; }

    public ICollection MetadataNames => _metadata.Keys;

    public int MetadataCount => _metadata.Count;

    public IDictionary CloneCustomMetadata() => new Dictionary<string, string>(_metadata, StringComparer.OrdinalIgnoreCase);

    public void CopyMetadataTo(ITaskItem destinationItem)
    {
        foreach (var pair in _metadata)
        {
            destinationItem.SetMetadata(pair.Key, pair.Value);
        }
    }

    public string GetMetadata(string metadataName) => _metadata.TryGetValue(metadataName, out var value) ? value : string.Empty;

    public void RemoveMetadata(string metadataName) => _metadata.Remove(metadataName);

    public void SetMetadata(string metadataName, string metadataValue) => _metadata[metadataName] = metadataValue;
}
