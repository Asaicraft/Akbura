using Akbura.Language.CodeGeneration;

namespace Akbura.UnitTests;

public sealed class ComponentMarkupContextWriterTests
{
    [Fact]
    public void WriteFields_RequiredBaseUriUsesOwnerAssemblyAndResourcePath()
    {
        var plan = new ComponentLifecyclePlan(
            rootElementId: 0,
            ComponentLifecycleFlags.RequiresBaseUri);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var writer = new ComponentMarkupContextWriter(
            codeWriter,
            "global::Demo.PlannerView",
            "Views/PlannerView.akbura");
        codeWriter.WriteLine();
        var outputStart = codeWriter.Length;

        Assert.True(writer.WriteFields(plan));
        Assert.Equal(4, codeWriter.CurrentIndent);

        Assert.Equal(
            "    private static readonly global::System.Uri __akburaBaseUri =\r\n" +
            "        new global::System.Uri(\"avares://\" + " +
            "typeof(global::Demo.PlannerView).Assembly.GetName().Name + " +
            "\"/Views/PlannerView.akbura\");\r\n",
            codeWriter.GetText().ToString().Substring(outputStart));
    }

    [Fact]
    public void WriteFields_UnusedBaseUriWritesNothing()
    {
        var plan = new ComponentLifecyclePlan(
            rootElementId: 0,
            ComponentLifecycleFlags.None);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 8,
        };
        var writer = new ComponentMarkupContextWriter(
            codeWriter,
            "global::Demo.PlannerView",
            "PlannerView.akbura");

        Assert.False(writer.WriteFields(plan));
        Assert.Equal(8, codeWriter.CurrentIndent);
        Assert.Equal(string.Empty, codeWriter.GetText().ToString());
    }
}
