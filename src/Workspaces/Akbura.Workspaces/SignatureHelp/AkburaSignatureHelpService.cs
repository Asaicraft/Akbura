using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Workspaces.SignatureHelp;

internal sealed class AkburaSignatureHelpService :
    IAkburaSignatureHelpService
{
    public AkburaSignatureHelp? GetSignatureHelp(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }
        if ((uint)position > (uint)document.Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (semanticContext == null ||
            !document.TryGetEmbeddedCSharpContext(
                position,
                out var embeddedContext,
                cancellationToken) ||
            !AkburaCSharpProjectionFactory.TryCreate(
                document,
                semanticContext,
                embeddedContext,
                out var projection,
                cancellationToken))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var parseOptions = semanticContext.Project.CSharpCompilation
            .SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var tree = CSharpSyntaxTree.Create(
            projection.Root,
            parseOptions,
            document.FilePath + ".signature.cs",
            Encoding.UTF8);
        var compilation = semanticContext.Project.CSharpCompilation
            .AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(
            tree,
            ignoreAccessibility: true);
        var root = tree.GetRoot(cancellationToken);
        var projectedPosition = Math.Min(
            projection.ProjectedPosition,
            root.FullSpan.End);
        var argumentList = FindArgumentList(
            root,
            projectedPosition);
        if (argumentList == null)
        {
            return null;
        }

        var methods = GetMethods(
            model,
            argumentList,
            cancellationToken);
        if (methods.IsDefaultOrEmpty)
        {
            return null;
        }

        var orderedMethods = methods
            .OrderBy(static method => method.Parameters.Length)
            .ThenBy(
                static method => method.ToDisplayString(),
                StringComparer.Ordinal)
            .ToImmutableArray();
        using var signatures =
            ImmutableArrayBuilder<AkburaSignatureInformation>.Rent(
                orderedMethods.Length);
        foreach (var method in orderedMethods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var parameters =
                ImmutableArrayBuilder<AkburaSignatureParameter>.Rent(
                    method.Parameters.Length);
            foreach (var parameter in method.Parameters)
            {
                parameters.Add(new AkburaSignatureParameter(
                    FormatParameter(parameter),
                    null));
            }

            signatures.Add(new AkburaSignatureInformation(
                FormatMethod(method),
                documentation: null,
                parameters.ToImmutable()));
        }

        var activeParameter = GetActiveParameter(
            argumentList,
            projectedPosition);
        var activeSignature = FindBestSignature(
            orderedMethods,
            model,
            argumentList,
            cancellationToken);
        var projectedSpan = argumentList.Span;
        var hostSpan = projection.TryMapToHost(
            projectedSpan,
            out var mappedSpan)
                ? mappedSpan
                : embeddedContext.HostSpan;

        return new AkburaSignatureHelp(
            hostSpan,
            signatures.ToImmutable(),
            activeSignature,
            activeParameter);
    }

    private static SyntaxNode? FindArgumentList(
        SyntaxNode root,
        int position)
    {
        var lookup = position == root.FullSpan.End && position > 0
            ? position - 1
            : position;
        var token = root.FindToken(
            Math.Max(root.FullSpan.Start, lookup),
            findInsideTrivia: true);
        return token.Parent?.AncestorsAndSelf().FirstOrDefault(
            node => node is BaseArgumentListSyntax or
                AttributeArgumentListSyntax &&
                node.SpanStart <= position &&
                position <= node.Span.End);
    }

    private static ImmutableArray<IMethodSymbol> GetMethods(
        SemanticModel model,
        SyntaxNode argumentList,
        CancellationToken cancellationToken)
    {
        SyntaxNode? target = argumentList.Parent switch
        {
            InvocationExpressionSyntax invocation => invocation.Expression,
            ObjectCreationExpressionSyntax creation => creation,
            ImplicitObjectCreationExpressionSyntax implicitCreation =>
                implicitCreation,
            ConstructorInitializerSyntax initializer => initializer,
            ElementAccessExpressionSyntax element => element.Expression,
            AttributeSyntax attribute => attribute,
            _ => argumentList.Parent,
        };
        if (target == null)
        {
            return ImmutableArray<IMethodSymbol>.Empty;
        }

        var info = model.GetSymbolInfo(target, cancellationToken);
        using var result = ImmutableArrayBuilder<IMethodSymbol>.Rent();
        AddMethod(result, info.Symbol);
        foreach (var candidate in info.CandidateSymbols)
        {
            AddMethod(result, candidate);
        }

        if (target is ExpressionSyntax expression)
        {
            foreach (var member in model.GetMemberGroup(
                         expression,
                         cancellationToken))
            {
                AddMethod(result, member);
            }
        }

        return result.ToImmutable();
    }

    private static void AddMethod(
        ImmutableArrayBuilder<IMethodSymbol> result,
        ISymbol? symbol)
    {
        IMethodSymbol? method = symbol switch
        {
            IMethodSymbol candidate => candidate,
            IPropertySymbol { GetMethod: { } getter }
                when getter.Parameters.Length > 0 => getter,
            _ => null,
        };
        if (method == null)
        {
            return;
        }

        foreach (var existing in result.WrittenSpan)
        {
            if (SymbolEqualityComparer.Default.Equals(existing, method))
            {
                return;
            }
        }

        result.Add(method);
    }
    private static int GetActiveParameter(
        SyntaxNode argumentList,
        int position)
    {
        var count = 0;
        foreach (var token in argumentList.DescendantTokens())
        {
            if (token.SpanStart >= position)
            {
                break;
            }

            if (token.IsKind(SyntaxKind.CommaToken))
            {
                count++;
            }
        }

        return count;
    }

    private static int FindBestSignature(
        ImmutableArray<IMethodSymbol> methods,
        SemanticModel model,
        SyntaxNode argumentList,
        CancellationToken cancellationToken)
    {
        var parent = argumentList.Parent;
        if (parent == null)
        {
            return 0;
        }

        var symbol = model.GetSymbolInfo(
            parent,
            cancellationToken).Symbol as IMethodSymbol;
        if (symbol == null)
        {
            return 0;
        }

        for (var index = 0; index < methods.Length; index++)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    methods[index],
                    symbol))
            {
                return index;
            }
        }

        return 0;
    }

    private static string FormatMethod(IMethodSymbol method)
    {
        var name = method.MethodKind == MethodKind.Constructor
            ? method.ContainingType.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat)
            : method.Name;
        return name + "(" +
            string.Join(", ", method.Parameters.Select(FormatParameter)) +
            ")";
    }

    private static string FormatParameter(IParameterSymbol parameter)
    {
        var prefix = parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => string.Empty,
        };
        return prefix +
            parameter.Type.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat) +
            " " + parameter.Name;
    }
}