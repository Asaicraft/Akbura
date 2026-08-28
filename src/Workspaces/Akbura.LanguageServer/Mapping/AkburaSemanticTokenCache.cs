using System.Collections.Concurrent;

namespace Akbura.LanguageServer.Mapping;

internal sealed class AkburaSemanticTokenCache
{
    private readonly ConcurrentDictionary<Uri, History> _entries =
        new(AkburaUriComparer.Instance);

    public void Store(Uri uri, SemanticTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(tokens);
        var current = Snapshot.Create(tokens);
        _entries.AddOrUpdate(
            uri,
            static (_, value) => new History(value, null),
            static (_, existing, value) =>
                string.Equals(
                    existing.Current.ResultId,
                    value.ResultId,
                    StringComparison.Ordinal)
                    ? existing
                    : new History(value, existing.Current),
            current);
    }

    public bool TryGet(Uri uri, string resultId, out int[] data)
    {
        if (_entries.TryGetValue(uri, out var history))
        {
            if (string.Equals(
                    history.Current.ResultId,
                    resultId,
                    StringComparison.Ordinal))
            {
                data = history.Current.Data;
                return true;
            }

            if (history.Previous is { } previous &&
                string.Equals(
                    previous.ResultId,
                    resultId,
                    StringComparison.Ordinal))
            {
                data = previous.Data;
                return true;
            }
        }

        data = [];
        return false;
    }

    public void Remove(Uri uri)
    {
        _entries.TryRemove(uri, out _);
    }

    public static SemanticTokensDelta CreateDelta(
        int[] previous,
        SemanticTokens current)
    {
        var next = current.Data;
        var prefix = 0;
        while (prefix < previous.Length &&
               prefix < next.Length &&
               previous[prefix] == next[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < previous.Length - prefix &&
               suffix < next.Length - prefix &&
               previous[previous.Length - 1 - suffix] ==
               next[next.Length - 1 - suffix])
        {
            suffix++;
        }

        var insertLength = next.Length - prefix - suffix;
        int[]? inserted = null;
        if (insertLength > 0)
        {
            inserted = new int[insertLength];
            Array.Copy(next, prefix, inserted, 0, insertLength);
        }

        var deleteCount = previous.Length - prefix - suffix;
        return new SemanticTokensDelta
        {
            ResultId = current.ResultId,
            Edits = deleteCount == 0 && inserted == null
                ? []
                :
                [
                    new SemanticTokensEdit
                    {
                        Start = prefix,
                        DeleteCount = deleteCount,
                        Data = inserted,
                    },
                ],
        };
    }

    private sealed record History(Snapshot Current, Snapshot? Previous);

    private sealed record Snapshot(string ResultId, int[] Data)
    {
        public static Snapshot Create(SemanticTokens tokens) =>
            new(
                tokens.ResultId ?? string.Empty,
                (int[])tokens.Data.Clone());
    }
}