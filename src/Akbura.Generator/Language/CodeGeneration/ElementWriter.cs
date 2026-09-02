using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ElementWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly SourceMappingWriter _sourceMappingWriter;

    public ElementWriter(CodeWriter writer, ComponentGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _sourceMappingWriter = new SourceMappingWriter(writer!, sourceMap!);
    }

    public void WriteField(in ComponentElementPlan element)
    {
        if (element.IsLocal)
        {
            return;
        }

        _writer.Write("private ");
        _valueWriter.WriteTypeName(element.Type);
        _writer.Write(" ");
        _writer.Write(element.Identifier);
        _writer.WriteLine(" = null!;");
    }

    public void WriteCreation(in ComponentElementPlan element)
    {
        using var mapping = _sourceMappingWriter.WriteStart(element.Syntax);

        if (element.IsLocal)
        {
            _writer.Write("var ");
        }

        _writer.Write(element.Identifier);
        _writer.Write(" = new ");
        _valueWriter.WriteTypeName(element.Type);
        _writer.WriteLine("();");
    }

    public void WriteBeginInit(in ComponentElementPlan element)
    {
        if (!element.SupportsInitialize)
        {
            return;
        }

        _writer.Write("((global::System.ComponentModel.ISupportInitialize)");
        _writer.Write(element.Identifier);
        _writer.WriteLine(").BeginInit();");
    }

    public void WriteEndInit(in ComponentElementPlan element)
    {
        if (!element.SupportsInitialize)
        {
            return;
        }

        _writer.Write("((global::System.ComponentModel.ISupportInitialize)");
        _writer.Write(element.Identifier);
        _writer.WriteLine(").EndInit();");
    }
}
