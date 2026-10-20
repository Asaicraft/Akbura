using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;

namespace Akbura.UnitTests;

public sealed class ReactiveUtilityConditionSemanticTests
{
    private const string Header = "using Avalonia.Controls; using Akbura.Styles.akcss; using Demo; " +
        "inject Vm Vm; param bool Active = true; state double width = 800; ";

    [Theory]
    [InlineData("true")]
    [InlineData("Vm.Enabled")]
    [InlineData("width > 1000")]
    [InlineData("Vm.Count > 5")]
    [InlineData("Active && Vm.Enabled")]
    [InlineData("Vm.Profile?.Enabled == true")]
    [InlineData("Vm.Items[Vm.Count].Enabled")]
    [InlineData("Vm.Enabled ? Vm.Profile.Enabled : Active")]
    public void BoolConditions_UseSharedDependencyPlanAndNotClrExtension(string expression)
    {
        var (fixture, attribute, operation, plan) = Create(expression);
        Assert.False(operation.HasErrors, string.Join("\n", plan.Diagnostics.Select(d => d.Message)));
        Assert.True(operation.HasCondition);
        Assert.True(operation.Variant.IsPrefixed);
        Assert.Null(operation.ConditionMarkupExtension);
        Assert.Equal(SpecialType.System_Boolean, plan.TargetType!.SpecialType);
        Assert.Equal(SpecialType.System_Boolean, plan.ResultType!.SpecialType);
        Assert.True(plan.Conversion.IsImplicit);
        Assert.Same(plan.Expression.Operation, operation.ConditionOperation.Operation);
        Assert.NotNull(plan.Evaluation);
        Assert.NotEqual(ExpressionBindingObservability.Unsupported, plan.Observability);
        Assert.NotNull(operation.ConditionOperationTree);
        Assert.Same(operation, operation.ConditionOperationTree.Parent);
        Assert.Same(operation, fixture.SemanticModel.GetOperation(attribute));
        Assert.DoesNotContain(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            d => d.Severity == AkburaDiagnosticSeverity.Error);
        if (expression == "width > 1000")
            Assert.Contains(plan.Dependencies, d => d.Observation == ExpressionBindingObservation.AkburaValue);
        if (expression == "Vm.Enabled")
            Assert.Contains(plan.Dependencies, d => (d.Observation & ExpressionBindingObservation.PropertyChanged) != 0);
    }

    [Theory]
    [InlineData("Vm.Count")]
    [InlineData("Vm.Name")]
    [InlineData("Vm.NullableEnabled")]
    [InlineData("null")]
    [InlineData("Unknown.Enabled")]
    [InlineData("Vm.UnknownMember")]
    [InlineData("Vm.")]
    [InlineData("")]
    public void InvalidConditions_AreDiagnosedInSourceWithoutTruthiness(string expression)
    {
        var (_, attribute, operation, plan) = Create(expression);
        Assert.True(operation.HasErrors);
        Assert.Equal(ExpressionBindingObservability.Unsupported, plan.Observability);
        var binding = Assert.IsType<TailwindExpressionBindingConditionalPrefixSyntax>(attribute.Prefix).Binding;
        Assert.Contains(plan.Diagnostics, d => d.Severity == AkburaDiagnosticSeverity.Error);
        Assert.All(plan.Diagnostics, d =>
        {
            Assert.InRange(d.Span.Start, binding.Expression.FullSpan.Start, binding.Expression.FullSpan.End);
            Assert.InRange(d.Span.End, binding.Expression.FullSpan.Start, binding.Expression.FullSpan.End);
            Assert.DoesNotContain("__AkburaProbe", d.Message);
        });
    }

    [Fact]
    public void NullableCondition_RequiresExplicitNullResolution()
    {
        var (_, _, operation, plan) = Create("Vm.NullableEnabled == true");
        Assert.False(operation.HasErrors);
        Assert.True(plan.Conversion.IsImplicit);
    }

    [Fact]
    public void MissingMemberDiagnostic_HighlightsMemberRatherThanGeneratedProbe()
    {
        var (fixture, _, _, plan) = Create("Vm.UnknownMember");
        Assert.Contains(plan.Diagnostics, d =>
            fixture.ComponentTree.Text.ToString(d.Span) == "UnknownMember");
    }

    [Fact]
    public void ExistingAnalyzer_RejectsDeferredExpressionBodies()
    {
        var (_, _, operation, plan) = Create("((System.Func<bool>)(() => Vm.Enabled))()");
        Assert.True(operation.HasErrors);
        Assert.Equal(ExpressionBindingObservability.Unsupported, plan.Observability);
        Assert.Contains(plan.Diagnostics, d => d.Code == "AKBURA_SEMANTIC_ExpressionBindingUnsupportedExpression");
    }

    [Fact]
    public void BoundUpdateAndRewriter_PreservePlanIdentity()
    {
        var (fixture, attribute, _, plan) = Create("Vm.Enabled");
        var bound = Assert.IsType<BoundTailwindUtilityAttribute>(fixture.SemanticModel.BindingSession.BindOperationSyntax(attribute));
        Assert.Same(plan, bound.ConditionExpressionBinding);
        Assert.Same(bound, new BoundTreeRewriter().Visit(bound));
        var changed = bound.Update(bound.ContainingComponent, "other-utility", bound.Utility, bound.Utilities,
            bound.Arguments, bound.HasCondition, bound.ConditionText, bound.ConditionType,
            bound.ConditionOperation, bound.ConditionMarkupExtension, bound.Variant, bound.BindingPriority);
        Assert.NotSame(bound, changed);
        Assert.Same(plan, changed.ConditionExpressionBinding);
    }

    [Fact]
    public void SemanticReferences_UseRealSymbolsAndHostSpans()
    {
        var (fixture, attribute, _, _) = Create("Vm.Enabled && Vm.Profile.Enabled");
        var references = fixture.SemanticModel.GetCSharpSymbolReferences(attribute);
        Assert.Equal(2, references.Count(r => r.Name == "Vm"));
        Assert.Equal(2, references.Count(r => r.Name == "Enabled"));
        Assert.Contains(references, r => r.Name == "Profile");
        Assert.All(references, r => Assert.Equal(r.Name, fixture.ComponentTree.Text.ToString(r.SourceSpan)));
    }

    [Theory]
    [InlineData("var current = Vm.Profile; <TextBlock ${x.bind current.Enabled}:hidden/>", "current")]
    [InlineData("var Vm = new Demo.Vm(); <TextBlock ${x.bind Vm.Enabled}:hidden/>", "Vm")]
    [InlineData("<StackPanel>$foreach (var item in Vm.Items) { <TextBlock ${x.bind item.Enabled}:hidden/> }</StackPanel>", "item")]
    public void Scope_UsesLocalsAndForeachRatherThanGlobalTextMatching(string body, string name)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Header + body, Models);
        var attribute = Assert.Single(fixture.ComponentTree.GetRoot().DescendantNodes().OfType<TailwindFullAttributeSyntax>());
        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(fixture.SemanticModel.GetOperation(attribute));
        var plan = Assert.IsType<ExpressionBindingPlan>(operation.ConditionExpressionBinding);
        Assert.False(operation.HasErrors, string.Join("\n", plan.Diagnostics.Select(d => d.Message)));
        Assert.Contains(plan.Dependencies, d => d.Member?.Name == name || d.SourceSymbol?.Name == name);
    }

    [Fact]
    public void OrdinaryConditionAndMarkupExtension_KeepTheirExistingOperationKinds()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Header +
            "<TextBlock {width > 1000}:hidden ${hover}:bg-blue-500/>", Models);
        var attributes = fixture.ComponentTree.GetRoot().DescendantNodes().OfType<TailwindFullAttributeSyntax>();
        foreach (var attribute in attributes)
        {
            var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(fixture.SemanticModel.GetOperation(attribute));
            Assert.Null(operation.ConditionExpressionBinding);
            Assert.True(operation.HasCondition);
        }
    }

    private static (AkcssActivatorPlannerTests.PlannerFixture Fixture, TailwindFullAttributeSyntax Attribute,
        ITailwindUtilityAttributeOperation Operation, ExpressionBindingPlan Plan) Create(string expression)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Header + "<TextBlock ${x.bind " + expression + "}:hidden/>", Models);
        var attribute = Assert.Single(fixture.ComponentTree.GetRoot().DescendantNodes().OfType<TailwindFullAttributeSyntax>());
        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(fixture.SemanticModel.GetOperation(attribute));
        return (fixture, attribute, operation, Assert.IsType<ExpressionBindingPlan>(operation.ConditionExpressionBinding));
    }

    private static readonly string Models = MarkupExpressionBindingSemanticTests.Models.Replace(
        "public bool Enabled { get; set; }", "public bool Enabled { get; set; } public bool? NullableEnabled { get; set; }",
        StringComparison.Ordinal);
}
