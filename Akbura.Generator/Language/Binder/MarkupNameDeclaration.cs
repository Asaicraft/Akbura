using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Threading;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Binder;

internal sealed class MarkupNameDeclaration
{
    private IMarkupNameSymbol? _lazySymbol;

    private MarkupNameDeclaration(
        MarkupElementSyntax element,
        MarkupAttachedPropertyAttributeSyntax attribute,
        string name,
        string identifierText,
        string sourceText,
        MarkupNameDeclaration? originalDeclaration,
        MarkupNameDeclarationFailure failure)
    {
        Element = element;
        Attribute = attribute;
        Name = name;
        IdentifierText = identifierText;
        SourceText = sourceText;
        OriginalDeclaration = originalDeclaration;
        Failure = failure;
    }

    public MarkupElementSyntax Element { get; }

    public MarkupAttachedPropertyAttributeSyntax Attribute { get; }

    public string Name { get; }

    public string IdentifierText { get; }

    public string SourceText { get; }

    public MarkupNameDeclaration? OriginalDeclaration { get; }

    public MarkupNameDeclarationFailure Failure { get; }

    public bool IsValid => Failure == MarkupNameDeclarationFailure.None;

    public static MarkupNameDeclaration Create(
        MarkupElementSyntax element,
        MarkupAttachedPropertyAttributeSyntax attribute,
        MarkupNameDeclaration? originalDeclaration,
        bool isInsideTemplateContent)
    {
        var value = AkburaSemanticModel.GetMarkupAttributeValue(attribute);
        var sourceText = value?.ToFullString().Trim() ?? string.Empty;
        if (value is not MarkupLiteralAttributeValueSyntax literalValue)
        {
            return new MarkupNameDeclaration(
                element,
                attribute,
                string.Empty,
                string.Empty,
                sourceText,
                originalDeclaration: null,
                MarkupNameDeclarationFailure.InvalidIdentifier);
        }

        var identifierText = AkburaSemanticModel.GetMarkupLiteralAttributeValueText(literalValue);
        var parsedName = CSharpSyntaxFactory.ParseName(identifierText);
        if (parsedName is not CSharp.IdentifierNameSyntax identifierName)
        {
            return new MarkupNameDeclaration(
                element,
                attribute,
                string.Empty,
                identifierText,
                identifierText,
                originalDeclaration: null,
                MarkupNameDeclarationFailure.InvalidIdentifier);
        }

        if (parsedName.ContainsDiagnostics ||
            identifierName.Identifier.Text != identifierText ||
            !CSharpSyntaxFacts.IsValidIdentifier(identifierText))
        {
            return new MarkupNameDeclaration(
                element,
                attribute,
                string.Empty,
                identifierText,
                identifierText,
                originalDeclaration: null,
                MarkupNameDeclarationFailure.InvalidIdentifier);
        }

        var name = identifierName.Identifier.ValueText;
        return new MarkupNameDeclaration(
            element,
            attribute,
            name,
            identifierName.Identifier.Text,
            identifierText,
            originalDeclaration,
            originalDeclaration != null
                ? MarkupNameDeclarationFailure.Duplicate
                : isInsideTemplateContent
                    ? MarkupNameDeclarationFailure.InsideTemplateContent
                    : MarkupNameDeclarationFailure.None);
    }

    public IMarkupNameSymbol? GetOrCreateSymbol(AkburaSemanticModel semanticModel)
    {
        if (!IsValid)
        {
            return null;
        }

        var symbol = Volatile.Read(ref _lazySymbol);
        if (symbol != null)
        {
            return symbol;
        }

        if (!semanticModel.TryGetMarkupElementReferenceType(Element, out var type))
        {
            return null;
        }

        var created = new MarkupNameSymbol(
            Name,
            IdentifierText,
            type,
            Attribute,
            Element);
        return Interlocked.CompareExchange(ref _lazySymbol, created, comparand: null) ?? created;
    }

    public AkburaSemanticDiagnostic? CreateDiagnostic()
    {
        return Failure switch
        {
            MarkupNameDeclarationFailure.InvalidIdentifier => new AkburaSemanticDiagnostic(
                (AkburaSyntax?)Attribute.Value ?? Attribute,
                ErrorCodes.AKBURA_SEMANTIC_MarkupNameInvalid,
                ImmutableArray.Create<object?>(SourceText)),
            MarkupNameDeclarationFailure.Duplicate => new AkburaSemanticDiagnostic(
                Attribute,
                ErrorCodes.AKBURA_SEMANTIC_MarkupNameDuplicate,
                ImmutableArray.Create<object?>(IdentifierText)),
            MarkupNameDeclarationFailure.InsideTemplateContent => new AkburaSemanticDiagnostic(
                Attribute,
                ErrorCodes.AKBURA_SEMANTIC_MarkupNameInsideTemplateContent,
                ImmutableArray.Create<object?>(IdentifierText)),
            _ => null,
        };
    }
}

internal enum MarkupNameDeclarationFailure : byte
{
    None = 0,
    InvalidIdentifier,
    Duplicate,
    InsideTemplateContent,
}
