using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax.Green;

internal abstract partial class GreenSyntaxVisitor
{
    public virtual void DefaultVisit(GreenNode GreenNode)
    {

    }

    public virtual void Visit(GreenNode GreenNode)
    {
        GreenNode.Accept(this);
    }

    public virtual void VisitToken(GreenSyntaxToken token)
    {
        DefaultVisit(token);
    }

    public virtual void VisitTrivia(GreenSyntaxTrivia trivia)
    {
        DefaultVisit(trivia);
    }
}

internal abstract partial class GreenSyntaxVisitor<TResult>
{
    public virtual TResult? DefaultVisit(GreenNode GreenNode)
    {
        return default;
    }

    public virtual TResult? Visit(GreenNode? GreenNode)
    {
        if(GreenNode == null)
        {
            return default;
        }

        return GreenNode.Accept(this);
    }

    public virtual TResult? VisitToken(GreenSyntaxToken token)
    {
       return DefaultVisit(token);
    }

    public virtual TResult? VisitTrivia(GreenSyntaxTrivia trivia)
    {
        return DefaultVisit(trivia);
    }
}

internal abstract partial class GreenSyntaxVisitor<TParameter, TResult>
{
    public virtual TResult? DefaultVisit(GreenNode GreenNode, TParameter parameter)
    {
        return default;
    }

    public virtual TResult? Visit(GreenNode GreenNode, TParameter parameter)
    {
        return GreenNode.Accept(this, parameter);
    }

    public virtual TResult? VisitToken(GreenSyntaxToken token, TParameter parameter)
    {
        return DefaultVisit(token, parameter);
    }

    public virtual TResult? VisitTrivia(GreenSyntaxTrivia trivia, TParameter parameter)
    {
        return DefaultVisit(trivia, parameter);
    }
}