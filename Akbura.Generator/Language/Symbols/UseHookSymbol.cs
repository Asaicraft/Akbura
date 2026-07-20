using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class UseHookSymbol : Symbol, IUseHookSymbol
{
    public UseHookSymbol(
        string invocationName,
        IMethodSymbol method,
        IParameterSymbol? selfParameter,
        UseHookSelfKind selfKind,
        ISymbol? containingSymbol = null,
        ImmutableArray<Microsoft.CodeAnalysis.Location> locations = default,
        ImmutableArray<ISymbolDeclarationReference> declaringSyntaxReferences = default,
        bool isImplicitlyDeclared = false)
        : base(containingSymbol, locations, declaringSyntaxReferences, isImplicitlyDeclared)
    {
        if (string.IsNullOrWhiteSpace(invocationName))
        {
            throw new ArgumentException("Hook invocation name cannot be empty.", nameof(invocationName));
        }

        InvocationName = invocationName;
        Method = method ?? throw new ArgumentNullException(nameof(method));
        SelfParameter = selfParameter;
        SelfKind = selfKind;

        if ((selfParameter == null) != (selfKind == UseHookSelfKind.None))
        {
            throw new ArgumentException(
                "The self passing kind must agree with the selected hook method.",
                nameof(selfKind));
        }
    }

    public override SymbolKind Kind => SymbolKind.UseHook;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name => InvocationName;

    public override string MetadataName => Method.MetadataName;

    public override CSharpSymbolDefinition CSharpDefinition => new(Method);

    public string InvocationName { get; }

    public IMethodSymbol Method { get; }

    public ITypeSymbol ReturnType => Method.ReturnType;

    public IParameterSymbol? SelfParameter { get; }

    public UseHookSelfKind SelfKind { get; }

    public bool HasSelfParameter => SelfParameter != null;

    public bool IsSelfImplicit => SelfKind == UseHookSelfKind.Implicit;

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitUseHook(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitUseHook(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitUseHook(this, parameter);
    }

    public override string ToDisplayString()
    {
        return Method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }
}
