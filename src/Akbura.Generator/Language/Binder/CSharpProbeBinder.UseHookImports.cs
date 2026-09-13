using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBinder
{
    private const string UseHookImportAnnotationKind = "AkburaUseHookImport";

    private UseHookImports CreateUseHookImports(
        CSharp.InvocationExpressionSyntax invocation,
        ImmutableArray<INamedTypeSymbol> hookTypes)
    {
        if (invocation.Expression is not CSharp.SimpleNameSyntax name)
        {
            return default;
        }

        using var imports = ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        using var types = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        using var methods = ImmutableArrayBuilder<IMethodSymbol>.Rent();
        var componentName = string.IsNullOrWhiteSpace(SemanticModel.SyntaxTree.ComponentName)
            ? "__AkburaUseHookProbe"
            : ToCSharpIdentifier(SemanticModel.SyntaxTree.ComponentName);
        var namespaceName = SemanticModel.GetAkburaNamespaceText(
            SemanticModel.SyntaxTree.GetRoot(),
            SemanticModel.SyntaxTree);
        var componentTypeName = "global::" +
            (namespaceName.Length == 0 ? string.Empty : namespaceName + ".") + componentName;
        foreach (var hookType in hookTypes)
        {
            using var declarations = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
            foreach (var method in hookType.GetMembers(name.Identifier.ValueText).OfType<IMethodSymbol>())
            {
                if (!method.IsExtensionMethod || method.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                // A using-static import does not expose extension methods as bare
                // names. Project their signatures without `this` into the same
                // overload group as ordinary imports, then rebind the selected
                // method to its real declaring type before producing operations.
                var declaration = (CSharp.MethodDeclarationSyntax)CSharpSyntaxFactory.ParseMemberDeclaration(
                    method.ToDisplayString(s_useHookImportDisplayFormat) + " => throw null!;")!;
                declarations.Add(declaration.WithAdditionalAnnotations(new SyntaxAnnotation(
                    UseHookImportAnnotationKind,
                    methods.Count.ToString(CultureInfo.InvariantCulture))));
                methods.Add(method);
            }

            if (declarations.Count == 0)
            {
                continue;
            }

            var typeName = "__AkburaUseHookImports_" + types.Count.ToString(CultureInfo.InvariantCulture);
            types.Add(CSharpSyntaxFactory.ClassDeclaration(typeName)
                .WithModifiers(CSharpSyntaxFactory.TokenList(
                    CSharpSyntaxFactory.Token(CSharpSyntaxKind.PublicKeyword),
                    CSharpSyntaxFactory.Token(CSharpSyntaxKind.StaticKeyword)))
                .WithMembers(CSharpSyntaxFactory.List(declarations.ToImmutable())));
            imports.Add(CSharpSyntaxFactory.UsingDirective(
                    CSharpSyntaxFactory.ParseName(componentTypeName + "." + typeName))
                .WithStaticKeyword(CSharpSyntaxFactory.Token(CSharpSyntaxKind.StaticKeyword)));
        }

        return new UseHookImports(imports.ToImmutable(), types.ToImmutable(), methods.ToImmutable());
    }

    private static bool TryGetImportedHookInvocation(
        UseHookProbe probe,
        CSharp.InvocationExpressionSyntax sourceInvocation,
        ImmutableArray<IMethodSymbol> importedMethods,
        out CSharp.InvocationExpressionSyntax invocation)
    {
        if (!importedMethods.IsDefaultOrEmpty &&
            probe.SemanticModel.GetSymbolInfo(probe.Invocation).Symbol is IMethodSymbol selected)
        {
            foreach (var reference in selected.DeclaringSyntaxReferences)
            {
                var annotation = reference.GetSyntax().GetAnnotations(UseHookImportAnnotationKind)
                    .FirstOrDefault();
                if (!int.TryParse(annotation?.Data, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                    index < 0 || index >= importedMethods.Length)
                {
                    continue;
                }

                var original = importedMethods[index];
                CSharp.SimpleNameSyntax methodName = selected.IsGenericMethod
                    ? CSharpSyntaxFactory.GenericName(
                        CSharpSyntaxFactory.Identifier(original.Name),
                        CSharpSyntaxFactory.TypeArgumentList(CSharpSyntaxFactory.SeparatedList(
                            selected.TypeArguments.Select(type => CSharpSyntaxFactory.ParseTypeName(
                                type.ToDisplayString(s_stateTypeDisplayFormat))))))
                    : CSharpSyntaxFactory.IdentifierName(original.Name);
                invocation = sourceInvocation.WithExpression(CSharpSyntaxFactory.MemberAccessExpression(
                        CSharpSyntaxKind.SimpleMemberAccessExpression,
                        CSharpSyntaxFactory.ParseExpression(original.ContainingType.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat)),
                        methodName)
                    .WithTriviaFrom(sourceInvocation.Expression));
                return true;
            }
        }

        invocation = sourceInvocation;
        return false;
    }

    private static readonly SymbolDisplayFormat s_useHookImportDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters |
            SymbolDisplayGenericsOptions.IncludeTypeConstraints,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility |
            SymbolDisplayMemberOptions.IncludeModifiers |
            SymbolDisplayMemberOptions.IncludeType |
            SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType |
            SymbolDisplayParameterOptions.IncludeName |
            SymbolDisplayParameterOptions.IncludeDefaultValue |
            SymbolDisplayParameterOptions.IncludeParamsRefOut,
        extensionMethodStyle: SymbolDisplayExtensionMethodStyle.StaticMethod,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly struct UseHookImports
    {
        public UseHookImports(
            ImmutableArray<CSharp.UsingDirectiveSyntax> usingDirectives,
            ImmutableArray<CSharp.MemberDeclarationSyntax> types,
            ImmutableArray<IMethodSymbol> methods)
        {
            UsingDirectives = usingDirectives;
            Types = types;
            Methods = methods;
        }

        public ImmutableArray<CSharp.UsingDirectiveSyntax> UsingDirectives { get; }

        public ImmutableArray<CSharp.MemberDeclarationSyntax> Types { get; }

        public ImmutableArray<IMethodSymbol> Methods { get; }
    }
}
