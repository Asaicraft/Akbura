using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces;

public sealed partial class AkburaSyntacticDocument
{
    /// <summary>
    /// Determines whether <paramref name="position"/> is inside an embedded
    /// C# fragment that can be projected for Roslyn completion.
    /// </summary>
    public bool TryGetCSharpCompletionContext(
        int position,
        out AkburaCSharpCompletionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetEmbeddedCSharpContext(
                position,
                out var embeddedContext,
                cancellationToken))
        {
            context = default;
            return false;
        }

        context = embeddedContext.ToCompletionContext();
        return true;
    }

    internal bool TryGetEmbeddedCSharpContext(
        int position,
        out AkburaEmbeddedCSharpContext context,
        CancellationToken cancellationToken = default)
    {
        ValidatePosition(position);
        if (Text.Length == 0)
        {
            context = default;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var root = SyntaxTree.GetRootSyntax();
        EmbeddedCSharpCandidate? best = null;

        if (position < Text.Length)
        {
            CollectCandidates(
                root.FindTokenInternal(position).Parent);
        }

        if (position > 0)
        {
            CollectCandidates(
                root.FindTokenInternal(position - 1).Parent);
        }

        // Missing C# nodes can have a zero-width span and therefore are not
        // necessarily ancestors of either adjacent token.
        if (best == null)
        {
            foreach (var candidate in root.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (candidate.FullSpan.Start > position)
                {
                    break;
                }

                if (candidate.FullSpan.Start <= position &&
                    candidate.FullSpan.End >= position)
                {
                    TryAddCandidate(candidate);
                }
            }
        }

        if (best == null)
        {
            context = default;
            return false;
        }

        context = best.Value.Context;
        return true;

        void CollectCandidates(AkburaSyntax? syntax)
        {
            for (var current = syntax;
                 current != null;
                 current = current.Parent)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryAddCandidate(current);
            }
        }

        void TryAddCandidate(AkburaSyntax syntax)
        {
            switch (syntax)
            {
                case CSharpExpressionSyntax expression
                    when IsCSharpExpressionContext(expression):
                    AddCandidate(
                        AkburaCSharpCompletionContextKind.Expression,
                        expression,
                        expression.Tokens.FullSpan,
                        priority: 0);
                    break;

                case CSharpTypeSyntax type:
                    if (TryGetCSharpTypeContext(
                            type,
                            out var kind,
                            out var owner))
                    {
                        AddCandidate(
                            kind,
                            owner,
                            type.Tokens.FullSpan,
                            priority: 1);
                    }
                    break;

                case CSharpParameterListSyntax parameterList
                    when parameterList.Parent is
                        CommandDeclarationSyntax:
                    AddCandidate(
                        AkburaCSharpCompletionContextKind
                            .CommandParameterList,
                        parameterList,
                        parameterList.Parameters.FullSpan,
                        priority: 2);
                    break;

                case CSharpStatementSyntax statement:
                    AddCandidate(
                        AkburaCSharpCompletionContextKind.Statement,
                        statement,
                        EmbeddedCSharpSyntaxFacts
                            .GetStatementHostSpan(statement),
                        priority: 3);
                    break;
            }
        }

        bool IsCSharpExpressionContext(CSharpExpressionSyntax expression)
        {
            if ((expression.Parent is AkcssAssignmentSyntax assignment &&
                 ReferenceEquals(assignment.Expression, expression)) ||
                (expression.Parent is AkcssIfDirectiveSyntax ifDirective &&
                 ReferenceEquals(ifDirective.Condition, expression)))
            {
                return true;
            }

            return !IsInAkcssRegion(expression);
        }

        bool TryGetCSharpTypeContext(
            CSharpTypeSyntax type,
            out AkburaCSharpCompletionContextKind kind,
            out AkburaSyntax owner)
        {
            if (type.Parent is AkcssUsingDirectiveSyntax akcssUsingDirective &&
                ReferenceEquals(akcssUsingDirective.Name, type) &&
                !akcssUsingDirective.IsAkcssModuleImport)
            {
                kind = AkburaCSharpCompletionContextKind
                    .UsingDirectiveName;
                owner = akcssUsingDirective;
                return true;
            }

            switch (type.Parent)
            {
                case AkcssStyleSelectorSyntax selector
                    when ReferenceEquals(selector.TargetType, type):
                    kind = AkburaCSharpCompletionContextKind.Type;
                    owner = type;
                    return true;

                case AkcssUtilitySelectorSyntax utilitySelector
                    when ReferenceEquals(utilitySelector.TargetType, type):
                    kind = AkburaCSharpCompletionContextKind.Type;
                    owner = type;
                    return true;

                case AkcssUtilityParameterSyntax parameter
                    when ReferenceEquals(parameter.Type, type):
                    kind = AkburaCSharpCompletionContextKind.Type;
                    owner = type;
                    return true;

                case AkcssInterceptDirectiveSyntax intercept
                    when ReferenceEquals(intercept.Type, type):
                    kind = AkburaCSharpCompletionContextKind.Type;
                    owner = type;
                    return true;
            }

            if (IsInAkcssRegion(type))
            {
                kind = default;
                owner = null!;
                return false;
            }

            var usingDirective = type.Parent as UsingDirectiveSyntax;
            kind = usingDirective == null
                ? AkburaCSharpCompletionContextKind.Type
                : AkburaCSharpCompletionContextKind.UsingDirectiveName;
            owner = usingDirective is null
                ? type
                : usingDirective;
            return true;
        }

        bool IsInAkcssRegion(AkburaSyntax syntax)
        {
            if (SyntaxTree.Kind == SyntaxTreeKind.Akcss)
            {
                return true;
            }

            for (var current = syntax.Parent;
                 current != null;
                 current = current.Parent)
            {
                if (current is InlineAkcssBlockSyntax)
                {
                    return true;
                }
            }

            return false;
        }

        void AddCandidate(
            AkburaCSharpCompletionContextKind kind,
            AkburaSyntax owner,
            TextSpan hostSpan,
            int priority)
        {
            if (!ContainsPosition(hostSpan, position))
            {
                return;
            }

            var candidate = new EmbeddedCSharpCandidate(
                new AkburaEmbeddedCSharpContext(
                    kind,
                    owner.Kind,
                    owner.FullSpan,
                    hostSpan,
                    position),
                priority);
            if (best == null ||
                candidate.Priority < best.Value.Priority ||
                candidate.Priority == best.Value.Priority &&
                candidate.Context.HostSpan.Length <
                    best.Value.Context.HostSpan.Length)
            {
                best = candidate;
            }
        }
    }

    private static bool ContainsPosition(
        TextSpan span,
        int position)
    {
        return position >= span.Start &&
            position <= span.End;
    }

    private readonly struct EmbeddedCSharpCandidate
    {
        public EmbeddedCSharpCandidate(
            AkburaEmbeddedCSharpContext context,
            int priority)
        {
            Context = context;
            Priority = priority;
        }

        public AkburaEmbeddedCSharpContext Context { get; }

        public int Priority { get; }
    }
}
