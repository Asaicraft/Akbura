using System;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Stages the component aliases of states owned by the current hook frame.
/// </summary>
internal readonly ref struct ComponentHookStateWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;

    public ComponentHookStateWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);
        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
    }

    public void Write(in ComponentMemberPlan plan)
    {
        var states = plan.States.AsSpan();
        WriteFields(states);
        _writer.WriteLine();
        WritePrepare(states);
        _writer.WriteLine();
        WriteCommit(states);
        _writer.WriteLine();
        WriteAbort(states);
        _writer.WriteLine();
        WriteReset(states);
        _writer.WriteLine();
        WriteNames(states);
    }

    private void WriteFields(ReadOnlySpan<ComponentStatePlan> states)
    {
        _writer.WriteLine("#if !DEBUG");
        _writer.WriteLine(
            "private global::System.Collections.Immutable.ImmutableArray<" +
            "global::Akbura.ComponentTree.State> __previousHookStates;");

        foreach (ref readonly var state in states)
        {
            if (!state.IsComposable)
            {
                continue;
            }

            _writer.Write("private global::Akbura.ComponentTree.State<");
            _valueWriter.WriteTypeNameWithNullableAnnotation(state.ValueType);
            _writer.Write(">? ");
            WritePrevious(state);
            _writer.WriteLine(";");
        }

        _writer.WriteLine("#endif");
    }

    private void WritePrepare(ReadOnlySpan<ComponentStatePlan> states)
    {
        WriteMethodStart("PrepareHookStates");
        _writer.WriteLine("#if DEBUG");
        _writer.WriteLine("RuntimeStates.BeginFrame();");
        _writer.WriteLine("#else");
        _writer.WriteLine("__previousHookStates = __states;");
        foreach (ref readonly var state in states)
        {
            if (state.IsComposable)
            {
                WritePrevious(state);
                _writer.Write(" = ");
                GeneratedMemberNameWriter.WriteStateField(_writer, state.GeneratedName);
                _writer.WriteLine(";");
            }
        }

        _writer.WriteLine("#endif");

        foreach (ref readonly var state in states)
        {
            if (!state.IsComposable)
            {
                _writer.Write("_ = ");
                GeneratedMemberNameWriter.WriteStateAccessor(_writer, state.GeneratedName);
                _writer.WriteLine(";");
                continue;
            }

            _writer.WriteLine("#if DEBUG");
            _writer.Write("RuntimeStates.StageHook<");
            _valueWriter.WriteTypeNameWithNullableAnnotation(state.ValueType);
            _writer.Write(">(");
            _writer.WriteStringLiteral(state.HotReloadKey);
            _writer.Write(", BindHookState(");
            GeneratedMemberNameWriter.WriteStateFactory(_writer, state.GeneratedName);
            _writer.Write("(), RuntimeStates.GetBoundOrNull<");
            _valueWriter.WriteTypeNameWithNullableAnnotation(state.ValueType);
            _writer.Write(">(");
            _writer.WriteStringLiteral(state.HotReloadKey);
            _writer.WriteLine(")));");
            _writer.WriteLine("#else");
            GeneratedMemberNameWriter.WriteStateField(_writer, state.GeneratedName);
            _writer.Write(" = BindHookState(");
            GeneratedMemberNameWriter.WriteStateFactory(_writer, state.GeneratedName);
            _writer.Write("(), ");
            WritePrevious(state);
            _writer.WriteLine(");");
            _writer.WriteLine("#endif");
        }

        WriteMethodEnd();
    }

    private void WriteCommit(ReadOnlySpan<ComponentStatePlan> states)
    {
        WriteMethodStart("CommitHookStates");
        _writer.WriteLine("#if DEBUG");
        _writer.WriteLine("RuntimeStates.CommitFrame();");
        _writer.WriteLine("#else");
        WriteClearPrevious(states);
        _writer.WriteLine("#endif");
        WriteMethodEnd();
    }

    private void WriteAbort(ReadOnlySpan<ComponentStatePlan> states)
    {
        WriteMethodStart("AbortHookStates");
        _writer.WriteLine("#if DEBUG");
        _writer.WriteLine("RuntimeStates.AbortFrame();");
        _writer.WriteLine("#else");
        _writer.WriteLine("__states = __previousHookStates;");
        foreach (ref readonly var state in states)
        {
            if (state.IsComposable)
            {
                GeneratedMemberNameWriter.WriteStateField(_writer, state.GeneratedName);
                _writer.Write(" = ");
                WritePrevious(state);
                _writer.WriteLine(";");
            }
        }

        WriteClearPrevious(states);
        _writer.WriteLine("#endif");
        WriteMethodEnd();
    }

    private void WriteReset(ReadOnlySpan<ComponentStatePlan> states)
    {
        WriteMethodStart("ResetHookStatesForHotReload");
        _writer.WriteLine("#if DEBUG");
        _writer.WriteLine("RuntimeStates.ResetHookBindingsForHotReload();");
        _writer.WriteLine("#else");
        _writer.WriteLine("__states = default;");
        foreach (ref readonly var state in states)
        {
            if (state.IsComposable)
            {
                GeneratedMemberNameWriter.WriteStateField(_writer, state.GeneratedName);
                _writer.WriteLine(" = null;");
            }
        }

        WriteClearPrevious(states);
        _writer.WriteLine("#endif");
        WriteMethodEnd();
    }

    private void WriteClearPrevious(ReadOnlySpan<ComponentStatePlan> states)
    {
        _writer.WriteLine("__previousHookStates = default;");
        foreach (ref readonly var state in states)
        {
            if (state.IsComposable)
            {
                WritePrevious(state);
                _writer.WriteLine(" = null;");
            }
        }
    }

    private void WriteNames(ReadOnlySpan<ComponentStatePlan> states)
    {
        _writer.WriteLine("protected override string? GetStateName(int index) => index switch");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        for (var index = 0; index < states.Length; index++)
        {
            _writer.WriteIntegerLiteral(index);
            _writer.Write(" => ");
            _writer.WriteStringLiteral(states[index].Name);
            _writer.WriteLine(",");
        }

        _writer.WriteLine("_ => null,");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("};");
    }

    private void WriteMethodStart(string name)
    {
        _writer.Write("protected override void ");
        _writer.Write(name);
        _writer.WriteLine("()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
    }

    private void WriteMethodEnd()
    {
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WritePrevious(in ComponentStatePlan state)
    {
        _writer.Write("__previousHookState_");
        _writer.Write(state.GeneratedName);
    }
}
