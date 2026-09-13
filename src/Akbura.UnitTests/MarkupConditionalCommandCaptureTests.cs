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
public sealed class MarkupConditionalCommandCaptureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandCallback_RefreshesPatternCaptureOnRetainedChild(bool structural)
    {
        var type = Compile(structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Activator.CreateInstance(type)!;
            var original = type.GetProperty("Model")!.GetValue(owner)!;
            var root = Assert.IsType<StackPanel>(type.GetMethod("FirstForTest")!.Invoke(owner, null));
            var child = Assert.IsAssignableFrom<AkburaControl>(Assert.Single(root.Children));
            var commandProperty = child.GetType().GetProperty("Notify")!;
            var initial = Assert.IsAssignableFrom<IAkburaCommand>(commandProperty.GetValue(child));
            initial.Execute("first-command").GetAwaiter().GetResult();
            Assert.Equal("first-command", original.GetType().GetProperty("Name")!.GetValue(original));

            var changed = Activator.CreateInstance(type.Assembly.GetType("Demo.Person")!, "second")!;
            type.GetProperty("Model")!.SetValue(owner, changed);
            Assert.Same(root, type.GetMethod("UpdateForTest")!.Invoke(owner, null));
            Assert.Same(child, Assert.Single(root.Children));
            var current = Assert.IsAssignableFrom<IAkburaCommand>(commandProperty.GetValue(child));
            current.Execute("second-command").GetAwaiter().GetResult();
            Assert.Equal("second-command", changed.GetType().GetProperty("Name")!.GetValue(changed));
            Assert.Equal("first-command", original.GetType().GetProperty("Name")!.GetValue(original));
        }, CancellationToken.None);
    }

    private static Type Compile(bool structural)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            """
            using Avalonia.Controls;
            using Demo;
            <StackPanel>
                $if (Model is Person person)
                {
                    <CommandChild Notify={value => { person.Name = value; }} />
                }
            </StackPanel>
            """, HostSource);
        var child = AkburaSyntaxTree.ParseText(
            "using Avalonia.Controls; namespace Demo; command void Notify(string value); <Button />",
            "CommandChild.akbura");
        var semanticCompilation = new AkburaCompilation(fixture.CSharpCompilation,
            [fixture.ComponentTree, child], rootNamespace: "Demo");
        var generated = new List<string>();
        foreach (var tree in new[] { fixture.ComponentTree, child })
        {
            var model = semanticCompilation.GetSemanticModel(tree);
            Assert.Empty(model.GetSemanticDiagnostics(tree.GetRoot()));
            var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(model.GetSymbolInfo(tree.GetRoot()).Symbol);
            generated.Add(ComponentDocumentWriter.Generate(component, model, tree.FilePath,
                new Dictionary<AkburaSyntax, string>(),
                mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect).ToString());
        }

        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(generated.Select(source => CSharpSyntaxTree.ParseText(source, options)))
            .WithAssemblyName("ConditionalCommand_" + Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.AsEnumerable()) +
            Environment.NewLine + string.Join(Environment.NewLine, generated));
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
            public object Model { get; set; } = new Person("first");
            public Avalonia.Controls.Control FirstForTest() => FirstUpdate();
            public Avalonia.Controls.Control UpdateForTest() => Update();
        }
        public partial class CommandChild : Akbura.AkburaControl
        {
            public CommandChild() : base(Akbura.Engine.AkburaEngine.Empty) { }
        }
        public sealed class Person
        {
            public Person(string name) { Name = name; }
            public string Name { get; set; }
        }
        """;
}
