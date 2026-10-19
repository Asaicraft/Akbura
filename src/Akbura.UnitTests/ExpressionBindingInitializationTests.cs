using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.UnitTests;

public sealed class ExpressionBindingInitializationTests
{
    [Theory]
    [InlineData("<TextBlock Text=${x.bind Vm.Name} />", true)]
    [InlineData("<TextBlock Width=${x.bind Hello + a} />", true)]
    [InlineData("<TextBlock Text=${x.bind Service.Name} />", true)]
    [InlineData("<TextBlock Text=${x.bind Vm.Profile?.Name ?? \"Empty\"} />", true)]
    [InlineData("<TextBlock Text=${x.bind Vm.Items[Vm.Count].Name} />", true)]
    [InlineData("<TextBlock Text=${x.bind \"Constant\"} />", true)]
    [InlineData("<TextBlock Text=${x.bind Stable.Name} />", true)]
    [InlineData("<TextBlock x.Name=\"text\" Width=${x.bind text.Height} />", true)]
    [InlineData("<StackPanel><TextBlock x.Name=\"text\" /><TextBlock Width=${x.bind text.Height} /></StackPanel>", true)]
    [InlineData("var current = Vm.Profile; <TextBlock Text=${x.bind current.Name} />", false)]
    [InlineData("var Vm = Stable; <TextBlock Text=${x.bind Vm.Name} />", false)]
    [InlineData("<TextBlock Text=${x.bind Mutable.Name} />", false)]
    [InlineData("<TextBlock Text=${x.bind Member.Name} />", false)]
    [InlineData("<TextBlock Text=${x.bind Vm.MutableField.ToString()} />", false)]
    [InlineData("<TextBlock Text=${x.bind Vm.Calculate()} />", false)]
    [InlineData("<TextBlock Text=${x.bind Vm.Array[0].Name} />", false)]
    [InlineData("<StackPanel>$foreach (var item in Vm.Items) { <TextBlock Text=${x.bind item.Name} /> }</StackPanel>", false)]
    [InlineData("<StackPanel>$foreach (var item in Vm.Items) { <TextBlock Width=${x.bind index} /> }</StackPanel>", false)]
    [InlineData("<StackPanel>$if (Vm.Enabled) { <TextBlock x.Name=\"text\" /><TextBlock Width=${x.bind text.Width} /> }</StackPanel>", false)]
    public void Classification_ProvesStableRootsInsteadOfAssumingNoLocalsIsEnough(string markup, bool initialOnly)
    {
        const string declarations = "using Avalonia.Controls; using Demo; state Vm Vm = new(); param int Hello = 200; state int a = 11; inject Vm Service; ";
        var fixture = AkcssActivatorPlannerTests.CreateFixture(declarations + markup,
            MarkupExpressionBindingSemanticTests.Models + """

            public partial class PlannerView
            {
                public readonly Vm Stable = new();
                public Vm Mutable = new();
            }
            """);
        var errors = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot())
            .Where(diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error);
        Assert.Empty(errors);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var plan = ComponentPlanner.Create(component, fixture.SemanticModel, new Dictionary<AkburaSyntax, string>(), ComponentGenerationMode.DebugStructural);
        try
        {
            var property = Assert.Single(plan.PropertyWrites.Where(property => property.ValueKind == ComponentPropertyValueKind.ExpressionBinding));
            var evaluation = plan.CSharpValues[property.PayloadIndex].ExpressionBinding!.Evaluation!;
            var dump = DumpEvaluation(evaluation);
            Assert.True(property.Phase == (initialOnly ? ComponentPropertyWritePhase.FirstUpdate : ComponentPropertyWritePhase.Update), dump);
            Assert.Equal(initialOnly ? ExpressionBindingInitializationKind.InitialOnly : ExpressionBindingInitializationKind.RequiresRenderRefresh,
                plan.CSharpValues[property.PayloadIndex].ExpressionInitialization);
        }
        finally { plan.ReturnToPool(); }
    }

    private static string DumpEvaluation(Akbura.Language.Binder.ExpressionBindingEvaluation evaluation) =>
        $"{evaluation.Operation.Kind}: {evaluation.Operation.Syntax}; member={evaluation.Dependency?.Member}; source={evaluation.Dependency?.SourceSymbol?.Kind}; observation={evaluation.Dependency?.Observation}; receiver={evaluation.Dependency?.ReceiverType}\n" +
        string.Join("\n", evaluation.Children.Select(DumpEvaluation));
}
