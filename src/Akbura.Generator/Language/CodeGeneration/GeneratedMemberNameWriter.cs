using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal static class GeneratedMemberNameWriter
{
    public static void WriteParameterField(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("s_parameter");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteCollectionField(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__collection");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteCollectionAddMethod(CodeWriter writer, string parameterName)
    {
        Debug.Assert(!string.IsNullOrEmpty(parameterName));

        writer.Write("__AkburaAddCollection_");
        writer.Write(parameterName);
    }

    public static void WriteCollectionSubscribedField(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__contentSubscribed");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteCollectionLogicalChildrenField(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__contentLogicalChildren");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteCollectionSynchronizeMethod(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__SynchronizeContentLogicalChildren");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteCollectionChangedMethod(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__OnContentCollectionChanged");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteStateInfoField(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("s_stateInfo");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteStateField(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__state");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteStateAccessor(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__State");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteStateValueFactory(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__CreateStateValue");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteStateFactory(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__CreateState");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteServiceField(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__service");
        writer.WriteIntegerLiteral(id);
    }

    public static void WriteServiceSetter(CodeWriter writer, int id)
    {
        Debug.Assert(id >= 0);

        writer.Write("__SetService");
        writer.WriteIntegerLiteral(id);
    }
}
