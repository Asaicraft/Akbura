using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBinder
{
    internal CSharpProbeScope CreateProbeScope(
        AkburaSyntax scope,
        SyntaxNode csharpNode,
        ImmutableArray<string> excludedNames = default)
    {
        if (scope == null ||
            csharpNode == null)
        {
            return CSharpProbeScope.Empty;
        }

        var names = CollectIdentifierNames(csharpNode, excludedNames);
        if (names.IsDefaultOrEmpty)
        {
            return CSharpProbeScope.Empty;
        }

        using var memberDeclarations = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        using var localStatements = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        var addedNames = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = BindingDiagnosticBag.GetInstance();

        foreach (var name in names)
        {
            if (!addedNames.Add(name))
            {
                continue;
            }

            var symbol = Next?.LookupSymbol(
                name,
                BinderLookupOptions.None,
                scope,
                diagnostics).Symbol;
            AddProbeSymbol(
                symbol,
                memberDeclarations,
                localStatements);
        }

        return new CSharpProbeScope(
            memberDeclarations.ToImmutable(),
            localStatements.ToImmutable());
    }

    private static ImmutableArray<string> CollectIdentifierNames(
        SyntaxNode node,
        ImmutableArray<string> excludedNames)
    {
        using var builder = ImmutableArrayBuilder<string>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!excludedNames.IsDefaultOrEmpty)
        {
            foreach (var excludedName in excludedNames)
            {
                if (!string.IsNullOrWhiteSpace(excludedName))
                {
                    seen.Add(excludedName);
                }
            }
        }

        AddDeclaredIdentifierNames(node, seen);

        foreach (var identifier in node.DescendantNodesAndSelf().OfType<CSharp.IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.ValueText;
            if (!string.IsNullOrWhiteSpace(name) &&
                seen.Add(name))
            {
                builder.Add(name);
            }
        }

        return builder.ToImmutable();
    }

    private static void AddDeclaredIdentifierNames(
        SyntaxNode node,
        HashSet<string> names)
    {
        foreach (var parameter in node.DescendantNodesAndSelf().OfType<CSharp.ParameterSyntax>())
        {
            AddDeclaredIdentifierName(names, parameter.Identifier.ValueText);
        }

        foreach (var variable in node.DescendantNodesAndSelf().OfType<CSharp.VariableDeclaratorSyntax>())
        {
            AddDeclaredIdentifierName(names, variable.Identifier.ValueText);
        }

        foreach (var foreachStatement in node.DescendantNodesAndSelf().OfType<CSharp.ForEachStatementSyntax>())
        {
            AddDeclaredIdentifierName(names, foreachStatement.Identifier.ValueText);
        }

        foreach (var designation in node.DescendantNodesAndSelf().OfType<CSharp.SingleVariableDesignationSyntax>())
        {
            AddDeclaredIdentifierName(names, designation.Identifier.ValueText);
        }
    }

    private static void AddDeclaredIdentifierName(
        HashSet<string> names,
        string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name);
        }
    }

    private static ImmutableArray<CSharp.MemberDeclarationSyntax> AddProbeMethod(
        ImmutableArray<CSharp.MemberDeclarationSyntax> memberDeclarations,
        CSharp.MethodDeclarationSyntax method)
    {
        if (memberDeclarations.IsDefaultOrEmpty)
        {
            return ImmutableArray.Create<CSharp.MemberDeclarationSyntax>(method);
        }

        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        foreach (var memberDeclaration in memberDeclarations)
        {
            builder.Add(memberDeclaration);
        }

        builder.Add(method);
        return builder.ToImmutable();
    }

    private static CSharp.BlockSyntax CreateProbeBlock(
        ImmutableArray<CSharp.StatementSyntax> localStatements,
        CSharp.StatementSyntax statement)
    {
        if (localStatements.IsDefaultOrEmpty)
        {
            return CSharpSyntaxFactory.Block(statement);
        }

        using var builder = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        foreach (var localStatement in localStatements)
        {
            builder.Add(localStatement);
        }

        builder.Add(statement);
        return CSharpSyntaxFactory.Block(CSharpSyntaxFactory.List(builder.ToImmutable()));
    }

    private static void AddProbeSymbol(
        AkburaSymbol? symbol,
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> memberDeclarations,
        ImmutableArrayBuilder<CSharp.StatementSyntax> localStatements)
    {
        if (symbol == null)
        {
            return;
        }

        switch (symbol.Kind)
        {
            case AkburaSymbolKind.State:
            {
                var state = (IStateSymbol)symbol;
                AddProbeLocal(localStatements, state.Name, state.Type);
                break;
            }

            case AkburaSymbolKind.Parameter:
            {
                var parameter = (IParamSymbol)symbol;
                AddProbeLocal(localStatements, parameter.Name, parameter.Type);
                break;
            }

            case AkburaSymbolKind.CommandParameter:
            {
                var parameter = (ICommandParameterSymbol)symbol;
                AddProbeLocal(localStatements, parameter.Name, parameter.Type);
                break;
            }

            case AkburaSymbolKind.TailwindUtilityParameter:
            {
                var parameter = (ITailwindUtilityParameterSymbol)symbol;
                AddProbeLocal(localStatements, parameter.Name, parameter.Type);
                break;
            }

            case AkburaSymbolKind.InjectedService:
            {
                var inject = (IInjectSymbol)symbol;
                AddProbeLocal(localStatements, inject.Name, inject.Type);
                break;
            }

            case AkburaSymbolKind.CSharpSymbol:
            {
                var local = (CSharpLocalSymbol)symbol;
                AddProbeLocal(localStatements, local.Name, new CSharpSymbolDefinition(local.Local.Type));
                break;
            }

            case AkburaSymbolKind.Command:
            {
                var command = (ICommandSymbol)symbol;
                AddCommandProbeMembers(memberDeclarations, command);
                break;
            }
        }
    }

    private static void AddProbeLocal(
        ImmutableArrayBuilder<CSharp.StatementSyntax> localStatements,
        string name,
        CSharpSymbolDefinition type)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            type.Symbol is not ITypeSymbol typeSymbol)
        {
            return;
        }

        var typeSyntax = CSharpSyntaxFactory.ParseTypeName(
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        localStatements.Add(CSharpSyntaxFactory.LocalDeclarationStatement(
            CSharpSyntaxFactory.VariableDeclaration(typeSyntax)
                .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                    CSharpSyntaxFactory.VariableDeclarator(CSharpSyntaxFactory.Identifier(name))
                        .WithInitializer(CSharpSyntaxFactory.EqualsValueClause(
                            CSharpSyntaxFactory.LiteralExpression(CSharpSyntaxKind.DefaultLiteralExpression)))))));
    }

    private static void AddCommandProbeMembers(
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> memberDeclarations,
        ICommandSymbol command)
    {
        var commandTypeName = "__AkburaCommand_" + ToCSharpIdentifier(command.Name);
        var commandType = CSharpSyntaxFactory.IdentifierName(commandTypeName);
        memberDeclarations.Add(CSharpSyntaxFactory.ClassDeclaration(commandTypeName)
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(CSharpSyntaxKind.PrivateKeyword),
                CSharpSyntaxFactory.Token(CSharpSyntaxKind.SealedKeyword)))
            .WithMembers(CSharpSyntaxFactory.List(CreateCommandProbeTypeMembers(command))));
        memberDeclarations.Add(CreateProbeField(commandType, command.Name));
    }

    private static ImmutableArray<CSharp.MemberDeclarationSyntax> CreateCommandProbeTypeMembers(ICommandSymbol command)
    {
        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();

        builder.Add(CSharpSyntaxFactory.PropertyDeclaration(
                CSharpSyntaxFactory.ParseTypeName("global::System.IObservable<bool>"),
                "IsExecuting")
            .WithModifiers(CSharpSyntaxFactory.TokenList(CSharpSyntaxFactory.Token(CSharpSyntaxKind.PublicKeyword)))
            .WithExpressionBody(CSharpSyntaxFactory.ArrowExpressionClause(
                CSharpSyntaxFactory.LiteralExpression(CSharpSyntaxKind.DefaultLiteralExpression)))
            .WithSemicolonToken(CSharpSyntaxFactory.Token(CSharpSyntaxKind.SemicolonToken)));

        builder.Add(CSharpSyntaxFactory.PropertyDeclaration(
                CSharpSyntaxFactory.ParseTypeName("global::System.IObservable<bool>"),
                "CanExecute")
            .WithModifiers(CSharpSyntaxFactory.TokenList(CSharpSyntaxFactory.Token(CSharpSyntaxKind.PublicKeyword)))
            .WithExpressionBody(CSharpSyntaxFactory.ArrowExpressionClause(
                CSharpSyntaxFactory.LiteralExpression(CSharpSyntaxKind.DefaultLiteralExpression)))
            .WithSemicolonToken(CSharpSyntaxFactory.Token(CSharpSyntaxKind.SemicolonToken)));

        builder.Add(CSharpSyntaxFactory.MethodDeclaration(
                GetCommandExecuteReturnTypeSyntax(command),
                "Execute")
            .WithModifiers(CSharpSyntaxFactory.TokenList(CSharpSyntaxFactory.Token(CSharpSyntaxKind.PublicKeyword)))
            .WithParameterList(CreateCommandExecuteParameterList(command))
            .WithBody(CSharpSyntaxFactory.Block(CSharpSyntaxFactory.ThrowStatement(
                CSharpSyntaxFactory.ObjectCreationExpression(
                        CSharpSyntaxFactory.ParseTypeName("global::System.NotImplementedException"))
                    .WithArgumentList(CSharpSyntaxFactory.ArgumentList())))));

        return builder.ToImmutable();
    }

    private static CSharp.ParameterListSyntax CreateCommandExecuteParameterList(ICommandSymbol command)
    {
        if (command.Parameters.IsDefaultOrEmpty)
        {
            return CSharpSyntaxFactory.ParameterList();
        }

        using var builder = ImmutableArrayBuilder<CSharp.ParameterSyntax>.Rent();
        foreach (var parameter in command.Parameters.OrderBy(parameter => parameter.Ordinal))
        {
            var type = parameter.Type.Symbol is ITypeSymbol typeSymbol
                ? CSharpSyntaxFactory.ParseTypeName(
                    typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                : CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(CSharpSyntaxKind.ObjectKeyword));
            builder.Add(CSharpSyntaxFactory.Parameter(
                    CSharpSyntaxFactory.Identifier(parameter.Name))
                .WithType(type));
        }

        return CSharpSyntaxFactory.ParameterList(
            CSharpSyntaxFactory.SeparatedList(builder.ToImmutable()));
    }

    private static CSharp.TypeSyntax GetCommandExecuteReturnTypeSyntax(ICommandSymbol command)
    {
        if (command.HasResult &&
            command.ResultType.Symbol is ITypeSymbol resultType)
        {
            return CSharpSyntaxFactory.ParseTypeName(
                "global::System.Threading.Tasks.ValueTask<" +
                resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ">");
        }

        return CSharpSyntaxFactory.ParseTypeName("global::System.Threading.Tasks.ValueTask");
    }

    private static CSharp.FieldDeclarationSyntax CreateProbeField(
        CSharp.TypeSyntax type,
        string name)
    {
        return CSharpSyntaxFactory.FieldDeclaration(
                CSharpSyntaxFactory.VariableDeclaration(type)
                    .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                        CSharpSyntaxFactory.VariableDeclarator(CSharpSyntaxFactory.Identifier(name)))))
            .WithModifiers(CSharpSyntaxFactory.TokenList(CSharpSyntaxFactory.Token(CSharpSyntaxKind.PrivateKeyword)));
    }

    private static string ToCSharpIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "_";
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            builder.Append(index == 0
                ? CSharpSyntaxFacts.IsIdentifierStartCharacter(ch) ? ch : '_'
                : CSharpSyntaxFacts.IsIdentifierPartCharacter(ch) ? ch : '_');
        }

        return builder.ToString();
    }

}

internal readonly struct CSharpProbeScope
{
    public static readonly CSharpProbeScope Empty = new(
        ImmutableArray<CSharp.MemberDeclarationSyntax>.Empty,
        ImmutableArray<CSharp.StatementSyntax>.Empty);

    public CSharpProbeScope(
        ImmutableArray<CSharp.MemberDeclarationSyntax> memberDeclarations,
        ImmutableArray<CSharp.StatementSyntax> localStatements)
    {
        MemberDeclarations = memberDeclarations.IsDefault
            ? ImmutableArray<CSharp.MemberDeclarationSyntax>.Empty
            : memberDeclarations;
        LocalStatements = localStatements.IsDefault
            ? ImmutableArray<CSharp.StatementSyntax>.Empty
            : localStatements;
    }

    public ImmutableArray<CSharp.MemberDeclarationSyntax> MemberDeclarations { get; }

    public ImmutableArray<CSharp.StatementSyntax> LocalStatements { get; }
}
