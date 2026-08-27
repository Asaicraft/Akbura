using Akbura.Pools;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Rename;

internal sealed class AkburaRenameService :
    IAkburaRenameService
{
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

        if (!IsRenameableKind(occurrence.Key.Kind))
        {
            return new AkburaRenameInfo(
                canRename: false,
                occurrence.Span,
                occurrence.Name,
                "The selected symbol is defined outside editable Akbura source.",
                occurrence.Key);
        }

        var references = _references.FindReferences(
            context,
            position,
            includeDeclaration: true,
            cancellationToken);
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
        var changes = ImmutableDictionary.CreateBuilder<
            Uri,
            ImmutableArray<TextChange>>(
                AkburaDocumentUriComparer.Instance);

        foreach (var group in references.Locations
                     .Where(location =>
                         context.Solution.TryGetDocument(
                             location.Uri,
                             out _))
                     .GroupBy(
                         static location => location.Uri,
                         AkburaDocumentUriComparer.Instance))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var documentChanges =
                ImmutableArrayBuilder<TextChange>.Rent();
            foreach (var location in group
                         .OrderByDescending(
                             static location =>
                                 location.Span.Start))
            {
                if (documentChanges.Count > 0 &&
                    documentChanges.WrittenSpan[^1]
                        .Span.OverlapsWith(location.Span))
                {
                    continue;
                }

                documentChanges.Add(new TextChange(
                    location.Span,
                    newName));
            }

            if (documentChanges.Count > 0)
            {
                changes[group.Key] =
                    documentChanges.ToImmutable();
            }
        }

        return changes.Count == 0
            ? AkburaWorkspaceEdit.Empty
            : new AkburaWorkspaceEdit(
                changes.ToImmutable());
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
        return _references
            .GetOccurrences(
                context,
                cancellationToken)
            .Where(occurrence =>
                occurrence.Span.Contains(lookupPosition))
            .OrderBy(occurrence =>
                occurrence.Span.Length)
            .FirstOrDefault();
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
            AkburaSymbolKind.MarkupName or
            AkburaSymbolKind.InjectedService or
            AkburaSymbolKind.Command or
            AkburaSymbolKind.Function or
            AkburaSymbolKind.Hook or
            AkburaSymbolKind.AkcssClass or
            AkburaSymbolKind.AkcssUtility;
    }

    private static bool IsValidName(
        AkburaSymbolKind kind,
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
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

    private static AkburaRenameInfo CannotRename(
        string message)
    {
        return new AkburaRenameInfo(
            canRename: false,
            default,
            placeholder: null,
            message,
            symbol: null);
    }

    private sealed class AkburaDocumentUriComparer :
        IEqualityComparer<Uri>
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
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(
                    obj.LocalPath)
                : StringComparer.Ordinal.GetHashCode(
                    obj.AbsoluteUri);
        }
    }
}
