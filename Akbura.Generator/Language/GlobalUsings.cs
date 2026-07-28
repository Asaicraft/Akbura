using System;
using System.IO;

namespace Akbura.Language;

internal static class GlobalUsings
{
    public const string ComponentFileName = "GlobalUsings.akbura";
    public const string AkcssFileName = "GlobalUsings.akcss";

    public static bool IsComponentFile(AkburaSyntaxTree syntaxTree)
    {
        return syntaxTree is ComponentSyntaxTree &&
            HasFileName(syntaxTree.FilePath, ComponentFileName);
    }

    public static bool IsAkcssFile(AkburaSyntaxTree syntaxTree)
    {
        return syntaxTree is AkcssSyntaxTree &&
            HasFileName(syntaxTree.FilePath, AkcssFileName);
    }

    private static bool HasFileName(string path, string fileName)
    {
        return string.Equals(
            Path.GetFileName(path),
            fileName,
            StringComparison.OrdinalIgnoreCase);
    }
}
