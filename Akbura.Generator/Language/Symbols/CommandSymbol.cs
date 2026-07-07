using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class CommandSymbol : Symbol, ICommandSymbol
{
    public CommandSymbol(
        CommandDeclarationSyntax declarationSyntax,
        CSharpSymbolDefinition returnType,
        CSharpSymbolDefinition resultType,
        ImmutableArray<ICommandParameterSymbol> parameters,
        bool isVoid,
        bool isAsyncLike,
        bool hasResult,
        ISymbol? containingSymbol = null,
        ImmutableArray<Microsoft.CodeAnalysis.Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        DeclarationSyntax = declarationSyntax ?? throw new ArgumentNullException(nameof(declarationSyntax));

        var name = declarationSyntax.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Command symbol name cannot be empty.", nameof(declarationSyntax));
        }

        Name = name;
        ReturnType = returnType;
        ResultType = resultType;
        Parameters = parameters.IsDefault
            ? ImmutableArray<ICommandParameterSymbol>.Empty
            : parameters;
        IsVoid = isVoid;
        IsAsyncLike = isAsyncLike;
        HasResult = hasResult;
    }

    public override SymbolKind Kind => SymbolKind.Command;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public CommandDeclarationSyntax DeclarationSyntax { get; }

    public CSharpSymbolDefinition ReturnType { get; }

    public CSharpSymbolDefinition ResultType { get; }

    public ImmutableArray<ICommandParameterSymbol> Parameters { get; }

    public bool IsVoid { get; }

    public bool IsAsyncLike { get; }

    public bool HasResult { get; }

    public bool SupportsIsExecuting => true;

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitCommand(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitCommand(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitCommand(this, parameter);
    }

    public override string ToDisplayString()
    {
        var returnType = ReturnType.IsDefault ? "unknown" : ReturnType.Name;
        return $"command {returnType} {Name}";
    }
}
