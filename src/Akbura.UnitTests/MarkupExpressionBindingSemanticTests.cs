using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class MarkupExpressionBindingSemanticTests
{
    [Theory]
    [InlineData("Text", "Vm.Name", SpecialType.System_String, SpecialType.System_String)]
    [InlineData("Width", "Vm.Count + 1", SpecialType.System_Int32, SpecialType.System_Double)]
    [InlineData("Text", "$\"{Vm.Name} {Vm.Profile.Name}\"", SpecialType.System_String, SpecialType.System_String)]
    [InlineData("Text", "Vm.Enabled ? Vm.Name : Vm.Profile.Name", SpecialType.System_String, SpecialType.System_String)]
    [InlineData("Text", "Vm.Profile.Profile.Name", SpecialType.System_String, SpecialType.System_String)]
    [InlineData("Text", "Vm.Profile?.Name ?? Vm.Name", SpecialType.System_String, SpecialType.System_String)]
    [InlineData("Text", "Vm.Items[Vm.Count].Name", SpecialType.System_String, SpecialType.System_String)]
    [InlineData("Text", "Vm.Count.ToString()", SpecialType.System_String, SpecialType.System_String)]
    [InlineData("Text", "Vm.Price.ToString(\"F2\")", SpecialType.System_String, SpecialType.System_String)]
    [InlineData("Text", "Format(Vm.Price, Vm.Name)", SpecialType.System_String, SpecialType.System_String)]
    [InlineData("Width", "(double)Vm.Count", SpecialType.System_Double, SpecialType.System_Double)]
    [InlineData("IsVisible", "Vm.Count > 0", SpecialType.System_Boolean, SpecialType.System_Boolean)]
    [InlineData("Tag", "42", SpecialType.System_Int32, SpecialType.System_Object)]
    public void ValidExpressions_ExposeOriginalTypeConversionAndOperation(string target, string expression,
        SpecialType sourceType, SpecialType destinationType)
    {
        var fixture = Create(target, expression);
        var (attribute, operation, plan) = GetBinding(fixture);
        Assert.False(operation.HasErrors, string.Join("\n", plan.Diagnostics.Select(d => d.Message)));
        Assert.Equal(MarkupAttributeValueKind.ExpressionBinding, operation.ValueKind);
        Assert.Equal(sourceType, plan.ResultType!.SpecialType);
        Assert.Equal(destinationType, plan.TargetType!.SpecialType);
        Assert.Equal(sourceType, plan.Conversion.SourceType!.SpecialType);
        Assert.True(plan.Conversion.IsImplicit);
        Assert.Same(operation.ValueOperation.Operation, plan.Expression.Operation);
        Assert.NotNull(operation.ValueOperationTree);
        Assert.NotEmpty(operation.Children);
        Assert.Same(operation, operation.Children[0].Parent);
        Assert.Same(operation, fixture.SemanticModel.GetOperation(attribute));
        Assert.Same(plan, ((IMarkupPropertySetterOperation)fixture.SemanticModel.GetOperation(attribute)!).ExpressionBindingPlan);
        var walker = new CountingWalker();
        walker.Visit(operation);
        Assert.True(walker.Count > 1);
        Assert.DoesNotContain(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("Text", "Unknown.Name")]
    [InlineData("Text", "Vm.DoesNotExist")]
    [InlineData("Text", "Vm.")]
    [InlineData("IsVisible", "Vm.Name")]
    [InlineData("Text", "")]
    [InlineData("IsVisible", "null")]
    public void InvalidExpressions_ReportErrorsOnExpressionRatherThanProbe(string target, string expression)
    {
        var fixture = Create(target, expression);
        var (_, operation, plan) = GetBinding(fixture);
        Assert.True(operation.HasErrors);
        Assert.NotEmpty(plan.Diagnostics);
        var syntax = (MarkupExpressionBindingSyntax)operation.ValueSyntax!;
        Assert.All(plan.Diagnostics, diagnostic =>
        {
            Assert.InRange(diagnostic.Span.Start, syntax.Expression.FullSpan.Start, syntax.Expression.FullSpan.End);
            Assert.InRange(diagnostic.Span.End, syntax.Expression.FullSpan.Start, syntax.Expression.FullSpan.End);
            Assert.DoesNotContain("__AkburaProbe", diagnostic.Message);
        });
        Assert.Contains(plan.Diagnostics, d => d.Code == (target == "IsVisible" && expression != "null"
            ? "AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert" : "AKBURA_SEMANTIC_MarkupExpressionError"));
    }

    [Fact]
    public void MissingMemberDiagnostic_HighlightsActualMember()
    {
        var fixture = Create("Text", "Vm.DoesNotExist");
        var (_, _, plan) = GetBinding(fixture);
        var diagnostic = Assert.Single(plan.Diagnostics.Where(d => d.Code == "AKBURA_SEMANTIC_MarkupExpressionError"));
        Assert.Equal("DoesNotExist", fixture.ComponentTree.GetRoot().ToFullString().Substring(diagnostic.Span.Start, diagnostic.Span.Length));
    }

    [Fact]
    public void NullLiteral_DoesNotInventAnOriginalStringTypeFromItsDestination()
    {
        var (_, operation, plan) = GetBinding(Create("Text", "null"));
        Assert.False(operation.HasErrors);
        Assert.Null(plan.ResultType);
        Assert.Equal(SpecialType.System_String, plan.TargetType!.SpecialType);
        Assert.True(plan.Conversion.IsImplicit);
    }

    [Fact]
    public void DuplicateAssignment_UsesExistingConflictDiagnostic()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Header +
            "<TextBlock Text=\"first\" Text=${x.bind Vm.Name} />", Models);
        var (_, operation, plan) = GetBinding(fixture);
        Assert.True(operation.HasErrors);
        Assert.Contains(plan.Diagnostics, d => d.Code == "AKBURA_SEMANTIC_MarkupDuplicatePropertySetter");
    }

    [Theory]
    [InlineData("<Button Click=${x.bind Vm.Name} />")]
    [InlineData("<TextBlock bind:Text=${x.bind Vm.Name} />")]
    [InlineData("<TextBlock out:Text=${x.bind Vm.Name} />")]
    [InlineData("<TextBlock x.Name=${x.bind Vm.Name} />")]
    [InlineData("<TextBlock x.DataType=${x.bind Vm.Name} />")]
    [InlineData("<TextBlock x.ItemName=${x.bind Vm.Name} />")]
    [InlineData("<Button Command=${x.bind Vm.Name} />")]
    [InlineData("<Target ReadOnly=${x.bind Vm.Name} />")]
    [InlineData("<Target Values=${x.bind Vm.Items} />")]
    [InlineData("<Target InitialOnly=${x.bind Vm.Name} />")]
    [InlineData("<Target WriteOnly=${x.bind Vm.Name} />")]
    [InlineData("<Target PrivateGetter=${x.bind Vm.Name} />")]
    [InlineData("<Target SpanValue=${x.bind default} />")]
    public void UnsupportedTargets_HaveExplicitDiagnosticAndNoIncompletePlan(string markup)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Header + markup, Models);
        var attribute = fixture.ComponentTree.GetRoot().DescendantNodes().OfType<MarkupAttributeSyntax>()
            .Single(a => AkburaSemanticModel.GetMarkupAttributeValue(a) is MarkupExpressionBindingSyntax);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(fixture.SemanticModel.GetOperation(attribute));
        Assert.True(operation.HasErrors);
        Assert.Equal(MarkupAttributeValueKind.ExpressionBinding, operation.ValueKind);
        Assert.Null(operation.ExpressionBindingPlan);
        Assert.Contains(fixture.SemanticModel.GetSemanticDiagnostics(attribute),
            d => d.Code == "AKBURA_SEMANTIC_ExpressionBindingUnsupportedTarget");
    }

    [Fact]
    public void ObservableStructReceiver_ReportsCopyingRiskInsteadOfGeneratingBoxedObservers()
    {
        var fixture = Create("Text", "Vm.ObservableValue.Name");
        var (_, operation, plan) = GetBinding(fixture);
        Assert.True(operation.HasErrors);
        Assert.Equal(ExpressionBindingObservability.Unsupported, plan.Observability);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Code == "AKBURA_SEMANTIC_ExpressionBindingUnsupportedExpression" &&
            diagnostic.Message.Contains("copying or boxing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("state int count = 1; param string name = \"Hi\";", "name + count.ToString()")]
    [InlineData("inject Vm service;", "service.Name")]
    [InlineData("", "Member.Name")]
    public void ExistingComponentScopes_AreUsed(string declarations, string expression)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Header + declarations + "<TextBlock Text=${x.bind " + expression + "} />", Models);
        var (_, operation, plan) = GetBinding(fixture);
        Assert.False(operation.HasErrors, string.Join("\n", plan.Diagnostics.Select(d => d.Message)));
        Assert.Equal(SpecialType.System_String, plan.ResultType!.SpecialType);
    }

    [Fact]
    public void ForeachAndTypedTemplateScopes_ResolveTheirActualItemSymbols()
    {
        foreach (var markup in new[] {
            "<StackPanel>$foreach (var person in Vm.Items) { <TextBlock Text=${x.bind person.Name} /> }</StackPanel>",
            "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Vm\" x.ItemName=\"person\"><TextBlock Text=${x.bind person.Name} /></ItemsControl.ItemTemplate></ItemsControl>" })
        {
            var fixture = AkcssActivatorPlannerTests.CreateFixture(Header + markup, Models);
            var (_, operation, plan) = GetBinding(fixture);
            Assert.False(operation.HasErrors, string.Join("\n", plan.Diagnostics.Select(d => d.Message)));
            var name = Assert.Single(plan.Dependencies.Where(d => d.Member?.Name == "Name"));
            Assert.Equal("Vm", name.Member!.ContainingType.Name);
            Assert.Equal("person", name.Receiver!.Member!.Name);
        }
    }

    [Fact]
    public void ContextualAssignBinding_UsesDestinationValueTypeNotBindingBaseOrObject()
    {
        foreach (var expression in new[] { "Vm.Enabled", "Vm.Name" })
        {
            var fixture = AkcssActivatorPlannerTests.CreateFixture(Header +
                "<Assignment Target=\"TextBlock.IsVisible\" Value=${x.bind " + expression + "} />", Models);
            var (_, operation, plan) = GetBinding(fixture);
            Assert.True(fixture.SemanticModel.GetMarkupPropertyAssignmentContract(operation.Property, fixture.GetRootElement()).AssignBinding);
            Assert.Equal(SpecialType.System_Boolean, plan.TargetType!.SpecialType);
            Assert.Equal(expression == "Vm.Name", operation.HasErrors);
        }
    }

    [Fact]
    public void ForeachLocalAndPatternCapture_UseExistingCSharpScopes()
    {
        foreach (var markup in new[] {
            "<StackPanel>$foreach (var person in Vm.Items) { var local = person; <TextBlock Text=${x.bind local.Name} /> }</StackPanel>",
            "<StackPanel>$if (Vm.Profile is Vm person) { <TextBlock Text=${x.bind person.Name} /> }</StackPanel>" })
        {
            var fixture = AkcssActivatorPlannerTests.CreateFixture(Header + markup, Models);
            var (_, operation, plan) = GetBinding(fixture);
            Assert.False(operation.HasErrors, string.Join("\n", plan.Diagnostics.Select(d => d.Message)));
            Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(Assert.Single(plan.Dependencies.Where(d => d.Member?.Name == "Name")).Member);
        }
    }

    [Fact]
    public void BoundTreeRewriterAndUpdate_PreservePlanAndIdentity()
    {
        var fixture = Create("Text", "Vm.Name");
        var (attribute, _, plan) = GetBinding(fixture);
        var bound = Assert.IsType<BoundMarkupPropertySetter>(fixture.SemanticModel.BindingSession.BindOperationSyntax(attribute));
        Assert.Same(bound, new BoundTreeRewriter().Visit(bound));
        Assert.Same(plan, bound.ExpressionBindingPlan);
        Assert.Same(bound, bound.Update(bound.ContainingComponent, bound.Property, bound.AppliedAkcssSymbols,
            bound.ValueType, bound.ValueOperation, bound.ValueConversion, bound.BindingKind, bound.ValueKind,
            bound.ValueSyntax, bound.LiteralValue, bound.ConvertedValue, bound.AssignsCollectionSource));
    }

    [Fact]
    public void DestinationTypeChange_InvalidatesConversionEvenWhenSyntaxIsUnchanged()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Header + "<Target Scalar=${x.bind Vm.Name} />", Models);
        var (_, original, originalPlan) = GetBinding(fixture);
        Assert.False(original.HasErrors);
        var compilation = fixture.SemanticModel.Compilation;
        var oldTree = compilation.CSharpCompilation.SyntaxTrees.Last();
        var newTree = CSharpSyntaxTree.ParseText(oldTree.ToString().Replace(
            "public string Scalar { get; set; } = \"\";", "public bool Scalar { get; set; }", StringComparison.Ordinal), (CSharpParseOptions)oldTree.Options);
        var updated = compilation.WithCSharpCompilation(compilation.CSharpCompilation.ReplaceSyntaxTree(oldTree, newTree));
        var (_, operation, plan) = GetBinding(updated.GetSemanticModel(fixture.ComponentTree), fixture.ComponentTree);
        Assert.NotSame(originalPlan, plan);
        Assert.Equal(SpecialType.System_Boolean, plan.TargetType!.SpecialType);
        Assert.False(plan.Conversion.IsImplicit);
        Assert.True(operation.HasErrors);
        Assert.Equal(SpecialType.System_String, originalPlan.TargetType!.SpecialType);
    }

    [Fact]
    public void IncrementalExpressionAndReferencedTypeChanges_ReplacePlansWithoutStaleDependencies()
    {
        var fixture = Create("Text", "Vm.Name");
        var compilation = fixture.SemanticModel.Compilation;
        var tree = Assert.IsType<ComponentSyntaxTree>(fixture.ComponentTree);
        var (_, _, first) = GetBinding(fixture);
        var newText = tree.Text.WithChanges(new TextChange(new TextSpan(tree.Text.ToString().IndexOf("Vm.Name", StringComparison.Ordinal), 7), "Vm.Profile.Name"));
        var updatedTree = tree.WithChangedText(newText);
        var updatedCompilation = compilation.ReplaceSyntaxTree(tree, updatedTree);
        var second = GetBinding(updatedCompilation.GetSemanticModel(updatedTree), updatedTree).Plan;
        Assert.NotSame(first, second);
        Assert.Contains(second.Dependencies, d => d.Member?.Name == "Profile");
        Assert.DoesNotContain(first.Dependencies, d => d.Member?.Name == "Profile");
        var oldCSharp = compilation.CSharpCompilation.SyntaxTrees.Last();
        var changedCSharp = CSharpSyntaxTree.ParseText(oldCSharp.ToString().Replace("public string Name { get; set; } = \"Name\";", "public int Name { get; set; }", StringComparison.Ordinal), (CSharpParseOptions)oldCSharp.Options);
        var changedCompilation = compilation.WithCSharpCompilation(compilation.CSharpCompilation.ReplaceSyntaxTree(oldCSharp, changedCSharp));
        var (_, changed, third) = GetBinding(changedCompilation.GetSemanticModel(tree), tree);
        Assert.NotSame(first, third);
        Assert.Equal(SpecialType.System_Int32, third.ResultType!.SpecialType);
        Assert.True(changed.HasErrors);
        Assert.Equal(SpecialType.System_String, first.ResultType!.SpecialType);
    }

    [Fact]
    public void DynamicAndMarkupExtensions_KeepTheirExistingValueKinds()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Header + "<TextBlock Text={Vm.Name} Tag=${Binding Name} ToolTip.Tip=${CompiledBinding Name} />", Models);
        var operations = fixture.GetRootElement().StartTag!.Attributes.OfType<MarkupPlainAttributeSyntax>()
            .Select(a => Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(fixture.SemanticModel.GetOperation(a))).ToArray();
        Assert.Equal(new[] { MarkupAttributeValueKind.DynamicExpression, MarkupAttributeValueKind.MarkupExtension }, operations.Select(o => o.ValueKind));
        Assert.All(operations, o => Assert.Null(o.ExpressionBindingPlan));
    }

    internal static AkcssActivatorPlannerTests.PlannerFixture Create(string target, string expression) =>
        AkcssActivatorPlannerTests.CreateFixture(Header + "<TextBlock " + target + "=${x.bind " + expression + "} />", Models);

    internal static (MarkupAttributeSyntax Attribute, IMarkupPropertySetterOperation Operation, ExpressionBindingPlan Plan) GetBinding(AkcssActivatorPlannerTests.PlannerFixture fixture) =>
        GetBinding(fixture.SemanticModel, fixture.ComponentTree);

    internal static (MarkupAttributeSyntax Attribute, IMarkupPropertySetterOperation Operation, ExpressionBindingPlan Plan) GetBinding(AkburaSemanticModel model, AkburaSyntaxTree tree)
    {
        var attribute = tree.GetRootSyntax().DescendantNodes().OfType<MarkupAttributeSyntax>()
            .Single(a => AkburaSemanticModel.GetMarkupAttributeValue(a) is MarkupExpressionBindingSyntax);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(model.GetOperation(attribute));
        return (attribute, operation, Assert.IsType<ExpressionBindingPlan>(operation.ExpressionBindingPlan));
    }

    private sealed class CountingWalker : OperationWalker
    {
        public int Count { get; private set; }
        public override void DefaultVisit(Akbura.Language.Operations.IOperation operation)
        { Count++; base.DefaultVisit(operation); }
    }

    internal const string Header = "using Avalonia.Controls; using Demo; param Vm Vm; ";
    internal const string Models = """
        using System;
        using System.ComponentModel;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using Avalonia.Controls;
        namespace Demo;
        public sealed class Vm : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            public string Name { get; set; } = "Name";
            public int Count { get; set; }
            public decimal Price { get; set; }
            public bool Enabled { get; set; }
            public Vm Profile { get; set; } = null!;
            public ObservableCollection<Vm> Items { get; set; } = new();
            public List<Vm> PlainItems { get; set; } = new();
            public Vm[] Array { get; set; } = [];
            public ObservableValue ObservableValue;
            public int MutableField;
            public static readonly int ReadOnlyField = 1;
            public const int Constant = 1;
            public string Calculate() => Name.ToString();
        }
        public class Plain { public string Name { get; set; } = ""; }
        public struct ObservableValue : INotifyPropertyChanged
        {
            public string Name { get; set; }
            public event PropertyChangedEventHandler? PropertyChanged;
        }
        public class Target : Control
        {
            public string InitialOnly { get; init; } = "";
            public string Scalar { get; set; } = "";
            public string ReadOnly => "";
            public string WriteOnly { set { } }
            public string PrivateGetter { private get; set; } = "";
            public Span<int> SpanValue { get => default; set { } }
            public List<Vm> Values { get; set; } = new();
        }
        public class Assignment
        {
            public Avalonia.AvaloniaProperty? Target { get; set; }
            [Avalonia.Data.AssignBinding, Avalonia.Metadata.DependsOn(nameof(Target))]
            public object? Value { get; set; }
        }
        public partial class PlannerView
        {
            public Vm Member { get; } = null!;
            public static string Format(decimal price, string currency) => price.ToString() + currency;
        }
        """;
}
