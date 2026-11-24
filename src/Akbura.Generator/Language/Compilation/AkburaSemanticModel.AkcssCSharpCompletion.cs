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

        var context = GetAkcssEditorContext(expressionSyntax) ??
            throw new InvalidOperationException("The AKCSS expression has no containing declaration.");
        var targetType = GetAkcssExpressionExpectedType(expressionSyntax, context);
        var receiver = expressionSyntax.Parent is AkcssBasedOnDirectiveSyntax or AkcssMixinParameterDefaultSyntax
            ? default : context.ExpressionReceiver;
        return CreateAkcssExpressionProjection(expressionSyntax, expression, context, receiver, targetType, relativePosition);
    }

    internal CSharpProbeProjection CreateAkcssEmptyIncludeArgumentProjection(AkcssMixinArgumentListSyntax list, int relativePosition)
    {
        if (list.Parent is not AkcssIncludeSyntax include ||
            !EmbeddedCSharpSyntaxFacts.TryGetEmptyIncludeArgumentSpan(list, out var span) ||
            GetAkcssEditorContext(include) is not { } context)
        {
            throw new InvalidOperationException("The AKCSS include has no available argument context.");
        }

        var text = list.ToFullString().Substring(span.Start - list.FullSpan.Start, span.Length);
        var expression = CSharpSyntaxFactory.ParseExpression(text);
        return CreateAkcssExpressionProjection(list, expression, context, context.ExpressionReceiver,
            GetAkcssIncludeParameterType(include, index: 0), relativePosition);
    }

    private CSharpProbeProjection CreateAkcssExpressionProjection(AkburaSyntax site, CSharp.ExpressionSyntax expression,
        AkcssBindingContext context, CSharpSymbolDefinition receiver, ITypeSymbol? targetType, int relativePosition)
    {
        var annotation = new SyntaxAnnotation("AkburaCSharpCompletionTarget");
        var annotatedExpression = expression
            .WithAdditionalAnnotations(annotation);
        var root = CreateAkcssExpressionProbe(
            annotatedExpression,
            context.Owner,
            targetType,
            includeCompletionMembers: true,
            completionScope: site,
            effectiveReceiver: receiver);
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

    private ImmutableArray<CSharp.UsingDirectiveSyntax>
        GetAkcssCSharpUsingDirectivesBefore(
            AkcssUsingDirectiveSyntax currentDirective)
    {
        using var builder =
            ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        var names = PooledHashSet<string>.GetInstance();
        try
        {
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
        finally
        {
            names.Free();
        }
    }
}
