using Akbura.Collections;
using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Rename;

internal sealed class AkburaRenameService : IAkburaRenameService
{
    private static readonly ObjectPool<Dictionary<Uri, ArrayBuilder<(TextSpan Span, int Index)>>> s_locationsByDocumentPool =
        new(static () => new(AkburaDocumentUriComparer.Instance));
    private static readonly ObjectPool<Dictionary<AkburaProjectId, ImmutableDictionary<AkburaDocumentId, AkburaDocumentSnapshot>.Builder>> s_projectUpdatesPool =
        new(static () => new());
    private static readonly IComparer<(TextSpan Span, int Index)> s_renameLocationComparer =
        Comparer<(TextSpan Span, int Index)>.Create(static (left, right) =>
        {
            var byStart = right.Span.Start.CompareTo(left.Span.Start);
            return byStart != 0 ? byStart : left.Index.CompareTo(right.Index);
        });

    private readonly AkburaFindReferencesService _references;

    public AkburaRenameService(
        AkburaFindReferencesService references)
    {
        _references = references ??
            throw new ArgumentNullException(nameof(references));
    }

    public AkburaRenameInfo GetRenameInfo(
        AkburaDocumentContext context,
        int position,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var occurrence = FindOccurrence(
            context,
            position,
            cancellationToken);
        if (occurrence == null)
        {
            return CannotRename(
                "No renameable symbol is available at this position.");
        }

        if (occurrence.NativeSymbol is AkcssThemeSymbol theme &&
            theme.DefinitionModel.Compilation.AkcssPublicExports.Contains(theme))
        {
            return new AkburaRenameInfo(false, occurrence.Span, occurrence.Name,
                "Exported themes can be referenced from ordinary C# files. Rename requires a cross-language edit.", occurrence.Key);
        }

        if (!IsRenameableKind(occurrence.Key.Kind))
        {
            return new AkburaRenameInfo(
                canRename: false,
                occurrence.Span,
                occurrence.Name,
                "The selected symbol is defined outside editable Akbura source.",
                occurrence.Key);
        }

        if (IsContextualKind(occurrence.Key.Kind) &&
            _references.HasConflictingAkcssBinding(context, occurrence.Span, occurrence.Key, cancellationToken))
        {
            return new AkburaRenameInfo(false, occurrence.Span, occurrence.Name,
                "This source token has more than one contextual binding.", occurrence.Key);
        }

        var references = _references.FindReferences(context, position, includeDeclaration: true, cancellationToken);
        if (!references.Locations.Any(location =>
                location.IsDeclaration &&
                context.Solution.TryGetDocument(
                    location.Uri,
                    out _)))
        {
            return new AkburaRenameInfo(
                canRename: false,
                occurrence.Span,
                occurrence.Name,
                "The selected symbol has no editable Akbura declaration.",
                occurrence.Key);
        }

        return new AkburaRenameInfo(
            canRename: true,
            occurrence.Span,
            occurrence.Name,
            errorMessage: null,
            occurrence.Key);
    }

    public AkburaWorkspaceEdit GetRenameChanges(
        AkburaDocumentContext context,
        int position,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (newName == null)
        {
            throw new ArgumentNullException(nameof(newName));
        }

        var info = GetRenameInfo(
            context,
            position,
            cancellationToken);
        if (!info.CanRename ||
            info.Symbol is not { } symbol)
        {
            throw new InvalidOperationException(
                info.ErrorMessage ??
                "The selected symbol cannot be renamed.");
        }

        if (!IsValidName(symbol.Kind, newName))
        {
            throw new ArgumentException(
                $"'{newName}' is not a valid name for " +
                $"a {symbol.Kind} symbol.",
                nameof(newName));
        }

        EnsureNoDeclarationConflict(
            context,
            symbol,
            newName,
            cancellationToken);

        var references = _references.FindReferences(
            context,
            position,
            includeDeclaration: true,
            cancellationToken);
        var isContextual = IsContextualKind(symbol.Kind);
        var changes = ImmutableDictionary.CreateBuilder<
            Uri,
            ImmutableArray<TextChange>>(
                AkburaDocumentUriComparer.Instance);

        CollectRenameChanges(context.Solution, references.Locations, symbol, newName, isContextual, changes, cancellationToken);
        if (isContextual && changes.Count != 0)
        {
            ValidateContextualRename(context.Solution, references.Locations, changes, cancellationToken);
        }

        return changes.Count == 0 ? AkburaWorkspaceEdit.Empty : new AkburaWorkspaceEdit(changes.ToImmutable());
    }

    private void CollectRenameChanges(AkburaSolutionSnapshot solution, ImmutableArray<AkburaReferenceLocation> locations,
        AkburaSymbolKey symbol, string newName, bool isContextual,
        ImmutableDictionary<Uri, ImmutableArray<TextChange>>.Builder changes, CancellationToken cancellationToken)
    {
        var byDocument = s_locationsByDocumentPool.Allocate();
        var documentChanges = ArrayBuilder<TextChange>.GetInstance();
        try
        {
            foreach (var location in locations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!byDocument.TryGetValue(location.Uri, out var spans))
                {
                    if (!solution.TryGetDocument(location.Uri, out _))
                    {
                        if (isContextual)
                        {
                            throw new InvalidOperationException("The rename has a reference outside the editable snapshot.");
                        }

                        continue;
                    }

                    spans = ArrayBuilder<(TextSpan, int)>.GetInstance();
                    byDocument.Add(location.Uri, spans);
                }

                spans.Add((location.Span, spans.Count));
            }

            foreach (var entry in byDocument)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var uri = entry.Key;
                var spans = entry.Value;
                spans.Sort(s_renameLocationComparer);
                AkburaDocumentContext? occurrenceContext = null;
                if (isContextual && !solution.TryGetDocumentContext(uri, out occurrenceContext))
                {
                    throw new InvalidOperationException("The rename would change another binding or edit a stale reference.");
                }

                foreach (var (span, _) in spans)
                {
                    if (documentChanges.Count > 0 && documentChanges[^1].Span.OverlapsWith(span))
                    {
                        if (isContextual)
                        {
                            throw new InvalidOperationException("The rename would require overlapping edits.");
                        }

                        continue;
                    }

                    if (occurrenceContext is not null &&
                        (span.End > occurrenceContext.Document.Text.Length ||
                         _references.HasConflictingAkcssBinding(occurrenceContext, span, symbol, cancellationToken)))
                    {
                        throw new InvalidOperationException("The rename would change another binding or edit a stale reference.");
                    }

                    documentChanges.Add(new TextChange(span, newName));
                }

                changes.Add(uri, documentChanges.ToImmutableAndClear());
            }
        }
        finally
        {
            documentChanges.Free();
            foreach (var spans in byDocument.Values)
            {
                spans.Free();
            }

            if (byDocument.Count <= 64)
            {
                byDocument.Clear();
                s_locationsByDocumentPool.Free(byDocument);
            }
            else
            {
                s_locationsByDocumentPool.ForgetTrackedObject(byDocument);
            }
        }
    }

    private void ValidateContextualRename(AkburaSolutionSnapshot original, ImmutableArray<AkburaReferenceLocation> locations,
        ImmutableDictionary<Uri, ImmutableArray<TextChange>>.Builder changes, CancellationToken cancellationToken)
    {
        var solution = original;
        var updated = s_projectUpdatesPool.Allocate();
        var documentChanges = ArrayBuilder<TextChange>.GetInstance();
        try
        {
            foreach (var entry in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var uri = entry.Key;
                var edits = entry.Value;
                if (!original.TryGetDocument(uri, out var document))
                {
                    throw new InvalidOperationException("An edited document has left the rename snapshot.");
                }

                if (!updated.TryGetValue(document.ProjectId, out var documents))
                {
                    documents = original.Projects[document.ProjectId].Documents.ToBuilder();
                    updated.Add(document.ProjectId, documents);
                }

                for (var i = edits.Length - 1; i >= 0; i--)
                {
                    documentChanges.Add(edits[i]);
                }

                var changed = document.Text.WithChanges(documentChanges);
                documentChanges.Clear();
                documents[document.Id] = document.WithText(changed, changed.GetChangeRanges(document.Text), cancellationToken);
            }

            foreach (var entry in updated)
            {
                solution = solution.WithProject(original.Projects[entry.Key].WithDocuments(entry.Value.ToImmutable()));
            }

            var declaration = locations.SingleOrDefault(static location => location.IsDeclaration);
            if (declaration.Uri is null || !solution.TryGetDocumentContext(declaration.Uri, out var declaringDocument) ||
                FindOccurrence(declaringDocument, MapRenamePosition(changes[declaration.Uri], declaration.Span.Start),
                    cancellationToken) is not { IsDeclaration: true } renamed)
            {
                throw new InvalidOperationException("The renamed declaration cannot be resolved.");
            }

            foreach (var projectId in updated.Keys)
            {
                var project = solution.Projects[projectId];
                foreach (var document in project.Documents.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var documentContext = new AkburaDocumentContext(solution, project, document);
                    changes.TryGetValue(document.Uri, out var edits);
                    if (!RenameBindingsMatch(documentContext, renamed.Key, edits, cancellationToken))
                    {
                        throw new InvalidOperationException("The requested name would change the bindings of one or more references.");
                    }
                }
            }
        }
        finally
        {
            documentChanges.Free();
            if (updated.Count <= 64)
            {
                updated.Clear();
                s_projectUpdatesPool.Free(updated);
            }
            else
            {
                s_projectUpdatesPool.ForgetTrackedObject(updated);
            }
        }
    }

    private bool RenameBindingsMatch(AkburaDocumentContext document, AkburaSymbolKey renamed,
        ImmutableArray<TextChange> edits, CancellationToken cancellationToken)
    {
        var occurrences = _references.GetOccurrences(document, cancellationToken);
        if (edits.IsDefaultOrEmpty)
        {
            return !occurrences.Any(static (occurrence, key) => occurrence.Key == key, renamed);
        }

        var spans = PooledHashSet<TextSpan>.GetInstance();
        try
        {
            foreach (var occurrence in occurrences)
            {
                if (occurrence.Key == renamed)
                {
                    spans.Add(occurrence.Span);
                }
            }

            if (spans.Count != edits.Length)
            {
                return false;
            }

            var offset = 0;
            for (var i = edits.Length - 1; i >= 0; i--)
            {
                var edit = edits[i];
                var length = edit.NewText!.Length;
                if (!spans.Contains(new TextSpan(edit.Span.Start + offset, length)))
                {
                    return false;
                }

                offset += length - edit.Span.Length;
            }

            return true;
        }
        finally
        {
            spans.Free();
        }
    }

    private static int MapRenamePosition(ImmutableArray<TextChange> edits, int position)
    {
        var offset = 0;
        for (var i = edits.Length - 1; i >= 0 && edits[i].Span.Start < position; i--)
        {
            var edit = edits[i];
            offset += edit.NewText!.Length - edit.Span.Length;
        }

        return position + offset;
    }

    private AkburaFindReferencesService.SemanticOccurrence?
        FindOccurrence(
            AkburaDocumentContext context,
            int position,
            CancellationToken cancellationToken)
    {
        if (context.Document.Text.Length == 0 ||
            position < 0 ||
            position > context.Document.Text.Length)
        {
            return null;
        }

        var lookupPosition =
            position == context.Document.Text.Length
                ? position - 1
                : position;
        AkburaFindReferencesService.SemanticOccurrence? result = null;
        foreach (var occurrence in _references.GetOccurrences(context, cancellationToken))
        {
            if (occurrence.Span.Contains(lookupPosition) &&
                (result is null || occurrence.Span.Length < result.Span.Length))
            {
                result = occurrence;
            }
        }

        return result;
    }

    private void EnsureNoDeclarationConflict(
        AkburaDocumentContext context,
        AkburaSymbolKey symbol,
        string newName,
        CancellationToken cancellationToken)
    {
        foreach (var project in
                 context.Solution.Projects.Values)
        {
            foreach (var document in
                     project.Documents.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var documentContext =
                    new AkburaDocumentContext(
                        context.Solution,
                        project,
                        document);
                foreach (var occurrence in
                         _references.GetOccurrences(
                             documentContext,
                             cancellationToken))
                {
                    if (!occurrence.IsDeclaration ||
                        occurrence.Key == symbol ||
                        occurrence.Key.Kind != symbol.Kind ||
                        !string.Equals(
                            occurrence.Key.ContainingSymbol,
                            symbol.ContainingSymbol,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            occurrence.Name,
                            newName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Rename would conflict with " +
                        $"'{occurrence.Name}' in " +
                        $"'{occurrence.Uri}'.");
                }
            }
        }
    }

    private static bool IsRenameableKind(
        AkburaSymbolKind kind)
    {
        return kind is
            AkburaSymbolKind.State or
            AkburaSymbolKind.Parameter or
            AkburaSymbolKind.CommandParameter or
            AkburaSymbolKind.UtilityParameter or
            AkburaSymbolKind.MarkupItem or
            AkburaSymbolKind.MarkupLoopLocal or
            AkburaSymbolKind.MarkupName or
            AkburaSymbolKind.InjectedService or
            AkburaSymbolKind.Command or
            AkburaSymbolKind.Function or
            AkburaSymbolKind.Hook or
            AkburaSymbolKind.AkcssClass or
            AkburaSymbolKind.AkcssUtility or
            AkburaSymbolKind.AkcssTheme or
            AkburaSymbolKind.AkcssMixin or
            AkburaSymbolKind.AkcssMixinParameter or
            AkburaSymbolKind.AkcssTemplatePart;
    }

    private static bool IsContextualKind(AkburaSymbolKind kind) =>
        kind is AkburaSymbolKind.AkcssTheme or AkburaSymbolKind.AkcssMixin or AkburaSymbolKind.AkcssMixinParameter or
            AkburaSymbolKind.AkcssTemplatePart;

    private static bool IsValidName(
        AkburaSymbolKind kind,
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (kind == AkburaSymbolKind.MarkupLoopLocal && name.TrimStart('@') == "index")
        {
            return false;
        }

        if (kind is
            AkburaSymbolKind.AkcssClass or
            AkburaSymbolKind.AkcssUtility)
        {
            if (!(char.IsLetter(name[0]) ||
                  name[0] == '_'))
            {
                return false;
            }

            return name.All(static character =>
                char.IsLetterOrDigit(character) ||
                character is '_' or '-');
        }

        return SyntaxFacts.IsValidIdentifier(name);
    }

    private static AkburaRenameInfo CannotRename(string message)
    {
        return new AkburaRenameInfo(
            canRename: false,
            default,
            placeholder: null,
            message,
            symbol: null);
    }

    private sealed class AkburaDocumentUriComparer : IEqualityComparer<Uri>
    {
        public static AkburaDocumentUriComparer Instance { get; } =
            new();

        public bool Equals(Uri? x, Uri? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            return x is not null &&
                   y is not null &&
                   DocumentUri.Equals(x, y);
        }

        public int GetHashCode(Uri obj)
        {
            return obj.IsFile
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.LocalPath)
                : StringComparer.Ordinal.GetHashCode(obj.AbsoluteUri);
        }
    }
}
