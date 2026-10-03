using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComponentDocumentWriterTests
{
    [Fact]
    public void Generate_EventHandlerWithoutRenderCapture_KeepsStableSubscription()
    {
        const string componentSource =
            """
            using Avalonia.Controls;

            state int count = 0;

            void Increment()
            {
                count++;
            }

            <Button Click={() => Increment()} />
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(componentSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var generated = ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());

        Assert.DoesNotContain("__akburaEventHandler", generated.ToString());
        Assert.DoesNotContain("__akburaNextEventHandler", generated.ToString());
        var tree = CSharpSyntaxTree.ParseText(
            generated,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        Assert.DoesNotContain(
            fixture.CSharpCompilation.AddSyntaxTrees(tree).GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GeneratedCallback_UsesCurrentRenderLocalAfterUpdate(
        bool structuralRuntime,
        bool commandBinding)
    {
        var componentSource =
            """
            using Avalonia.Controls;

            param int Value = 11;
            var current = Value;

            void Hi()
            {
                LastValue = current;
            }

            """ + (commandBinding
                ? "<Button Command={Hi} />"
                : "<Button Click={() => Hi()} />");
        const string ownerSource =
            """
            using Akbura;
            using Akbura.Engine;
            using Avalonia.Controls;

            namespace Demo;

            public partial class PlannerView : AkburaControl
            {
                public PlannerView() : base(AkburaEngine.Empty) { }

                public int LastValue { get; private set; }

                public void InitializeForTest() => base.OnInitialized();

                public void RenderAgain() => Update();
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(componentSource, ownerSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var generated = ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>(),
            mode: structuralRuntime
                ? ComponentGenerationMode.DebugStructural
                : ComponentGenerationMode.ReleaseDirect);
        Assert.DoesNotContain("private int current", generated.ToString());
        var tree = CSharpSyntaxTree.ParseText(
            generated,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(tree)
            .WithAssemblyName("RenderClosure_" + Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var type = assembly.GetType("Demo.PlannerView");
        Assert.NotNull(type);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var instance = Assert.IsAssignableFrom<Akbura.AkburaControl>(Activator.CreateInstance(type));
            type.GetMethod("InitializeForTest")!.Invoke(instance, null);
            var button = Assert.IsType<Avalonia.Controls.Button>(instance.Child);
            InvokeCallback(button, commandBinding);
            Assert.Equal(11, type.GetProperty("LastValue")!.GetValue(instance));

            type.GetProperty("Value")!.SetValue(instance, 42);
            type.GetMethod("RenderAgain")!.Invoke(instance, null);
            InvokeCallback(button, commandBinding);
            Assert.Equal(42, type.GetProperty("LastValue")!.GetValue(instance));
        }, CancellationToken.None);
    }

    private static void InvokeCallback(Avalonia.Controls.Button button, bool commandBinding)
    {
        if (commandBinding)
        {
            Assert.NotNull(button.Command);
            button.Command.Execute(null);
        }
        else
        {
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
                Avalonia.Controls.Button.ClickEvent));
        }
    }

    [Fact]
    public void Generate_UseEffectLambda_CapturesRenderLocal()
    {
        const string componentSource =
            """
            using Akbura.Hooks;
            using Avalonia.Controls;

            state double width = useTopLevelWidth();
            state bool isMobileExpanded = false;
            var isMobile = width < 640d;

            useEffect(() =>
            {
                if (!isMobile)
                    isMobileExpanded = false;
            }, [isMobile]);

            <Button />
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(componentSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());
        Assert.True(diagnostics.IsEmpty,
            string.Join(Environment.NewLine, diagnostics.Select(
                static diagnostic => diagnostic.Code + ": " + diagnostic.Message)));

        var generated = ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());
        var tree = CSharpSyntaxTree.ParseText(
            generated,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var compileErrors = fixture.CSharpCompilation.AddSyntaxTrees(tree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(compileErrors.Length == 0,
            string.Join(Environment.NewLine, compileErrors.Select(static diagnostic => diagnostic.ToString())));
    }

    [Fact]
    public void Generate_TopLevelWidthHookAndDerivedLocal_AreVisibleToUserMethod()
    {
        const string componentSource =
            """
            using Akbura.Hooks;
            using Avalonia.Controls;

            state bool isExpanded = false;
            state double width = useTopLevelWidth();

            var isMobile = width < 640d;
            var isCollapsible = width >= 640d && width < 1280d;
            var isDesktop = width >= 1280d;

            void ToggleSidebar()
            {
                if (isMobile || isCollapsible)
                {
                    isExpanded = !isExpanded;
                }
            }

            <Button Click={() => ToggleSidebar()} />
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(componentSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var semanticDiagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());
        Assert.True(semanticDiagnostics.IsEmpty,
            string.Join(Environment.NewLine, semanticDiagnostics.Select(
                static diagnostic => diagnostic.Code + ": " + diagnostic.Message)));

        var generated = ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());
        var generatedText = generated.ToString();
        Assert.DoesNotContain("private bool isMobile", generatedText);
        Assert.DoesNotContain("private bool isCollapsible", generatedText);
        Assert.DoesNotContain("private bool isDesktop", generatedText);
        Assert.Equal(2, generatedText.Split("width < 640d").Length - 1);
        Assert.Equal(2, generatedText.Split("void ToggleSidebar()").Length - 1);
        var tree = CSharpSyntaxTree.ParseText(
            generated,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var diagnostics = fixture.CSharpCompilation.AddSyntaxTrees(tree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generated);
    }

    [Fact]
    public void Generate_TemplateBindingOnAttachedProperty_BindsAvaloniaProperty()
    {
        const string componentSource =
            """
            using Avalonia.Controls;
            using Avalonia.Controls.Documents;
            using Avalonia.Controls.Presenters;
            using Avalonia.Markup.Xaml.Templates;
            using Avalonia.Styling;

            <TextBox>
                <TextBox.Theme>
                    <ControlTheme TargetType="TextBox">
                        <Setter Property="Template">
                            <ControlTemplate>
                                <TextPresenter TextElement.Foreground=${TemplateBinding Foreground} />
                            </ControlTemplate>
                        </Setter>
                    </ControlTheme>
                </TextBox.Theme>
            </TextBox>
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(componentSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var semanticDiagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());
        Assert.True(
            semanticDiagnostics.IsEmpty,
            string.Join(
                Environment.NewLine,
                semanticDiagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        var generatedText = ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>(),
            CancellationToken.None);
        var generatedSource = generatedText.ToString();

        Assert.Contains(
            ".Bind(global::Avalonia.Controls.Documents.TextElement.ForegroundProperty,",
            generatedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TextElement.SetForeground(", generatedSource, StringComparison.Ordinal);

        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedText,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var diagnostics = fixture.CSharpCompilation.AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    [Fact]
    public void Generate_WritesCompleteCompilableDocument()
    {
        const string componentSource =
            """
            using Avalonia.Controls;

            param string Title = "Hello";

            <Border Width="42" />
            """;

        const string csharpSource =
            """
            using Akbura;
            using Akbura.Engine;

            namespace Demo;

            public partial class PlannerView : AkburaControl
            {
                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }
            }
            """;

        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            componentSource,
            csharpSource);

        var component = Assert.IsType<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol, exactMatch: false);

        var generatedText = ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>(),
            CancellationToken.None);

        var generatedSource = generatedText.ToString();

        Assert.Contains(
            "// <auto-generated />",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "#nullable enable",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "using Avalonia.Controls;",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "namespace Demo",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "public partial class PlannerView : global::Akbura.AkburaControl",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "TitleProperty",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "private global::Avalonia.Controls.Border __element0 = null!;",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "protected override global::Avalonia.Controls.Control FirstUpdate()",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "protected override global::Avalonia.Controls.Control Update()",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetParameters()",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetCommands()",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetServices()",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetStates()",
            generatedSource,
            StringComparison.Ordinal);

        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedText,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: ComponentDocumentWriter.GetHintName(
                component,
                "Views/PlannerView.akbura"));

        var diagnostics = fixture.CSharpCompilation
            .AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity is
                    DiagnosticSeverity.Warning or
                    DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            generatedSource);
    }

    [Fact]
    public void Generate_DebugWritesCompleteStructuralHotReloadContract()
    {
        const string componentSource =
            """
            using Avalonia.Controls;
            using System.Collections.Generic;

            param string Title = "Hello";
            param IList<Control> Content;
            inject IClock clock;
            command void Save(string value);
            state int count = 1;

            <Border>
                <TextBlock Text="Before" />
            </Border>
            """;
        const string csharpSource =
            """
            namespace Demo;

            public interface IClock
            {
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            componentSource,
            csharpSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);
        var generatedText = ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>(),
            CancellationToken.None);
        var generatedSource = generatedText.ToString();

        Assert.Contains(
            "private static string s_akburaAppliedDescriptorShape =",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static string s_akburaAppliedStateShape =",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "s_akburaHotReloadProperties",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"param:Title:System.String:styled:normal\"",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"service:clock:Demo.IClock:direct\"",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"command:Save:System.String:System.Void\"",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.ComponentTree.Parameter.RecreateForHotReload",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.ComponentTree.Parameter." +
            "RecreateCollectionForHotReload",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.ComponentTree.InjectService.RecreateForHotReload",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.HotReload.AkburaHotReloadRuntime.FindProperty<",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.HotReload.AkburaHotReloadRuntime." +
            "BeginPropertyUpdate(",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "__component.__states = default;",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "private void __AkburaHotReloadUpdateInitialValues()",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static void __AkburaHotReloadPrepare(",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "__component.__AkburaHotReloadUpdateInitialValues();",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.HotReload.AkburaHotReloadRuntime.Refresh<",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "__AkburaHotReloadPrepare);",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "internal static void __AkburaHotReloadApply()",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static global::Akbura.ComponentTree.Parameter<",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static readonly global::Akbura.ComponentTree.Parameter<",
            generatedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "__state0",
            generatedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "__service0",
            generatedSource,
            StringComparison.Ordinal);

        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedText,
            CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithPreprocessorSymbols("DEBUG"),
            path: ComponentDocumentWriter.GetHintName(
                component,
                "Views/PlannerView.akbura"));
        GeneratedCodeAssertions.AssertDoubleUnderscoreMethodsAreHidden(
            generatedSource);
        var diagnostics = fixture.CSharpCompilation
            .AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity is
                    DiagnosticSeverity.Warning or
                    DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            generatedSource);
    }

    [Fact]
    public void GetHintName_IsStableAndDistinguishesSourcePaths()
    {
        const string componentSource =
            """
            using Avalonia.Controls;

            <Border />
            """;

        var fixture = AkcssActivatorPlannerTests.CreateFixture(componentSource);

        var component = Assert.IsType<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol, exactMatch: false);

        var first = ComponentDocumentWriter.GetHintName(
            component,
            "Views/PlannerView.akbura");

        var second = ComponentDocumentWriter.GetHintName(
            component,
            "Views/PlannerView.akbura");

        var other = ComponentDocumentWriter.GetHintName(
            component,
            "Controls/PlannerView.akbura");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.EndsWith(".g.cs", first, StringComparison.Ordinal);
        Assert.DoesNotContain("/", first, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", first, StringComparison.Ordinal);
    }
}
