namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentRenderCaptureWriter
{
    private readonly CodeWriter _writer;

    public ComponentRenderCaptureWriter(CodeWriter writer)
    {
        _writer = writer;
    }

    public void WritePublishes(in ComponentPlan plan, ComponentRenderStatementPhase phase)
    {
        var values = new CSharpValueWriter(_writer);
        foreach (var statement in plan.RenderStatements)
        {
            if ((statement.Phase & phase) == 0 || statement.RenderCaptures.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var capture in statement.RenderCaptures)
            {
                _writer.Write("this.__akburaRenderState.SetRenderCapture<");
                values.WriteTypeNameWithNullableAnnotation(capture.Type);
                _writer.Write(">(");
                _writer.WriteStringLiteral(capture.Key);
                _writer.Write(", ");
                values.WriteIdentifier(capture.Name);
                _writer.WriteLine(");");
            }
        }
    }

    public void WriteReads(in ComponentPlan plan, int scopeId = -1)
    {
        var values = new CSharpValueWriter(_writer);
        foreach (var statement in plan.RenderStatements)
        {
            if (statement.RenderCaptures.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var capture in statement.RenderCaptures)
            {
                if (scopeId >= 0 && !capture.IsReadByScope(scopeId))
                {
                    continue;
                }

                _writer.Write("var ");
                values.WriteIdentifier(capture.Name);
                _writer.Write(" = this.__akburaRenderState.GetRenderCapture<");
                values.WriteTypeNameWithNullableAnnotation(capture.Type);
                _writer.Write(">(");
                _writer.WriteStringLiteral(capture.Key);
                _writer.WriteLine(");");
            }
        }
    }
}
