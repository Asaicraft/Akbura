using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.ObjectModel;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class CollectionContentRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeometryContentCollection_PreservesValuesWithoutAddingLogicalChildren(bool debug)
    {
        var ownerType = CompileOwner("StreamGeometry", debug);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(
            () =>
            {
                var owner = CreateOwner(ownerType);
                var logicalOwner = Assert.IsAssignableFrom<ILogical>(owner);
                var content = GetContent<StreamGeometry>(ownerType, owner);
                var first = new StreamGeometry();
                var second = new StreamGeometry();
                var replacement = new StreamGeometry();

                Assert.Empty(content);
                Assert.Empty(logicalOwner.LogicalChildren);
                Assert.Same(content, GetContent<StreamGeometry>(ownerType, owner));

                content.Add(first);
                content.Add(second);

                Assert.Equal(new[] { first, second }, content.ToArray());
                Assert.Empty(logicalOwner.LogicalChildren);

                content.Move(0, 1);
                Assert.Equal(new[] { second, first }, content.ToArray());
                Assert.Empty(logicalOwner.LogicalChildren);

                content[0] = replacement;
                Assert.Equal(new[] { replacement, first }, content.ToArray());
                Assert.Empty(logicalOwner.LogicalChildren);

                while (content.Count > 0)
                {
                    content.RemoveAt(content.Count - 1);
                    Assert.Empty(logicalOwner.LogicalChildren);
                }

                Assert.Empty(content);
                Assert.Same(content, GetContent<StreamGeometry>(ownerType, owner));
            },
            CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedContentCollection_TracksOnlyDistinctControlsAndReleasesReplacedChildren(bool debug)
    {
        var ownerType = CompileOwner("object?", debug);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(
            () =>
            {
                var owner = CreateOwner(ownerType);
                var logicalOwner = Assert.IsAssignableFrom<ILogical>(owner);
                var content = GetContent<object?>(ownerType, owner);
                var first = new TextBlock();
                var second = new Border();
                var replacement = new TextBox();
                var geometry = new StreamGeometry();

                content.Add(null);
                content.Add(geometry);
                content.Add("not a control");
                content.Add(42);

                Assert.Empty(logicalOwner.LogicalChildren);
                Assert.Equal(4, content.Count);
                Assert.Null(content[0]);
                Assert.Same(geometry, content[1]);
                Assert.Equal("not a control", Assert.IsType<string>(content[2]));
                Assert.Equal(42, Assert.IsType<int>(content[3]));

                content.Add(first);
                content.Add(second);
                content.Add(first);

                AssertLogicalChildren(owner, first, second);
                Assert.Same(content, GetContent<object?>(ownerType, owner));

                content.Move(content.IndexOf(second), 0);
                AssertLogicalChildren(owner, second, first);

                Assert.True(content.Remove(first));
                AssertLogicalChildren(owner, second, first);

                Assert.True(content.Remove(first));
                Assert.Null(((ILogical)first).LogicalParent);
                AssertLogicalChildren(owner, second);

                content[content.IndexOf(second)] = replacement;
                Assert.Null(((ILogical)second).LogicalParent);
                AssertLogicalChildren(owner, replacement);

                content[content.IndexOf(replacement)] = null;
                Assert.Null(((ILogical)replacement).LogicalParent);
                Assert.Empty(logicalOwner.LogicalChildren);

                content.Add(first);
                AssertLogicalChildren(owner, first);

                // Remove entries individually: Reset has a separate, unchanged runtime policy.
                while (content.Count > 0)
                {
                    content.RemoveAt(content.Count - 1);
                }

                Assert.Empty(content);
                Assert.Empty(logicalOwner.LogicalChildren);
                Assert.Null(((ILogical)first).LogicalParent);
                Assert.Null(((ILogical)second).LogicalParent);
                Assert.Null(((ILogical)replacement).LogicalParent);
            },
            CancellationToken.None);
    }

    private static void AssertLogicalChildren(AkburaControl owner, params Control[] expected)
    {
        var logicalOwner = Assert.IsAssignableFrom<ILogical>(owner);
        Assert.Equal(expected.Length, logicalOwner.LogicalChildren.Count);
        Assert.Equal(expected, logicalOwner.LogicalChildren.OfType<Control>().ToArray());
        foreach (var child in expected)
        {
            Assert.Contains(child, logicalOwner.LogicalChildren);
            Assert.Same(owner, ((ILogical)child).LogicalParent);
        }
    }

    private static AkburaControl CreateOwner(Type ownerType) =>
        Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));

    private static ObservableCollection<T> GetContent<T>(Type ownerType, AkburaControl owner)
    {
        var property = ownerType.GetProperty("Content");
        Assert.NotNull(property);
        return Assert.IsType<ObservableCollection<T>>(property.GetValue(owner));
    }

    private static Type CompileOwner(string elementType, bool debug)
    {
        var component =
            "using Avalonia.Media;\r\n" +
            "using System.Collections.ObjectModel;\r\n\r\n" +
            "param ObservableCollection<" + elementType + "> Content;";
        const string ownerSource =
            """
            using Akbura;
            using Akbura.Engine;
            using Avalonia.Controls;

            namespace Demo;

            public partial class PlannerView : AkburaControl
            {
                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }

                protected override Control FirstUpdate() => new Border();

                protected override Control Update() => Child ?? new Border();
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, ownerSource);
        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        using var writer = new CodeWriter("\r\n");
        using var componentWriter = new ComponentWriter(
            writer,
            symbol,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());

        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine("namespace Demo;");
        writer.WriteLine();
        writer.WriteLine("public partial class PlannerView");
        writer.WriteLine("{");
        writer.CurrentIndent = 4;
        Assert.True(componentWriter.WriteComponentMembers());
        writer.WriteLine();
        componentWriter.WriteDescriptorMembers();
        writer.CurrentIndent = 0;
        writer.WriteLine("}");

        var generatedSource = writer.GetText().ToString();
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols(debug ? new[] { "DEBUG" } : Array.Empty<string>());
        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            parseOptions,
            path: "PlannerView.CollectionContent.g.cs");
        var compilation = fixture.CSharpCompilation
            .AddSyntaxTrees(generatedTree)
            .WithAssemblyName("CollectionContentRuntime_" + Guid.NewGuid().ToString("N"))
            .WithOptions(fixture.CSharpCompilation.Options.WithOptimizationLevel(
                debug ? OptimizationLevel.Debug : OptimizationLevel.Release));
        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);

        using var assemblyStream = new MemoryStream();
        var result = compilation.Emit(assemblyStream);
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics) + Environment.NewLine + generatedSource);

        var ownerType = Assembly.Load(assemblyStream.ToArray()).GetType("Demo.PlannerView");
        Assert.NotNull(ownerType);
        return ownerType;
    }
}
