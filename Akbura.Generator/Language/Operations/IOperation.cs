using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal interface IOperation : IEquatable<IOperation>
{
    OperationKind Kind { get; }

    OperationLanguage Language { get; }

    AkburaSyntax Syntax { get; }

    IOperation? Parent { get; }

    ImmutableArray<IOperation> Children { get; }

    ISymbol? TargetSymbol { get; }

    ISymbol? TypeSymbol { get; }

    CSharpOperationDefinition CSharpDefinition { get; }

    bool IsImplicit { get; }

    bool HasErrors { get; }

    object? ConstantValue { get; }

    void Accept(OperationVisitor visitor);

    TResult? Accept<TParameter, TResult>(OperationVisitor<TParameter, TResult> visitor, TParameter parameter);

    string ToDisplayString();
}
