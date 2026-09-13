using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    /// <summary>Returns the locals declared by this statement through the existing bound tree.</summary>
    public ImmutableArray<CSharpLocalSymbol> GetCSharpDeclaredLocals(CSharpStatementSyntax syntax)
    {
        ValidateSyntaxTreeOwnership(syntax);
        var statement = BindingSession.BindSemanticSyntax(syntax);
        using var locals = ImmutableArrayBuilder<CSharpLocalSymbol>.Rent();
        var seen = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        if (statement is BoundLocalDeclarationStatement declaration)
        {
            foreach (var local in declaration.Locals)
            {
                locals.Add(new CSharpLocalSymbol(local, syntax));
                seen.Add(local);
            }
        }

        var operation = statement switch
        {
            BoundLocalDeclarationStatement local => local.BindingResult.OperationDefinition.Operation,
            BoundCSharpStatement other => other.BindingResult.OperationDefinition.Operation,
            _ => null,
        };
        if (operation?.SemanticModel is { } semanticModel)
        {
            CollectCSharpStatementDeclaredLocals(operation, operation, semanticModel, syntax, seen, locals);
        }

        return locals.ToImmutable();
    }

    /// <summary>Returns declarations from the already-bound real markup condition.</summary>
    public ImmutableArray<CSharpLocalSymbol> GetCSharpDeclaredLocals(CSharpExpressionSyntax syntax)
    {
        ValidateSyntaxTreeOwnership(syntax);
        var conditional = syntax.Parent switch
        {
            MarkupIfStatementSyntax statement => statement,
            MarkupElseIfClauseSyntax clause => clause.Parent as MarkupIfStatementSyntax,
            _ => null,
        };
        if (conditional == null || GetOperation(conditional) is not IMarkupIfOperation operation)
        {
            return [];
        }

        foreach (var branch in operation.Branches)
        {
            if (ReferenceEquals(branch.ConditionSyntax, syntax) && branch.Condition.Operation is { } condition)
            {
                using var locals = ImmutableArrayBuilder<CSharpLocalSymbol>.Rent();
                var seen = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                CollectCSharpConditionDeclaredLocals(condition, syntax, seen, locals);
                return locals.ToImmutable();
            }
        }

        return [];
    }

    private static void CollectCSharpConditionDeclaredLocals(Microsoft.CodeAnalysis.IOperation operation,
        CSharpExpressionSyntax syntax, HashSet<ILocalSymbol> seen,
        ImmutableArrayBuilder<CSharpLocalSymbol> locals)
    {
        if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation or IBlockOperation)
        {
            return;
        }

        if (GetCSharpOperationDeclaredLocal(operation) is { } local && seen.Add(local))
        {
            locals.Add(new CSharpLocalSymbol(local, syntax));
        }

        foreach (var child in operation.ChildOperations)
        {
            CollectCSharpConditionDeclaredLocals(child, syntax, seen, locals);
        }
    }

    private static void CollectCSharpStatementDeclaredLocals(Microsoft.CodeAnalysis.IOperation operation,
        Microsoft.CodeAnalysis.IOperation root, Microsoft.CodeAnalysis.SemanticModel semanticModel,
        CSharpStatementSyntax syntax, HashSet<ILocalSymbol> seen,
        ImmutableArrayBuilder<CSharpLocalSymbol> locals)
    {
        if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation or IBlockOperation)
        {
            return;
        }

        var local = GetCSharpOperationDeclaredLocal(operation);
        if (local != null && !seen.Contains(local))
        {
            // Roslyn decides the real lexical scope; loop/body/lambda locals are not exports.
            foreach (var visible in semanticModel.LookupSymbols(root.Syntax.Span.End, name: local.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(visible, local))
                {
                    seen.Add(local);
                    locals.Add(new CSharpLocalSymbol(local, syntax));
                    break;
                }
            }
        }

        foreach (var child in operation.ChildOperations)
        {
            CollectCSharpStatementDeclaredLocals(child, root, semanticModel, syntax, seen, locals);
        }
    }

    private static ILocalSymbol? GetCSharpOperationDeclaredLocal(Microsoft.CodeAnalysis.IOperation operation)
    {
        return operation switch
        {
            IVariableDeclaratorOperation declaration => declaration.Symbol,
            ILocalReferenceOperation { IsDeclaration: true } reference => reference.Local,
            IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol declaration } => declaration,
            IRecursivePatternOperation { DeclaredSymbol: ILocalSymbol declaration } => declaration,
            _ => null,
        };
    }
}
