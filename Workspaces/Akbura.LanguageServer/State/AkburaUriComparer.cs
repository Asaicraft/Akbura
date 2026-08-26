namespace Akbura.LanguageServer.State;

internal sealed class AkburaUriComparer : IEqualityComparer<Uri>
{
    public static AkburaUriComparer Instance { get; } = new();

    private AkburaUriComparer()
    {
    }

    public bool Equals(Uri? x, Uri? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x == null || y == null)
        {
            return false;
        }

        if (x.IsFile && y.IsFile)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFullPath(x.LocalPath),
                Path.GetFullPath(y.LocalPath));
        }

        return Uri.Compare(
            x,
            y,
            UriComponents.AbsoluteUri,
            UriFormat.SafeUnescaped,
            StringComparison.Ordinal) == 0;
    }

    public int GetHashCode(Uri obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.IsFile
            ? StringComparer.OrdinalIgnoreCase.GetHashCode(
                Path.GetFullPath(obj.LocalPath))
            : StringComparer.Ordinal.GetHashCode(obj.AbsoluteUri);
    }
}