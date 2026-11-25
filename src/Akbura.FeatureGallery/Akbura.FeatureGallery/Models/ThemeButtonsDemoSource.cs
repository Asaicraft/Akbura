using System;
using System.IO;

namespace Akbura.FeatureGallery.Models;

public static class ThemeButtonsDemoSource
{
    public static string Code { get; } =
        Read("Components/ThemeButtons.akcss") + "\r\n" + Read("Components/ThemeButtonsDemo.akbura");

    private static string Read(string path)
    {
        using var stream = typeof(ThemeButtonsDemoSource).Assembly.GetManifestResourceStream(path)
            ?? throw new InvalidOperationException("Missing gallery source: " + path);
        using var reader = new StreamReader(stream);
        return "// " + path + "\r\n\r\n" + reader.ReadToEnd();
    }
}
