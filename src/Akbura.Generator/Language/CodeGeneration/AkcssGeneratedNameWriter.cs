using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal static class AkcssGeneratedNameWriter
{
    public static void WriteMetadataTypeName(CodeWriter writer, int symbolIndex)
    {
        Debug.Assert(symbolIndex >= 0);

        writer.Write("__AkcssMetadata_");
        writer.WriteIntegerLiteral(symbolIndex);
    }

    public static void WriteStyleTypeName(CodeWriter writer, int symbolIndex)
    {
        Debug.Assert(symbolIndex >= 0);

        writer.Write("Style_");
        writer.WriteIntegerLiteral(symbolIndex);
    }
}
