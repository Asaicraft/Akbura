using Akbura.Language.CodeGeneration;

namespace Akbura.UnitTests;

public sealed class GeneratedMemberNameWriterTests
{
    [Fact]
    public void StableIdentityMethods_UseIdentitySuffixInsteadOfOrdinal()
    {
        const string identity = "Content_0123456789abcdef";
        using var writer = new CodeWriter("\r\n");

        GeneratedMemberNameWriter.WriteCollectionField(writer, identity);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteCollectionBackingGetter(writer, identity);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteParameterFactory(writer, identity);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteStateInfoField(writer, identity);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteStateFactory(writer, identity);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteServiceField(writer, identity);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteServiceValueSetter(writer, identity);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteCommandFactory(writer, identity);

        Assert.Equal(
            "__collection_Content_0123456789abcdef\r\n" +
            "__GetCollectionBacking_Content_0123456789abcdef\r\n" +
            "__AkburaCreateParameter_Content_0123456789abcdef\r\n" +
            "s_stateInfo_Content_0123456789abcdef\r\n" +
            "__CreateState_Content_0123456789abcdef\r\n" +
            "__service_Content_0123456789abcdef\r\n" +
            "__SetServiceValue_Content_0123456789abcdef\r\n" +
            "__AkburaCreateCommand_Content_0123456789abcdef",
            writer.GetText().ToString());
    }
}
