using Akbura.Pools;
using Akbura.Language.Syntax;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Declarations;

internal sealed class AkburaDeclarationCollector : SyntaxVisitor
{
    private static readonly ObjectPool<AkburaDeclarationCollector> s_pool = new(
        pool => new AkburaDeclarationCollector(pool),
        size: 16);

    private readonly ObjectPool<AkburaDeclarationCollector> _pool;
    private readonly ImmutableArray<AkburaDeclaration>.Builder _children = ImmutableArray.CreateBuilder<AkburaDeclaration>();
    private AkburaSyntaxTree? _syntaxTree;
    private AkcssSyntaxTree? _akcssSyntaxTree;

    private AkburaDeclarationCollector(ObjectPool<AkburaDeclarationCollector> pool)
    {
        _pool = pool;
    }

    public static AkburaDeclaration Collect(AkburaSyntaxTree syntaxTree)
    {
        var collector = s_pool.Allocate();
        try
        {
            return collector.CollectCore(syntaxTree);
        }
        finally
        {
            collector.Free();
        }
    }

    public static AkburaDeclaration Collect(AkcssSyntaxTree syntaxTree)
    {
        var collector = s_pool.Allocate();
        try
        {
            return collector.CollectCore(syntaxTree);
        }
        finally
        {
            collector.Free();
        }
    }

    private AkburaDeclaration CollectCore(AkburaSyntaxTree syntaxTree)
    {
        Debug.Assert(_children.Count == 0);
        Debug.Assert(_syntaxTree == null);
        Debug.Assert(_akcssSyntaxTree == null);

        _syntaxTree = syntaxTree;

        var root = syntaxTree.GetRoot();
        VisitAkburaDocumentSyntax(root);

        return new AkburaDeclaration(
            AkburaDeclarationKind.Component,
            syntaxTree.ComponentName,
            root,
            syntaxTree,
            children: _children.ToImmutable());
    }

    private AkburaDeclaration CollectCore(AkcssSyntaxTree syntaxTree)
    {
        Debug.Assert(_children.Count == 0);
        Debug.Assert(_syntaxTree == null);
        Debug.Assert(_akcssSyntaxTree == null);

        _akcssSyntaxTree = syntaxTree;

        var root = syntaxTree.GetRoot();
        VisitAkcssDocumentSyntax(root);

        return new AkburaDeclaration(
            AkburaDeclarationKind.AkcssModule,
            syntaxTree.LogicalName,
            root,
            akcssSyntaxTree: syntaxTree,
            children: _children.ToImmutable());
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
        Add(AkburaDeclarationKind.UseEffect, "useEffect", node, CollectUseEffectChildren(node));
    }

    public override void VisitUserHookSyntax(UserHookSyntax node)
    {
        Add(
            AkburaDeclarationKind.UserHook,
            node.Name.ToFullString().Trim(),
            node,
            ImmutableArray.Create(CreateCSharpBlockDeclaration(node.Body)));
    }

    public override void VisitCSharpStatementSyntax(CSharpStatementSyntax node)
    {
        Add(
            AkburaDeclarationKind.CSharpStatement,
            GetCSharpStatementName(node),
            node,
            CollectCSharpStatementChildren(node));
    }

    public override void VisitCSharpBlockSyntax(CSharpBlockSyntax node)
    {
        Add(
            AkburaDeclarationKind.CSharpBlock,
            "{...}",
            node,
            CollectCSharpBlockMembers(node));
    }

    public override void VisitMarkupRootSyntax(MarkupRootSyntax node)
    {
        Add(CreateMarkupRootDeclaration(node));
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
        var nested = s_pool.Allocate();
        try
        {
            nested._syntaxTree = _syntaxTree;
            nested._akcssSyntaxTree = _akcssSyntaxTree;

            foreach (var member in members)
            {
                nested.Visit(member);
            }

            return nested._children.ToImmutable();
        }
        finally
        {
            nested.Free();
        }
    }

    private ImmutableArray<AkburaDeclaration> CollectUseEffectChildren(UseEffectDeclarationSyntax node)
    {
        var builder = ImmutableArray.CreateBuilder<AkburaDeclaration>(1 + node.Tails.Count);
        builder.Add(CreateCSharpBlockDeclaration(node.Body));

        foreach (var tail in node.Tails)
        {
            builder.Add(CreateCSharpBlockDeclaration(tail.Body));
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<AkburaDeclaration> CollectCSharpStatementChildren(CSharpStatementSyntax statement)
    {
        return statement.Body == null
            ? ImmutableArray<AkburaDeclaration>.Empty
            : ImmutableArray.Create(CreateCSharpBlockDeclaration(statement.Body));
    }

    private ImmutableArray<AkburaDeclaration> CollectCSharpBlockMembers(CSharpBlockSyntax block)
    {
        var nested = s_pool.Allocate();
        try
        {
            nested._syntaxTree = _syntaxTree;
            nested._akcssSyntaxTree = _akcssSyntaxTree;

            foreach (var member in block.Tokens)
            {
                nested.Visit(member);
            }

            return nested._children.ToImmutable();
        }
        finally
        {
            nested.Free();
        }
    }

    private AkburaDeclaration CreateCSharpBlockDeclaration(CSharpBlockSyntax block)
    {
        return new AkburaDeclaration(
            AkburaDeclarationKind.CSharpBlock,
            "{...}",
            block,
            _syntaxTree,
            _akcssSyntaxTree,
            CollectCSharpBlockMembers(block));
    }

    private AkburaDeclaration CreateMarkupRootDeclaration(MarkupRootSyntax root)
    {
        return new AkburaDeclaration(
            AkburaDeclarationKind.MarkupRoot,
            GetMarkupElementName(root.Element),
            root,
            _syntaxTree,
            _akcssSyntaxTree,
            CollectMarkupElementChildren(root.Element));
    }

    private ImmutableArray<AkburaDeclaration> CollectMarkupElementChildren(MarkupElementSyntax element)
    {
        var builder = ImmutableArray.CreateBuilder<AkburaDeclaration>();
        foreach (var content in element.Body)
        {
            if (content.Kind == AkburaSyntaxKind.MarkupElementContentSyntax)
            {
                builder.Add(CreateMarkupElementDeclaration(Unsafe.As<MarkupElementContentSyntax>(content).Element));
            }
        }

        return builder.ToImmutable();
    }

    private AkburaDeclaration CreateMarkupElementDeclaration(MarkupElementSyntax element)
    {
        return new AkburaDeclaration(
            AkburaDeclarationKind.MarkupElement,
            GetMarkupElementName(element),
            element,
            _syntaxTree,
            _akcssSyntaxTree,
            CollectMarkupElementChildren(element));
    }

    private static string GetMarkupElementName(MarkupElementSyntax element)
    {
        return element.StartTag?.Name.ToFullString().Trim() ?? string.Empty;
    }

    private static string GetCSharpStatementName(CSharpStatementSyntax statement)
    {
        var text = statement.Tokens.ToFullString().Trim();
        return text.Length == 0
            ? "statement"
            : text;
    }

    private void Add(
        AkburaDeclarationKind kind,
        string name,
        AkburaSyntax syntax,
        ImmutableArray<AkburaDeclaration> children = default)
    {
        Add(new AkburaDeclaration(
            kind,
            name,
            syntax,
            _syntaxTree,
            _akcssSyntaxTree,
            children));
    }

    private void Add(AkburaDeclaration declaration)
    {
        _children.Add(declaration);
    }

    private void Free()
    {
        _syntaxTree = null;
        _akcssSyntaxTree = null;
        _children.Clear();
        _pool.Free(this);
    }
}
