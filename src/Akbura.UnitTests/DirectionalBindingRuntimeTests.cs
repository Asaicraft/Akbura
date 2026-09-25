using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DirectionalBindingRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentChildBindingWorksEndToEnd(bool structural)
    {
        var assembly = Compile(structural);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var parent = Assert.IsAssignableFrom<AkburaControl>(
                Activator.CreateInstance(
                    assembly.GetType("Demo.ParentView")!));
            parent.GetType().GetMethod("InitializeForTest")!
                .Invoke(parent, null);
            var root = Assert.IsType<StackPanel>(parent.Child);
            var editor = Assert.IsAssignableFrom<AkburaControl>(
                root.Children[0]);
            editor.GetType().GetMethod("InitializeForTest")!
                .Invoke(editor, null);
            var editorRoot = Assert.IsType<StackPanel>(editor.Child);
            var textBox = Assert.IsType<TextBox>(
                editorRoot.Children[0]);
            var submit = Assert.IsType<Button>(
                editorRoot.Children[1]);
            var parentText = Assert.IsType<TextBlock>(
                root.Children[1]);
            var submittedText = Assert.IsType<TextBlock>(
                root.Children[2]);
            var set = Assert.IsType<Button>(root.Children[3]);
            var setSubmitted = Assert.IsType<Button>(
                root.Children[4]);
            var observedTextBox = Assert.IsType<TextBox>(
                root.Children[5]);
            var firstObservedText = Assert.IsType<TextBlock>(
                root.Children[6]);
            var secondObservedText = Assert.IsType<TextBlock>(
                root.Children[7]);

            Assert.Equal("Initial", textBox.Text);
            Assert.Equal("Initial", parentText.Text);
            Assert.Equal("Parent seed", submittedText.Text);

            observedTextBox.Text = "Observed";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Observed", firstObservedText.Text);
            Assert.Equal("Observed", secondObservedText.Text);

            textBox.Text = "Child update";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Child update", parentText.Text);
            Assert.Equal(
                "Child update",
                editor.GetType().GetProperty("Text")!
                    .GetValue(editor));

            set.RaiseEvent(new RoutedEventArgs(
                Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Parent update", parentText.Text);
            Assert.Equal("Parent update", textBox.Text);

            setSubmitted.RaiseEvent(new RoutedEventArgs(
                Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Parent-only", submittedText.Text);
            Assert.Equal(
                string.Empty,
                editor.GetType().GetProperty("Submitted")!
                    .GetValue(editor));

            submit.RaiseEvent(new RoutedEventArgs(
                Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Parent update", submittedText.Text);
            Assert.Equal(
                "Parent update",
                editor.GetType().GetProperty("Submitted")!
                    .GetValue(editor));
            return true;
        }, CancellationToken.None);
    }

    private static Assembly Compile(bool structural)
    {
        const string editorField =
            """
            using Avalonia.Controls;
            namespace Demo;

            param bind string Text = "";
            param out string Submitted = "";

            <StackPanel>
                <TextBox bind:Text={Text} />
                <Button Click={() => { Submitted = Text; }}>Submit</Button>
            </StackPanel>
            """;
        const string parentView =
            """
            using Avalonia.Controls;
            namespace Demo;

            state string text = "Initial";
            state string submitted = "Parent seed";
            state string firstObserved = "";
            state string secondObserved = "";

            <StackPanel>
                <EditorField bind:Text={text} out:Submitted={submitted} />
                <TextBlock Text={text} />
                <TextBlock Text={submitted} />
                <Button Click={() => { text = "Parent update"; }}>Set</Button>
                <Button Click={() => { submitted = "Parent-only"; }}>Set output target</Button>
                <TextBox out:Text={firstObserved} out:Text={secondObserved} />
                <TextBlock Text={firstObserved} />
                <TextBlock Text={secondObserved} />
            </StackPanel>
            """;
        const string host =
            """
            namespace Demo;

            public partial class EditorField : Akbura.AkburaControl
            {
                public EditorField() :
                    base(Akbura.Engine.AkburaEngine.Empty)
                {
                }

                public void InitializeForTest() =>
                    base.OnInitialized();
            }

            public partial class ParentView : Akbura.AkburaControl
            {
                public ParentView() :
                    base(Akbura.Engine.AkburaEngine.Empty)
                {
                }

                public void InitializeForTest() =>
                    base.OnInitialized();
            }
            """;
        var options = CSharpParseOptions.Default.WithLanguageVersion(
            LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = CSharpCompilation.Create(
            "DirectionalBindingRuntime_" +
                Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(host, options)],
            SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions:
                    NullableContextOptions.Enable));
        var editorTree = AkburaSyntaxTree.ParseText(
            editorField,
            "EditorField.akbura");
        var parentTree = AkburaSyntaxTree.ParseText(
            parentView,
            "ParentView.akbura");
        var documents = new[] { editorTree, parentTree };
        var compilation = new AkburaCompilation(
            csharpCompilation,
            documents,
            rootNamespace: "Demo");
        var generated = new List<string>();

        foreach (var tree in documents)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var diagnostics = semanticModel
                .GetSemanticDiagnostics(tree.GetRoot())
                .Where(static diagnostic =>
                    diagnostic.Severity ==
                        AkburaDiagnosticSeverity.Error)
                .ToArray();
            Assert.True(
                diagnostics.Length == 0,
                string.Join(
                    Environment.NewLine,
                    diagnostics.Select(static diagnostic =>
                        diagnostic.Code + ": " +
                        diagnostic.Message)));
            var symbol = Assert.IsAssignableFrom<
                IAkburaComponentSymbol>(
                    semanticModel.GetSymbolInfo(
                        tree.GetRoot()).Symbol);
            generated.Add(ComponentDocumentWriter.Generate(
                symbol,
                semanticModel,
                tree.FilePath,
                new Dictionary<AkburaSyntax, string>(),
                mode: structural
                    ? ComponentGenerationMode.DebugStructural
                    : ComponentGenerationMode.ReleaseDirect)
                .ToString());
        }

        var emittedCompilation = csharpCompilation.AddSyntaxTrees(
            generated.Select(source =>
                CSharpSyntaxTree.ParseText(source, options)));
        var compilationDiagnostics = emittedCompilation
            .GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity is
                    DiagnosticSeverity.Warning or
                    DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            compilationDiagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                compilationDiagnostics.AsEnumerable()) +
            Environment.NewLine +
            string.Join(Environment.NewLine, generated));
        using var output = new MemoryStream();
        var result = emittedCompilation.Emit(output);
        Assert.True(
            result.Success,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.AsEnumerable()));
        return Assembly.Load(output.ToArray());
    }
}
