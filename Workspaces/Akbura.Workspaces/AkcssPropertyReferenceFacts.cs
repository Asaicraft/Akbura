using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;

namespace Akbura.Workspaces;

internal readonly struct AkcssPropertyReferenceSpans
{
    public AkcssPropertyReferenceSpans(
        TextSpan? ownerSpan,
        TextSpan propertySpan)
    {
        OwnerSpan = ownerSpan;
        PropertySpan = propertySpan;
    }

    public TextSpan? OwnerSpan { get; }

    public TextSpan PropertySpan { get; }
}

internal static class AkcssPropertyReferenceFacts
{
    public static bool TryGetSpans(
        CSharpTypeSyntax syntax,
        out AkcssPropertyReferenceSpans spans)
    {
        CSharp.TypeSyntax csharpSyntax;
        try
        {
            csharpSyntax = syntax.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            spans = default;
            return false;
        }

        var sourceOffset = syntax.Tokens.Span.Start -
            csharpSyntax.FullSpan.Start;
        switch (csharpSyntax)
        {
            case CSharp.IdentifierNameSyntax identifier:
                spans = new AkcssPropertyReferenceSpans(
                    ownerSpan: null,
                    Map(identifier.Identifier.Span, sourceOffset));
                return true;

            case CSharp.GenericNameSyntax generic:
                spans = new AkcssPropertyReferenceSpans(
                    ownerSpan: null,
                    Map(generic.Identifier.Span, sourceOffset));
                return true;

            case CSharp.QualifiedNameSyntax qualified:
                spans = new AkcssPropertyReferenceSpans(
                    Map(qualified.Left.Span, sourceOffset),
                    Map(qualified.Right.Identifier.Span, sourceOffset));
                return true;

            case CSharp.AliasQualifiedNameSyntax alias:
                spans = new AkcssPropertyReferenceSpans(
                    Map(alias.Alias.Span, sourceOffset),
                    Map(alias.Name.Identifier.Span, sourceOffset));
                return true;

            default:
                spans = default;
                return false;
        }
    }

    public static ITypeSymbol? GetPropertyOwnerType(
        AkburaPropertySymbol property)
    {
        return property.WriteDefinition.Symbol?.ContainingType ??
            property.ReadDefinition.Symbol?.ContainingType ??
            property.ClrPropertyDefinition.Symbol?.ContainingType ??
            property.AttachedSetterDefinition.Symbol?.ContainingType ??
            property.AttachedGetterDefinition.Symbol?.ContainingType ??
            property.AvaloniaPropertyDefinition.Symbol?.ContainingType;
    }

    private static TextSpan Map(TextSpan span, int sourceOffset)
    {
        return new TextSpan(sourceOffset + span.Start, span.Length);
    }
}
