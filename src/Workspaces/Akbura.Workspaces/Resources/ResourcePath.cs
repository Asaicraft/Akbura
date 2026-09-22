using System.Text;

namespace Akbura.Workspaces.Resources;

internal readonly struct ResourcePath : IEquatable<ResourcePath>
{
    private readonly string? _value;

    private ResourcePath(string value)
    {
        _value = value;
    }

    public string Value => _value ?? string.Empty;

    public bool IsDefault => _value == null;

    public static bool TryCreateExportPath(string? path, out ResourcePath resourcePath)
    {
        resourcePath = default;
        if (path == null ||
            string.IsNullOrWhiteSpace(path) ||
            !string.Equals(path, path.Trim(), StringComparison.Ordinal) ||
            path[0] == '\\' ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.IndexOf(':') >= 0 ||
            path.IndexOf('?') >= 0 ||
            path.IndexOf('#') >= 0 ||
            path.IndexOf('\0') >= 0)
        {
            return false;
        }

        var relativePath = path![0] == '/'
            ? path.Substring(1)
            : path;
        var normalized = NormalizeRelativePath(relativePath);
        if (normalized == null)
        {
            return false;
        }

        resourcePath = new ResourcePath(normalized);
        return true;
    }

    private static string? NormalizeRelativePath(string path)
    {
        var segments = new List<string>();
        var segmentStart = 0;

        for (var index = 0; index <= path.Length; index++)
        {
            if (index < path.Length &&
                path[index] != '/' &&
                path[index] != '\\')
            {
                continue;
            }

            var length = index - segmentStart;
            if (length > 0)
            {
                var segment = path.Substring(segmentStart, length);
                if (segment == "..")
                {
                    if (segments.Count == 0)
                    {
                        return null;
                    }

                    segments.RemoveAt(segments.Count - 1);
                }
                else if (segment != ".")
                {
                    segments.Add(segment);
                }
            }

            segmentStart = index + 1;
        }

        if (segments.Count == 0)
        {
            return null;
        }

        if (segments.Count == 1)
        {
            return segments[0];
        }

        var builder = new StringBuilder(path.Length);
        for (var index = 0; index < segments.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('/');
            }

            builder.Append(segments[index]);
        }

        return builder.ToString();
    }

    public bool Equals(ResourcePath other)
    {
        return string.Equals(
            Value,
            other.Value,
            StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is ResourcePath other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        return Value;
    }

    public static bool operator ==(ResourcePath left, ResourcePath right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ResourcePath left, ResourcePath right)
    {
        return !left.Equals(right);
    }
}
