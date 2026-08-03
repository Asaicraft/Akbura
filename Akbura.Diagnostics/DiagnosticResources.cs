using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.Threading;

namespace Akbura.Diagnostics;

internal static class DiagnosticResources
{
    private const string ResourceUri =
        "avares://Akbura.Diagnostics/Resources.axaml";

    private static readonly Lazy<ResourceDictionary> Resources = new(
        LoadResources,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static StreamGeometry InputBuilderIcon =>
        GetStreamGeometry("Akbura.Diagnostics.InputBuilder.Icon");

    public static StreamGeometry StringInputBuilderIcon =>
        GetStreamGeometry("Akbura.Diagnostics.StringInputBuilder.Icon");

    public static StreamGeometry UniversalInputBuilderIcon =>
        GetStreamGeometry("Akbura.Diagnostics.UniversalInputBuilder.Icon");

    public static StreamGeometry CollectionInputBuilderIcon =>
        GetStreamGeometry("Akbura.Diagnostics.CollectionInputBuilder.Icon");

    public static StreamGeometry ApplyIcon =>
        GetStreamGeometry("Akbura.Diagnostics.InputBuilder.ApplyIcon");

    public static StreamGeometry ReloadIcon =>
        GetStreamGeometry("Akbura.Diagnostics.InputBuilder.ReloadIcon");

    private static StreamGeometry GetStreamGeometry(string key)
    {
        return Resources.Value[key] as StreamGeometry
            ?? throw new InvalidOperationException(
                $"Diagnostics resource '{key}' is not a StreamGeometry.");
    }

    private static ResourceDictionary LoadResources()
    {
        return AvaloniaXamlLoader.Load(
                new Uri(ResourceUri),
                baseUri: null)
            as ResourceDictionary
            ?? throw new InvalidOperationException(
                $"Diagnostics resource dictionary '{ResourceUri}' could not be loaded.");
    }
}
