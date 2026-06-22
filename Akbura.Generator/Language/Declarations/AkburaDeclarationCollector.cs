using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Declarations;

internal sealed class AkburaDeclarationCollector : SyntaxVisitor
{
    private readonly ImmutableArray<AkburaDeclaration>.Builder _children = ImmutableArray.CreateBuilder<AkburaDeclaration>();
    private readonly AkburaSyntaxTree? _syntaxTree;
    private readonly AkcssSyntaxTree? _akcssSyntaxTree;

    private AkburaDeclarationCollector(AkburaSyntaxTree syntaxTree)
    {
        _syntaxTree = syntaxTree;
    }

    private AkburaDeclarationCollector(AkcssSyntaxTree akcssSyntaxTree)
    {
        _akcssSyntaxTree = akcssSyntaxTree;
    }

    public static AkburaDeclaration Collect(AkburaSyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRoot();
        var collector = new AkburaDeclarationCollector(syntaxTree);
        collector.VisitAkburaDocumentSyntax(root);

        return new AkburaDeclaration(
            AkburaDeclarationKind.Component,
            syntaxTree.ComponentName,
            root,
            syntaxTree,
            children: collector._children.ToImmutable());
    }

    public static AkburaDeclaration Collect(AkcssSyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRoot();
        var collector = new AkburaDeclarationCollector(syntaxTree);
        collector.VisitAkcssDocumentSyntax(root);

        return new AkburaDeclaration(
            AkburaDeclarationKind.AkcssModule,
            syntaxTree.LogicalName,
            root,
            akcssSyntaxTree: syntaxTree,
            children: collector._children.ToImmutable());
    }

    public override void VisitAkburaDocumentSyntax(AkburaDocumentSyntax node)
    {
        foreach (var member in node.Members)
        {
            Visit(member);
        }
    }

    public override void VisitAkcssDocumentSyntax(AkcssDocumentSyntax node)
    {
        foreach (var member in node.Members)
        {
            Visit(member);
        }
    }

    public override void VisitNamespaceDeclarationSyntax(NamespaceDeclarationSyntax node)
    {
        Add(AkburaDeclarationKind.Namespace, node.Name.ToFullString().Trim(), node);
    }

    public override void VisitUsingDirectiveSyntax(UsingDirectiveSyntax node)
    {
        Add(AkburaDeclarationKind.Using, node.Name.ToFullString().Trim(), node);
    }

    public override void VisitStateDeclarationSyntax(StateDeclarationSyntax node)
    {
        Add(AkburaDeclarationKind.State, node.Name.ToFullString().Trim(), node);
    }

    public override void VisitParamDeclarationSyntax(ParamDeclarationSyntax node)
    {
        Add(AkburaDeclarationKind.Parameter, node.Name.ToFullString().Trim(), node);
    }

    public override void VisitInjectDeclarationSyntax(InjectDeclarationSyntax node)
    {
        Add(AkburaDeclarationKind.InjectedService, node.Name.ToFullString().Trim(), node);
    }

    public override void VisitCommandDeclarationSyntax(CommandDeclarationSyntax node)
    {
        Add(AkburaDeclarationKind.Command, node.Name.ToFullString().Trim(), node);
    }

    public override void VisitUseEffectDeclarationSyntax(UseEffectDeclarationSyntax node)
    {
        Add(AkburaDeclarationKind.UseEffect, "useEffect", node);
    }

    public override void VisitUserHookSyntax(UserHookSyntax node)
    {
        Add(AkburaDeclarationKind.UserHook, node.Name.ToFullString().Trim(), node);
    }

    public override void VisitMarkupRootSyntax(MarkupRootSyntax node)
    {
        Add(AkburaDeclarationKind.MarkupRoot, node.Element.StartTag?.Name.ToFullString().Trim() ?? string.Empty, node);
    }

    public override void VisitInlineAkcssBlockSyntax(InlineAkcssBlockSyntax node)
    {
        var children = CollectAkcssMembers(node.Members);
        Add(AkburaDeclarationKind.AkcssModule, "@akcss", node, children);
    }

    public override void VisitAkcssUsingDirectiveSyntax(AkcssUsingDirectiveSyntax node)
    {
        Add(AkburaDeclarationKind.AkcssUsing, node.Name.ToFullString().Trim(), node);
    }

    public override void VisitAkcssStyleRuleSyntax(AkcssStyleRuleSyntax node)
    {
        Add(AkburaDeclarationKind.AkcssStyle, node.Selector.ToFullString().Trim(), node);
    }

    public override void VisitAkcssUtilityDeclarationSyntax(AkcssUtilityDeclarationSyntax node)
    {
        Add(AkburaDeclarationKind.AkcssUtility, node.Selector.Name.ToFullString().Trim(), node);
    }

    public override void VisitAkcssUtilitiesSectionSyntax(AkcssUtilitiesSectionSyntax node)
    {
        foreach (var utility in node.Utilities)
        {
            Visit(utility);
        }
    }

    private ImmutableArray<AkburaDeclaration> CollectAkcssMembers(SyntaxList<AkcssTopLevelMemberSyntax> members)
    {
        var nested = new AkburaDeclarationCollector(_syntaxTree!);
        foreach (var member in members)
        {
            nested.Visit(member);
        }

        return nested._children.ToImmutable();
    }

    private void Add(
        AkburaDeclarationKind kind,
        string name,
        AkburaSyntax syntax,
        ImmutableArray<AkburaDeclaration> children = default)
    {
        _children.Add(new AkburaDeclaration(
            kind,
            name,
            syntax,
            _syntaxTree,
            _akcssSyntaxTree,
            children));
    }
}
