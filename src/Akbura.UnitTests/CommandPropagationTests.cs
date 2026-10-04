using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class CommandPropagationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CommandChange_RefreshesChildButtonWithCurrentParentClosure(bool structural, bool conditional)
    {
        var type = Compile(structural, conditional);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            type.GetMethod("InitializeForTest")!.Invoke(owner, null);
            var root = Assert.IsType<StackPanel>(owner.Child);
            var child = Assert.IsAssignableFrom<AkburaControl>(Assert.Single(root.Children));
            child.GetType().GetMethod("InitializeForTest")!.Invoke(child, null);
            var button = Assert.IsType<Button>(child.Child);
            var commandProperty = child.GetType().GetProperty("ToggleSidebar")!;
            var widthProperty = type.GetProperty("WidthForTest")!;
            var modeProperty = type.GetProperty("LastMode")!;

            Assert.Equal("unchanged", button.Content);
            Assert.Same(commandProperty.GetValue(child), button.Command);
            button.Command!.Execute(null);
            Assert.Equal("desktop", modeProperty.GetValue(owner));

            foreach (var (width, expectedMode) in new[] { (500d, "mobile"), (1100d, "collapsible"), (1400d, "desktop") })
            {
                var previousCommand = button.Command;
                widthProperty.SetValue(owner, width);

                // Only the parent's width changes. The child's ordinary parameter stays the same.
                Assert.Same(root, owner.Child);
                Assert.Same(child, Assert.Single(root.Children));
                Assert.Same(button, child.Child);
                Assert.Equal("unchanged", button.Content);
                Assert.NotSame(previousCommand, commandProperty.GetValue(child));
                button.Command!.Execute(null);
                Assert.Equal(expectedMode, modeProperty.GetValue(owner));
                Assert.Same(commandProperty.GetValue(child), button.Command);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CommandChange_AfterDescriptorRebuild_KeepsSingleUpdateNotification()
    {
        var type = Compile(structural: true, conditional: false);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            type.GetMethod("InitializeForTest")!.Invoke(owner, null);
            var child = Assert.IsAssignableFrom<AkburaControl>(Assert.Single(Assert.IsType<StackPanel>(owner.Child).Children));
            var childType = child.GetType();
            childType.GetMethod("InitializeForTest")!.Invoke(child, null);
            var button = Assert.IsType<Button>(child.Child);
            var commandField = childType.GetField("ToggleSidebarProperty")!;
            var originalProperty = commandField.GetValue(null);
            var rebuild = childType.GetMethod("__AkburaHotReloadRebuildDescriptors", BindingFlags.NonPublic | BindingFlags.Static)!;

            rebuild.Invoke(null, null);
            rebuild.Invoke(null, null);
            Assert.Same(originalProperty, commandField.GetValue(null));

            var renderCountProperty = childType.GetProperty("RenderCount")!;
            var previousRenderCount = Assert.IsType<int>(renderCountProperty.GetValue(child));
            var calls = 0;
            var command = Akbura.Commands.AkburaCommandFactory.CreateAction<object>(() => calls++);
            childType.GetProperty("ToggleSidebar")!.SetValue(child, command);

            Assert.Equal(previousRenderCount + 1, renderCountProperty.GetValue(child));
            Assert.Same(button, child.Child);
            Assert.Same(command, button.Command);
            button.Command!.Execute(null);
            Assert.Equal(1, calls);
        }, CancellationToken.None);
    }

    private static Type Compile(bool structural, bool conditional)
    {
        var parentSource =
            """
            using Avalonia.Controls;
            using Demo;

            param double WidthForTest = 1400d;
            var isMobile = WidthForTest < 640d;
            var isCollapsible = WidthForTest >= 1024d && WidthForTest < 1280d;

            void ToggleSidebar()
            {
                LastMode = isMobile ? "mobile" : isCollapsible ? "collapsible" : "desktop";
            }

            <StackPanel>
            """ + (conditional
                ? "$if (WidthForTest > 0d) { <CommandChild Label=\"unchanged\" ToggleSidebar={ToggleSidebar} /> }"
                : "<CommandChild Label=\"unchanged\" ToggleSidebar={ToggleSidebar} />") + "</StackPanel>";
        var fixture = AkcssActivatorPlannerTests.CreateFixture(parentSource, HostSource);
        var childTree = AkburaSyntaxTree.ParseText(
            """
            using Avalonia.Controls;
            namespace Demo;

            param string Label = "unchanged";
            command void ToggleSidebar();

            var caption = RecordRender(Label);

            <Button Content={caption} Command={ToggleSidebar} />
            """,
            "CommandChild.akbura");
        var semanticCompilation = new AkburaCompilation(
            fixture.CSharpCompilation, [fixture.ComponentTree, childTree], rootNamespace: "Demo");
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var generatedTrees = new List<SyntaxTree>();
        foreach (var tree in new[] { fixture.ComponentTree, childTree })
        {
            var model = semanticCompilation.GetSemanticModel(tree);
            Assert.False(tree.GetRoot().ContainsDiagnostics);
            Assert.Empty(model.GetSemanticDiagnostics(tree.GetRoot()));
            var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(model.GetSymbolInfo(tree.GetRoot()).Symbol);
            var source = ComponentDocumentWriter.Generate(
                component, model, tree.FilePath, new Dictionary<AkburaSyntax, string>(),
                mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
            generatedTrees.Add(CSharpSyntaxTree.ParseText(source, options));
        }

        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(generatedTrees)
            .WithAssemblyName("CommandPropagation_" + Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.AsEnumerable()));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.PlannerView"));
    }

    private const string HostSource =
        """
        namespace Demo;

        public partial class PlannerView : Akbura.AkburaControl
        {
            public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public string? LastMode { get; private set; }
            public void InitializeForTest() => base.OnInitialized();
        }

        public partial class CommandChild : Akbura.AkburaControl
        {
            public CommandChild() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public int RenderCount { get; private set; }
            public void InitializeForTest() => base.OnInitialized();

            public string RecordRender(string label)
            {
                RenderCount++;
                return label;
            }
        }
        """;
}
