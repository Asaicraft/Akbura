using Akbura.Language.CodeGeneration;

namespace Akbura.UnitTests;

public sealed class GeneratedMemberNameWriterTests
{
    [Fact]
    public void WriteMethods_UseStablePrefixesAndNames()
    {
        using var writer = new CodeWriter("\r\n");

        GeneratedMemberNameWriter.WriteParameterField(writer, 0);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteCollectionField(writer, 1);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteCollectionAddMethod(writer, "Content");
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteCollectionSubscribedField(writer, 3);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteCollectionLogicalChildrenField(writer, 4);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteCollectionSynchronizeMethod(writer, 5);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteCollectionChangedMethod(writer, 6);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteStateInfoField(writer, 7);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteStateField(writer, 8);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteStateAccessor(writer, 9);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteStateValueFactory(writer, 10);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteStateFactory(writer, 11);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteServiceField(writer, 12);
        writer.WriteLine();
        GeneratedMemberNameWriter.WriteServiceSetter(writer, 13);

        Assert.Equal(
            "s_parameter0\r\n" +
            "__collection1\r\n" +
            "__AkburaAddCollection_Content\r\n" +
            "__contentSubscribed3\r\n" +
            "__contentLogicalChildren4\r\n" +
            "__SynchronizeContentLogicalChildren5\r\n" +
            "__OnContentCollectionChanged6\r\n" +
            "s_stateInfo7\r\n" +
            "__state8\r\n" +
            "__State9\r\n" +
            "__CreateStateValue10\r\n" +
            "__CreateState11\r\n" +
            "__service12\r\n" +
            "__SetService13",
            writer.GetText().ToString());
    }
}
