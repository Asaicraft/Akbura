using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RoslynOperationKind = Microsoft.CodeAnalysis.OperationKind;

namespace Akbura.Language.Operations;

internal sealed class CSharpOperation : ICSharpOperation
{
    private ImmutableArray<IOperation> _children;

    internal CSharpOperation(
        AkburaSyntax syntax,
        IOperation? parent,
        CSharpOperationDefinition csharpDefinition,
        CSharpSymbolDefinition csharpTargetDefinition,
        CSharpSymbolDefinition csharpTypeDefinition,
        ISymbol? targetSymbol,
        bool hasErrors)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Parent = parent;
        CSharpDefinition = csharpDefinition;
        CSharpTargetDefinition = csharpTargetDefinition;
        CSharpTypeDefinition = csharpTypeDefinition;
        TargetSymbol = targetSymbol;
        HasErrors = hasErrors;
        _children = ImmutableArray<IOperation>.Empty;
    }

    public OperationKind Kind => OperationKind.CSharpExpression;

    public OperationLanguage Language => OperationLanguage.CSharp;

    public AkburaSyntax Syntax { get; }

    public IOperation? Parent { get; private set; }

    public ImmutableArray<IOperation> Children => _children;

    public ISymbol? TargetSymbol { get; }

    public ISymbol? TypeSymbol => null;

    public CSharpOperationDefinition CSharpDefinition { get; }

    public CSharpSymbolDefinition CSharpTargetDefinition { get; }

    public CSharpSymbolDefinition CSharpTypeDefinition { get; }

    public bool IsImplicit => CSharpDefinition.Operation?.IsImplicit ?? false;

    public bool HasErrors { get; private set; }

    public object? ConstantValue => CSharpDefinition.ConstantValue.HasValue
        ? CSharpDefinition.ConstantValue.Value
        : null;

    public RoslynOperationKind RoslynKind => CSharpDefinition.Kind ?? RoslynOperationKind.None;

    public Microsoft.CodeAnalysis.SyntaxNode CSharpSyntax => CSharpDefinition.Syntax!;

    internal void SetChildren(ImmutableArray<IOperation> children)
    {
        _children = children.IsDefault
            ? ImmutableArray<IOperation>.Empty
            : children;

        for (var i = 0; i < _children.Length; i++)
        {
            if (_children[i].HasErrors)
            {
                HasErrors = true;
                break;
            }
        }
    }

    internal void SetParent(IOperation? parent)
    {
        Parent = parent;
    }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitCSharpOperation(this);
    }

    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitCSharpOperation(this, parameter);
    }

    public bool Equals(IOperation? other)
    {
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj)
    {
        return obj is IOperation operation && Equals(operation);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public string ToDisplayString()
    {
        return CSharpDefinition.ToDisplayString();
    }

    public override string ToString()
    {
        return ToDisplayString();
    }
}
