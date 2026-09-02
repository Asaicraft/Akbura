using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.UnitTests;

public sealed class ComponentPropertyWritePhaseTests
{
    [Fact]
    public void LiteralProperty_IsFirstUpdateOnly()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border Width="42" />
            """;
        var write = Assert.Single(CreatePlan(component).PropertyWrites);

        AssertPhase(write, ComponentPropertyWritePhase.FirstUpdate);
    }

    [Fact]
    public void MarkupExtension_IsFirstUpdateOnly()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border Background=${DynamicResource AccentBrush} />
            """;
        var write = Assert.Single(CreatePlan(component).PropertyWrites);

        AssertPhase(write, ComponentPropertyWritePhase.FirstUpdate);
    }

    [Fact]
    public void DynamicProperty_IsUpdateOnly()
    {
        const string component =
            """
            using Avalonia.Controls;

            state double width = 42;

            <Border Width={width} />
            """;
        var write = Assert.Single(CreatePlan(component).PropertyWrites);

        AssertPhase(write, ComponentPropertyWritePhase.Update);
    }

    [Fact]
    public void DynamicComponentParameter_WritesInBothPhases()
    {
        const string component =
            """
            state string value = "";

            <Child Value={value} />
            """;
        const string childComponent =
            """
            param string Value = "";
            """;
        var write = Assert.Single(
            CreatePlanWithChildComponent(component, childComponent)
                .PropertyWrites);

        Assert.Equal(PropertyWriteKind.ComponentParameter, write.Destination.Kind);
        AssertPhase(write, ComponentPropertyWritePhase.Both);
    }

    [Fact]
    public void LiteralComponentParameter_IsFirstUpdateOnly()
    {
        const string component =
            """
            <Child Value="initial" />
            """;
        const string childComponent =
            """
            param string Value = "";
            """;
        var write = Assert.Single(
            CreatePlanWithChildComponent(component, childComponent)
                .PropertyWrites);

        Assert.Equal(PropertyWriteKind.ComponentParameter, write.Destination.Kind);
        AssertPhase(write, ComponentPropertyWritePhase.FirstUpdate);
    }

    [Fact]
    public void OutBinding_CreatesNoForwardWrite()
    {
        const string component =
            """
            using Avalonia.Controls;

            state string value = "";

            <TextBox out:Text={value} />
            """;
        var plan = CreatePlan(component);

        Assert.Empty(plan.PropertyWrites);
        Assert.Single(plan.PropertySubscriptions);
    }

    private static ComponentPlan CreatePlan(string component)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component);
        return CreatePlan(fixture);
    }

    private static ComponentPlan CreatePlanWithChildComponent(
        string component,
        string childComponent)
    {
        var baseFixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var childTree = AkburaSyntaxTree.ParseText(
            childComponent,
            "Child.akbura");
        var compilation = new AkburaCompilation(
            baseFixture.CSharpCompilation,
            [baseFixture.ComponentTree, childTree],
            rootNamespace: "Demo");
        var fixture = new AkcssActivatorPlannerTests.PlannerFixture(
            baseFixture.CSharpCompilation,
            baseFixture.ComponentTree,
            externalAkcssTree: null,
            compilation.GetSemanticModel(baseFixture.ComponentTree));

        return CreatePlan(fixture);
    }

    private static ComponentPlan CreatePlan(
        AkcssActivatorPlannerTests.PlannerFixture fixture)
    {
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);

        return ComponentPlanner.Create(
            component,
            fixture.SemanticModel,
            new Dictionary<AkburaSyntax, string>());
    }

    private static void AssertPhase(
        in ComponentPropertyWritePlan write,
        ComponentPropertyWritePhase phase)
    {
        Assert.Equal(phase, write.Phase);
        Assert.Equal(
            (phase & ComponentPropertyWritePhase.FirstUpdate) != 0,
            write.WritesDuringFirstUpdate);
        Assert.Equal(
            (phase & ComponentPropertyWritePhase.Update) != 0,
            write.WritesDuringUpdate);
    }
}
