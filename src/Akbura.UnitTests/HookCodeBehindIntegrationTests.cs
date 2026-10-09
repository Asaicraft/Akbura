using System.Reflection;
using Akbura.BlackSilence;
using Akbura.ComponentTree;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class HookCodeBehindIntegrationTests
{
    [Theory]
    [InlineData("useAvaloniaProperty(Width)", false)]
    [InlineData("useAvaloniaProperty(Width)", true)]
    [InlineData("useAvaloniaProperty(this, WidthProperty)", false)]
    [InlineData("useAvaloniaProperty(this, WidthProperty)", true)]
    [InlineData("AvaloniaPropertyHooks.useAvaloniaProperty(this, WidthProperty)", false)]
    [InlineData("AvaloniaPropertyHooks.useAvaloniaProperty(this, WidthProperty)", true)]
    public async Task AvaloniaPropertyHook_AllInvocationFormsTrackComponentWidth(string invocation, bool debugStructural)
    {
        // .akbura: state double selfWidth = useAvaloniaProperty(Width);
        var source =
            "using Akbura.Hooks;\r\n" +
            "using Avalonia.Controls;\r\n" +
            $"state double selfWidth = {invocation};\r\n" +
            "<TextBlock Text={$\"Current component Width is {selfWidth}\"}/>";
        const string ownerSource = """
            namespace Demo;

            public partial class MyComponent
            {
                public MyComponent() : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                    Width = 200;
                }
            }
            """;
        var ownerType = Compile(source, ownerSource, debugStructural);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                var state = Assert.IsType<State<double>>(Assert.Single(owner.GetDiagnosticStates()));
                Assert.Equal(200, state.Value);
                Assert.Equal("Current component Width is 200", text.Text);

                owner.Width = 320;

                Assert.Equal(320, state.Value);
                Assert.Same(text, owner.Child);
                Assert.Equal("Current component Width is 320", text.Text);

                state.Value = 480;

                Assert.Equal(480, owner.Width);
                Assert.Equal("Current component Width is 480", text.Text);

                owner.InvalidState();

                Assert.Same(state, Assert.Single(owner.GetDiagnosticStates()));
                Assert.Same(text, owner.Child);
                Assert.Equal("Current component Width is 480", text.Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialComponent_PrivateFieldAndNamedControlWorkDuringInitialization(bool debugStructural)
    {
        // MyComponent.akbura: <TextBlock x.Name="text" Width={_myField}/>
        const string source = """
            using Avalonia.Controls;

            <TextBlock x.Name="text" Width={_myField}/>
            """;
        const string ownerSource = """
            namespace Demo;

            public partial class MyComponent
            {
                private int _myField = 200;

                public MyComponent() : base(global::Akbura.Engine.AkburaEngine.Empty) { }

                protected override void OnInitialized()
                {
                    base.OnInitialized();
                    text.Text = "Hello from class";
                }

                public void SetWidthForTest(int value)
                {
                    _myField = value;
                    InvalidState();
                }
            }
            """;
        var ownerType = Compile(source, ownerSource, debugStructural);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(owner.Child);
                Assert.Equal(200, text.Width);
                Assert.Equal("Hello from class", text.Text);

                ownerType.GetMethod("SetWidthForTest")!.Invoke(owner, [320]);

                Assert.Same(text, owner.Child);
                Assert.Equal(320, text.Width);
                Assert.Equal("Hello from class", text.Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Type Compile(string source, string ownerSource, bool debugStructural)
    {
        var projectDirectory = Path.Combine(Path.GetTempPath(), nameof(HookCodeBehindIntegrationTests));
        var file = new AkburaBlackSilenceGeneratorTests.TestAdditionalText(
            Path.Combine(projectDirectory, "MyComponent.akbura"), SourceText.From(source));
        var options = new AkburaBlackSilenceGeneratorTests.TestAnalyzerConfigOptionsProvider("Demo", projectDirectory);
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (debugStructural)
        {
            parseOptions = parseOptions.WithPreprocessorSymbols("DEBUG");
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: [file],
            parseOptions: parseOptions,
            optionsProvider: options);
        var compilation = AkburaBlackSilenceGeneratorTests.CreateCompilation(ownerSource)
            .WithAssemblyName(nameof(HookCodeBehindIntegrationTests) + "_" + Guid.NewGuid().ToString("N"));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var generatedCompilation, out var diagnostics);

        Assert.DoesNotContain(diagnostics,
            static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);
        AkburaBlackSilenceGeneratorTests.AssertGeneratedCompilation(driver, generatedCompilation);

        using var output = new MemoryStream();
        var result = generatedCompilation.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.MyComponent"));
    }
}
