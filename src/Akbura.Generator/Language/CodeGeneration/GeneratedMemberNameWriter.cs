using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal static class GeneratedMemberNameWriter
{
    public static void WriteCollectionField(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__collection_", generatedName);
    }

    public static void WriteCollectionGetter(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__GetCollection_", generatedName);
    }

    public static void WriteCollectionBackingGetter(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__GetCollectionBacking_", generatedName);
    }

    public static void WriteParameterFactory(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__AkburaCreateParameter_", generatedName);
    }

    public static void WriteCollectionAddMethod(CodeWriter writer, string parameterName)
    {
        Debug.Assert(!string.IsNullOrEmpty(parameterName));

        writer.Write("__AkburaAddCollection_");
        writer.Write(parameterName);
    }

    public static void WriteCollectionSubscribedField(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__contentSubscribed_", generatedName);
    }

    public static void WriteCollectionLogicalChildrenField(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__contentLogicalChildren_", generatedName);
    }

    public static void WriteCollectionLogicalChildrenGetter(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__GetContentLogicalChildren_", generatedName);
    }

    public static void WriteCollectionSynchronizeMethod(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__SynchronizeContentLogicalChildren_", generatedName);
    }

    public static void WriteCollectionChangedMethod(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__OnContentCollectionChanged_", generatedName);
    }

    public static void WriteStateInfoField(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "s_stateInfo_", generatedName);
    }

    public static void WriteStateInfoFactory(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__AkburaCreateStateInfo_", generatedName);
    }

    public static void WriteStateInfoValueFactory(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__AkburaCreateStateValueForOwner_", generatedName);
    }

    public static void WriteStateInfoStateFactory(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__AkburaCreateStateForOwner_", generatedName);
    }

    public static void WriteStateField(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__state_", generatedName);
    }

    public static void WriteStateAccessor(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__State_", generatedName);
    }

    public static void WriteStateValueFactory(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__CreateStateValue_", generatedName);
    }

    public static void WriteStateFactory(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__CreateState_", generatedName);
    }

    public static void WriteServiceField(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__service_", generatedName);
    }

    public static void WriteServiceGetter(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__GetService_", generatedName);
    }

    public static void WriteServiceSetter(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__SetService_", generatedName);
    }

    public static void WriteServiceValueSetter(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__SetServiceValue_", generatedName);
    }

    public static void WriteServiceFactory(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__AkburaCreateService_", generatedName);
    }

    public static void WriteCommandFactory(CodeWriter writer, string generatedName)
    {
        WriteStableName(writer, "__AkburaCreateCommand_", generatedName);
    }

    private static void WriteStableName(
        CodeWriter writer,
        string prefix,
        string generatedName)
    {
        Debug.Assert(writer != null);
        Debug.Assert(!string.IsNullOrEmpty(generatedName));

        writer!.Write(prefix);
        writer.Write(generatedName!);
    }
}
