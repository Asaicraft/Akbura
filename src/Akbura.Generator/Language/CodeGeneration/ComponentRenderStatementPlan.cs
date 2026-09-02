using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal enum ComponentRenderStatementKind : byte
{
    None,
    Statement,
    UseHookInvocation,
}

[Flags]
internal enum ComponentRenderStatementPhase : byte
{
    None = 0,
    FirstUpdate = 1 << 0,
    Update = 1 << 1,
    Both = FirstUpdate | Update,
}

internal readonly struct ComponentRenderStatementPlan
{
    public ComponentRenderStatementPlan(
        ComponentRenderStatementKind kind,
        CSharpSyntaxNode node,
        CSharpStatementSyntax syntax,
        ComponentRenderStatementPhase phase = ComponentRenderStatementPhase.Update)
    {
        Debug.Assert(kind != ComponentRenderStatementKind.None);
        Debug.Assert(node != null);
        Debug.Assert(syntax != null);
        Debug.Assert(phase != ComponentRenderStatementPhase.None);

        Kind = kind;
        Node = node!;
        Syntax = syntax!;
        Phase = phase;
    }

    public ComponentRenderStatementKind Kind { get; }

    public CSharpSyntaxNode Node { get; }

    public CSharpStatementSyntax Syntax { get; }

    public ComponentRenderStatementPhase Phase { get; }

    public bool WritesDuringFirstUpdate =>
        (Phase & ComponentRenderStatementPhase.FirstUpdate) != 0;

    public bool WritesDuringUpdate =>
        (Phase & ComponentRenderStatementPhase.Update) != 0;
}
