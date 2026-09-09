using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentHotReloadWriter
{
    private const string ManifestFieldName =
        "s_akburaHotReloadProperties";
    private const string PrepareMethodName =
        "__AkburaHotReloadPrepare";
    private const string PreviousManifestName =
        "__previousProperties";

    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;
    private readonly ComponentGenerationMode _generationMode;

    public ComponentHotReloadWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName,
        ComponentGenerationMode generationMode =
            ComponentGenerationMode.ReleaseDirect)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _sourceMap = sourceMap!;
        _ownerTypeName = ownerTypeName;
        _generationMode = generationMode;
    }

    public void Write(
        in ComponentMemberPlan memberPlan,
        in ComponentHotReloadPlan hotReloadPlan)
    {
        _writer.WriteLine("#if DEBUG");
        _writer.WriteLine();
        WriteAppliedShapeFields();
        _writer.WriteLine();
        WriteManifestField();
        _writer.WriteLine();
        WriteShapeMethod(
            "__AkburaCurrentDescriptorShape",
            hotReloadPlan.DescriptorShape);
        _writer.WriteLine();
        WriteShapeMethod(
            "__AkburaCurrentStateShape",
            hotReloadPlan.StateShape);
        _writer.WriteLine();
        WriteManifestFactory(memberPlan);
        _writer.WriteLine();
        WriteDescriptorRebuilder(memberPlan);
        _writer.WriteLine();
        WriteStateInfoRebuilder(memberPlan);
        _writer.WriteLine();
        WritePrepare();
        _writer.WriteLine();
        WriteStateReset();
        _writer.WriteLine();
        WriteApply();
        _writer.WriteLine();
        _writer.WriteLine("#endif");
    }

    private void WriteAppliedShapeFields()
    {
        _writer.WriteLine(
            "private static string s_akburaAppliedDescriptorShape =");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__AkburaCurrentDescriptorShape();");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine();
        _writer.WriteLine(
            "private static string s_akburaAppliedStateShape =");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__AkburaCurrentStateShape();");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteManifestField()
    {
        _writer.WriteLine(
            "private static global::System.Collections.Immutable.ImmutableArray<");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "global::Akbura.HotReload.AkburaHotReloadPropertyRegistration>");
        _writer.Write(ManifestFieldName);
        _writer.WriteLine(" =");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__AkburaCreateHotReloadProperties();");
        _writer.CurrentIndent -= _writer.TabSize * 2;
    }

    private void WriteShapeMethod(
        string methodName,
        string fingerprint)
    {
        _writer.Write("private static string ");
        _writer.Write(methodName);
        _writer.WriteLine("() =>");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(fingerprint);
        _writer.WriteLine(";");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteManifestFactory(in ComponentMemberPlan plan)
    {
        _writer.WriteLine(
            "private static global::System.Collections.Immutable.ImmutableArray<");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "global::Akbura.HotReload.AkburaHotReloadPropertyRegistration>");
        _writer.WriteLine("__AkburaCreateHotReloadProperties()");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("return");
        _writer.WriteLine("[");
        _writer.CurrentIndent += _writer.TabSize;
        WriteParameterRegistrations(plan);
        WriteServiceRegistrations(plan);
        WriteCommandRegistrations(plan);
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("];");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteParameterRegistrations(in ComponentMemberPlan plan)
    {
        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            ref readonly var parameter = ref plan.Parameters.ItemRef(i);
            WriteRegistrationStart(parameter.HotReloadKey);
            WriteDescriptorName(parameter.Name);
            _writer.WriteLine(".AvaloniaProperty),");
            _writer.CurrentIndent -= _writer.TabSize;
        }
    }

    private void WriteServiceRegistrations(in ComponentMemberPlan plan)
    {
        for (var i = 0; i < plan.Services.Length; i++)
        {
            ref readonly var service = ref plan.Services.ItemRef(i);
            WriteRegistrationStart(service.HotReloadKey);
            WriteDescriptorName(service.Name);
            _writer.WriteLine(".AvaloniaProperty),");
            _writer.CurrentIndent -= _writer.TabSize;
        }
    }

    private void WriteCommandRegistrations(in ComponentMemberPlan plan)
    {
        for (var i = 0; i < plan.Commands.Length; i++)
        {
            ref readonly var command = ref plan.Commands.ItemRef(i);
            WriteRegistrationStart(command.HotReloadKey);
            WriteDescriptorName(command.Name);
            _writer.WriteLine("),");
            _writer.CurrentIndent -= _writer.TabSize;
        }
    }

    private void WriteRegistrationStart(string key)
    {
        _writer.WriteLine(
            "new global::Akbura.HotReload.AkburaHotReloadPropertyRegistration(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(key);
        _writer.WriteLine(",");
    }

    private void WriteDescriptorRebuilder(in ComponentMemberPlan plan)
    {
        _writer.WriteLine(
            "private static void __AkburaHotReloadRebuildDescriptors()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("var ");
        _writer.Write(PreviousManifestName);
        _writer.Write(" = ");
        _writer.Write(ManifestFieldName);
        _writer.WriteLine(";");
        _writer.WriteLine("var __currentKeys = new global::System.String[]");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        WriteCurrentKeys(plan);
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("};");
        _writer.WriteLine();
        _writer.WriteLine("using var __update =");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "global::Akbura.HotReload.AkburaHotReloadRuntime.BeginPropertyUpdate(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("typeof(");
        _writer.Write(_ownerTypeName);
        _writer.WriteLine("),");
        _writer.Write(PreviousManifestName);
        _writer.WriteLine(",");
        _writer.WriteLine("__currentKeys);");
        _writer.CurrentIndent -= _writer.TabSize * 2;

        if (HasDescriptors(plan))
        {
            _writer.WriteLine();
            WriteDescriptorAssignments(plan);
        }

        _writer.WriteLine();
        var arrayWriter = new DescriptorArrayWriter(_writer);
        arrayWriter.WriteHotReloadAssignments(plan);
        _writer.WriteLine();
        _writer.Write(ManifestFieldName);
        _writer.WriteLine(" =");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__AkburaCreateHotReloadProperties();");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteCurrentKeys(in ComponentMemberPlan plan)
    {
        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            _writer.WriteStringLiteral(plan.Parameters.ItemRef(i).HotReloadKey);
            _writer.WriteLine(",");
        }

        for (var i = 0; i < plan.Services.Length; i++)
        {
            _writer.WriteStringLiteral(plan.Services.ItemRef(i).HotReloadKey);
            _writer.WriteLine(",");
        }

        for (var i = 0; i < plan.Commands.Length; i++)
        {
            _writer.WriteStringLiteral(plan.Commands.ItemRef(i).HotReloadKey);
            _writer.WriteLine(",");
        }
    }

    private void WriteDescriptorAssignments(in ComponentMemberPlan plan)
    {
        var parameterWriter = new ParameterWriter(
            _writer,
            _sourceMap,
            _ownerTypeName);

        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            parameterWriter.WriteHotReloadAssignment(
                plan.Parameters.ItemRef(i),
                PreviousManifestName);
        }

        if (!plan.Parameters.IsDefaultOrEmpty &&
            (!plan.Services.IsDefaultOrEmpty || !plan.Commands.IsDefaultOrEmpty))
        {
            _writer.WriteLine();
        }

        var serviceWriter = new InjectServiceWriter(
            _writer,
            _ownerTypeName);

        for (var i = 0; i < plan.Services.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            serviceWriter.WriteHotReloadAssignment(
                plan.Services.ItemRef(i),
                PreviousManifestName);
        }

        if (!plan.Services.IsDefaultOrEmpty && !plan.Commands.IsDefaultOrEmpty)
        {
            _writer.WriteLine();
        }

        var commandWriter = new CommandWriter(
            _writer,
            _ownerTypeName);

        for (var i = 0; i < plan.Commands.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            commandWriter.WriteHotReloadAssignment(
                plan.Commands.ItemRef(i),
                PreviousManifestName);
        }
    }

    private void WriteStateInfoRebuilder(in ComponentMemberPlan plan)
    {
        _writer.WriteLine(
            "private static void __AkburaHotReloadRebuildStateInfos()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        var stateWriter = new StateWriter(
            _writer,
            _sourceMap,
            _ownerTypeName);

        for (var i = 0; i < plan.States.Length; i++)
        {
            stateWriter.WriteHotReloadAssignment(plan.States.ItemRef(i));
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteStateReset()
    {
        _writer.Write("private static void __AkburaHotReloadResetStates(");
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(" __component)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__component.__states = default;");
        WritePrepareInvocation();
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WritePrepare()
    {
        _writer.Write("private static void ");
        _writer.Write(PrepareMethodName);
        _writer.Write("(");
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(" __component)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        WritePrepareInvocation();
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WritePrepareInvocation()
    {
        if (_generationMode == ComponentGenerationMode.DebugStructural)
        {
            _writer.Write("__component.");
            _writer.Write(
                ComponentStructuralHotReloadWriter.RenderStateFieldName);
            _writer.WriteLine(".Invalidate();");
            return;
        }

        _writer.Write("__component.");
        _writer.Write(
            ComponentLifecycleWriter.HotReloadUpdateInitialValuesMethodName);
        _writer.WriteLine("();");
    }

    private void WriteApply()
    {
        _writer.WriteHiddenApiAttributes();
        _writer.WriteLine("internal static void __AkburaHotReloadApply()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "var __currentDescriptorShape = __AkburaCurrentDescriptorShape();");
        _writer.WriteLine(
            "var __descriptorShapeChanged = !global::System.String.Equals(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("s_akburaAppliedDescriptorShape,");
        _writer.WriteLine("__currentDescriptorShape,");
        _writer.WriteLine("global::System.StringComparison.Ordinal);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine();
        _writer.WriteLine("if (__descriptorShapeChanged)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__AkburaHotReloadRebuildDescriptors();");
        _writer.WriteLine(
            "s_akburaAppliedDescriptorShape = __currentDescriptorShape;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine(
            "var __currentStateShape = __AkburaCurrentStateShape();");
        _writer.WriteLine(
            "var __stateShapeChanged = !global::System.String.Equals(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("s_akburaAppliedStateShape,");
        _writer.WriteLine("__currentStateShape,");
        _writer.WriteLine("global::System.StringComparison.Ordinal);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine();
        _writer.WriteLine("if (__stateShapeChanged)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__AkburaHotReloadRebuildStateInfos();");
        _writer.WriteLine(
            "s_akburaAppliedStateShape = __currentStateShape;");
        _writer.Write(
            "global::Akbura.HotReload.AkburaHotReloadRuntime.Refresh<");
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__AkburaHotReloadResetStates);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("return;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.Write(
            "global::Akbura.HotReload.AkburaHotReloadRuntime.Refresh<");
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(PrepareMethodName);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteDescriptorName(string name)
    {
        _valueWriter.WriteIdentifier(name);
        _writer.Write("Property");
    }

    private static bool HasDescriptors(in ComponentMemberPlan plan)
    {
        return !plan.Parameters.IsDefaultOrEmpty ||
            !plan.Services.IsDefaultOrEmpty ||
            !plan.Commands.IsDefaultOrEmpty;
    }
}
