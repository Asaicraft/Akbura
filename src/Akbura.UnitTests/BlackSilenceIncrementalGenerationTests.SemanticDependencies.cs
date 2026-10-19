using Akbura.BlackSilence;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    private const string DependencyApi = """
        namespace Demo;
        public class Api
        {
            public static string Read() => "one";
            public static string Read(object value) => "object";
            public static string Name { get => "one"; }
        }
        """;

    public static IEnumerable<object[]> SemanticDeclarationEdits()
    {
        yield return ["return type", DependencyApi, DependencyApi.Replace("string Read() => \"one\"", "int Read() => 1"), "Read().ToString()"];
        yield return ["property type", DependencyApi, DependencyApi.Replace("string Name { get => \"one\"; }", "int Name { get => 1; }"), "Name.ToString()"];
        yield return ["parameter type", DependencyApi, DependencyApi.Replace("Read(object value)", "Read(string value)"), "Read(\"x\")"];
        yield return ["optional parameter", DependencyApi, DependencyApi.Replace("Read()", "Read(int value = 1)"), "Read()"];
        yield return ["add overload", DependencyApi, DependencyApi.Replace("public static string Name", "public static string Read(string value) => value; public static string Name"), "Read(\"x\")"];
        yield return ["remove overload", DependencyApi, DependencyApi.Replace("public static string Read(object value) => \"object\";", ""), "Read(\"x\")"];
        yield return ["remove method", DependencyApi, DependencyApi.Replace("public static string Read() => \"one\";", ""), "Read()"];
        yield return ["negative becomes valid", DependencyApi, DependencyApi.Replace("public static string Name", "public static string Missing() => \"found\"; public static string Name"), "Missing()"];
        yield return ["accessibility", DependencyApi, DependencyApi.Replace("public static string Read()", "private static string Read()"), "Read()"];
        yield return ["ambiguous null candidate", DependencyApi, DependencyApi.Replace("Read(object value)", "Read(string value)").Replace("public static string Name", "public static string Read(System.Uri value) => \"uri\"; public static string Name"), "Read(null)"];
        yield return ["numeric argument conversion", DependencyApi.Replace("object value", "long value"), DependencyApi.Replace("object value", "double value"), "Read(1)"];
        yield return ["auto property", DependencyApi, DependencyApi.Replace("get => \"one\";", "get; set;"), "Name"];
        yield return ["escaped declaration", DependencyApi.Replace("class Api", "class @Api"), DependencyApi.Replace("class Api", "class @Api").Replace("string Read() => \"one\"", "object Read() => \"one\""), "Read().ToString()"];
    }

    [Theory]
    [MemberData(nameof(SemanticDeclarationEdits))]
    public void SemanticDependencies_DeclarationEditAndRevertBindConsumerButReuseIndependentDocument(string name, string before, string after, string expression)
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[]
        {
            project.File("Uses.akbura", Component("<TextBlock Text={global::Demo.Api." + expression + "} />")),
            project.File("Other.akbura", Component("<TextBlock Text=\"independent\" />")),
        };
        var tree = CSharpSyntaxTree.ParseText(before, project.Options.ParseOptions, "Api.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var current = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        foreach (var source in new[] { after, before })
        {
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
            using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
            current = ArtifactRun(project, current.Driver, compilation);
            Assert.Equal(ArtifactReuseDecision.ValidateDocumentDependencies, current.Request.ArtifactValidation.Decision);
            Assert.Null(current.Request.Components.Single(component => component.Identity.EndsWith("Uses.akbura")).Previous);
            Assert.NotNull(current.Request.Components.Single(component => component.Identity.EndsWith("Other.akbura")).Previous);
            Assert.Null(current.Request.Diagnostics.Single(diagnostic => diagnostic.Document.FilePath.EndsWith("Uses.akbura")).Previous);
            Assert.NotNull(current.Request.Diagnostics.Single(diagnostic => diagnostic.Document.FilePath.EndsWith("Other.akbura")).Previous);
#if STATS
            Assert.Equal(1, measurement.GetSnapshot().GetStageInvocationCount(GenerationStatisticStage.SemanticBinding));
#endif
            AssertArtifactsEqualToFresh(project, current, compilation, files);
            AssertDetachedGraph(current.Request.State.Artifacts);
        }
        GC.KeepAlive(name);
    }

    [Fact]
    public void SemanticDependencies_NestedStaticReceiverTracksHidingMemberCandidate()
    {
        const string before = "namespace Demo; public class Base { public static string Read() => \"base\"; } public class Derived : Base { }";
        const string after = "namespace Demo; public class Base { public static string Read() => \"base\"; } public class Derived : Base { public new static int Read() => 1; }";
        SemanticDependencies_DeclarationEditAndRevertBindConsumerButReuseIndependentDocument("hiding receiver",
            before.Replace("Derived", "Api"), after.Replace("Derived", "Api"), "Read().ToString()");
    }

    [Fact]
    public void SemanticDependencies_QualifiedTypeIdentityDoesNotCollideOnSimpleNames()
    {
        const string before = "namespace Left { public class Api { public static string Read() => \"left\"; } } namespace Right { public class Api { public static string Read() => \"right\"; } }";
        var project = new TestProject(publishDiagnostics: true);
        var files = new[]
        {
            project.File("Left.akbura", Component("<TextBlock Text={global::Left.Api.Read().ToString()} />")),
            project.File("Right.akbura", Component("<TextBlock Text={global::Right.Api.Read()} />")),
        };
        var tree = CSharpSyntaxTree.ParseText(before, project.Options.ParseOptions, "Api.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(before.Replace("string Read() => \"left\"", "int Read() => 1"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Null(current.Request.Components.Single(component => component.Identity.EndsWith("Left.akbura")).Previous);
        Assert.NotNull(current.Request.Components.Single(component => component.Identity.EndsWith("Right.akbura")).Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, files);
    }

    [Fact]
    public void SemanticDependencies_NewOverloadChangesActualSelectedMethodEvenWhenGeneratedTextIsEqual()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Uses.akbura", Component("<TextBlock Text={global::Demo.Api.Read(\"x\")} />"));
        var tree = CSharpSyntaxTree.ParseText(DependencyApi, project.Options.ParseOptions, "Api.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var source = DependencyApi.Replace("public static string Name", "public static string Read(string value) => value; public static string Name");
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Equal(SpecialType.System_Object, Selected(initial.Output).Parameters.Single().Type.SpecialType);
        Assert.Equal(SpecialType.System_String, Selected(current.Output).Parameters.Single().Type.SpecialType);
        Assert.Null(current.Request.Components.Single().Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, file);

        static IMethodSymbol Selected(Compilation output)
        {
            foreach (var syntaxTree in output.SyntaxTrees)
            {
                var call = syntaxTree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                    .FirstOrDefault(static invocation => invocation.Expression.ToString() == "global::Demo.Api.Read");
                if (call != null)
                {
                    return Assert.IsAssignableFrom<IMethodSymbol>(output.GetSemanticModel(syntaxTree).GetSymbolInfo(call).Symbol);
                }
            }
            throw new InvalidOperationException("The expected generated call was not found.");
        }
    }

    [Theory]
    [InlineData("using Alias = Demo.Api; <TextBlock Text={Alias.Read()} />")]
    [InlineData("using static Demo.Api; <TextBlock Text={Read()} />")]
    [InlineData("state int count = 0; <TextBlock Text={count.ToString()} />")]
    [InlineData("param string Value = \"x\"; <TextBlock Text={Value} />")]
    [InlineData("<StackPanel>$if (true) { <Border /> }</StackPanel>")]
    [InlineData("<StackPanel>$foreach (var item in new[] { 1 }) { <TextBlock Text={item.ToString()} /> }</StackPanel>")]
    [InlineData("<Border><Border.Background><SolidColorBrush Color=\"Red\" /></Border.Background></Border>")]
    [InlineData("<TextBlock Text={global::System.Linq.Enumerable.Empty<int>().Count().ToString()} />")]
    [InlineData("<DataTemplate><TextBlock Text=\"template\" /></DataTemplate>")]
    public void SemanticDependencies_UnclassifiedActiveScopeRebindsEvenForUnrelatedClassChange(string markup)
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component(markup));
        var tree = CSharpSyntaxTree.ParseText(DependencyApi, project.Options.ParseOptions, "Api.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(DependencyApi.Replace("string Read() => \"one\"", "int Read() => 1"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Null(current.Request.Components.Single().Previous);
        Assert.Null(current.Request.Diagnostics.Single().Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, file);
    }

    public static IEnumerable<object[]> UnclosedSemanticUniverses() => ArtifactAdversaries().Where(static row =>
        (string)row[0] is "extension" or "alias" or "constant" or "base type" or "constraint" or "attribute" or "invalid syntax" or "global using");

    [Theory]
    [MemberData(nameof(UnclosedSemanticUniverses))]
    public void SemanticDependencies_UnclosedDeclarationUniverseCannotReuseIndependentArtifacts(string name, string before, string after, string markup)
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Uses.akbura", Component(markup)), project.File("Other.akbura", Component("<Border />")) };
        var tree = CSharpSyntaxTree.ParseText(before, project.Options.ParseOptions, "Api.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(after, project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.All(current.Request.Components, static component => Assert.Null(component.Previous));
        Assert.All(current.Request.Diagnostics, static diagnostic => Assert.Null(diagnostic.Previous));
        AssertArtifactsEqualToFresh(project, current, compilation, files);
        GC.KeepAlive(name);
    }

    [Fact]
    public void SemanticDependencies_DuplicatePartialIdentityClosesTheProjectProof()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border />"));
        var tree = CSharpSyntaxTree.ParseText(DependencyApi, project.Options.ParseOptions, "Api.cs");
        var partial = CSharpSyntaxTree.ParseText("namespace Demo; public partial class @Api { }", project.Options.ParseOptions, "Partial.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree, partial);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(DependencyApi.Replace("string Read() => \"one\"", "int Read() => 1"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Equal("AmbiguousOrPartialContainingType", current.Request.ArtifactValidation.Reason);
        Assert.Null(current.Request.Components.Single().Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, file);
    }

    [Fact]
    public void SemanticDependencies_UserPartialComponentSurfaceCannotSupplyAFixedExportProof()
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Child.akbura", Component("<Border />")),
            project.File("Parent.akbura", Component("using Demo; <Child />")), project.File("Other.akbura", Component("<Border />")) };
        var tree = CSharpSyntaxTree.ParseText(DependencyApi, project.Options.ParseOptions, "Api.cs");
        var partial = CSharpSyntaxTree.ParseText("namespace Demo; public partial class Child { }", project.Options.ParseOptions, "Child.akbura.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree, partial);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(DependencyApi.Replace("string Read() => \"one\"", "int Read() => 1"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Null(current.Request.Components.Single(component => component.Identity.EndsWith("Child.akbura")).Previous);
        Assert.Null(current.Request.Components.Single(component => component.Identity.EndsWith("Parent.akbura")).Previous);
        Assert.NotNull(current.Request.Components.Single(component => component.Identity.EndsWith("Other.akbura")).Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, files);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SemanticDependencies_ExistingSurfaceGraphDoesNotPropagateAnEquivalentBody(bool inferredSurface)
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[]
        {
            project.File("Child.akbura", Component((inferredSurface ? "param string Value = \"x\"; " : "") + "<TextBlock Text={global::Demo.Api.Read().ToString()} />")),
            project.File("Parent.akbura", Component("using Demo; <Child />")),
            project.File("Other.akbura", Component("<Border />")),
        };
        var tree = CSharpSyntaxTree.ParseText(DependencyApi, project.Options.ParseOptions, "Api.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(DependencyApi.Replace("string Read() => \"one\"", "int Read() => 1"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Null(current.Request.Components.Single(component => component.Identity.EndsWith("Child.akbura")).Previous);
        Assert.Equal(!inferredSurface, current.Request.Components.Single(component => component.Identity.EndsWith("Parent.akbura")).Previous != null);
        Assert.NotNull(current.Request.Components.Single(component => component.Identity.EndsWith("Other.akbura")).Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, files);
    }
    private const string SemanticDependencySources = """
        namespace Demo;
        public sealed class UserViewModel
        {
            public static UserViewModel Current { get; } = new UserViewModel();
            public string GetName() => "John";
            public string Name { get => "John"; }
        }
        public sealed class OrderService
        {
            public static OrderService Current { get; } = new OrderService();
            public string GetOrders() => "orders";
        }
        public sealed class SettingsViewModel
        {
            public static SettingsViewModel Current { get; } = new SettingsViewModel();
            public string Theme { get => "dark"; }
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SemanticDependencies_ReturnTypeChangeRebindsOnlyItsConsumer(bool debugStructural)
    {
        var project = new TestProject(publishDiagnostics: true, debugStructural: debugStructural);
        var files = new[]
        {
            project.File("Profile.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName().ToString()} />")),
            project.File("Orders.akbura", Component("<TextBlock Text={global::Demo.OrderService.Current.GetOrders()} />")),
            project.File("Settings.akbura", Component("<TextBlock Text={global::Demo.SettingsViewModel.Current.Theme} />")),
        };
        var tree = CSharpSyntaxTree.ParseText(SemanticDependencySources, project.Options.ParseOptions, "Models.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        Assert.Empty(initial.Output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        var source = SemanticDependencySources.Replace("string GetName() => \"John\"", "int GetName() => 42", StringComparison.Ordinal);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Null(current.Request.Components.Single(component => component.Identity.EndsWith("Profile.akbura", StringComparison.Ordinal)).Previous);
        Assert.True(current.Request.Components.Single(component => component.Identity.EndsWith("Orders.akbura", StringComparison.Ordinal)).Previous != null,
            "Independent Orders was rebound: " + current.Request.ArtifactValidation.Reason + "; " +
            string.Join(", ", initial.Snapshot.DetachedArtifacts!.Entries.Values.Where(artifact => artifact.DocumentPath.EndsWith("Orders.akbura"))
                .SelectMany(artifact => artifact.SemanticDependencies!.IncompleteReasons)));
        Assert.NotNull(current.Request.Components.Single(component => component.Identity.EndsWith("Settings.akbura", StringComparison.Ordinal)).Previous);
        Assert.All(current.Request.Diagnostics.Where(diagnostic => !diagnostic.Document.FilePath.EndsWith("Profile.akbura", StringComparison.Ordinal)),
            diagnostic => Assert.NotNull(diagnostic.Previous));
        Assert.Equal(SpecialType.System_String, ((IMethodSymbol)initial.Output.GetTypeByMetadataName("Demo.UserViewModel")!.GetMembers("GetName").Single()).ReturnType.SpecialType);
        Assert.Equal(SpecialType.System_Int32, ((IMethodSymbol)current.Output.GetTypeByMetadataName("Demo.UserViewModel")!.GetMembers("GetName").Single()).ReturnType.SpecialType);
        Assert.Empty(current.Output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
#if STATS
        var statistics = measurement.GetSnapshot();
        Assert.Equal(1, statistics.ComponentGeneratedCount);
        Assert.Equal(2, statistics.ComponentReusedCount);
        Assert.Equal(1, statistics.GetStageInvocationCount(GenerationStatisticStage.SemanticBinding));
#endif
        AssertArtifactsEqualToFresh(project, current, compilation, files);
        AssertDetachedGraph(current.Request.State.Artifacts);
    }
}
