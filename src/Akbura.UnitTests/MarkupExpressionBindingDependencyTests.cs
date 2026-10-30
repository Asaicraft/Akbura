using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using static Akbura.UnitTests.MarkupExpressionBindingSemanticTests;

namespace Akbura.UnitTests;

public sealed class MarkupExpressionBindingDependencyTests
{
    [Fact]
    public void Property_UsesResolvedMemberAndNotificationContract()
    {
        var plan = Plan("Vm.Name");
        var name = Assert.Single(plan.Dependencies.Where(d => d.Kind == ExpressionBindingDependencyKind.Property));
        Assert.IsAssignableFrom<IPropertySymbol>(name.Member);
        Assert.Equal("Demo.Vm", name.ReceiverType!.ToDisplayString());
        Assert.Equal(SpecialType.System_String, name.ResultType!.SpecialType);
        Assert.Equal(ExpressionBindingObservation.PropertyChanged, name.Observation);
        Assert.Equal(ExpressionBindingObservation.AkburaValue, name.Receiver!.Observation);
        Assert.IsAssignableFrom<Akbura.Language.Symbols.IParamSymbol>(name.Receiver.SourceSymbol);
        Assert.Empty(name.Conditions);
        Assert.Equal(ExpressionBindingObservability.Observable, plan.Observability);
    }

    [Fact]
    public void NestedChain_PreservesReceiversAndDeduplicatesOnlySubscriptions()
    {
        var plan = Plan("Vm.Profile.Profile.Name + Vm.Profile.Profile.Name");
        var name = Assert.Single(plan.Dependencies.Where(d => d.Member?.Name == "Name"));
        Assert.Equal("Profile", name.Receiver!.Member!.Name);
        Assert.Equal("Profile", name.Receiver.Receiver!.Member!.Name);
        Assert.Equal("Vm", name.Receiver.Receiver.Receiver!.Member!.Name);
        var occurrences = Evaluations(plan.Evaluation!).Where(e => e.Operation is IPropertyReferenceOperation p && p.Property.Name == "Name").ToArray();
        Assert.Equal(2, occurrences.Length);
        Assert.NotSame(occurrences[0], occurrences[1]);
        Assert.Same(occurrences[0].Dependency, occurrences[1].Dependency);
    }

    [Fact]
    public void Interpolation_DiscoversEveryMember()
    {
        var plan = Plan("$\"{Vm.Name} {Vm.Count}\"");
        Assert.Equal(new[] { "Count", "Name" }, plan.Dependencies.Where(d => d.Kind == ExpressionBindingDependencyKind.Property).Select(d => d.Member!.Name).Order());
    }

    [Theory]
    [InlineData("Vm.Enabled ? Vm.Name : Vm.Profile.Name", "Name", (int)ExpressionBindingBranch.WhenTrue)]
    [InlineData("Vm.Enabled && Vm.Count > 0", "Count", (int)ExpressionBindingBranch.WhenTrue)]
    [InlineData("Vm.Enabled || Vm.Count > 0", "Count", (int)ExpressionBindingBranch.WhenFalse)]
    [InlineData("Vm.Name ?? Vm.Profile.Name", "Profile", (int)ExpressionBindingBranch.WhenNull)]
    [InlineData("Vm.Profile?.Name", "Name", (int)ExpressionBindingBranch.WhenNotNull)]
    public void Branches_RetainEvaluationConditions(string expression, string member, int branch)
    {
        var plan = Plan(expression);
        Assert.Contains(plan.Dependencies, d => d.Member?.Name == member && d.Conditions.Any(c => (int)c.Branch == branch));
        Assert.Contains(plan.Dependencies, d => d.Member?.Name == "Vm" && d.Conditions.IsEmpty);
    }

    [Fact]
    public void Switch_RetainsArmOrderGuardAndConditionalValue()
    {
        var plan = Plan("Vm.Count switch { 0 when Vm.Enabled => Vm.Name, _ => Vm.Profile.Name }");
        var names = plan.Dependencies.Where(d => d.Member?.Name == "Name").ToArray();
        Assert.Equal(2, names.Length);
        Assert.Contains(names[0].Conditions, c => c.Branch == ExpressionBindingBranch.SwitchArm && c.Arm == 0);
        Assert.Contains(names[0].Conditions, c => c.Branch == ExpressionBindingBranch.WhenGuardTrue);
        Assert.Contains(names[1].Conditions, c => c.Branch == ExpressionBindingBranch.SwitchArm && c.Arm == 1);
    }

    [Theory]
    [InlineData("Items", true)]
    [InlineData("PlainItems", false)]
    [InlineData("Array", false)]
    public void Indexers_SeparateCollectionIndexAndElementPath(string source, bool notifiesCollection)
    {
        var plan = Plan("Vm." + source + "[Vm.Count].Name");
        var name = Assert.Single(plan.Dependencies.Where(d => d.Member?.Name == "Name"));
        var element = name.Receiver!;
        Assert.True(element.Kind is ExpressionBindingDependencyKind.Indexer or ExpressionBindingDependencyKind.ArrayElement);
        Assert.Equal(source, element.Receiver!.Member!.Name);
        Assert.Single(element.Indices);
        Assert.Contains(Evaluations(element.Indices[0]), e => e.Dependency?.Member?.Name == "Count");
        Assert.Equal(notifiesCollection, element.Observation.HasFlag(ExpressionBindingObservation.CollectionChanged));
    }

    [Fact]
    public void StaticConstantsFieldsAndPlainMembers_AreNotInventedAsObservable()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Header + "param Plain plain; <TextBlock Text=${x.bind plain.Name + Vm.MutableField + Demo.Vm.ReadOnlyField + Demo.Vm.Constant} />", Models);
        var (_, _, plan) = GetBinding(fixture);
        foreach (var name in new[] { "Name", "MutableField", "ReadOnlyField", "Constant" })
            Assert.Equal(ExpressionBindingObservation.None, Assert.Single(plan.Dependencies.Where(d => d.Member?.Name == name)).Observation);
        Assert.Equal(ExpressionBindingValueClassification.Constant, plan.Dependencies.Single(d => d.Member?.Name == "Constant").Classification);
        Assert.True(plan.HasUnknownDependencies);
        Assert.Equal(ExpressionBindingObservability.Partial, plan.Observability);
    }

    [Fact]
    public void AvaloniaProperty_UsesActualRegistrationField()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Header + "param TextBlock control; <TextBlock Text=${x.bind control.Text} />", Models);
        var (_, _, plan) = GetBinding(fixture);
        var text = Assert.Single(plan.Dependencies.Where(d => d.Member?.Name == "Text"));
        Assert.Equal(ExpressionBindingObservation.AvaloniaProperty, text.Observation);
        Assert.Equal("TextProperty", text.NotificationMember!.Name);
        Assert.IsAssignableFrom<IFieldSymbol>(text.NotificationMember);
    }

    [Fact]
    public void MethodCalls_VisitInputsButWarnAboutHiddenImplementationReads()
    {
        var plan = Plan("Format(Vm.Price, Vm.Name) + Vm.Calculate()");
        Assert.Contains(plan.Dependencies, d => d.Member?.Name == "Price");
        Assert.Single(plan.Dependencies.Where(d => d.Member?.Name == "Name"));
        Assert.Equal(2, plan.Diagnostics.Count(d => d.Code == "AKBURA_SEMANTIC_ExpressionBindingHiddenDependencies"));
        Assert.All(plan.Diagnostics, d => Assert.Equal(AkburaDiagnosticSeverity.Warning, d.Severity));
        Assert.True(plan.HasUnknownDependencies);
    }

    [Theory]
    [InlineData("Vm.Count++")]
    [InlineData("Vm.Count = 1")]
    [InlineData("new object()")]
    [InlineData("(System.Func<int>)(() => Vm.Count)")]
    [InlineData("Vm is { Profile.Name: \"Name\" }")]
    public void UnsupportedForms_ReportErrorRatherThanClaimingCompleteReactivity(string expression)
    {
        var (_, operation, plan) = GetBinding(Create("Tag", expression));
        Assert.True(operation.HasErrors);
        Assert.Contains(plan.Diagnostics, d => d.Code == "AKBURA_SEMANTIC_ExpressionBindingUnsupportedExpression");
        Assert.True(plan.HasUnknownDependencies);
    }

    [Fact]
    public void Nameof_DoesNotSubscribeToUnevaluatedMembers()
    {
        var plan = Plan("nameof(Vm.Name)");
        Assert.DoesNotContain(plan.Dependencies, d => d.Member?.Name == "Name");
    }

    [Theory]
    [InlineData("!Vm.Enabled")]
    [InlineData("-Vm.Count")]
    [InlineData("Vm.Count is > 0 and < 10")]
    [InlineData("Vm.Count is not 0")]
    public void UnaryAndScalarPatternOperations_RetainInputDependencies(string expression)
    {
        var (_, operation, plan) = GetBinding(Create(expression == "-Vm.Count" ? "Width" : "IsVisible", expression));
        Assert.False(operation.HasErrors, string.Join("\n", plan.Diagnostics.Select(d => d.Message)));
        Assert.Contains(plan.Dependencies, d => d.Member?.Name is "Count" or "Enabled");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Vm.Profile?.Name")]
    [InlineData("Vm.Items[Vm.Count].Name")]
    public void EvaluationSnapshots_RemainValidAcrossLaterAnalyses(string expression)
    {
        var plan = Plan(expression);
        Assert.NotNull(plan.Evaluation);
        var kinds = Evaluations(plan.Evaluation).Select(static evaluation => evaluation.Operation.Kind).ToArray();
        var members = Evaluations(plan.Evaluation).Select(static evaluation => evaluation.Dependency?.Member?.Name).ToArray();

        _ = Plan("Vm.Enabled ? Vm.Name : Vm.Profile.Name");
        _ = Plan("nameof(Vm.Name)");

        Assert.Equal(kinds, Evaluations(plan.Evaluation).Select(static evaluation => evaluation.Operation.Kind));
        Assert.Equal(members, Evaluations(plan.Evaluation).Select(static evaluation => evaluation.Dependency?.Member?.Name));
    }

    private static ExpressionBindingPlan Plan(string expression) => GetBinding(Create("Tag", expression)).Plan;
    private static IEnumerable<ExpressionBindingEvaluation> Evaluations(ExpressionBindingEvaluation evaluation)
    {
        yield return evaluation;
        foreach (var child in evaluation.Children)
            foreach (var nested in Evaluations(child)) yield return nested;
    }
}
