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
        WriteStateArray(plan.States.AsSpan());
        _writer.WriteLine();
        WriteGetters(!plan.States.IsDefaultOrEmpty);
    }

    private void WriteParameterArray(ReadOnlySpan<ComponentParameterPlan> parameters)
    {
        WriteStaticArrayStart(ParameterType, "s_parameters");

        for (var i = 0; i < parameters.Length; i++)
        {
            ref readonly var parameter = ref parameters[i];
            _valueWriter.WriteIdentifier(parameter.Name);
            _writer.WriteLine("Property,");
        }

        WriteArrayEnd();
    }

    private void WriteCommandArray(ReadOnlySpan<ComponentCommandPlan> commands)
    {
        WriteStaticArrayStart(CommandType, "s_commands");

        for (var i = 0; i < commands.Length; i++)
        {
            ref readonly var command = ref commands[i];
            _valueWriter.WriteIdentifier(command.Name);
            _writer.WriteLine("Property,");
        }

        WriteArrayEnd();
    }

    private void WriteServiceArray(ReadOnlySpan<ComponentInjectServicePlan> services)
    {
        WriteStaticArrayStart(ServiceType, "s_services");

        for (var i = 0; i < services.Length; i++)
        {
            ref readonly var service = ref services[i];
            _valueWriter.WriteIdentifier(service.Name);
            _writer.WriteLine("Property,");
        }

        WriteArrayEnd();
    }

    private void WriteStateArray(ReadOnlySpan<ComponentStatePlan> states)
    {
        if (states.IsEmpty)
        {
            WriteStaticArrayStart(StateType, "s_states");
            WriteArrayEnd();
            return;
        }

        _writer.Write("private global::System.Collections.Immutable.ImmutableArray<");
        _writer.Write(StateType);
        _writer.WriteLine("> __states;");
        _writer.WriteLine();
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
            GeneratedMemberNameWriter.WriteStateAccessor(_writer, state.Id);
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

    private void WriteStaticArrayStart(string elementType, string fieldName)
    {
        _writer.Write("private static readonly global::System.Collections.Immutable.ImmutableArray<");
        _writer.Write(elementType);
        _writer.Write("> ");
        _writer.Write(fieldName);
        _writer.WriteLine(" =");
        _writer.WriteLine("[");
        _writer.CurrentIndent += _writer.TabSize;
    }

    private void WriteArrayEnd()
    {
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("];");
    }

    private void WriteGetters(bool hasStates)
    {
        WriteGetter(ParameterType, "GetParameters", "s_parameters");
        _writer.WriteLine();
        WriteGetter(CommandType, "GetCommands", "s_commands");
        _writer.WriteLine();
        WriteGetter(ServiceType, "GetServices", "s_services");
        _writer.WriteLine();
        WriteGetter(StateType, "GetStates", hasStates ? "__GetStates()" : "s_states");
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
}
