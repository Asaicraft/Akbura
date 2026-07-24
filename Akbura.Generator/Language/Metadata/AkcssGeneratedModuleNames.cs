using System;

namespace Akbura.Language;

internal static class AkcssGeneratedModuleNames
{
    public const string NamespaceName = "Akbura.Generated";

    private const string TypeNamePrefix = "__AkburaAkcssModule_";

    public static string GetMetadataName(
        string rootNamespace,
        string sourcePath)
    {
        var normalizedPath = NormalizeSourcePath(sourcePath)
            .TrimStart('/');

        var pathWithoutExtension = normalizedPath.EndsWith(
            ".akcss",
            StringComparison.OrdinalIgnoreCase)
            ? normalizedPath[..^".akcss".Length]
            : normalizedPath;

        var name = pathWithoutExtension
            .Replace('/', '.')
            .Replace('\\', '.')
            .Trim('.');

        return string.IsNullOrWhiteSpace(rootNamespace)
            ? name + ".akcss"
            : rootNamespace.Trim('.') + "." + name + ".akcss";
    }

    public static string GetTypeName(string sourcePath)
    {
        return TypeNamePrefix + GetStableHash(NormalizeSourcePath(sourcePath)).ToString("x8");
    }

    public static string GetFullyQualifiedTypeName(string sourcePath)
    {
        return "global::" + NamespaceName + "." + GetTypeName(sourcePath);
    }

    public static uint GetStableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return hash;
        }
    }

    public static string NormalizeSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("AKCSS source path cannot be empty.", nameof(sourcePath));
        }

        var normalized = sourcePath.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }
}
