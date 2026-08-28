using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal CSharpProbeProjection CreateAkcssCSharpCompletionProjection(
        CSharpExpressionSyntax expressionSyntax,
        int relativePosition)
    {
        if (!EmbeddedCSharpSyntaxFacts.TryGetExpression(
                expressionSyntax,
                out var expression,
                out _))
        {
            throw new InvalidOperationException(
                "The AKCSS expression could not be parsed as C#.");
        }

        var containingSymbol = GetAkcssCompletionContainingSymbol(
            expressionSyntax);
        var targetType = GetAkcssCompletionExpectedType(
            expressionSyntax,
            containingSymbol);
        var annotation = new SyntaxAnnotation(
            "AkburaCSharpCompletionTarget");
        var annotatedExpression = expression
            .WithAdditionalAnnotations(annotation);
        var root = CreateAkcssExpressionProbe(
            annotatedExpression,
            containingSymbol,
            targetType,
            includeCompletionMembers: true,
            completionScope: expressionSyntax);
        return CSharpProbeBuilder.CreateProjection(
            root,
            annotatedExpression,
            annotation,
            relativePosition);
    }

    internal CSharpProbeProjection CreateAkcssCSharpCompletionProjection(
        CSharpTypeSyntax typeSyntax,
        int relativePosition)
    {
        var sourceText = typeSyntax.Tokens.ToFullString();
        var type = CSharpSyntaxFactory.ParseTypeName(sourceText);
        var annotation = new SyntaxAnnotation(
            "AkburaCSharpCompletionTarget");
        var annotatedType = type.WithAdditionalAnnotations(annotation);
        var field = CSharpSyntaxFactory.FieldDeclaration(
                CSharpSyntaxFactory.VariableDeclaration(annotatedType)
                    .WithVariables(CSharpSyntaxFactory
                        .SingletonSeparatedList(
                            CSharpSyntaxFactory.VariableDeclarator(
                                "__akbura_type_probe"))))
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(
                    CSharpSyntaxKind.PrivateKeyword)));
        var probeClass = CSharpSyntaxFactory
            .ClassDeclaration("__AkburaTypeProbe")
            .WithMembers(CSharpSyntaxFactory
                .SingletonList<CSharp.MemberDeclarationSyntax>(field));
        var root = CreateCSharpProbeCompilationUnit(
            probeClass,
            GetAkcssCSharpUsingDirectives(typeSyntax));
        return CSharpProbeBuilder.CreateProjection(
            root,
            annotatedType,
            annotation,
            relativePosition);
    }

    internal CSharpProbeProjection CreateAkcssCSharpCompletionProjection(
        AkcssUsingDirectiveSyntax usingSyntax,
        int relativePosition)
    {
        if (usingSyntax.IsAkcssModuleImport)
        {
            throw new InvalidOperationException(
                "AKCSS module imports use native completion.");
        }

        var annotation = new SyntaxAnnotation(
            "AkburaCSharpCompletionTarget");
        var currentUsing = usingSyntax.ToCSharp();
        var annotatedName = currentUsing.Name!
            .WithAdditionalAnnotations(annotation);
        currentUsing = currentUsing.WithName(annotatedName);

        using var usings =
            ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        usings.AddRange(GetAkcssCSharpUsingDirectivesBefore(usingSyntax));
        usings.Add(currentUsing);
        var root = CreateCSharpProbeCompilationUnit(
            CSharpSyntaxFactory.ClassDeclaration("__AkburaUsingProbe"),
            usings.ToImmutable());
        return CSharpProbeBuilder.CreateProjection(
            root,
            annotatedName,
            annotation,
            relativePosition);
    }

    private IAkcssSymbol GetAkcssCompletionContainingSymbol(
        AkburaSyntax syntax)
    {
        for (var current = syntax.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is not (AkcssStyleRuleSyntax or
                    AkcssUtilityDeclarationSyntax))
            {
                continue;
            }

            if (TryGetAkcssCompletionContainingSymbol(
                    current,
                    out var containingSymbol))
            {
                return containingSymbol;
            }

            break;
        }

        throw new InvalidOperationException(
            "The AKCSS completion expression has no containing declaration.");
    }

    private ITypeSymbol? GetAkcssCompletionExpectedType(
        CSharpExpressionSyntax expressionSyntax,
        IAkcssSymbol containingSymbol)
    {
        if (expressionSyntax.Parent is AkcssIfDirectiveSyntax)
        {
            return Compilation.CSharpCompilation.GetSpecialType(
                SpecialType.System_Boolean);
        }

        if (expressionSyntax.Parent is not
                AkcssAssignmentSyntax assignment)
        {
            return null;
        }

        for (var current = assignment.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is not (AkcssStyleRuleSyntax or
                    AkcssUtilityDeclarationSyntax))
            {
                continue;
            }

            if (TryGetAkcssValueCompletionInfo(
                    current.FullSpan,
                    assignment.PropertyName.ToFullString(),
                    out var info))
            {
                return info.ExpectedType;
            }

            break;
        }

        using var diagnostics =
            ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        return ResolveAkcssProperty(
                assignment,
                containingSymbol,
                diagnostics)
            ?.Type.Symbol as ITypeSymbol;
    }

    private ImmutableArray<CSharp.UsingDirectiveSyntax>
        GetAkcssCSharpUsingDirectivesBefore(
            AkcssUsingDirectiveSyntax currentDirective)
    {
        using var builder =
            ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        var names = new HashSet<string>(StringComparer.Ordinal);
        AddCSharpUsingDirectives(
            builder,
            names,
            Compilation.GlobalCSharpUsingDirectives);

        foreach (var usingDirective in
                 Compilation.GlobalAkcssUsingDirectives)
        {
            if (!usingDirective.IsAkcssModuleImport)
            {
                AddCSharpUsingDirective(
                    builder,
                    names,
                    usingDirective.ToCSharp());
            }
        }

        foreach (var member in GetContainingAkcssTopLevelMembers(
                     currentDirective))
        {
            if (member.Position >= currentDirective.Position)
            {
                break;
            }

            if (member is AkcssUsingDirectiveSyntax usingDirective &&
                !usingDirective.IsAkcssModuleImport)
            {
                AddCSharpUsingDirective(
                    builder,
                    names,
                    usingDirective.ToCSharp());
            }
        }

        AddAkcssImplicitUsing(builder, names, "Avalonia");
        AddAkcssImplicitUsing(builder, names, "Avalonia.Layout");
        AddAkcssImplicitUsing(builder, names, "Avalonia.Media");
        AddAkcssImplicitUsing(builder, names, "Akbura");
        return builder.ToImmutable();
    }
}
