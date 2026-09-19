namespace Scarlet.Bun.Core;

/// <summary>
/// Where a <see cref="BunRuntimePack"/> came from.
/// </summary>
/// <remarks>
/// This exists only to report the deprecated property contract. Delete it together with the
/// <c>BunRuntime_&lt;rid&gt;</c> parameters on <c>BunRunTask</c>; the compiler will then point at every piece of legacy handling that has to go with them.
/// </remarks>
public enum BunRuntimePackSource
{
    /// <summary>Declared as a <c>BunRuntimePack</c> item. The supported contract.</summary>
    Item,

    /// <summary>Derived from a legacy <c>BunRuntime_&lt;rid&gt;</c> property.</summary>
    LegacyProperty
}
