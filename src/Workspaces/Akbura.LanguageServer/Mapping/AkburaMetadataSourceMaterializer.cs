using System.Security.Cryptography;
using System.Text;

namespace Akbura.LanguageServer.Mapping;

internal sealed class AkburaMetadataSourceMaterializer
{
    private readonly string _root;

    public AkburaMetadataSourceMaterializer(string? root = null)
    {
        _root = root ?? GetDefaultRoot();
    }

    public async Task<string> MaterializeAsync(
        AkburaDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.TargetText == null)
        {
            return definition.TargetFilePath;
        }

        var assembly = definition.TargetAssemblyName ??
            "unknown";
        var assemblyHash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(assembly)))[..16];
        var sourcePath = SanitizeRelativePath(
            definition.TargetSourcePath ??
            Path.GetFileName(definition.TargetFilePath));
        var path = Path.GetFullPath(
            Path.Combine(_root, assemblyHash, sourcePath));
        var root = Path.GetFullPath(_root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!path.StartsWith(
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The embedded source path escapes the metadata cache.");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);
        var content = definition.TargetText.ToString();
        if (!File.Exists(path) ||
            !string.Equals(
                await File.ReadAllTextAsync(path, cancellationToken)
                    .ConfigureAwait(false),
                content,
                StringComparison.Ordinal))
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            await File.WriteAllTextAsync(
                    path,
                    content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            File.SetAttributes(path, FileAttributes.ReadOnly);
        }

        return path;
    }

    private static string SanitizeRelativePath(string path)
    {
        var segments = path
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries)
            .Where(static segment =>
                segment != "." &&
                segment != ".." &&
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            .ToArray();
        return segments.Length == 0
            ? "source.akbura"
            : Path.Combine(segments);
    }

    private static string GetDefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Akbura",
                "metadata");
        }

        var xdg = Environment.GetEnvironmentVariable(
            "XDG_CACHE_HOME");
        var cache = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".cache")
            : xdg;
        return Path.Combine(cache, "akbura", "metadata");
    }
}
