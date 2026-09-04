using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentRenderStatementWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpSyntaxWriter _syntaxWriter;
    private readonly SourceMappingWriter _mappings;

    public ComponentRenderStatementWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _writer = writer!;
        _syntaxWriter = new CSharpSyntaxWriter(writer!);
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
    }

    public void Write(in ComponentRenderStatementPlan plan)
    {
        using var mapping = _mappings.WriteStart(plan.Syntax);

        switch (plan.Kind)
        {
            case ComponentRenderStatementKind.Statement:
                _syntaxWriter.WriteStatement((StatementSyntax)plan.Node);
                return;

            case ComponentRenderStatementKind.UseHookInvocation:
                _syntaxWriter.WriteExpression((ExpressionSyntax)plan.Node);
                _writer.WriteLine(";");
                return;

            default:
                Debug.Fail("An invalid render statement reached code generation.");
                return;
        }
    }

}
