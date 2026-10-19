using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Akbura.BlackSilence;

/// <summary>
/// Exact source between explicitly approved implementation regions. This is not
/// a public-signature hash or a blanket removal of executable syntax. Unknown
/// members remain in the exact segments, so changing them forces rebinding.
/// </summary>
internal sealed partial record DetachedCSharpDeclarationContract(
    int Schema,
    ImmutableArray<string> Segments,
    ImmutableArray<DetachedMemberContract> Members,
    ImmutableArray<DetachedCSharpTypeContract> Types = default,
    ImmutableArray<string> TypeSegments = default)
{
    private const int CurrentSchema = 2;

    internal static DetachedCSharpDeclarationContract? Create(SyntaxNode root, string source, CancellationToken cancellationToken)
    {
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.DeclarationContractConstruction);
#endif
        cancellationToken.ThrowIfCancellationRequested();
        if (root is not CompilationUnitSyntax unit || unit.ContainsDiagnostics || unit.ContainsDirectives || unit.ContainsAnnotations ||
            unit.Members.Any(static member => member is GlobalStatementSyntax) || root.DescendantNodes().Any(static node =>
                node is AttributeSyntax || node is BaseTypeDeclarationSyntax type && type.Modifiers.Any(SyntaxKind.FileKeyword)))
        {
            // Even unchanged attribute syntax can acquire a different caller-line
            // constructor argument when an earlier implementation adds newlines.
            return null;
        }
        var regions = new List<(SyntaxNode Body, string Type, string Member)>();
        foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.Parent is not (CompilationUnitSyntax or BaseNamespaceDeclarationSyntax) || type.TypeParameterList != null ||
                type.ParameterList != null || type.ConstraintClauses.Count != 0 || type.AttributeLists.Count != 0 ||
                type.Modifiers.Any(SyntaxKind.PartialKeyword) || type.Modifiers.Any(SyntaxKind.UnsafeKeyword))
            {
                continue;
            }
            foreach (var member in type.Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member is MethodDeclarationSyntax method && method.AttributeLists.Count == 0 && method.TypeParameterList == null &&
                    method.ConstraintClauses.Count == 0 && method.ExplicitInterfaceSpecifier == null &&
                    !HasExcludedModifier(method.Modifiers) && method.ReturnType is not (RefTypeSyntax or PointerTypeSyntax or FunctionPointerTypeSyntax) &&
                    !method.ParameterList.Parameters.Any(static parameter => parameter.Modifiers.Any(SyntaxKind.ThisKeyword)))
                {
                    Add((SyntaxNode?)method.Body ?? method.ExpressionBody, method.Identifier.ValueText);
                }
                else if (member is PropertyDeclarationSyntax property && property.AttributeLists.Count == 0 && property.Initializer == null &&
                    property.ExplicitInterfaceSpecifier == null && !HasExcludedModifier(property.Modifiers) &&
                    !property.Modifiers.Any(SyntaxKind.RequiredKeyword) && property.Type is not (RefTypeSyntax or PointerTypeSyntax or FunctionPointerTypeSyntax))
                {
                    if (property.ExpressionBody != null)
                    {
                        Add(property.ExpressionBody, property.Identifier.ValueText + ".get");
                    }
                    else if (property.AccessorList is { } accessors && accessors.Accessors.Count > 0 &&
                        accessors.Accessors.All(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration) || accessor.IsKind(SyntaxKind.SetAccessorDeclaration)) &&
                        accessors.Accessors.All(static accessor => accessor.AttributeLists.Count == 0 &&
                            (accessor.Body != null || accessor.ExpressionBody != null)))
                    {
                        foreach (var accessor in accessors.Accessors)
                        {
                            Add((SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
                                property.Identifier.ValueText + "." + accessor.Keyword.ValueText);
                        }
                    }
                }
            }

            void Add(SyntaxNode? body, string member)
            {
                if (body != null && IsSupportedBody(body, cancellationToken))
                {
                    regions.Add((body, type.Identifier.ValueText, member));
                }
            }
        }
        var (types, typeSegments) = CaptureTypes(unit, source, cancellationToken);
        if (regions.Count == 0 && types.IsEmpty)
        {
            return null;
        }
        var segments = ImmutableArray.CreateBuilder<string>();
        var members = ImmutableArray.CreateBuilder<DetachedMemberContract>();
        var position = 0;
        foreach (var region in regions.OrderBy(static region => region.Body.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var span = region.Body.Span;
            segments.Add(source.Substring(position, span.Start - position));
            members.Add(new DetachedMemberContract(region.Type, region.Member, region.Body.RawKind, source.Substring(span.Start, span.Length)));
            position = span.End;
        }
        segments.Add(source.Substring(position));
        return new DetachedCSharpDeclarationContract(CurrentSchema, segments.ToImmutable(), members.ToImmutable(), types, typeSegments);
    }

    internal bool Matches(DetachedCSharpDeclarationContract other) => Schema == CurrentSchema && Schema == other.Schema &&
        Segments.SequenceEqual(other.Segments, StringComparer.Ordinal) && Members.Length == other.Members.Length &&
        !Members.Where((member, index) => !member.HasSameDeclaration(other.Members[index])).Any();

    private static bool HasExcludedModifier(SyntaxTokenList modifiers) => modifiers.Any(static token => token.Kind() is
        SyntaxKind.PartialKeyword or SyntaxKind.AsyncKeyword or SyntaxKind.AbstractKeyword or SyntaxKind.ExternKeyword or SyntaxKind.UnsafeKeyword);

    private static bool IsSupportedBody(SyntaxNode body, CancellationToken cancellationToken)
    {
        // C# 14's contextual field syntax has a different node/token shape from
        // the 4.11 package host. Compare its spelling without assuming an old kind.
        if (body.DescendantTokens().Any(static token => token.ValueText == "field"))
        {
            return false;
        }
        foreach (var node in body.DescendantNodesAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or YieldStatementSyntax or
                AwaitExpressionSyntax or AnonymousObjectCreationExpressionSyntax or StackAllocArrayCreationExpressionSyntax or
                ImplicitStackAllocArrayCreationExpressionSyntax or UnsafeStatementSyntax or FixedStatementSyntax or
                PointerTypeSyntax or FunctionPointerTypeSyntax || node is IdentifierNameSyntax { Identifier.ValueText: "field" })
            {
                return false;
            }
        }
        return true;
    }
}

internal sealed record DetachedMemberContract(string TypeName, string MemberName, int BodyKind, string Body)
{
    internal bool HasSameDeclaration(DetachedMemberContract other) => TypeName == other.TypeName && MemberName == other.MemberName && BodyKind == other.BodyKind;
}
