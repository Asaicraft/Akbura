namespace Akbura.Workspaces;

internal static class DocumentUri
{
    public static string GetFilePath(Uri uri)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        if (!uri.IsFile)
        {
            return uri.AbsoluteUri;
        }

        return Path.GetFullPath(uri.LocalPath);
    }

    public static bool Equals(Uri left, Uri right)
    {
        if (left == null)
        {
            throw new ArgumentNullException(nameof(left));
        }
        if (right == null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        if (left.IsFile && right.IsFile)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFullPath(left.LocalPath),
                Path.GetFullPath(right.LocalPath));
        }

        return Uri.Compare(
            left,
            right,
            UriComponents.AbsoluteUri,
            UriFormat.SafeUnescaped,
            StringComparison.Ordinal) == 0;
    }
}
