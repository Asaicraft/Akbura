using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Symbols;

/// <summary>
/// Describes a searchable declaration and its source document.
/// </summary>
public sealed class AkburaWorkspaceSymbol
{
    public AkburaWorkspaceSymbol(
        string name,
        string? detail,
        string? containerName,
        AkburaWorkspaceSymbolKind kind,
        Uri uri,
        TextSpan span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Detail = detail;
        ContainerName = containerName;
        Kind = kind;
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        Span = span;
    }

    public string Name { get; }

    public string? Detail { get; }

    public string? ContainerName { get; }

    public AkburaWorkspaceSymbolKind Kind { get; }

    public Uri Uri { get; }

    public TextSpan Span { get; }
}