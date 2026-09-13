using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.UnitTests;

public sealed class ComponentLocalFactoryIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalFactoryBodyEdits_PreserveTheFactoryContract(bool deferred)
    {
        var original = GetIdentity(Wrap("$if (Expanded) { <TextBlock Text=\"initial\" /> }", deferred), deferred);
        var valueEdited = GetIdentity(Wrap("$if (Expanded) { <TextBlock Text=\"changed\" /> }", deferred), deferred);
        var typeEdited = GetIdentity(Wrap("$if (Expanded) { <Button Content=\"changed type\" /> }", deferred), deferred);
        var emptied = GetIdentity(Wrap("$if (Expanded) {}", deferred), deferred);

        Assert.Equal(original, valueEdited);
        Assert.Equal(original, typeEdited);
        Assert.Equal(original, emptied);
    }

    [Fact]
    public void ConditionalFactoryItemTypeOrNameChanges_ReplaceTheFactoryContract()
    {
        var original = GetIdentity(WithTypedHeader("Person", "person"));
        var typeEdited = GetIdentity(WithTypedHeader("OtherPerson", "person"));
        var nameEdited = GetIdentity(WithTypedHeader("Person", "item"));

        Assert.NotEqual(original, typeEdited);
        Assert.NotEqual(original, nameEdited);
    }

    [Fact]
    public void ReferencedCaptureTypeChanges_ReplaceTheFactoryContract()
    {
        var body = Wrap("$if (Expanded) { <TextBlock Text={prefix.ToString()} /> }", deferred: false);
        var original = GetIdentity("var prefix = Text; " + body);
        var typeEdited = GetIdentity("var prefix = 7; " + body);

        Assert.NotEqual(original, typeEdited);
    }

    [Fact]
    public void UnusedLocalChanges_DoNotReplaceTheFactoryContract()
    {
        var body = Wrap("$if (Expanded) { <TextBlock Text=\"initial\" /> }", deferred: false);

        Assert.Equal(GetIdentity("var unused = Text; " + body), GetIdentity("var unused = 7; " + body));
    }

    [Fact]
    public void UnconditionalFactoryRootTypeChanges_ReplaceTheFactoryContract()
    {
        var original = GetIdentity(Wrap("<Border>$if (Expanded) { <TextBlock /> }</Border>", deferred: false));
        var typeEdited = GetIdentity(Wrap("<StackPanel>$if (Expanded) { <TextBlock /> }</StackPanel>", deferred: false));

        Assert.NotEqual(original, typeEdited);
    }

    private static string Wrap(string body, bool deferred) => deferred
        ? "<ItemsControl><ItemsControl.ItemTemplate><DataTemplate><DataTemplate.Content>" + body +
            "</DataTemplate.Content></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>"
        : "<ItemsControl><ItemsControl.ItemTemplate>" + body + "</ItemsControl.ItemTemplate></ItemsControl>";

    private static string WithTypedHeader(string type, string name) =>
        "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"" + type + "\" x.ItemName=\"" + name + "\">" +
        "$if (Expanded) { <TextBlock Text={" + name + ".Name} /> }" +
        "</ItemsControl.ItemTemplate></ItemsControl>";

    private static string GetIdentity(string source, bool deferred = false)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Avalonia.Markup.Xaml.Templates; using Demo; " + source,
            """
            namespace Demo;
            public partial class PlannerView
            {
                public string Text => "initial";
                public bool Expanded => true;
            }
            public sealed class Person { public string Name => "person"; }
            public sealed class OtherPerson { public string Name => "other"; }
            """);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        using var codeWriter = new CodeWriter("\r\n");
        using var writer = new ComponentWriter(codeWriter, component, fixture.SemanticModel, "PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>(), ComponentGenerationMode.DebugStructural);
        if (deferred)
        {
            var factory = Assert.Single(writer.Plan.DeferredContents);
            var content = Assert.Single(writer.Plan.PropertyContents,
                value => value.FirstUpdateValue.Kind == ComponentContentValueKind.DeferredContent &&
                    value.FirstUpdateValue.Index == factory.Id);
            return ComponentHotReloadIdentity.CreateLocalDeferredFactoryIdentity(writer.Plan, content, factory);
        }
        else
        {
            var factory = Assert.Single(writer.Plan.Templates);
            var content = Assert.Single(writer.Plan.PropertyContents,
                value => value.FirstUpdateValue.Kind == ComponentContentValueKind.Template &&
                    value.FirstUpdateValue.Index == factory.Id);
            return ComponentHotReloadIdentity.CreateLocalTemplateFactoryIdentity(writer.Plan, content, factory);
        }
    }
}
