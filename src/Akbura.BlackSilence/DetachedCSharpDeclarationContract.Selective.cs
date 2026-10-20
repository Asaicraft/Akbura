using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Akbura.BlackSilence;

internal sealed partial record DetachedCSharpDeclarationContract
{
    private static (ImmutableArray<DetachedCSharpTypeContract> Types, ImmutableArray<string> Segments) CaptureTypes(
        CompilationUnitSyntax unit, string source, CancellationToken cancellationToken)
    {
        var types = ImmutableArray.CreateBuilder<DetachedCSharpTypeContract>();
        var segments = ImmutableArray.CreateBuilder<string>();
        var position = 0;
        foreach (var type in unit.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.Parent is not (CompilationUnitSyntax or BaseNamespaceDeclarationSyntax) || type.TypeParameterList != null ||
                type.ParameterList != null || type.ConstraintClauses.Count != 0 || type.AttributeLists.Count != 0 ||
                type.Modifiers.Any(SyntaxKind.PartialKeyword) || type.Modifiers.Any(SyntaxKind.UnsafeKeyword) ||
                !type.OpenBraceToken.IsKind(SyntaxKind.OpenBraceToken) || !type.CloseBraceToken.IsKind(SyntaxKind.CloseBraceToken))
            {
                // Bodyless declarations remain in the exact source segments.
                // Their default brace tokens have no usable source positions.
                continue;
            }
            var name = string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(space =>
                string.Join(".", space.Name.DescendantTokens().Where(static token => token.IsKind(SyntaxKind.IdentifierToken))
                    .Select(static token => token.ValueText))).Append(type.Identifier.ValueText));
            var members = ImmutableArray.CreateBuilder<DetachedCSharpDeclarationMember>();
            foreach (var member in type.Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mayChange = CanChange(member, cancellationToken);
                members.Add(new DetachedCSharpDeclarationMember(MemberName(member), member.ToFullString(), mayChange,
                    mayChange ? DeclarationSegments(member) : []));
            }
            segments.Add(source.Substring(position, type.SpanStart - position));
            types.Add(new DetachedCSharpTypeContract(name, source.Substring(type.SpanStart, type.OpenBraceToken.Span.End - type.SpanStart),
                type.CloseBraceToken.Text + type.SemicolonToken.Text, members.ToImmutable()));
            position = type.Span.End;
        }
        segments.Add(source.Substring(position));
        return (types.ToImmutable(), segments.ToImmutable());
    }

    private static string MemberName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        _ => string.Empty,
    };

    private static bool CanChange(MemberDeclarationSyntax member, CancellationToken cancellationToken)
    {
        if (member is MethodDeclarationSyntax method)
        {
            return method.TypeParameterList == null && method.ConstraintClauses.Count == 0 && method.ExplicitInterfaceSpecifier == null &&
                !HasExcludedModifier(method.Modifiers) && method.ReturnType is not (RefTypeSyntax or PointerTypeSyntax or FunctionPointerTypeSyntax) &&
                !method.ParameterList.Parameters.Any(static parameter => parameter.Modifiers.Any(SyntaxKind.ThisKeyword) ||
                    parameter.Type is RefTypeSyntax or PointerTypeSyntax or FunctionPointerTypeSyntax ||
                    parameter.Default != null && parameter.Default.Value is not LiteralExpressionSyntax) &&
                ((SyntaxNode?)method.Body ?? method.ExpressionBody) is { } body && IsSupportedBody(body, cancellationToken);
        }
        if (member is not PropertyDeclarationSyntax property || property.Initializer != null || property.ExplicitInterfaceSpecifier != null ||
            HasExcludedModifier(property.Modifiers) || property.Modifiers.Any(SyntaxKind.RequiredKeyword) ||
            property.Type is RefTypeSyntax or PointerTypeSyntax or FunctionPointerTypeSyntax)
        {
            return false;
        }
        if (property.ExpressionBody != null)
        {
            return IsSupportedBody(property.ExpressionBody, cancellationToken);
        }
        if (property.AccessorList is not { Accessors.Count: > 0 } accessors)
        {
            return false;
        }
        foreach (var accessor in accessors.Accessors)
        {
            if (!accessor.IsKind(SyntaxKind.GetAccessorDeclaration) && !accessor.IsKind(SyntaxKind.SetAccessorDeclaration))
            {
                return false;
            }
            var body = (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody;
            if (body != null && !IsSupportedBody(body, cancellationToken))
            {
                return false;
            }
        }
        return true;
    }

    private static ImmutableArray<string> DeclarationSegments(MemberDeclarationSyntax member)
    {
        // Called only after the explicit supported-member/body checks above.
        var bodies = member is MethodDeclarationSyntax method
            ? new[] { (SyntaxNode?)method.Body ?? method.ExpressionBody }
            : member is PropertyDeclarationSyntax { ExpressionBody: { } expression } ? new SyntaxNode?[] { expression }
            : ((PropertyDeclarationSyntax)member).AccessorList!.Accessors.Select(static accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody);
        var text = member.ToFullString();
        var segments = ImmutableArray.CreateBuilder<string>();
        var position = 0;
        foreach (var body in bodies)
        {
            if (body == null)
            {
                continue;
            }
            var start = body.SpanStart - member.FullSpan.Start;
            segments.Add(text.Substring(position, start - position));
            segments.Add(body.RawKind.ToString(System.Globalization.CultureInfo.InvariantCulture));
            position = body.Span.End - member.FullSpan.Start;
        }
        segments.Add(text.Substring(position));
        return segments.ToImmutable();
    }

    internal bool TryGetDeclarationChanges(DetachedCSharpDeclarationContract current,
        ImmutableArray<DetachedCSharpDeclarationChange>.Builder changes, CancellationToken cancellationToken)
    {
        if (Schema != CurrentSchema || current.Schema != Schema || Types.IsDefaultOrEmpty || current.Types.Length != Types.Length ||
            !TypeSegments.SequenceEqual(current.TypeSegments, StringComparer.Ordinal))
        {
            return false;
        }
        for (var index = 0; index < Types.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var old = Types[index];
            var next = current.Types[index];
            if (old.MetadataName != next.MetadataName || old.Header != next.Header || old.Tail != next.Tail)
            {
                return false;
            }
            if (old.Members.Length == next.Members.Length &&
                !old.Members.Where((member, position) => !member.HasSameDeclaration(next.Members[position])).Any())
            {
                continue;
            }
            // Fixed members include initializers, constructors, constants, nested
            // types and unsupported declarations. No public-signature hash is used.
            if (!old.Members.Where(static member => !member.MayChange).Select(static member => member.Source)
                    .SequenceEqual(next.Members.Where(static member => !member.MayChange).Select(static member => member.Source), StringComparer.Ordinal) ||
                old.MetadataName is "System" or "Avalonia" or "Akbura" ||
                old.MetadataName.StartsWith("System.", StringComparison.Ordinal) || old.MetadataName.StartsWith("Avalonia.", StringComparison.Ordinal) ||
                old.MetadataName.StartsWith("Akbura.", StringComparison.Ordinal))
            {
                return false;
            }
            var oldMembers = old.Members.Where(static member => member.MayChange).ToArray();
            var nextMembers = next.Members.Where(static member => member.MayChange).ToArray();
            var namesChanged = oldMembers.Length != nextMembers.Length ||
                !oldMembers.Select(static member => member.Name).SequenceEqual(nextMembers.Select(static member => member.Name), StringComparer.Ordinal);
            changes.Add(new DetachedCSharpDeclarationChange(old.MetadataName,
                namesChanged ? "MemberCandidateSetChanged" : "MemberDeclarationChanged",
                Math.Max(oldMembers.Count(member => !nextMembers.Any(member.HasSameDeclaration)),
                    nextMembers.Count(member => !oldMembers.Any(member.HasSameDeclaration)))));
        }
        return true;
    }
}

internal sealed record DetachedCSharpTypeContract(string MetadataName, string Header, string Tail,
    ImmutableArray<DetachedCSharpDeclarationMember> Members);
internal sealed record DetachedCSharpDeclarationMember(string Name, string Source, bool MayChange, ImmutableArray<string> DeclarationSegments)
{
    internal bool HasSameDeclaration(DetachedCSharpDeclarationMember other) => Name == other.Name && MayChange == other.MayChange &&
        (MayChange ? DeclarationSegments.SequenceEqual(other.DeclarationSegments, StringComparer.Ordinal) : Source == other.Source);
}
internal sealed record DetachedCSharpDeclarationChange(string MetadataName, string Kind, int ChangedMembers);
internal sealed record DetachedCSharpChangeSet(string AssemblyIdentity, ImmutableArray<DetachedCSharpDeclarationChange> Changes);
