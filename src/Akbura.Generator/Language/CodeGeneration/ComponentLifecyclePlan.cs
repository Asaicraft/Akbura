using System;

namespace Akbura.Language.CodeGeneration;

[Flags]
internal enum ComponentLifecycleFlags : byte
{
    None = 0,
    UsesFallbackRoot = 1 << 0,
    RequiresBaseUri = 1 << 1,
    HasExplicitRootDataContext = 1 << 2,
    HasComponentContentPresenters = 1 << 3,
}

internal readonly struct ComponentLifecyclePlan
{
    public ComponentLifecyclePlan(
        int rootElementId,
        ComponentLifecycleFlags flags)
    {
        RootElementId = rootElementId;
        Flags = flags;
    }

    /// <summary>
    /// Dense component element ID, or -1 when a generated fallback root is
    /// required.
    /// </summary>
    public int RootElementId { get; }

    public ComponentLifecycleFlags Flags { get; }

    public bool HasRootElement => RootElementId >= 0;

    public bool UsesFallbackRoot =>
        (Flags & ComponentLifecycleFlags.UsesFallbackRoot) != 0;

    public bool RequiresBaseUri =>
        (Flags & ComponentLifecycleFlags.RequiresBaseUri) != 0;

    public bool HasExplicitRootDataContext =>
        (Flags & ComponentLifecycleFlags.HasExplicitRootDataContext) != 0;

    public bool HasComponentContentPresenters =>
        (Flags & ComponentLifecycleFlags.HasComponentContentPresenters) != 0;
}
