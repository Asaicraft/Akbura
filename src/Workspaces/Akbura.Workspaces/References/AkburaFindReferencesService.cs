using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using NativeSymbol = Akbura.Language.Symbols.ISymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Workspaces.References;

internal sealed class AkburaFindReferencesService :
    IAkburaFindReferencesService,
    IAkburaDocumentHighlightService
{
    private const int MaximumCachedDocuments = 128;

    private readonly AkcssReferenceResolver _akcssReferenceResolver;
    private readonly ConcurrentDictionary<
        ReferenceCacheKey,
        ImmutableArray<SemanticOccurrence>> _cache = new();

    public AkburaFindReferencesService(
        AkcssReferenceResolver akcssReferenceResolver)
    {
        _akcssReferenceResolver = akcssReferenceResolver ??
            throw new ArgumentNullException(
                nameof(akcssReferenceResolver));
    }

    public AkburaReferenceResult FindReferences(
        AkburaDocumentContext context,
        int position,
        bool includeDeclaration,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Document.Text.Length == 0 ||
            position < 0 ||
            position > context.Document.Text.Length)
        {
            return EmptyResult();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var lookupPosition = position == context.Document.Text.Length
            ? position - 1
            : position;
        var sourceOccurrences = GetOccurrences(
            context,
            cancellationToken);
        var target = sourceOccurrences
            .Where(occurrence =>
                occurrence.Span.Contains(lookupPosition))
            .OrderBy(occurrence => occurrence.Span.Length)
            .ThenByDescending(occurrence => occurrence.IsDeclaration)
            .FirstOrDefault();

        if (target == null)
        {
            return EmptyResult();
        }

        using var locations =
            ImmutableArrayBuilder<AkburaReferenceLocation>.Rent();
        var seen = new HashSet<ReferenceLocationIdentity>();

        foreach (var project in context.Solution.Projects.Values)
        {
            foreach (var document in project.Documents.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var documentContext = new AkburaDocumentContext(
                    context.Solution,
                    project,
                    document);
                foreach (var occurrence in GetOccurrences(
                             documentContext,
                             cancellationToken))
                {
                    if (occurrence.Key != target.Key ||
                        !includeDeclaration &&
                        occurrence.IsDeclaration)
                    {
                        continue;
                    }

                    AddLocation(
                        locations,
                        seen,
                        new AkburaReferenceLocation(
                            occurrence.Uri,
                            occurrence.Span,
                            occurrence.IsDeclaration,
                            occurrence.IsWrite));
                }
            }
        }

        if (includeDeclaration &&
            target.RoslynSymbol is { } csharpSymbol)
        {
            AddCSharpDeclarations(
                csharpSymbol,
                locations,
                seen,
                cancellationToken);
        }

        return new AkburaReferenceResult(
            target.Key,
            target.Name,
            locations.AsEnumerable()
                .OrderBy(static location =>
                    location.Uri.AbsoluteUri,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(static location => location.Span.Start)
                .ToImmutableArray());
    }

    public ImmutableArray<AkburaDocumentHighlight> GetHighlights(
        AkburaDocumentContext context,
        int position,
        CancellationToken cancellationToken = default)
    {
        var references = FindReferences(
            context,
            position,
            includeDeclaration: true,
            cancellationToken);
        if (references.IsEmpty)
        {
            return ImmutableArray<AkburaDocumentHighlight>.Empty;
        }

        using var builder =
            ImmutableArrayBuilder<AkburaDocumentHighlight>.Rent();
        foreach (var location in references.Locations)
        {
            if (DocumentUri.Equals(
                    location.Uri,
                    context.Document.Uri))
            {
                builder.Add(new AkburaDocumentHighlight(
                    location.Span,
                    location.IsWrite));
            }
        }

        return builder.ToImmutable();
    }

    internal ImmutableArray<SemanticOccurrence> GetOccurrences(
        AkburaDocumentContext context,
        CancellationToken cancellationToken)
    {
        var key = new ReferenceCacheKey(
            context.Solution.Version,
            context.Document.Id,
            context.Document.Version);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var occurrences = CollectOccurrences(
            context,
            cancellationToken);
        if (_cache.Count >= MaximumCachedDocuments)
        {
            _cache.Clear();
        }

        return _cache.GetOrAdd(key, occurrences);
    }

    private ImmutableArray<SemanticOccurrence> CollectOccurrences(
        AkburaDocumentContext context,
        CancellationToken cancellationToken)
    {
        var document = context.Document;
        var root = document.SyntaxTree.GetRootSyntax();
        var semanticModel = context.Project.Compilation
            .GetSemanticModel(document.SyntaxTree);
        using var builder =
            ImmutableArrayBuilder<SemanticOccurrence>.Rent();
        var seen = new HashSet<OccurrenceIdentity>();

        foreach (var node in root.DescendantNodesAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (node)
            {
                case StateDeclarationSyntax declaration:
                    AddDeclaration(
                        context,
                        semanticModel,
                        declaration,
                        declaration.Name.Span,
                        declaration.Name.Identifier.ValueText,
                        builder,
                        seen);
                    break;

                case ParamDeclarationSyntax declaration:
                    AddDeclaration(
                        context,
                        semanticModel,
                        declaration,
                        declaration.Name.Span,
                        declaration.Name.Identifier.ValueText,
                        builder,
                        seen);
                    break;

                case InjectDeclarationSyntax declaration:
                    AddDeclaration(
                        context,
                        semanticModel,
                        declaration,
                        declaration.Name.Span,
                        declaration.Name.Identifier.ValueText,
                        builder,
                        seen);
                    break;

                case CommandDeclarationSyntax declaration:
                    AddDeclaration(
                        context,
                        semanticModel,
                        declaration,
                        declaration.Name.Span,
                        declaration.Name.Identifier.ValueText,
                        builder,
                        seen);
                    break;

                case AkcssStyleRuleSyntax declaration:
                    if (declaration.Selector.Name is { } styleName)
                    {
                        AddDeclaration(
                            context,
                            semanticModel,
                            declaration,
                            styleName.Span,
                            styleName.Identifier.ValueText,
                            builder,
                            seen);
                    }
                    break;

                case AkcssUtilityDeclarationSyntax declaration:
                    AddDeclaration(
                        context,
                        semanticModel,
                        declaration,
                        declaration.Selector.Name.Span,
                        declaration.Selector.Name.Identifier.ValueText,
                        builder,
                        seen);
                    break;

                case AkcssUtilityParameterSyntax parameter:
                    AddAkcssResolvedAtPosition(
                        context,
                        parameter.ParamName.Identifier.Span.Start,
                        isDeclaration: true,
                        builder,
                        seen,
                        cancellationToken);
                    break;

                case MarkupElementSyntax element:
                    AddMarkupElement(
                        context,
                        semanticModel,
                        element,
                        builder,
                        seen);
                    break;

                case TailwindAttributeSyntax utility:
                    AddTailwindUtility(
                        context,
                        semanticModel,
                        utility,
                        builder,
                        seen);
                    AddCSharpReferences(
                        context,
                        semanticModel.GetCSharpSymbolReferences(utility),
                        builder,
                        seen);
                    break;

                case MarkupAttributeSyntax attribute:
                    AddMarkupAttribute(
                        context,
                        semanticModel,
                        attribute,
                        builder,
                        seen);
                    AddCSharpReferences(
                        context,
                        semanticModel.GetCSharpSymbolReferences(attribute),
                        builder,
                        seen);
                    break;

                case CSharpStatementSyntax statement:
                    AddCSharpReferences(
                        context,
                        semanticModel.GetCSharpSymbolReferences(statement),
                        builder,
                        seen);
                    break;

                case InlineExpressionSyntax expression:
                    AddCSharpReferences(
                        context,
                        semanticModel.GetCSharpSymbolReferences(expression),
                        builder,
                        seen);
                    break;

                case AkcssUsingDirectiveSyntax usingDirective:
                    AddAkcssResolvedAtPosition(
                        context,
                        usingDirective.Name.Tokens.Span.Start,
                        isDeclaration: false,
                        builder,
                        seen,
                        cancellationToken);
                    break;

                case AkcssApplyDirectiveSyntax apply:
                    foreach (var reference in
                             _akcssReferenceResolver.GetApplyReferences(
                                 context,
                                 apply,
                                 cancellationToken))
                    {
                        AddResolvedReference(
                            context,
                            reference,
                            isDeclaration: false,
                            builder,
                            seen);
                    }
                    break;

                case AkcssAssignmentSyntax assignment:
                    foreach (var reference in
                             _akcssReferenceResolver.GetPropertyReferences(
                                 context,
                                 assignment,
                                 cancellationToken))
                    {
                        AddResolvedReference(
                            context,
                            reference,
                            isDeclaration: false,
                            builder,
                            seen,
                            isWrite: true);
                    }
                    break;
            }
        }

        return builder.ToImmutable();
    }

    private static void AddDeclaration(
        AkburaDocumentContext context,
        AkburaSemanticModel semanticModel,
        AkburaSyntax declaration,
        TextSpan span,
        string name,
        ImmutableArrayBuilder<SemanticOccurrence> builder,
        HashSet<OccurrenceIdentity> seen)
    {
        var symbol = semanticModel.GetDeclaredSymbol(declaration);
        if (symbol == null)
        {
            return;
        }

        AddNativeOccurrence(
            context,
            symbol,
            span,
            name,
            isDeclaration: true,
            isWrite: true,
            builder,
            seen);
    }

    private static void AddMarkupElement(
        AkburaDocumentContext context,
        AkburaSemanticModel semanticModel,
        MarkupElementSyntax element,
        ImmutableArrayBuilder<SemanticOccurrence> builder,
        HashSet<OccurrenceIdentity> seen)
    {
        var symbol = semanticModel.GetSymbolInfo(element).Symbol;
        if (symbol == null)
        {
            return;
        }

        if (element.StartTag is { } startTag)
        {
            AddNativeOccurrence(
                context,
                symbol,
                startTag.Name.Span,
                startTag.Name.ToString().Trim(),
                isDeclaration: false,
                isWrite: false,
                builder,
                seen);
        }

        if (element.EndTag is { } endTag &&
            !endTag.IsMissing)
        {
            AddNativeOccurrence(
                context,
                symbol,
                endTag.Name.Span,
                endTag.Name.ToString().Trim(),
                isDeclaration: false,
                isWrite: false,
                builder,
                seen);
        }
    }

    private static void AddMarkupAttribute(
        AkburaDocumentContext context,
        AkburaSemanticModel semanticModel,
        MarkupAttributeSyntax attribute,
        ImmutableArrayBuilder<SemanticOccurrence> builder,
        HashSet<OccurrenceIdentity> seen)
    {
        var span = GetMarkupAttributeNameSpan(attribute);
        if (span == null)
        {
            return;
        }

        var declaredSymbol = semanticModel.GetDeclaredSymbol(attribute);
        var symbol = declaredSymbol ??
            semanticModel.GetSymbolInfo(attribute).Symbol;
        if (symbol == null)
        {
            return;
        }

        AddNativeOccurrence(
            context,
            symbol,
            span.Value,
            context.Document.Text.ToString(span.Value),
            isDeclaration: declaredSymbol != null,
            isWrite: true,
            builder,
            seen);
    }

    private static void AddTailwindUtility(
        AkburaDocumentContext context,
        AkburaSemanticModel semanticModel,
        TailwindAttributeSyntax attribute,
        ImmutableArrayBuilder<SemanticOccurrence> builder,
        HashSet<OccurrenceIdentity> seen)
    {
        if (semanticModel.GetOperation(attribute) is not
            ITailwindUtilityAttributeOperation
            {
                Utility: { } utility,
            })
        {
            return;
        }

        var sourceSpan = GetTailwindUtilityNameSpan(
            context.Document.Text,
            attribute,
            utility.Name);
        AddNativeOccurrence(
            context,
            utility,
            sourceSpan,
            context.Document.Text.ToString(sourceSpan),
            isDeclaration: false,
            isWrite: false,
            builder,
            seen);
    }

    private void AddAkcssResolvedAtPosition(
        AkburaDocumentContext context,
        int position,
        bool isDeclaration,
        ImmutableArrayBuilder<SemanticOccurrence> builder,
        HashSet<OccurrenceIdentity> seen,
        CancellationToken cancellationToken)
    {
        if (_akcssReferenceResolver.TryResolve(
                context,
                position,
                out var reference,
                cancellationToken))
        {
            AddResolvedReference(
                context,
                reference,
                isDeclaration,
                builder,
                seen);
        }
    }

    private static void AddResolvedReference(
        AkburaDocumentContext context,
        AkcssResolvedReference reference,
        bool isDeclaration,
        ImmutableArrayBuilder<SemanticOccurrence> builder,
        HashSet<OccurrenceIdentity> seen,
        bool isWrite = false)
    {
        if (reference.Symbol is { } native)
        {
            var span = GetSymbolNameSpan(
                context.Document.Text,
                reference.SourceSpan,
                native.Name);
            AddNativeOccurrence(
                context,
                native,
                span,
                context.Document.Text.ToString(span),
                isDeclaration,
                isWrite,
                builder,
                seen);
            return;
        }

        if (reference.CSharpDefinition.Symbol is { } csharp)
        {
            AddRoslynOccurrence(
                context,
                csharp,
                reference.SourceSpan,
                context.Document.Text.ToString(reference.SourceSpan),
                isDeclaration,
                isWrite,
                builder,
                seen);
        }
    }

    private static void AddCSharpReferences(
        AkburaDocumentContext context,
        ImmutableArray<CSharpSymbolReference> references,
        ImmutableArrayBuilder<SemanticOccurrence> builder,
        HashSet<OccurrenceIdentity> seen)
    {
        foreach (var reference in references)
        {
            if (reference.SourceSpan.End >
                context.Document.Text.Length)
            {
                continue;
            }

            if (reference.AkburaSymbol is { } native)
            {
                AddNativeOccurrence(
                    context,
                    native,
                    reference.SourceSpan,
                    reference.Name,
                    isDeclaration: false,
                    IsWriteReference(reference.Syntax),
                    builder,
                    seen,
                    reference.CSharpDefinition.Symbol);
            }
            else if (reference.CSharpDefinition.Symbol is { } csharp)
            {
                AddRoslynOccurrence(
                    context,
                    csharp,
                    reference.SourceSpan,
                    reference.Name,
                    isDeclaration: false,
                    IsWriteReference(reference.Syntax),
                    builder,
                    seen);
            }
        }
    }

    private static void AddNativeOccurrence(
        AkburaDocumentContext context,
        NativeSymbol symbol,
        TextSpan span,
        string name,
        bool isDeclaration,
        bool isWrite,
        ImmutableArrayBuilder<SemanticOccurrence> builder,
        HashSet<OccurrenceIdentity> seen,
        RoslynSymbol? csharpSymbol = null)
    {
        if (span.End > context.Document.Text.Length)
        {
            return;
        }

        var key = AkburaSymbolKeyFactory.Create(
            context,
            symbol);
        AddOccurrence(
            new SemanticOccurrence(
                key,
                context.Document.Uri,
                span,
                string.IsNullOrWhiteSpace(name)
                    ? symbol.Name
                    : name,
                isDeclaration,
                isWrite,
                symbol,
                csharpSymbol ??
                    symbol.CSharpDefinition.Symbol),
            builder,
            seen);
    }

    private static void AddRoslynOccurrence(
        AkburaDocumentContext context,
        RoslynSymbol symbol,
        TextSpan span,
        string name,
        bool isDeclaration,
        bool isWrite,
        ImmutableArrayBuilder<SemanticOccurrence> builder,
        HashSet<OccurrenceIdentity> seen)
    {
        if (span.End > context.Document.Text.Length)
        {
            return;
        }

        AddOccurrence(
            new SemanticOccurrence(
                AkburaSymbolKeyFactory.Create(
                    context,
                    symbol),
                context.Document.Uri,
                span,
                string.IsNullOrWhiteSpace(name)
                    ? symbol.Name
                    : name,
                isDeclaration,
                isWrite,
                nativeSymbol: null,
                symbol),
            builder,
            seen);
    }

    private static void AddOccurrence(
        SemanticOccurrence occurrence,
        ImmutableArrayBuilder<SemanticOccurrence> builder,
        HashSet<OccurrenceIdentity> seen)
    {
        if (occurrence.Span.Length <= 0)
        {
            return;
        }

        var identity = new OccurrenceIdentity(
            occurrence.Key,
            occurrence.Uri,
            occurrence.Span,
            occurrence.IsDeclaration);
        if (seen.Add(identity))
        {
            builder.Add(occurrence);
        }
    }

    private static void AddCSharpDeclarations(
        RoslynSymbol symbol,
        ImmutableArrayBuilder<AkburaReferenceLocation> builder,
        HashSet<ReferenceLocationIdentity> seen,
        CancellationToken cancellationToken)
    {
        foreach (var location in symbol.OriginalDefinition.Locations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!location.IsInSource ||
                string.IsNullOrWhiteSpace(
                    location.SourceTree?.FilePath))
            {
                continue;
            }

            var uri = new Uri(Path.GetFullPath(
                location.SourceTree!.FilePath));
            AddLocation(
                builder,
                seen,
                new AkburaReferenceLocation(
                    uri,
                    location.SourceSpan,
                    IsDeclaration: true,
                    IsWrite: true));
        }
    }

    private static void AddLocation(
        ImmutableArrayBuilder<AkburaReferenceLocation> builder,
        HashSet<ReferenceLocationIdentity> seen,
        AkburaReferenceLocation location)
    {
        if (seen.Add(new ReferenceLocationIdentity(
                location.Uri,
                location.Span,
                location.IsDeclaration)))
        {
            builder.Add(location);
        }
    }

    private static TextSpan? GetMarkupAttributeNameSpan(
        MarkupAttributeSyntax attribute)
    {
        return attribute switch
        {
            MarkupPlainAttributeSyntax plain =>
                plain.Name.Span,
            MarkupAttachedPropertyAttributeSyntax attached =>
                attached.Name.Span,
            MarkupPrefixedAttributeSyntax prefixed =>
                prefixed.Name.Span,
            _ => null,
        };
    }

    private static TextSpan GetTailwindUtilityNameSpan(
        SourceText text,
        TailwindAttributeSyntax attribute,
        string utilityName)
    {
        var start = attribute switch
        {
            TailwindFullAttributeSyntax full =>
                full.Name.Span.Start,
            TailwindFlagAttributeSyntax flag =>
                flag.Name.Span.Start,
            _ => attribute.Span.Start,
        };
        var maximum = Math.Min(
            utilityName.Length,
            text.Length - start);
        return new TextSpan(
            start,
            Math.Max(1, maximum));
    }

    private static TextSpan GetSymbolNameSpan(
        SourceText text,
        TextSpan sourceSpan,
        string symbolName)
    {
        if (sourceSpan.Length <= 0 ||
            sourceSpan.End > text.Length ||
            string.IsNullOrEmpty(symbolName))
        {
            return sourceSpan;
        }

        var source = text.ToString(sourceSpan);
        var offset = source.IndexOf(
            symbolName,
            StringComparison.Ordinal);
        return offset < 0
            ? sourceSpan
            : new TextSpan(
                sourceSpan.Start + offset,
                symbolName.Length);
    }

    private static bool IsWriteReference(
        ExpressionSyntax syntax)
    {
        for (SyntaxNode? current = syntax;
             current != null;
             current = current.Parent)
        {
            switch (current)
            {
                case AssignmentExpressionSyntax assignment:
                    return assignment.Left.Span.Contains(
                        syntax.Span);

                case PrefixUnaryExpressionSyntax:
                case PostfixUnaryExpressionSyntax:
                    return true;

                case ArgumentSyntax argument:
                    return !argument.RefOrOutKeyword.IsKind(
                        Microsoft.CodeAnalysis.CSharp.SyntaxKind.None);

                case StatementSyntax:
                    return false;
            }
        }

        return false;
    }

    private static AkburaReferenceResult EmptyResult()
    {
        return new AkburaReferenceResult(
            symbol: null,
            name: null,
            ImmutableArray<AkburaReferenceLocation>.Empty);
    }

    private readonly record struct ReferenceCacheKey(
        VersionStamp SolutionVersion,
        AkburaDocumentId DocumentId,
        VersionStamp DocumentVersion);

    private readonly record struct OccurrenceIdentity(
        AkburaSymbolKey Key,
        Uri Uri,
        TextSpan Span,
        bool IsDeclaration);

    private readonly record struct ReferenceLocationIdentity(
        Uri Uri,
        TextSpan Span,
        bool IsDeclaration);

    internal sealed class SemanticOccurrence
    {
        public SemanticOccurrence(
            AkburaSymbolKey key,
            Uri uri,
            TextSpan span,
            string name,
            bool isDeclaration,
            bool isWrite,
            NativeSymbol? nativeSymbol,
            RoslynSymbol? roslynSymbol)
        {
            Key = key;
            Uri = uri;
            Span = span;
            Name = name;
            IsDeclaration = isDeclaration;
            IsWrite = isWrite;
            NativeSymbol = nativeSymbol;
            RoslynSymbol = roslynSymbol;
        }

        public AkburaSymbolKey Key { get; }

        public Uri Uri { get; }

        public TextSpan Span { get; }

        public string Name { get; }

        public bool IsDeclaration { get; }

        public bool IsWrite { get; }

        public NativeSymbol? NativeSymbol { get; }

        public RoslynSymbol? RoslynSymbol { get; }
    }
}
