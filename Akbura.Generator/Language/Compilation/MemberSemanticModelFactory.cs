using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language;

internal sealed class MemberSemanticModelFactory
{
    private readonly AkburaSemanticModel _semanticModel;
    private readonly Dictionary<MemberSemanticModelCacheKey, MemberSemanticModel> _cache = new();

    public MemberSemanticModelFactory(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public MemberSemanticModel GetMemberSemanticModel(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        var scope = FindDocumentScope(syntax);
        var kind = GetModelKind(syntax);
        var root = GetModelRoot(syntax, kind, scope);
        var key = new MemberSemanticModelCacheKey(root.Green, kind);
        if (_cache.TryGetValue(key, out var model))
        {
            return model;
        }

        model = kind switch
        {
            MemberSemanticModelKind.Component => new ComponentMemberSemanticModel(_semanticModel, scope),
            MemberSemanticModelKind.Initializer => new InitializerMemberSemanticModel(_semanticModel, scope, root),
            MemberSemanticModelKind.Executable => new ExecutableMemberSemanticModel(_semanticModel, scope, root),
            MemberSemanticModelKind.Markup => new MarkupMemberSemanticModel(_semanticModel, scope, root),
            MemberSemanticModelKind.Akcss => new AkcssMemberSemanticModel(_semanticModel, scope, root),
            _ => new ComponentMemberSemanticModel(_semanticModel, scope),
        };
        _cache.Add(key, model);
        return model;
    }

    private AkburaDocumentSyntax FindDocumentScope(AkburaSyntax syntax)
    {
        for (var node = syntax; node != null; node = node.Parent)
        {
            if (node.Kind == AkburaSyntaxKind.AkburaDocumentSyntax)
            {
                return Unsafe.As<AkburaDocumentSyntax>(node);
            }
        }

        return _semanticModel.SyntaxTree.GetRoot();
    }

    private static MemberSemanticModelKind GetModelKind(AkburaSyntax syntax)
    {
        if (IsParamDefaultValue(syntax))
        {
            return MemberSemanticModelKind.Initializer;
        }

        for (var node = syntax; node != null; node = node.Parent)
        {
            switch (node.Kind)
            {
                case AkburaSyntaxKind.InlineAkcssBlockSyntax:
                case AkburaSyntaxKind.AkcssDocumentSyntax:
                case AkburaSyntaxKind.AkcssStyleRuleSyntax:
                case AkburaSyntaxKind.AkcssUtilityDeclarationSyntax:
                case AkburaSyntaxKind.AkcssAssignmentSyntax:
                case AkburaSyntaxKind.AkcssIfDirectiveSyntax:
                case AkburaSyntaxKind.AkcssApplyDirectiveSyntax:
                case AkburaSyntaxKind.AkcssInterceptDirectiveSyntax:
                    return MemberSemanticModelKind.Akcss;

                case AkburaSyntaxKind.MarkupRootSyntax:
                case AkburaSyntaxKind.MarkupElementSyntax:
                case AkburaSyntaxKind.MarkupElementContentSyntax:
                case AkburaSyntaxKind.MarkupInlineExpressionSyntax:
                case AkburaSyntaxKind.MarkupTextLiteralSyntax:
                case AkburaSyntaxKind.MarkupPlainAttributeSyntax:
                case AkburaSyntaxKind.MarkupPrefixedAttributeSyntax:
                case AkburaSyntaxKind.TailwindFlagAttributeSyntax:
                case AkburaSyntaxKind.TailwindFullAttributeSyntax:
                    return MemberSemanticModelKind.Markup;

                case AkburaSyntaxKind.CSharpBlockSyntax:
                case AkburaSyntaxKind.CSharpStatementSyntax:
                    return MemberSemanticModelKind.Executable;

                case AkburaSyntaxKind.SimpleStateInitializer:
                case AkburaSyntaxKind.BindableStateInitializer:
                    return MemberSemanticModelKind.Initializer;

                case AkburaSyntaxKind.AkburaDocumentSyntax:
                case AkburaSyntaxKind.StateDeclarationSyntax:
                case AkburaSyntaxKind.ParamDeclarationSyntax:
                case AkburaSyntaxKind.InjectDeclarationSyntax:
                case AkburaSyntaxKind.CommandDeclarationSyntax:
                case AkburaSyntaxKind.UseEffectDeclarationSyntax:
                case AkburaSyntaxKind.UserHook:
                    return MemberSemanticModelKind.Component;
            }
        }

        return MemberSemanticModelKind.Component;
    }

    private static bool IsParamDefaultValue(AkburaSyntax syntax)
    {
        return syntax.Parent?.Kind == AkburaSyntaxKind.ParamDeclarationSyntax &&
               ReferenceEquals(
                   Unsafe.As<ParamDeclarationSyntax>(syntax.Parent).DefaultValue?.Green,
                   syntax.Green);
    }

    private static AkburaSyntax GetModelRoot(
        AkburaSyntax syntax,
        MemberSemanticModelKind kind,
        AkburaDocumentSyntax scope)
    {
        if (kind == MemberSemanticModelKind.Component)
        {
            return scope;
        }

        for (var node = syntax; node != null; node = node.Parent)
        {
            if (IsModelRoot(node.Kind, kind))
            {
                return node;
            }
        }

        return scope;
    }

    private static bool IsModelRoot(
        AkburaSyntaxKind syntaxKind,
        MemberSemanticModelKind modelKind)
    {
        return modelKind switch
        {
            MemberSemanticModelKind.Initializer =>
                syntaxKind is AkburaSyntaxKind.SimpleStateInitializer or
                    AkburaSyntaxKind.BindableStateInitializer,
            MemberSemanticModelKind.Executable =>
                syntaxKind is AkburaSyntaxKind.CSharpBlockSyntax or
                    AkburaSyntaxKind.CSharpStatementSyntax,
            MemberSemanticModelKind.Markup =>
                syntaxKind is AkburaSyntaxKind.MarkupRootSyntax or
                    AkburaSyntaxKind.MarkupElementSyntax,
            MemberSemanticModelKind.Akcss =>
                syntaxKind is AkburaSyntaxKind.InlineAkcssBlockSyntax or
                    AkburaSyntaxKind.AkcssDocumentSyntax or
                    AkburaSyntaxKind.AkcssStyleRuleSyntax or
                    AkburaSyntaxKind.AkcssUtilityDeclarationSyntax,
            _ => syntaxKind == AkburaSyntaxKind.AkburaDocumentSyntax,
        };
    }

    private readonly struct MemberSemanticModelCacheKey : IEquatable<MemberSemanticModelCacheKey>
    {
        private readonly GreenNode _root;
        private readonly MemberSemanticModelKind _kind;

        public MemberSemanticModelCacheKey(
            GreenNode root,
            MemberSemanticModelKind kind)
        {
            _root = root;
            _kind = kind;
        }

        public bool Equals(MemberSemanticModelCacheKey other)
        {
            return ReferenceEquals(_root, other._root) &&
                   _kind == other._kind;
        }

        public override bool Equals(object? obj)
        {
            return obj is MemberSemanticModelCacheKey other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            return (RuntimeHelpers.GetHashCode(_root) * 397) ^ (int)_kind;
        }
    }
}
