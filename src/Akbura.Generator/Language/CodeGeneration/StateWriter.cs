using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct StateWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly CSharpSyntaxWriter _syntaxWriter;
    private readonly UseHookInvocationWriter _hookWriter;
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
        _hookWriter = new UseHookInvocationWriter(writer!);
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
        _ownerTypeName = ownerTypeName;
    }

    public void Write(in ComponentStatePlan plan)
    {
        WriteStateInfo(plan);
        _writer.WriteLine();
        WriteStateInfoFactory(plan);
        _writer.WriteLine();
        WriteStateInfoDelegate(plan);
        _writer.WriteLine();
        WriteStateField(plan);
        _writer.WriteLine();
        WriteStateAccessor(plan);
        _writer.WriteLine();
        WriteValueProperty(plan);
        _writer.WriteLine();
        WriteFactory(plan);
    }

    public void WriteHotReloadAssignment(in ComponentStatePlan plan)
    {
        GeneratedMemberNameWriter.WriteStateInfoField(
            _writer,
            plan.GeneratedName);
        _writer.Write(" = ");
        GeneratedMemberNameWriter.WriteStateInfoFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("();");
    }

    private void WriteStateInfo(in ComponentStatePlan plan)
    {
        _writer.WriteLine("#if DEBUG");
        WriteStateInfoDeclaration(plan, isReadOnly: false);
        _writer.WriteLine("#else");
        WriteStateInfoDeclaration(plan, isReadOnly: true);
        _writer.WriteLine("#endif");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteStateInfoFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("();");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteStateInfoDeclaration(
        in ComponentStatePlan plan,
        bool isReadOnly)
    {
        _writer.Write("private static ");

        if (isReadOnly)
        {
            _writer.Write("readonly ");
        }

        _writer.Write("global::Akbura.ComponentTree.StateInfo<");
        WriteValueType(plan);
        _writer.Write("> ");
        GeneratedMemberNameWriter.WriteStateInfoField(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(" =");
    }

    private void WriteStateInfoFactory(in ComponentStatePlan plan)
    {
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private static global::Akbura.ComponentTree.StateInfo<");
        WriteValueType(plan);
        _writer.Write("> ");
        GeneratedMemberNameWriter.WriteStateInfoFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("return ");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            _writer.Write("global::Akbura.ComponentTree.StateInfo<");
            WriteValueType(plan);
            _writer.WriteLine(">.FromState(");
        }
        else
        {
            _writer.Write("new global::Akbura.ComponentTree.StateInfo<");
            WriteValueType(plan);
            _writer.WriteLine(">(");
        }

        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(",");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            GeneratedMemberNameWriter.WriteStateInfoStateFactory(
                _writer,
                plan.GeneratedName);
        }
        else
        {
            GeneratedMemberNameWriter.WriteStateInfoValueFactory(
                _writer,
                plan.GeneratedName);
        }

        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize * 2;
        _writer.WriteLine("}");
    }

    private void WriteStateInfoDelegate(in ComponentStatePlan plan)
    {
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private static ");

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
            GeneratedMemberNameWriter.WriteStateInfoStateFactory(
                _writer,
                plan.GeneratedName);
        }
        else
        {
            GeneratedMemberNameWriter.WriteStateInfoValueFactory(
                _writer,
                plan.GeneratedName);
        }

        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("global::Akbura.AkburaControl __owner) =>");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("((");
        _writer.Write(_ownerTypeName);
        _writer.Write(")__owner).");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            GeneratedMemberNameWriter.WriteStateFactory(
                _writer,
                plan.GeneratedName);
        }
        else
        {
            GeneratedMemberNameWriter.WriteStateValueFactory(
                _writer,
                plan.GeneratedName);
        }

        _writer.WriteLine("();");
        _writer.CurrentIndent -= _writer.TabSize * 2;
    }

    private void WriteStateField(in ComponentStatePlan plan)
    {
        _writer.Write("private global::Akbura.ComponentTree.State<");
        WriteValueType(plan);
        _writer.Write(">? ");
        GeneratedMemberNameWriter.WriteStateField(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(";");
    }

    private void WriteStateAccessor(in ComponentStatePlan plan)
    {
        _writer.Write("private global::Akbura.ComponentTree.State<");
        WriteValueType(plan);
        _writer.Write("> ");
        GeneratedMemberNameWriter.WriteStateAccessor(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(" =>");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteStateField(
            _writer,
            plan.GeneratedName);
        if (plan.IsComposable)
        {
            _writer.WriteLine(" ?? throw new global::System.InvalidOperationException(");
            _writer.CurrentIndent += _writer.TabSize;
            _writer.WriteLine("\"The composed hook state is not prepared for this frame.\");");
            _writer.CurrentIndent -= _writer.TabSize * 2;
            return;
        }

        _writer.Write(" ??= CreateState(");
        GeneratedMemberNameWriter.WriteStateInfoField(
            _writer,
            plan.GeneratedName);
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
        GeneratedMemberNameWriter.WriteStateAccessor(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(".Value;");

        if (!plan.IsReadOnly)
        {
            _writer.Write("set => ");
            GeneratedMemberNameWriter.WriteStateAccessor(
                _writer,
                plan.GeneratedName);
            _writer.WriteLine(".Value = value;");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteFactory(in ComponentStatePlan plan)
    {
        _writer.WriteHiddenApiAttributes();
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
            GeneratedMemberNameWriter.WriteStateFactory(
                _writer,
                plan.GeneratedName);
        }
        else
        {
            GeneratedMemberNameWriter.WriteStateValueFactory(
                _writer,
                plan.GeneratedName);
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
            WriteInitializer(plan);
            _writer.WriteLine(";");
        }
        else
        {
            _writer.Write(returnPrefix);
            WriteInitializer(plan);
            _writer.WriteLine(";");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteInitializer(in ComponentStatePlan plan)
    {
        if (plan.HookMethod is { } method)
        {
            _hookWriter.Write(method, (InvocationExpressionSyntax)plan.Initializer, plan.StateArguments);
        }
        else
        {
            _syntaxWriter.WriteExpression(plan.Initializer);
        }
    }

    private void WriteValueType(in ComponentStatePlan plan)
    {
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.ValueType);
    }
}
