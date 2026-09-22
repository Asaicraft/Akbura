using System.Collections.Immutable;

namespace Akbura.Workspaces.Resources;

internal readonly struct BuiltInAvaloniaResourceHint
{
    public BuiltInAvaloniaResourceHint(string key, string resourceTypeMetadataName)
    {
        Key = key;
        ResourceTypeMetadataName = resourceTypeMetadataName;
    }

    public string Key { get; }

    public string ResourceTypeMetadataName { get; }

    public string SourceDescription =>
        "Built-in Avalonia resource hint";
}

internal static class BuiltInAvaloniaResourceHints
{
    private const string ColorMetadataName = "Avalonia.Media.Color";

    // Verified against the system accent palette exposed by Avalonia 12.0.4.
    // These are editor hints only; their presence at runtime depends on the
    // application theme and platform configuration.
    public static ImmutableArray<BuiltInAvaloniaResourceHint> All { get; } =
        ImmutableArray.Create(
            new BuiltInAvaloniaResourceHint(
                "SystemAccentColor",
                ColorMetadataName),
            new BuiltInAvaloniaResourceHint(
                "SystemAccentColorLight1",
                ColorMetadataName),
            new BuiltInAvaloniaResourceHint(
                "SystemAccentColorLight2",
                ColorMetadataName),
            new BuiltInAvaloniaResourceHint(
                "SystemAccentColorLight3",
                ColorMetadataName),
            new BuiltInAvaloniaResourceHint(
                "SystemAccentColorDark1",
                ColorMetadataName),
            new BuiltInAvaloniaResourceHint(
                "SystemAccentColorDark2",
                ColorMetadataName),
            new BuiltInAvaloniaResourceHint(
                "SystemAccentColorDark3",
                ColorMetadataName),
            new BuiltInAvaloniaResourceHint(
                "SystemRegionColor",
                ColorMetadataName));
}
