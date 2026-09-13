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
public sealed class MarkupCommandBindingRuntimeTests
{
    [Theory]
    [InlineData(false, "void", "value => { person.Count = value; }", 7, -1)]
    [InlineData(true, "void", "value => { person.Count = value; }", 7, -1)]
    [InlineData(false, "int", "value => person.Count += value", 7, 7)]
    [InlineData(true, "int", "value => person.Count += value", 7, 7)]
    [InlineData(false, "void", "() => { person.Count++; }", 1, -1)]
    [InlineData(true, "void", "() => { person.Count++; }", 1, -1)]
    [InlineData(false, "int", "() => ++person.Count", 1, 1)]
    [InlineData(true, "int", "() => ++person.Count", 1, 1)]
    [InlineData(false, "void", "async value => { await Task.Yield(); person.Count = value; }", 7, -1)]
    [InlineData(true, "void", "async value => { await Task.Yield(); person.Count = value; }", 7, -1)]
    [InlineData(false, "int", "async value => { await Task.Yield(); person.Count = value; return value; }", 7, 7)]
    [InlineData(true, "int", "async value => { await Task.Yield(); person.Count = value; return value; }", 7, 7)]
    [InlineData(false, "int", "person.Command", 7, 7)]
    [InlineData(true, "int", "person.Command", 7, 7)]
    [InlineData(false, "int", "person.TaskCallback", 7, 7)]
    [InlineData(true, "int", "person.TaskCallback", 7, 7)]
    [InlineData(false, "int", "person.ValueTaskCallback", 7, 7)]
    [InlineData(true, "int", "person.ValueTaskCallback", 7, 7)]
    [InlineData(false, "int", "person.Fetch", 7, 7)]
    [InlineData(true, "int", "person.Fetch", 7, 7)]
    [InlineData(false, "int", "person.FetchValue", 7, 7)]
    [InlineData(true, "int", "person.FetchValue", 7, 7)]
    [InlineData(false, "void", "person.NotifyTask", 7, -1)]
    [InlineData(true, "void", "person.NotifyTask", 7, -1)]
    [InlineData(false, "void", "person.NotifyValueTask", 7, -1)]
    [InlineData(true, "void", "person.NotifyValueTask", 7, -1)]
    public async Task NativeCommandBinding_PreservesHandlerModesAndRefreshesCurrentPatternScope(
        bool structural, string resultType, string handler, int count, int result)
    {
        var type = Compile(structural, resultType, handler);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(async () =>
        {
            var owner = Activator.CreateInstance(type)!;
            var original = type.GetProperty("Model")!.GetValue(owner)!;
            var root = Assert.IsType<StackPanel>(type.GetMethod("FirstForTest")!.Invoke(owner, null));
            var child = Assert.IsAssignableFrom<AkburaControl>(Assert.Single(root.Children));
            var property = child.GetType().GetProperty("Run")!;
            var initial = Assert.IsAssignableFrom<IAkburaCommand>(property.GetValue(child));
            AssertResult(result, await initial.Execute(7));
            Assert.Equal(count, original.GetType().GetProperty("Count")!.GetValue(original));

            var changed = Activator.CreateInstance(type.Assembly.GetType("Demo.Person")!)!;
            type.GetProperty("Model")!.SetValue(owner, changed);
            Assert.Same(root, type.GetMethod("UpdateForTest")!.Invoke(owner, null));
            Assert.Same(child, Assert.Single(root.Children));
            var current = Assert.IsAssignableFrom<IAkburaCommand>(property.GetValue(child));
            AssertResult(result, await current.Execute(7));
            Assert.Equal(count, changed.GetType().GetProperty("Count")!.GetValue(changed));
            Assert.Equal(count, original.GetType().GetProperty("Count")!.GetValue(original));
            if (handler == "person.Command")
            {
                Assert.Same(changed.GetType().GetProperty("Command")!.GetValue(changed), current);
            }
        }, CancellationToken.None);
    }

    private static void AssertResult(int expected, object? actual)
    {
        if (expected < 0)
        {
            Assert.Null(actual);
        }
        else
        {
            Assert.Equal(expected, Assert.IsType<int>(actual));
        }
    }

    private static Type Compile(bool structural, string resultType, string handler)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using System.Threading.Tasks; using Demo; " +
            "<StackPanel>$if (Model is Person person) { <CommandChild Run={" + handler + "} /> }</StackPanel>",
            HostSource);
        var child = AkburaSyntaxTree.ParseText(
            "using Avalonia.Controls; namespace Demo; command " + resultType + " Run(int value); <Button />",
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

        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(generated.Select(source =>
            CSharpSyntaxTree.ParseText(source, options))).WithAssemblyName("CommandBinding_" + Guid.NewGuid().ToString("N"));
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
            public object Model { get; set; } = new Person();
            public Avalonia.Controls.Control FirstForTest() => FirstUpdate();
            public Avalonia.Controls.Control UpdateForTest() => Update();
        }
        public partial class CommandChild : Akbura.AkburaControl
        {
            public CommandChild() : base(Akbura.Engine.AkburaEngine.Empty) { }
        }
        public sealed class Person
        {
            public Person()
            {
                Command = Akbura.Commands.AkburaCommandFactory.CreateFunction<int, int>(value => Count = value);
            }
            public int Count { get; set; }
            public Akbura.IAkburaCommand<int, int> Command { get; }
            public System.Func<int, System.Threading.Tasks.Task<int>> TaskCallback => Fetch;
            public System.Func<int, System.Threading.Tasks.ValueTask<int>> ValueTaskCallback => FetchValue;
            public System.Threading.Tasks.Task<int> Fetch(int value) =>
                System.Threading.Tasks.Task.FromResult(Count = value);
            public System.Threading.Tasks.ValueTask<int> FetchValue(int value) => new(Count = value);
            public System.Threading.Tasks.Task NotifyTask(int value)
            {
                Count = value;
                return System.Threading.Tasks.Task.CompletedTask;
            }
            public System.Threading.Tasks.ValueTask NotifyValueTask(int value)
            {
                Count = value;
                return System.Threading.Tasks.ValueTask.CompletedTask;
            }
        }
        """;
}
