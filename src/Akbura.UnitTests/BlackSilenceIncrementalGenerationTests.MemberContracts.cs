using Akbura.BlackSilence;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    private const string MemberViewModel = """
        namespace Demo;
        public sealed class UserViewModel
        {
            private string _name = "John";
            public static UserViewModel Current { get; } = new UserViewModel();
            public string GetName() { return _name; }
            public int GetAge() => 20;
            public string Name { get => _name; set => _name = value; }
        }
        """;

    public static IEnumerable<object[]> SupportedMemberBodies()
    {
        yield return ["referenced block", MemberViewModel.Replace("return _name;", "return _name + \"Alex\";"), "GetName()"];
        yield return ["unrelated expression", MemberViewModel.Replace("=> 20", "=> 21"), "GetName()"];
        yield return ["multiple methods", MemberViewModel.Replace("return _name;", "var name = _name; return name + \"!\";").Replace("=> 20", "=> 21"), "GetName()"];
        yield return ["referenced expression", MemberViewModel.Replace("=> 20", "=> 21 + 1"), "GetAge().ToString()"];
        yield return ["getter", MemberViewModel.Replace("get => _name", "get => _name + \"Alex\""), "Name"];
        yield return ["setter", MemberViewModel.Replace("_name = value;", "_name = value.Trim();"), "Name"];
        yield return ["longer body and mapping shift", MemberViewModel.Replace("return _name;", "\nvar result = _name + \"!\";\nreturn result;\n"), "GetAge().ToString()"];
    }

    [Theory]
    [InlineData("public class Empty;", false)]
    [InlineData("namespace Demo; public class Empty;", false)]
    [InlineData("namespace Demo { public class Empty; }", false)]
    [InlineData("public class Empty; public class Model { public int Read() => 1; }", true)]
    [InlineData("public class Model { public int Read() => 1; } public class Empty;", true)]
    [InlineData("namespace Demo { public class Empty; public class Model { public int Read() => 1; } }", true)]
    public void MemberContracts_BodylessClassesRemainInExactSourceSegments(string source, bool hasContract)
    {
        var root = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)).GetRoot();
        Assert.Empty(root.GetDiagnostics());
        var contract = DetachedCSharpDeclarationContract.Create(root, source, CancellationToken.None);
        Assert.Equal(hasContract, contract != null);
        if (contract != null)
        {
            Assert.Single(contract.Types);
            Assert.Contains("public class Empty;", string.Concat(contract.TypeSegments));
        }
    }

    [Theory]
    [InlineData("public class Empty;", "public class Renamed;")]
    [InlineData("public class Empty;", "public class Empty { public int Read() => 1; }")]
    [InlineData("public class Empty { public int Read() => 1; }", "public class Empty;")]
    public void MemberContracts_BodylessDeclarationChangesRebindAndMatchFreshDriver(string before, string after)
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName()} />"));
        var source = MemberViewModel + "\r\n" + before;
        var tree = CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var current = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        foreach (var declaration in new[] { after, before })
        {
            var changed = CSharpSyntaxTree.ParseText(MemberViewModel + "\r\n" + declaration, project.Options.ParseOptions, tree.FilePath);
            var compilation = baseline.ReplaceSyntaxTree(tree, changed);
            current = ArtifactRun(project, current.Driver, compilation);
            Assert.Null(current.Request.Components.Single().Previous);
            Assert.Empty(current.Output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            AssertArtifactsEqualToFresh(project, current, compilation, file);
        }
    }

    [Theory]
    [MemberData(nameof(SupportedMemberBodies))]
    public void MemberContracts_ReferencedAndIndependentBodiesReuseWithoutBinding(string name, string source, string expression)
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current." + expression + "} />")),
            project.File("Other.akbura", Component("<Border />")) };
        var tree = CSharpSyntaxTree.ParseText(MemberViewModel, project.Options.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        using var measurement = GenerationStatistics.BeginMeasurement();
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.All(current.Request.Components, component => Assert.NotNull(component.Previous));
        Assert.All(current.Request.Diagnostics, diagnostic => Assert.NotNull(diagnostic.Previous));
        Assert.Empty(current.Output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(current.Snapshot.SemanticStates);
#if STATS
        var statistics = measurement.GetSnapshot();
        Assert.Equal(0, statistics.GetStageInvocationCount(GenerationStatisticStage.SemanticBinding));
        Assert.Equal(0, statistics.GetCounterCount(GenerationStatisticCounter.ProbeCompilationCreated));
        Assert.Equal(0, statistics.ComponentGeneratedCount);
        Assert.Equal(2, statistics.GetCounterCount(GenerationStatisticCounter.CrossCompilationArtifactHit));
#endif
        AssertArtifactsEqualToFresh(project, current, compilation, files);
        AssertDetachedGraph(current.Request.State.Artifacts);
        GC.KeepAlive(name);
    }

    public static IEnumerable<object[]> UnsupportedMemberContracts()
    {
        yield return ["return type", MemberViewModel, MemberViewModel.Replace("string GetName()", "object GetName()")];
        yield return ["parameter", MemberViewModel, MemberViewModel.Replace("GetName()", "GetName(int value)")];
        yield return ["overload", MemberViewModel, MemberViewModel.Replace("public int GetAge()", "public string GetName(int value) => value.ToString(); public int GetAge()")];
        yield return ["constant", MemberViewModel + " public static class Values { public const int Number = 1; }", MemberViewModel + " public static class Values { public const int Number = 2; }"];
        yield return ["attribute", MemberViewModel, MemberViewModel.Replace("public string GetName()", "[System.Obsolete(\"changed\")] public string GetName()")];
        yield return ["generic method", MemberViewModel.Replace("GetName()", "GetName<T>()"), MemberViewModel.Replace("GetName()", "GetName<T>()").Replace("return _name;", "return \"Alex\";")];
        yield return ["generic constraint", MemberViewModel.Replace("GetName()", "GetName<T>() where T : class"), MemberViewModel.Replace("GetName()", "GetName<T>() where T : struct")];
        yield return ["partial type", MemberViewModel.Replace("sealed class", "sealed partial class"), MemberViewModel.Replace("sealed class", "sealed partial class").Replace("return _name;", "return \"Alex\";")];
        yield return ["partial method", MemberViewModel + " public partial class Partial { partial void Apply(); partial void Apply() { int value = 1; } }",
            MemberViewModel + " public partial class Partial { partial void Apply(); partial void Apply() { int value = 2; } }"];
        yield return ["generic containing type", MemberViewModel + " public class Generic<T> { public string Read() => \"One\"; }",
            MemberViewModel + " public class Generic<T> { public string Read() => \"Two\"; }"];
        yield return ["nested containing type", MemberViewModel + " public class Parent { public class Nested { public string Read() => \"One\"; } }",
            MemberViewModel + " public class Parent { public class Nested { public string Read() => \"Two\"; } }"];
        yield return ["file-local declaration", MemberViewModel + " file class LocalModel { public int Read() => 1; }",
            MemberViewModel + " file class LocalModel { public int Read() => 2; }"];
        yield return ["attributed method body", MemberViewModel.Replace("public string GetName()", "[System.Obsolete] public string GetName()"),
            MemberViewModel.Replace("public string GetName()", "[System.Obsolete] public string GetName()").Replace("return _name;", "return \"Alex\";")];
        yield return ["async body", MemberViewModel + " public class Worker { public async System.Threading.Tasks.Task<int> Read() { await System.Threading.Tasks.Task.Yield(); return 1; } }",
            MemberViewModel + " public class Worker { public async System.Threading.Tasks.Task<int> Read() { await System.Threading.Tasks.Task.Yield(); return 2; } }"];
        yield return ["iterator body", MemberViewModel + " public class Worker { public System.Collections.Generic.IEnumerable<int> Read() { yield return 1; } }",
            MemberViewModel + " public class Worker { public System.Collections.Generic.IEnumerable<int> Read() { yield return 2; } }"];
        yield return ["property type", MemberViewModel, MemberViewModel.Replace("string Name", "object Name")];
        yield return ["auto property", MemberViewModel, MemberViewModel.Replace("get => _name; set => _name = value;", "get; set;")];
        yield return ["init accessor", MemberViewModel, MemberViewModel.Replace("set => _name = value", "init => _name = value")];
        yield return ["constructor", MemberViewModel.Replace("private string", "public UserViewModel() { _name = \"One\"; } private string"), MemberViewModel.Replace("private string", "public UserViewModel() { _name = \"Two\"; } private string")];
        yield return ["directive", MemberViewModel, "#nullable enable\n" + MemberViewModel];
        yield return ["field initializer", MemberViewModel, MemberViewModel.Replace("_name = \"John\"", "_name = \"Alex\"")];
        yield return ["local function", MemberViewModel, MemberViewModel.Replace("return _name;", "string Local() => _name; return Local();")];
        yield return ["field backed property", MemberViewModel, MemberViewModel.Replace("get => _name", "get => field")];
        yield return ["syntax error", MemberViewModel, MemberViewModel.Replace("return _name;", "return ???;")];
        yield return ["namespace alias", "using Alias = System.String;\n" + MemberViewModel, "using Alias = System.Object;\n" + MemberViewModel];
        yield return ["new global using", MemberViewModel, "global using System.Text;\n" + MemberViewModel];
        yield return ["extension candidate", MemberViewModel + " public static class Extensions { public static int Size(this object value) => 1; }",
            MemberViewModel + " public static class Extensions { public static int Size(this object value) => 1; public static int Size(this string value) => 2; }"];
        yield return ["unresolved becomes valid", MemberViewModel.Replace("string GetName()", "Missing GetName()"), MemberViewModel];
    }

    [Fact]
    public void MemberContracts_TopLevelEquivalenceDoesNotAuthorizeChangedFieldInitializers()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border />"));
        var tree = CSharpSyntaxTree.ParseText(MemberViewModel, project.Options.ParseOptions, "ViewModel.cs");
        var changed = CSharpSyntaxTree.ParseText(MemberViewModel.Replace("_name = \"John\"", "_name = \"Alex\""), project.Options.ParseOptions, tree.FilePath);
        Assert.True(tree.IsEquivalentTo(changed, topLevel: true));
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var current = ArtifactRun(project, initial.Driver, baseline.ReplaceSyntaxTree(tree, changed));
        Assert.Null(current.Request.Components.Single().Previous);
        AssertArtifactsEqualToFresh(project, current, current.Request.State.CSharpCompilation, file);
    }

    [Fact]
    public void MemberContracts_UnchangedAttributeSyntaxCanHaveChangedCallerLineArguments()
    {
        const string attributes = """

            public sealed class CallerLineAttribute : System.Attribute
            {
                public CallerLineAttribute([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { }
            }
            [CallerLine]
            public sealed class Marked { }
            """;
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border />"));
        var source = MemberViewModel + attributes;
        var tree = CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, "ViewModel.cs");
        var changed = CSharpSyntaxTree.ParseText(source.Replace("return _name;", "\nreturn _name + \"!\";\n"), project.Options.ParseOptions, tree.FilePath);
        Assert.True(tree.IsEquivalentTo(changed, topLevel: true));
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var compilation = baseline.ReplaceSyntaxTree(tree, changed);
        var oldLine = baseline.GetTypeByMetadataName("Demo.Marked")!.GetAttributes().Single().ConstructorArguments.Single().Value;
        var newLine = compilation.GetTypeByMetadataName("Demo.Marked")!.GetAttributes().Single().ConstructorArguments.Single().Value;
        Assert.NotEqual(oldLine, newLine);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Null(current.Request.Components.Single().Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, file);
    }

    [Theory]
    [MemberData(nameof(UnsupportedMemberContracts))]
    public void MemberContracts_UnsupportedEditsAndRecoveryBindCurrentCompilation(string name, string before, string after)
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName().ToString()} />"));
        var tree = CSharpSyntaxTree.ParseText(before, project.Options.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var current = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        foreach (var source in new[] { after, before })
        {
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
            current = ArtifactRun(project, current.Driver, compilation);
            Assert.Null(current.Request.Components.Single().Previous);
            AssertArtifactsEqualToFresh(project, current, compilation, file);
        }
        GC.KeepAlive(name);
    }

    [Fact]
    public void MemberContracts_MixedBodyAndMarkupEditReusesOnlyUnchangedIndependentDocument()
    {
        var project = new TestProject(publishDiagnostics: true);
        var first = project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName()} />"));
        var second = project.File("Other.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.Name} />"));
        var tree = CSharpSyntaxTree.ParseText(MemberViewModel, project.Options.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, first, second), baseline);
        var changed = first.Replace("GetName()", "GetName() + \"!\"");
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(MemberViewModel.Replace("return _name;", "return \"Alex\";"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(first, changed), compilation);
        Assert.Null(current.Request.Components.Single(component => component.Identity.EndsWith("Page.akbura")).Previous);
        Assert.NotNull(current.Request.Components.Single(component => component.Identity.EndsWith("Other.akbura")).Previous);
        Assert.Null(current.Request.Diagnostics.Single(diagnostic => diagnostic.Document.FilePath == first.Path).Previous);
        Assert.NotNull(current.Request.Diagnostics.Single(diagnostic => diagnostic.Document.FilePath == second.Path).Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, changed, second);
    }

    [Fact]
    public void MemberContracts_BodyCompilerErrorsRemainVisibleWithReusedGeneratorOutput()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName()} />"));
        var tree = CSharpSyntaxTree.ParseText(MemberViewModel, project.Options.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(MemberViewModel.Replace("return _name;", "return MissingValue;"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.NotNull(current.Request.Components.Single().Previous);
        Assert.Contains(current.Output.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0103" && diagnostic.Location.SourceTree!.FilePath == tree.FilePath);
        AssertArtifactsEqualToFresh(project, current, compilation, file);
    }

    [Fact]
    public void MemberContracts_SourceReuseDoesNotSuppressNewlyRequestedDiagnostics()
    {
        var project = new TestProject(publishDiagnostics: true);
        var off = new TestOptions("Demo", project.Directory, diagnosticMode: "Off");
        var file = project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName()} />"));
        var tree = CSharpSyntaxTree.ParseText(MemberViewModel, off.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(off, file), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(MemberViewModel.Replace("return _name;", "return \"Alex\";"), off.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver.WithUpdatedAnalyzerConfigOptions(project.Options), compilation);
        Assert.NotNull(current.Request.Components.Single().Previous);
        Assert.Null(current.Request.Diagnostics.Single().Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, file);
    }

    [Fact]
    public void MemberContracts_MixedAkcssDependencyEditDoesNotReuseItsConsumer()
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Page.akbura", Component("using Demo.Styles.Base.akcss; <Border class=\"basic\" />")),
            project.File("Other.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName()} />")),
            project.File("Styles/Base.akcss", "@using Avalonia.Controls; Border.basic { Width: 10; }") };
        var tree = CSharpSyntaxTree.ParseText(MemberViewModel, project.Options.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var changed = project.File("Styles/Base.akcss", files[2].Text.ToString().Replace("10", "20"));
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(MemberViewModel.Replace("return _name;", "return \"Alex\";"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(files[2], changed), compilation);
        Assert.Null(current.Request.Components.Single(component => component.Identity.EndsWith("Page.akbura")).Previous);
        Assert.NotNull(current.Request.Components.Single(component => component.Identity.EndsWith("Other.akbura")).Previous);
        Assert.All(current.Request.ExternalAkcss, module => Assert.Null(module.Previous));
        AssertArtifactsEqualToFresh(project, current, compilation, files[0], files[1], changed);
    }

    [Fact]
    public void MemberContracts_OptOutUsesExistingConservativePath()
    {
        var project = new TestProject(publishDiagnostics: true);
        var options = new TestOptions("Demo", project.Directory, publishDiagnostics: true, crossCompilationReuse: false);
        var file = project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName()} />"));
        var tree = CSharpSyntaxTree.ParseText(MemberViewModel, options.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(options, file), baseline);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(MemberViewModel.Replace("return _name;", "return \"Alex\";"), options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Null(current.Request.Components.Single().Previous);
        Assert.Null(current.Request.State.Artifacts.Latest);
        AssertArtifactsEqualToFresh(project, current, compilation, file);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void MemberContracts_MixedLongSessionKeepsOnlyNewestDetachedGeneration(int revisions)
    {
        var result = RunMemberLifetime(revisions);
        AssertCollected(result.WeakObjects);
        Assert.Single(result.Owner.Latest!.Entries);
        AssertDetachedGraph(result.Owner);
        GC.KeepAlive(result.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactLifetimeResult RunMemberLifetime(int revisions)
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName().ToString()} />"));
        var tree = CSharpSyntaxTree.ParseText(MemberViewModel, project.Options.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var current = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var weak = new List<(string Kind, WeakReference Reference)>();
        CaptureArtifactWeakObjects(current, weak);
        for (var index = 1; index <= revisions; index++)
        {
            var source = index % 11 == 0 ? MemberViewModel : MemberViewModel.Replace("return _name;", "return _name + \"" + index + "\";");
            if (index % 7 == 0)
            {
                source = source.Replace("string GetName()", "object GetName()");
            }
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
            var driver = current.Driver;
            if (index % 13 == 0)
            {
                var changed = project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName().ToString() + \"" + index + "\"} />"));
                driver = driver.ReplaceAdditionalText(file, changed);
                file = changed;
            }
            current = ArtifactRun(project, driver, compilation);
            AssertArtifactsEqualToFresh(project, current, compilation, file);
            CaptureArtifactWeakObjects(current, weak);
            Assert.Single(current.Request.State.Artifacts.Latest!.Entries);
        }
        return new ArtifactLifetimeResult(current.Request.State.Artifacts, weak);
    }
}
