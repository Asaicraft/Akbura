using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Resources;

internal enum LocalResourceScopeKind
{
    Element,
    Application,
    Style,
    ResourceDictionary,
}

internal readonly struct LocalResourceScopeFact
{
    public LocalResourceScopeFact(int id, int? parentScopeId, LocalResourceScopeKind kind, TextSpan ownerSpan, TextSpan resourcesSpan, bool isDocumentRoot)
    {
        Id = id;
        ParentScopeId = parentScopeId;
        Kind = kind;
        OwnerSpan = ownerSpan;
        ResourcesSpan = resourcesSpan;
        IsDocumentRoot = isDocumentRoot;
    }

    public int Id { get; }

    public int? ParentScopeId { get; }

    public LocalResourceScopeKind Kind { get; }

    public TextSpan OwnerSpan { get; }

    public TextSpan ResourcesSpan { get; }

    public bool IsDocumentRoot { get; }
}

internal readonly struct LocalResourceDeclarationFact
{
    public LocalResourceDeclarationFact(string key, string typeName, string? typeNamespace, int scopeId, string? themeVariant, TextSpan declarationSpan, TextSpan keySpan)
    {
        Key = key;
        TypeName = typeName;
        TypeNamespace = typeNamespace;
        ScopeId = scopeId;
        ThemeVariant = themeVariant;
        DeclarationSpan = declarationSpan;
        KeySpan = keySpan;
    }

    public string Key { get; }

    public string TypeName { get; }

    public string? TypeNamespace { get; }

    public int ScopeId { get; }

    public string? ThemeVariant { get; }

    public TextSpan DeclarationSpan { get; }

    public TextSpan KeySpan { get; }
}

internal readonly struct LocalResourceImportFact
{
    public LocalResourceImportFact(string source, int scopeId, TextSpan directiveSpan, TextSpan sourceSpan)
    {
        Source = source;
        ScopeId = scopeId;
        DirectiveSpan = directiveSpan;
        SourceSpan = sourceSpan;
    }

    public string Source { get; }

    public int ScopeId { get; }

    public TextSpan DirectiveSpan { get; }

    public TextSpan SourceSpan { get; }
}

internal readonly struct LocalApplicationResourceRootFact
{
    public LocalApplicationResourceRootFact(TextSpan span, int? scopeId)
    {
        Span = span;
        ScopeId = scopeId;
    }

    public TextSpan Span { get; }

    public int? ScopeId { get; }
}

internal sealed class LocalResourceFacts
{
    public LocalResourceFacts(ResourcePathFacts path, ImmutableArray<LocalResourceDeclarationFact> declarations, ImmutableArray<LocalResourceImportFact> imports, ImmutableArray<LocalResourceScopeFact> scopes, LocalApplicationResourceRootFact? applicationRoot)
    {
        Path = path;
        Declarations = declarations.IsDefault
            ? ImmutableArray<LocalResourceDeclarationFact>.Empty
            : declarations;
        Imports = imports.IsDefault
            ? ImmutableArray<LocalResourceImportFact>.Empty
            : imports;
        Scopes = scopes.IsDefault
            ? ImmutableArray<LocalResourceScopeFact>.Empty
            : scopes;
        ApplicationRoot = applicationRoot;
    }

    public ResourcePathFacts Path { get; }

    public ImmutableArray<LocalResourceDeclarationFact> Declarations { get; }

    public ImmutableArray<LocalResourceImportFact> Imports { get; }

    public ImmutableArray<LocalResourceScopeFact> Scopes { get; }

    public LocalApplicationResourceRootFact? ApplicationRoot { get; }

    public bool TryGetRootScopeId(out int scopeId)
    {
        if (ApplicationRoot is { ScopeId: int applicationScopeId })
        {
            scopeId = applicationScopeId;
            return true;
        }

        foreach (var scope in Scopes)
        {
            if (scope.IsDocumentRoot &&
                (scope.Kind == LocalResourceScopeKind.ResourceDictionary ||
                 scope.Kind == LocalResourceScopeKind.Style))
            {
                scopeId = scope.Id;
                return true;
            }
        }

        scopeId = -1;
        return false;
    }

    public bool IsScopeVisibleFromRoot(int scopeId)
    {
        return TryGetRootScopeId(out var rootScopeId) &&
            scopeId == rootScopeId;
    }
}
