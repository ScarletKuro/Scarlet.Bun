using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Build.Framework;
using Scarlet.Bun.Core;

namespace Scarlet.Bun.MSBuild;

/// <summary>
/// Builds <see cref="BunRuntimePack"/> instances from MSBuild items.
/// </summary>
/// <remarks>
/// This is the only piece of the pack contract that knows about MSBuild, which is why it lives here rather
/// than beside <see cref="BunRuntimePack"/> in Scarlet.Bun.Core - the CLI shares the pack type but has no
/// <see cref="ITaskItem"/> to translate.
/// </remarks>
public static class BunRuntimePackFactory
{
    /// <summary>
    /// Builds the list of packs described by the given MSBuild items, dropping malformed and duplicate entries.
    /// </summary>
    /// <param name="items">The <c>BunRuntimePack</c> items. The sequence, and any entry in it, may be <see langword="null"/>.</param>
    /// <param name="onInvalidItem">Invoked with a human readable reason for every item that had to be dropped.</param>
    /// <returns>The valid, de-duplicated packs in declaration order.</returns>
    public static IReadOnlyList<BunRuntimePack> FromTaskItems(IEnumerable<ITaskItem?>? items, Action<string>? onInvalidItem = null)
    {
        if (items is null)
        {
            return Array.Empty<BunRuntimePack>();
        }

        var packs = new List<BunRuntimePack>();

        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            var id = item.ItemSpec?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                onInvalidItem?.Invoke($"A {BunRuntimePack.ItemName} item without an identity was ignored.");
                continue;
            }

            var rid = item.GetMetadata(BunRuntimePack.RidMetadataName)?.Trim();
            if (string.IsNullOrEmpty(rid))
            {
                onInvalidItem?.Invoke($"{BunRuntimePack.ItemName} \"{id}\" was ignored because it does not set the \"{BunRuntimePack.RidMetadataName}\" metadata.");
                continue;
            }

            var runtimesPath = item.GetMetadata(BunRuntimePack.RuntimesPathMetadataName)?.Trim();
            if (string.IsNullOrEmpty(runtimesPath))
            {
                onInvalidItem?.Invoke($"{BunRuntimePack.ItemName} \"{id}\" was ignored because it does not set the \"{BunRuntimePack.RuntimesPathMetadataName}\" metadata.");
                continue;
            }

            var variant = item.GetMetadata(BunRuntimePack.VariantMetadataName);
            var priorityText = item.GetMetadata(BunRuntimePack.PriorityMetadataName)?.Trim();
            var priority = 0;

            if (!string.IsNullOrEmpty(priorityText)
                && !int.TryParse(priorityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out priority))
            {
                onInvalidItem?.Invoke($"{BunRuntimePack.ItemName} \"{id}\" has a non-numeric \"{BunRuntimePack.PriorityMetadataName}\" metadata (\"{priorityText}\"); 0 was used instead.");
                priority = 0;
            }

            packs.Add(new BunRuntimePack(id!, rid!, runtimesPath!, variant, priority));
        }

        return BunRuntimePack.Deduplicate(packs);
    }
}
