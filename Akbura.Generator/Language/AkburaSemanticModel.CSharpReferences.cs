using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language;

internal sealed partial class AkburaSemanticModel
{
    public ImmutableArray<CSharpSymbolReference> GetCSharpSymbolReferences(CSharpStatementSyntax statementSyntax)
    {
        if (statementSyntax == null)
        {
            throw new ArgumentNullException(nameof(statementSyntax));
        }

        ValidateSyntaxTreeOwnership(statementSyntax);

        var statement = ParseCSharpStatement(statementSyntax);
        if (statement == null)
        {
            return ImmutableArray<CSharpSymbolReference>.Empty;
        }

        using var classMembersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        using var statementsBuilder = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        var akburaSymbolsByName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        var akburaSymbolsByCommandTypeName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        foreach (var local in CreateCSharpProbeMembersBefore(
            statementSyntax,
            classMembersBuilder,
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName))
        {
            statementsBuilder.Add(local);
        }

        statementsBuilder.Add(statement);

        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaSemanticProbe")
            .WithBody(CSharpSyntaxFactory.Block(CSharpSyntaxFactory.List(statementsBuilder.ToImmutable())));

        classMembersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(classMembersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);

        var parseOptions = Compilation.CSharpCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ??
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        var syntaxTree = CSharpSyntaxTree.Create(compilationUnit, parseOptions);
        var probeCompilation = Compilation.CSharpCompilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = probeCompilation.GetSemanticModel(syntaxTree);
        var probeStatement = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.MethodDeclarationSyntax>()
            .Single(methodDeclaration => methodDeclaration.Identifier.ValueText == "__AkburaSemanticProbe")
            .Body!
            .Statements
            .Last();

        using var references = ImmutableArrayBuilder<CSharpSymbolReference>.Rent();
        var seenReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in probeStatement.DescendantNodesAndSelf().OfType<CSharp.IdentifierNameSyntax>())
        {
            AddCSharpSymbolReference(
                references,
                seenReferences,
                identifier,
                semanticModel.GetSymbolInfo(identifier).Symbol,
                akburaSymbolsByName,
                akburaSymbolsByCommandTypeName);
        }

        foreach (var memberAccess in probeStatement.DescendantNodesAndSelf().OfType<CSharp.MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Name is CSharp.IdentifierNameSyntax name)
            {
                var symbol = GetBestSymbolInfo(semanticModel, memberAccess);
                if (symbol == null &&
                    memberAccess.Parent is CSharp.InvocationExpressionSyntax invocation &&
                    invocation.Expression == memberAccess)
                {
                    symbol = GetBestSymbolInfo(semanticModel, invocation);
                }

                AddCSharpSymbolReference(
                    references,
                    seenReferences,
                    name,
                    symbol,
                    akburaSymbolsByName,
                    akburaSymbolsByCommandTypeName);
            }
        }

        return references.ToImmutable();
    }

    private static RoslynSymbol? GetBestSymbolInfo(
        Microsoft.CodeAnalysis.SemanticModel semanticModel,
        CSharp.ExpressionSyntax syntax)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(syntax);
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }

    private static void AddCSharpSymbolReference(
        ImmutableArrayBuilder<CSharpSymbolReference> references,
        HashSet<string> seenReferences,
        CSharp.ExpressionSyntax syntax,
        RoslynSymbol? symbol,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        if (symbol == null)
        {
            return;
        }

        var key = syntax.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":" +
            syntax.Span.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":" +
            symbol.Kind.ToString() +
            ":" +
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!seenReferences.Add(key))
        {
            return;
        }

        references.Add(new CSharpSymbolReference(
            syntax,
            new CSharpSymbolDefinition(symbol),
            TryGetReferencedAkburaSymbol(
                symbol,
                akburaSymbolsByName,
                akburaSymbolsByCommandTypeName),
            GetCSharpReferenceName(syntax)));
    }

    private static string GetCSharpReferenceName(CSharp.ExpressionSyntax syntax)
    {
        return syntax switch
        {
            CSharp.IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
            CSharp.GenericNameSyntax genericName => genericName.Identifier.ValueText,
            CSharp.MemberAccessExpressionSyntax memberAccess => GetCSharpReferenceName(memberAccess.Name),
            _ => syntax.ToString()
        };
    }

    private static CSharp.StatementSyntax? ParseCSharpStatement(CSharpStatementSyntax statementSyntax)
    {
        var text = statementSyntax.Tokens.ToFullString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return CSharpSyntaxFactory.ParseStatement(text);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private ImmutableArray<CSharp.StatementSyntax> CreateCSharpProbeMembersBefore(
        AkburaSyntax scope,
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> classMembersBuilder,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        using var builder = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member.Position >= scope.Position)
            {
                break;
            }

            switch (member.Kind)
            {
                case AkburaSyntaxKind.StateDeclarationSyntax:
                    var stateDeclaration = Unsafe.As<StateDeclarationSyntax>(member);
                    AddCSharpProbeLocal(
                        builder,
                        akburaSymbolsByName,
                        stateDeclaration.Name.Identifier.ValueText,
                        GetStateProbeFieldType(stateDeclaration),
                        GetSymbolInfo(stateDeclaration).Symbol);
                    break;

                case AkburaSyntaxKind.ParamDeclarationSyntax:
                    var paramDeclaration = Unsafe.As<ParamDeclarationSyntax>(member);
                    AddCSharpProbeLocal(
                        builder,
                        akburaSymbolsByName,
                        paramDeclaration.Name.Identifier.ValueText,
                        GetParamProbeType(paramDeclaration),
                        GetSymbolInfo(paramDeclaration).Symbol);
                    break;

                case AkburaSyntaxKind.InjectDeclarationSyntax:
                    var injectDeclaration = Unsafe.As<InjectDeclarationSyntax>(member);
                    AddCSharpProbeLocal(
                        builder,
                        akburaSymbolsByName,
                        injectDeclaration.Name.Identifier.ValueText,
                        GetInjectProbeType(injectDeclaration),
                        GetSymbolInfo(injectDeclaration).Symbol);
                    break;

                case AkburaSyntaxKind.CommandDeclarationSyntax:
                    var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
                    AddCSharpCommandProbeMembers(
                        classMembersBuilder,
                        akburaSymbolsByName,
                        akburaSymbolsByCommandTypeName,
                        commandDeclaration);
                    break;
            }
        }

        return builder.ToImmutable();
    }

    private void AddCSharpCommandProbeMembers(
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> classMembersBuilder,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName,
        CommandDeclarationSyntax commandDeclaration)
    {
        if (GetSymbolInfo(commandDeclaration).Symbol is not ICommandSymbol command)
        {
            return;
        }

        foreach (var member in CreateCommandProbeMembers(commandDeclaration))
        {
            classMembersBuilder.Add(member);
        }

        akburaSymbolsByName[command.Name] = command;
        akburaSymbolsByCommandTypeName["__AkburaCommand_" + ToCSharpIdentifier(command.Name)] = command;
    }

    private static void AddCSharpProbeLocal(
        ImmutableArrayBuilder<CSharp.StatementSyntax> builder,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        string name,
        CSharp.TypeSyntax? type,
        AkburaSymbol? symbol)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            type == null ||
            symbol == null)
        {
            return;
        }

        builder.Add(CSharpSyntaxFactory.LocalDeclarationStatement(
            CSharpSyntaxFactory.VariableDeclaration(type)
                .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                    CSharpSyntaxFactory.VariableDeclarator(CSharpSyntaxFactory.Identifier(name))
                        .WithInitializer(CSharpSyntaxFactory.EqualsValueClause(
                            CSharpSyntaxFactory.LiteralExpression(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression)))))));
        akburaSymbolsByName[name] = symbol;
    }

    private CSharp.TypeSyntax? GetParamProbeType(ParamDeclarationSyntax paramDeclaration)
    {
        if (paramDeclaration.Type != null)
        {
            try
            {
                return paramDeclaration.Type.ToCSharp();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return GetSymbolInfo(paramDeclaration).Symbol is IParamSymbol paramSymbol &&
            paramSymbol.Type.Symbol is ITypeSymbol typeSymbol
                ? CSharpSyntaxFactory.ParseTypeName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                : null;
    }

    private static CSharp.TypeSyntax? GetInjectProbeType(InjectDeclarationSyntax injectDeclaration)
    {
        try
        {
            return injectDeclaration.Type.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static AkburaSymbol? TryGetReferencedAkburaSymbol(
        RoslynSymbol csharpSymbol,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        if (csharpSymbol is ILocalSymbol local &&
            akburaSymbolsByName.TryGetValue(local.Name, out var symbol))
        {
            return symbol;
        }

        if (csharpSymbol is IFieldSymbol field &&
            akburaSymbolsByName.TryGetValue(field.Name, out symbol))
        {
            return symbol;
        }

        if (csharpSymbol.ContainingType != null &&
            akburaSymbolsByCommandTypeName.TryGetValue(csharpSymbol.ContainingType.Name, out symbol))
        {
            return symbol;
        }

        return null;
    }
}
