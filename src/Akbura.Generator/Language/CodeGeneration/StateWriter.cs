using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct StateWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly CSharpSyntaxWriter _syntaxWriter;
    private readonly SourceMappingWriter _mappings;
    private readonly string _ownerTypeName;

    public StateWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _syntaxWriter = new CSharpSyntaxWriter(writer!);
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
        _ownerTypeName = ownerTypeName;
    }

    public void Write(in ComponentStatePlan plan)
    {
        WriteStateInfo(plan);
        _writer.WriteLine();
        WriteStateField(plan);
        _writer.WriteLine();
        WriteStateAccessor(plan);
        _writer.WriteLine();
        WriteValueProperty(plan);
        _writer.WriteLine();
        WriteFactory(plan);
    }

    private void WriteStateInfo(in ComponentStatePlan plan)
    {
        _writer.Write("private static readonly global::Akbura.ComponentTree.StateInfo<");
        WriteValueType(plan);
        _writer.Write("> ");
        GeneratedMemberNameWriter.WriteStateInfoField(_writer, plan.Id);
        _writer.WriteLine(" =");
        _writer.CurrentIndent += _writer.TabSize;

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            _writer.Write("global::Akbura.ComponentTree.StateInfo<");
            WriteValueType(plan);
            _writer.WriteLine(">.FromState(");
        }
        else
        {
            _writer.WriteLine("new(");
        }

        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(",");
        _writer.WriteLine("static __owner =>");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("((");
        _writer.Write(_ownerTypeName);
        _writer.Write(")__owner).");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            GeneratedMemberNameWriter.WriteStateFactory(_writer, plan.Id);
        }
        else
        {
            GeneratedMemberNameWriter.WriteStateValueFactory(_writer, plan.Id);
        }

        _writer.WriteLine("());");
        _writer.CurrentIndent -= _writer.TabSize * 3;
    }

    private void WriteStateField(in ComponentStatePlan plan)
    {
        _writer.Write("private global::Akbura.ComponentTree.State<");
        WriteValueType(plan);
        _writer.Write(">? ");
        GeneratedMemberNameWriter.WriteStateField(_writer, plan.Id);
        _writer.WriteLine(";");
    }

    private void WriteStateAccessor(in ComponentStatePlan plan)
    {
        _writer.Write("private global::Akbura.ComponentTree.State<");
        WriteValueType(plan);
        _writer.Write("> ");
        GeneratedMemberNameWriter.WriteStateAccessor(_writer, plan.Id);
        _writer.WriteLine(" =>");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteStateField(_writer, plan.Id);
        _writer.Write(" ??= CreateState(");
        GeneratedMemberNameWriter.WriteStateInfoField(_writer, plan.Id);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteValueProperty(in ComponentStatePlan plan)
    {
        _writer.Write("private ");
        WriteValueType(plan);
        _writer.Write(" ");
        _valueWriter.WriteIdentifier(plan.Name);
        _writer.WriteLine();
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("get => ");
        GeneratedMemberNameWriter.WriteStateAccessor(_writer, plan.Id);
        _writer.WriteLine(".Value;");

        if (!plan.IsReadOnly)
        {
            _writer.Write("set => ");
            GeneratedMemberNameWriter.WriteStateAccessor(_writer, plan.Id);
            _writer.WriteLine(".Value = value;");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteFactory(in ComponentStatePlan plan)
    {
        _writer.Write("private ");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            _writer.Write("global::Akbura.ComponentTree.State<");
            WriteValueType(plan);
            _writer.Write(">");
        }
        else
        {
            WriteValueType(plan);
        }

        _writer.Write(" ");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            GeneratedMemberNameWriter.WriteStateFactory(_writer, plan.Id);
        }
        else
        {
            GeneratedMemberNameWriter.WriteStateValueFactory(_writer, plan.Id);
        }

        _writer.WriteLine("()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        const string returnPrefix = "return ";

        if (plan.Syntax.Initializer.Expression is { } sourceSyntax &&
            sourceSyntax.GetRawCSharpExpression() is { } sourceExpression)
        {
            using var mapping = _mappings.WriteStart(
                sourceSyntax,
                sourceExpression.Span,
                returnPrefix.Length);

            _writer.Write(returnPrefix);
            _syntaxWriter.WriteExpression(plan.Initializer);
            _writer.WriteLine(";");
        }
        else
        {
            _writer.Write(returnPrefix);
            _syntaxWriter.WriteExpression(plan.Initializer);
            _writer.WriteLine(";");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteValueType(in ComponentStatePlan plan)
    {
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.ValueType);
    }
}
