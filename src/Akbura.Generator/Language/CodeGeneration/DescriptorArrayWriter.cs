using System;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct DescriptorArrayWriter
{
    private const string ParameterType =
        "global::Akbura.ComponentTree.Parameter";
    private const string CommandType =
        "global::Avalonia.AvaloniaProperty<global::Akbura.IAkburaCommand>";
    private const string ServiceType =
        "global::Akbura.ComponentTree.InjectService";
    private const string StateType =
        "global::Akbura.ComponentTree.State";

    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;

    public DescriptorArrayWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
    }

    public void Write(in ComponentMemberPlan plan)
    {
        WriteParameterArray(plan.Parameters.AsSpan());
        _writer.WriteLine();
        WriteCommandArray(plan.Commands.AsSpan());
        _writer.WriteLine();
        WriteServiceArray(plan.Services.AsSpan());
        _writer.WriteLine();
        WriteStateCache(plan.States.AsSpan());
        _writer.WriteLine();
        WriteGetters();
    }

    public void WriteHotReloadAssignments(in ComponentMemberPlan plan)
    {
        WriteArrayAssignment(
            "s_parameters",
            plan.Parameters.AsSpan());
        _writer.WriteLine();
        WriteArrayAssignment(
            "s_commands",
            plan.Commands.AsSpan());
        _writer.WriteLine();
        WriteArrayAssignment(
            "s_services",
            plan.Services.AsSpan());
    }

    private void WriteParameterArray(ReadOnlySpan<ComponentParameterPlan> parameters)
    {
        WriteStaticArrayStart(ParameterType, "s_parameters");
        WriteDescriptorNames(parameters);
        WriteArrayEnd();
    }

    private void WriteCommandArray(ReadOnlySpan<ComponentCommandPlan> commands)
    {
        WriteStaticArrayStart(CommandType, "s_commands");
        WriteDescriptorNames(commands);
        WriteArrayEnd();
    }

    private void WriteServiceArray(ReadOnlySpan<ComponentInjectServicePlan> services)
    {
        WriteStaticArrayStart(ServiceType, "s_services");
        WriteDescriptorNames(services);
        WriteArrayEnd();
    }

    private void WriteStateCache(ReadOnlySpan<ComponentStatePlan> states)
    {
        _writer.Write("private global::System.Collections.Immutable.ImmutableArray<");
        _writer.Write(StateType);
        _writer.WriteLine("> __states;");
        _writer.WriteLine();
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private global::System.Collections.Immutable.ImmutableArray<");
        _writer.Write(StateType);
        _writer.WriteLine("> __GetStates()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("if (__states.IsDefault)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__states =");
        _writer.WriteLine("[");
        _writer.CurrentIndent += _writer.TabSize;

        for (var i = 0; i < states.Length; i++)
        {
            ref readonly var state = ref states[i];
            GeneratedMemberNameWriter.WriteStateAccessor(
                _writer,
                state.GeneratedName);
            _writer.WriteLine(",");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("];");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("return __states;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteStaticArrayStart(
        string elementType,
        string fieldName)
    {
        _writer.WriteLine("#if DEBUG");
        WriteStaticArrayDeclaration(
            elementType,
            fieldName,
            isReadOnly: false);
        _writer.WriteLine("#else");
        WriteStaticArrayDeclaration(
            elementType,
            fieldName,
            isReadOnly: true);
        _writer.WriteLine("#endif");
        _writer.WriteLine("[");
        _writer.CurrentIndent += _writer.TabSize;
    }

    private void WriteStaticArrayDeclaration(
        string elementType,
        string fieldName,
        bool isReadOnly)
    {
        _writer.Write("private static ");

        if (isReadOnly)
        {
            _writer.Write("readonly ");
        }

        _writer.Write("global::System.Collections.Immutable.ImmutableArray<");
        _writer.Write(elementType);
        _writer.Write("> ");
        _writer.Write(fieldName);
        _writer.WriteLine(" =");
    }

    private void WriteArrayAssignment(
        string fieldName,
        ReadOnlySpan<ComponentParameterPlan> parameters)
    {
        _writer.Write(fieldName);
        _writer.WriteLine(" =");
        _writer.WriteLine("[");
        _writer.CurrentIndent += _writer.TabSize;
        WriteDescriptorNames(parameters);
        WriteArrayEnd();
    }

    private void WriteArrayAssignment(
        string fieldName,
        ReadOnlySpan<ComponentCommandPlan> commands)
    {
        _writer.Write(fieldName);
        _writer.WriteLine(" =");
        _writer.WriteLine("[");
        _writer.CurrentIndent += _writer.TabSize;
        WriteDescriptorNames(commands);
        WriteArrayEnd();
    }

    private void WriteArrayAssignment(
        string fieldName,
        ReadOnlySpan<ComponentInjectServicePlan> services)
    {
        _writer.Write(fieldName);
        _writer.WriteLine(" =");
        _writer.WriteLine("[");
        _writer.CurrentIndent += _writer.TabSize;
        WriteDescriptorNames(services);
        WriteArrayEnd();
    }

    private void WriteDescriptorNames(ReadOnlySpan<ComponentParameterPlan> parameters)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            ref readonly var parameter = ref parameters[i];
            WriteDescriptorName(parameter.Name);
            _writer.WriteLine(",");
        }
    }

    private void WriteDescriptorNames(ReadOnlySpan<ComponentCommandPlan> commands)
    {
        for (var i = 0; i < commands.Length; i++)
        {
            ref readonly var command = ref commands[i];
            WriteDescriptorName(command.Name);
            _writer.WriteLine(",");
        }
    }

    private void WriteDescriptorNames(ReadOnlySpan<ComponentInjectServicePlan> services)
    {
        for (var i = 0; i < services.Length; i++)
        {
            ref readonly var service = ref services[i];
            WriteDescriptorName(service.Name);
            _writer.WriteLine(",");
        }
    }

    private void WriteArrayEnd()
    {
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("];");
    }

    private void WriteGetters()
    {
        WriteGetter(ParameterType, "GetParameters", "s_parameters");
        _writer.WriteLine();
        WriteGetter(CommandType, "GetCommands", "s_commands");
        _writer.WriteLine();
        WriteGetter(ServiceType, "GetServices", "s_services");
        _writer.WriteLine();
        WriteGetter(StateType, "GetStates", "__GetStates()");
    }

    private void WriteGetter(
        string elementType,
        string methodName,
        string result)
    {
        _writer.Write("protected override global::System.Collections.Immutable.ImmutableArray<");
        _writer.Write(elementType);
        _writer.Write("> ");
        _writer.Write(methodName);
        _writer.WriteLine("() =>");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(result);
        _writer.WriteLine(";");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteDescriptorName(string name)
    {
        _valueWriter.WriteIdentifier(name);
        _writer.Write("Property");
    }
}
