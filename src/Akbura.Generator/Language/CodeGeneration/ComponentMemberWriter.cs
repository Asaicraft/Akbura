using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentMemberWriter
{
    private readonly CodeWriter _writer;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;

    public ComponentMemberWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _sourceMap = sourceMap!;
        _ownerTypeName = ownerTypeName;
    }

    public bool WriteDeclarations(in ComponentMemberPlan plan)
    {
        var indent = _writer.CurrentIndent;

        try
        {
            var wroteAny = false;
            WriteParameters(plan, ref wroteAny);
            WriteServices(plan, ref wroteAny);
            WriteCommands(plan, ref wroteAny);
            WriteStates(plan, ref wroteAny);
            WriteUserMembers(plan, ref wroteAny);
            return wroteAny;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public void WriteDescriptors(in ComponentMemberPlan plan)
    {
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = new DescriptorArrayWriter(_writer);
            writer.Write(plan);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private void WriteParameters(
        in ComponentMemberPlan plan,
        ref bool wroteAny)
    {
        if (plan.Parameters.IsDefaultOrEmpty)
        {
            return;
        }

        WriteSectionSeparator(ref wroteAny);
        var writer = new ParameterWriter(
            _writer,
            _sourceMap,
            _ownerTypeName);

        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            ref readonly var parameter = ref plan.Parameters.ItemRef(i);
            writer.Write(parameter);
        }
    }

    private void WriteServices(
        in ComponentMemberPlan plan,
        ref bool wroteAny)
    {
        if (plan.Services.IsDefaultOrEmpty)
        {
            return;
        }

        WriteSectionSeparator(ref wroteAny);
        var writer = new InjectServiceWriter(_writer, _ownerTypeName);

        for (var i = 0; i < plan.Services.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            ref readonly var service = ref plan.Services.ItemRef(i);
            writer.Write(service);
        }
    }

    private void WriteCommands(
        in ComponentMemberPlan plan,
        ref bool wroteAny)
    {
        if (plan.Commands.IsDefaultOrEmpty)
        {
            return;
        }

        WriteSectionSeparator(ref wroteAny);
        var writer = new CommandWriter(_writer, _ownerTypeName);

        for (var i = 0; i < plan.Commands.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            ref readonly var command = ref plan.Commands.ItemRef(i);
            writer.Write(plan, command);
        }
    }

    private void WriteStates(
        in ComponentMemberPlan plan,
        ref bool wroteAny)
    {
        if (plan.States.IsDefaultOrEmpty)
        {
            return;
        }

        WriteSectionSeparator(ref wroteAny);
        var writer = new StateWriter(
            _writer,
            _sourceMap,
            _ownerTypeName);

        for (var i = 0; i < plan.States.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            ref readonly var state = ref plan.States.ItemRef(i);
            writer.Write(state);
        }
    }

    private void WriteUserMembers(
        in ComponentMemberPlan plan,
        ref bool wroteAny)
    {
        if (plan.UserMembers.IsDefaultOrEmpty)
        {
            return;
        }

        WriteSectionSeparator(ref wroteAny);
        var writer = new ComponentUserMemberWriter(_writer, _sourceMap);

        for (var i = 0; i < plan.UserMembers.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            ref readonly var member = ref plan.UserMembers.ItemRef(i);
            writer.Write(member);
        }
    }

    private void WriteSectionSeparator(ref bool wroteAny)
    {
        if (wroteAny)
        {
            _writer.WriteLine();
        }

        wroteAny = true;
    }
}
