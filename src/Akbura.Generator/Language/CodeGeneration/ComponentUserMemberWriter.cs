using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentUserMemberWriter
{
    private readonly CSharpSyntaxWriter _syntaxWriter;
    private readonly SourceMappingWriter _mappings;

    public ComponentUserMemberWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _syntaxWriter = new CSharpSyntaxWriter(writer!);
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
    }

    public void Write(in ComponentUserMemberPlan plan)
    {
        using var mapping = _mappings.WriteStart(plan.Syntax);
        _syntaxWriter.WriteStatement(plan.Member);
    }
}
