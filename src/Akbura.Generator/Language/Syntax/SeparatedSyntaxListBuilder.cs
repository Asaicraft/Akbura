using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax;
internal struct SeparatedSyntaxListBuilder<TNode> where TNode : AkburaSyntax
{
    private readonly SyntaxListBuilder _builder;
    private bool _expectedSeparator;

    public SeparatedSyntaxListBuilder(int size)
        : this(new SyntaxListBuilder(size))
    {
    }

    public static SeparatedSyntaxListBuilder<TNode> Create()
    {
        return new SeparatedSyntaxListBuilder<TNode>(8);
    }

    public SeparatedSyntaxListBuilder(SyntaxListBuilder builder)
    {
        _builder = builder;
        _expectedSeparator = false;
    }

    public readonly bool IsNull => _builder == null;

    public readonly int Count => _builder.Count;

    public readonly void Clear()
    {
        _builder.Clear();
    }

    private readonly void CheckExpectedElement()
    {
        if (_expectedSeparator)
        {
            throw new InvalidOperationException("Separator is expected.");
        }
    }

    private readonly void CheckExpectedSeparator()
    {
        if (!_expectedSeparator)
        {
            throw new InvalidOperationException("Element is expected.");
        }
    }

    public SeparatedSyntaxListBuilder<TNode> Add(TNode node)
    {
        CheckExpectedElement();
        _expectedSeparator = true;
        _builder.Add(node);
        return this;
    }

    public SeparatedSyntaxListBuilder<TNode> AddSeparator(in SyntaxToken separatorToken)
    {
        Debug.Assert(separatorToken.Node is not null);
        CheckExpectedSeparator();
        _expectedSeparator = false;
        _builder.AddInternal(separatorToken.Node!);
        return this;
    }

    public SeparatedSyntaxListBuilder<TNode> AddRange(in SeparatedSyntaxList<TNode> nodes)
    {
        CheckExpectedElement();
        var list = nodes.GetWithSeparators();
        _builder.AddRange(list);
        _expectedSeparator = ((_builder.Count & 1) != 0);
        return this;
    }

    public SeparatedSyntaxListBuilder<TNode> AddRange(in SeparatedSyntaxList<TNode> nodes, int count)
    {
        CheckExpectedElement();
        var list = nodes.GetWithSeparators();
        _builder.AddRange(list, Count, Math.Min(count << 1, list.Count));
        _expectedSeparator = ((_builder.Count & 1) != 0);
        return this;
    }

    public SeparatedSyntaxList<TNode> ToList()
    {
        if (_builder == null)
        {
            return new SeparatedSyntaxList<TNode>();
        }

        return _builder.ToSeparatedList<TNode>();
    }

    public SeparatedSyntaxList<TDerived> ToList<TDerived>() where TDerived : TNode
    {
        if (_builder == null)
        {
            return new SeparatedSyntaxList<TDerived>();
        }

        return _builder.ToSeparatedList<TDerived>();
    }

    public static implicit operator SyntaxListBuilder(in SeparatedSyntaxListBuilder<TNode> builder)
    {
        return builder._builder;
    }

    public static implicit operator SeparatedSyntaxList<TNode>(in SeparatedSyntaxListBuilder<TNode> builder)
    {
        if (builder._builder != null)
        {
            return builder.ToList();
        }

        return default;
    }
}