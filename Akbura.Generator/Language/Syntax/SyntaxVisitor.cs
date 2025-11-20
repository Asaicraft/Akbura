using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax;
internal abstract partial class SyntaxVisitor
{
    public virtual void DefaultVisit(AkburaSyntax akburaSyntax)
    {

    }

    public virtual void Visit(AkburaSyntax akburaSyntax)
    {
        akburaSyntax.Accept(this);
    }
}

internal abstract partial class SyntaxVisitor<TResult>
{
    public virtual TResult? DefaultVisit(AkburaSyntax akburaSyntax)
    {
        return default;
    }

    public virtual TResult? Visit(AkburaSyntax akburaSyntax)
    {
        return akburaSyntax.Accept(this);
    }
}

internal abstract partial class SyntaxVisitor<TParameter, TResult>
{
    public virtual TResult? DefaultVisit(AkburaSyntax akburaSyntax, TParameter parameter)
    {
        return default;
    }

    public virtual TResult? Visit(AkburaSyntax akburaSyntax, TParameter parameter)
    {
        return akburaSyntax.Accept(this, parameter);
    }
}