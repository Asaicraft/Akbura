using System;
using System.IO;

namespace Akbura.FeatureGallery.Models;

// These resources are the actual build inputs, not hand-maintained code snippets.
public static class ExpressionBindingDemoSource
{
    public static string Basics { get; } = Component("ExpressionBindingBasicsDemo.akbura");
    public static string Model { get; } = Component("ExpressionBindingModelDemo.akbura") + ModelSource();
    public static string Indexed { get; } = Component("ExpressionBindingIndexedDemo.akbura") + ModelSource();
    public static string Utilities { get; } = Component("ExpressionBindingUtilityDemo.akbura") + ModelSource();

    private static string Component(string file) => Read("GlobalUsings.akbura") + Read("Components/" + file);
    private static string ModelSource() => Read("Models/ExpressionBindingDemoModel.cs");

    private static string Read(string path)
    {
        using var stream = typeof(ExpressionBindingDemoSource).Assembly.GetManifestResourceStream(path)
            ?? throw new InvalidOperationException("Missing showcase source: " + path);
        using var reader = new StreamReader(stream);
        return "// " + path + "\r\n\r\n" + reader.ReadToEnd() + "\r\n\r\n";
    }
}
