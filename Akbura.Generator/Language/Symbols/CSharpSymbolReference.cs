using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.Symbols;

internal readonly struct CSharpSymbolReference
{
    public CSharpSymbolReference(
        CSharp.ExpressionSyntax syntax,
        TextSpan sourceSpan,
        CSharpSymbolDefinition csharpDefinition,
        ISymbol? akburaSymbol,
        string? name = null)
    {
        Syntax = syntax ??
            throw new ArgumentNullException(
                nameof(syntax));

        SourceSpan = sourceSpan;
        CSharpDefinition = csharpDefinition;
        AkburaSymbol = akburaSymbol;

        Name = string.IsNullOrWhiteSpace(name)
            ? csharpDefinition.Name
            : name!;
    }

    public CSharp.ExpressionSyntax Syntax { get; }

    /// <summary>
    /// Gets the span inside the generated C# semantic probe.
    /// </summary>
    public TextSpan Span => Syntax.Span;

    /// <summary>
    /// Gets the span inside the original Akbura document.
    /// </summary>
    public TextSpan SourceSpan { get; }

    public string Name { get; }

    public CSharpSymbolDefinition CSharpDefinition { get; }

    public ISymbol? AkburaSymbol { get; }

    public bool IsAkburaSymbol =>
        AkburaSymbol != null;
}