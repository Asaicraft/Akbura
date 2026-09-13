using System.Reflection;
using System.Runtime.ExceptionServices;
using Akbura.ComponentTree;
using Akbura.Diagnostics;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComposableHookIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DebounceDsl_CompilesWithLiveStateArguments(bool debugStructural)
    {
        const string component =
            """
            using System;
            using Avalonia.Controls;
            using Akbura.Hooks;

            state int count = 0;
            state int debouncedCount = useDebounce(count, 300);
            state int debouncedEffectedCount = useDebounce(count, x => x + 1, 300);
            state string query = "";
            state string debouncedQuery = "";

            useEffect(() => { }, [count]);

            useDebounce(
                () => { debouncedQuery = query; },
                TimeSpan.FromMilliseconds(350),
                [query]);

            <TextBlock Text={debouncedCount.ToString()} />
            """;
        const string owner =
            """
            using Akbura;
            using Akbura.Engine;

            namespace Demo;

            public partial class PlannerView : AkburaControl
            {
                public PlannerView() : base(AkburaEngine.Empty) { }
            }
            """;

        _ = Compile(component, owner, debugStructural, out var generatedSource);
        Assert.Contains("global::Akbura.Hooks.DebounceHooks.useDebounce", generatedSource);
        Assert.Contains("__State_count", generatedSource);
        Assert.DoesNotContain("count.Source", generatedSource);
        Assert.DoesNotContain("count.State", generatedSource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedHooks_RunEveryFrameWithStableAliasesAndInitializedUi(bool debugStructural)
    {
        var ownerType = CompileRuntimeComponent(debugStructural);
        await OnDispatcher(() =>
        {
            var owner = CreateOwner(ownerType);
            Initialize(owner);

            Assert.Equal("11:21", Assert.IsType<TextBlock>(owner.Child).Text);
            Assert.Equal(1, Get<int>(owner, "OrdinaryInitializations"));
            Assert.Equal(2, Get<int>(owner, "HookCalls"));
            Assert.Equal(2, Get<int>(owner, "EffectRuns"));
            Assert.True(Get<bool>(owner, "EffectsSawAttachedStates"));

            var states = owner.GetDiagnosticStates();
            Assert.Equal(3, states.Length);
            Assert.Same(states[1], Get<State<int>>(owner, "PreparedFast"));
            Assert.Same(states[2], Get<State<int>>(owner, "PreparedSlow"));
            Assert.NotSame(states[1], states[2]);
            Assert.True(states == owner.GetDiagnosticStates());
            Assert.Equal("count", owner.GetDiagnosticStateName(0));
            Assert.Equal("fast", owner.GetDiagnosticStateName(1));
            Assert.Equal("slow", owner.GetDiagnosticStateName(2));

            owner.InvalidState();
            Assert.Equal(4, Get<int>(owner, "HookCalls"));
            Assert.Equal(2, Get<int>(owner, "EffectRuns"));
            Assert.Equal(1, Get<int>(owner, "OrdinaryInitializations"));
            Assert.True(states == owner.GetDiagnosticStates());
            Assert.Same(states[1], Get<State<int>>(owner, "PreparedFast"));
            Assert.Same(states[2], Get<State<int>>(owner, "PreparedSlow"));

            Assert.IsType<State<int>>(states[0]).Value = 5;
            Assert.Equal("15:25", Assert.IsType<TextBlock>(owner.Child).Text);
            Assert.Equal(15, Get<int>(owner, "FastForTest"));
            Assert.Equal(25, Get<int>(owner, "SlowForTest"));
            Assert.Equal(4, Get<int>(owner, "EffectRuns"));
            Assert.Equal(1, Get<int>(owner, "OrdinaryInitializations"));
            Assert.True(states == owner.GetDiagnosticStates());
            Assert.True(Get<bool>(owner, "EffectsSawAttachedStates"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFirstFrame_DiscardsProvisionalAliasesAndCanRetry(bool debugStructural)
    {
        var ownerType = CompileRuntimeComponent(debugStructural);
        await OnDispatcher(() =>
        {
            var owner = CreateOwner(ownerType);
            Set(owner, "ThrowFromRender", true);

            var exception = Assert.Throws<InvalidOperationException>(() => Initialize(owner));
            Assert.Equal("render failed", exception.Message);
            Assert.Equal(0, Get<int>(owner, "EffectRuns"));
            var provisionalFast = Get<State<int>>(owner, "PreparedFast");
            var provisionalSlow = Get<State<int>>(owner, "PreparedSlow");
            Assert.False(provisionalFast.IsAttached);
            Assert.False(provisionalSlow.IsAttached);

            var unavailable = Assert.Throws<TargetInvocationException>(
                () => Get<int>(owner, "FastForTest"));
            Assert.IsType<InvalidOperationException>(unavailable.InnerException);

            Set(owner, "ThrowFromRender", false);
            Initialize(owner);

            var states = owner.GetDiagnosticStates();
            Assert.NotSame(provisionalFast, states[1]);
            Assert.NotSame(provisionalSlow, states[2]);
            Assert.True(states[1].IsAttached);
            Assert.True(states[2].IsAttached);
            Assert.Equal("11:21", Assert.IsType<TextBlock>(owner.Child).Text);
            Assert.Equal(2, Get<int>(owner, "EffectRuns"));
            Assert.Equal(1, Get<int>(owner, "OrdinaryInitializations"));
            Assert.Same(states[1], Get<State<int>>(owner, "PreparedFast"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedLaterFrame_PreservesCommittedAliasesAndEffectRun(bool debugStructural)
    {
        var ownerType = CompileRuntimeComponent(debugStructural);
        await OnDispatcher(() =>
        {
            var owner = CreateOwner(ownerType);
            Initialize(owner);
            var states = owner.GetDiagnosticStates();
            var previousToken = Get<CancellationToken>(owner, "FastToken");
            Set(owner, "ThrowFromRender", true);

            var exception = Assert.Throws<InvalidOperationException>(
                () => Assert.IsType<State<int>>(states[0]).Value = 5);
            Assert.Equal("render failed", exception.Message);
            Assert.True(states == owner.GetDiagnosticStates());
            Assert.Same(states[1], Get<State<int>>(owner, "PreparedFast"));
            Assert.Same(states[2], Get<State<int>>(owner, "PreparedSlow"));
            Assert.False(previousToken.IsCancellationRequested);
            Assert.Equal(2, Get<int>(owner, "EffectRuns"));
            Assert.Equal(11, Get<int>(owner, "FastForTest"));
            Assert.Equal(21, Get<int>(owner, "SlowForTest"));

            Set(owner, "ThrowFromRender", false);
            owner.InvalidState();

            Assert.True(previousToken.IsCancellationRequested);
            Assert.Equal(4, Get<int>(owner, "EffectRuns"));
            Assert.True(states == owner.GetDiagnosticStates());
            Assert.Equal("15:25", Assert.IsType<TextBlock>(owner.Child).Text);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HotReload_RebindsHookAliasesAndPreservesOrdinaryState(bool debugStructural)
    {
        var ownerType = CompileRuntimeComponent(debugStructural);
        await OnDispatcher(() =>
        {
            var owner = CreateOwner(ownerType);
            Initialize(owner);
            var before = owner.GetDiagnosticStates();
            Assert.IsType<State<int>>(before[0]).Value = 5;
            Assert.IsType<State<int>>(before[1]).Value = 777;
            Assert.Equal(777, Get<int>(owner, "FastForTest"));

            owner.ApplyHotReload(static _ => { });

            var after = owner.GetDiagnosticStates();
            Assert.Same(before[0], after[0]);
            Assert.NotSame(before[1], after[1]);
            Assert.NotSame(before[2], after[2]);
            Assert.False(before[1].IsAttached);
            Assert.False(before[2].IsAttached);
            Assert.True(after[1].IsAttached);
            Assert.True(after[2].IsAttached);
            Assert.Same(after[1], Get<State<int>>(owner, "PreparedFast"));
            Assert.Same(after[2], Get<State<int>>(owner, "PreparedSlow"));
            Assert.Equal(15, Get<int>(owner, "FastForTest"));
            Assert.Equal(25, Get<int>(owner, "SlowForTest"));
            Assert.Equal("15:25", Assert.IsType<TextBlock>(owner.Child).Text);
            Assert.Equal(1, Get<int>(owner, "OrdinaryInitializations"));
            Assert.True(after == owner.GetDiagnosticStates());
            Assert.Equal("fast", owner.GetDiagnosticStateName(1));
            Assert.Equal("slow", owner.GetDiagnosticStateName(2));

            var hookCalls = Get<int>(owner, "HookCalls");
            Assert.IsType<State<int>>(before[1]).Value = 999;
            Assert.Equal(hookCalls, Get<int>(owner, "HookCalls"));
            Assert.Equal(15, Get<int>(owner, "FastForTest"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HookResultAsSource_PropagatesThroughTheGeneratedStateChain(bool debugStructural)
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            state int count = 1;
            state int first = useCombined(count, 10, (value, offset) => value + offset);
            state int second = useCombined(first, 20, (value, offset) => value + offset);

            <TextBlock Text={second.ToString()} />
            """;
        var ownerType = Compile(component, GenericOwnerSource, debugStructural, out _);
        await OnDispatcher(() =>
        {
            var owner = CreateOwner(ownerType);
            Initialize(owner);
            var states = owner.GetDiagnosticStates();
            Assert.Equal(11, Assert.IsType<State<int>>(states[1]).Value);
            Assert.Equal(31, Assert.IsType<State<int>>(states[2]).Value);
            Assert.Equal("31", Assert.IsType<TextBlock>(owner.Child).Text);

            Assert.IsType<State<int>>(states[0]).Value = 5;

            Assert.Equal(15, Assert.IsType<State<int>>(states[1]).Value);
            Assert.Equal(35, Assert.IsType<State<int>>(states[2]).Value);
            Assert.Equal("35", Assert.IsType<TextBlock>(owner.Child).Text);
            Assert.True(states == owner.GetDiagnosticStates());
            Assert.NotSame(states[1], states[2]);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedGenericArguments_PreserveNullableStateAndValueOccurrences(bool debugStructural)
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            state string? text = null;
            state string rendered = useCombined(
                selector: (source, snapshot) => (source ?? "none") + ":" + (snapshot ?? "none"),
                value: text,
                state: text);

            <TextBlock Text={rendered} />
            """;
        var ownerType = Compile(component, GenericOwnerSource, debugStructural, out _);
        await OnDispatcher(() =>
        {
            var owner = CreateOwner(ownerType);
            Initialize(owner);
            var states = owner.GetDiagnosticStates();
            Assert.Equal("none:none", Assert.IsType<State<string>>(states[1]).Value);
            Assert.Equal("none:none", Assert.IsType<TextBlock>(owner.Child).Text);

            Assert.IsType<State<string?>>(states[0]).Value = "ready";
            Assert.Equal("ready:ready", Assert.IsType<State<string>>(states[1]).Value);
            Assert.Equal("ready:ready", Assert.IsType<TextBlock>(owner.Child).Text);
            Assert.True(states == owner.GetDiagnosticStates());

            Assert.IsType<State<string?>>(states[0]).Value = null;
            Assert.Equal("none:none", Assert.IsType<State<string>>(states[1]).Value);
            Assert.Equal("none:none", Assert.IsType<TextBlock>(owner.Child).Text);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Diagnostics_HotReloadRebindsEditorsAndSubscriptionsToTheNewStates(bool debugStructural)
    {
        var ownerType = CompileRuntimeComponent(debugStructural);
        await OnDispatcher(() =>
        {
            var owner = CreateOwner(ownerType);
            var applicationWindow = new Window { Content = owner };
            var diagnosticsWindow = new DiagnosticsWindow();
            applicationWindow.Show();
            diagnosticsWindow.Show();
            try
            {
                var diagnostics = Assert.IsType<DiagnosticsRoot>(diagnosticsWindow.Content);
                Assert.Same(owner, diagnostics.SelectedComponent);
                var previousFast = Assert.IsType<State<int>>(owner.GetDiagnosticStates()[1]);
                previousFast.Value = 777;
                var beforeReset = diagnostics.DetailRenderVersion;

                owner.ApplyHotReload(static _ => { });

                var currentFast = Assert.IsType<State<int>>(owner.GetDiagnosticStates()[1]);
                Assert.NotSame(previousFast, currentFast);
                Assert.True(diagnostics.DetailRenderVersion > beforeReset);
                Assert.Equal(11, FindStateEditor(diagnosticsWindow, "fast").Value);

                FindStateEditor(diagnosticsWindow, "fast").CommitValue(42);
                Assert.Equal(42, currentFast.Value);
                Assert.Equal(777, previousFast.Value);

                var beforeChange = diagnostics.DetailRenderVersion;
                currentFast.Value = 43;
                Assert.True(diagnostics.DetailRenderVersion > beforeChange);
                Assert.Equal(43, FindStateEditor(diagnosticsWindow, "fast").Value);

                // A released state can become active under a different owner. It
                // must no longer notify the inspector of the original component.
                var differentOwner = CreateOwner(ownerType);
                ownerType.GetMethod("AdoptStateForTest")!.Invoke(differentOwner, [previousFast]);
                var beforeOldChange = diagnostics.DetailRenderVersion;
                previousFast.Value = 999;
                Assert.Equal(beforeOldChange, diagnostics.DetailRenderVersion);
                Assert.Equal(43, currentFast.Value);

                diagnosticsWindow.Close();
                var afterClose = diagnostics.DetailRenderVersion;
                currentFast.Value = 44;
                Assert.Equal(afterClose, diagnostics.DetailRenderVersion);
            }
            finally
            {
                diagnosticsWindow.Close();
                applicationWindow.Close();
            }
        });
    }

    private static DiagnosticInput FindStateEditor(DiagnosticsWindow window, string name) =>
        window.GetVisualDescendants()
            .OfType<DiagnosticInput>()
            .Single(editor =>
                editor.Request.Variation == DataVariation.State &&
                editor.Request.MemberName == name);

    private static Type CompileRuntimeComponent(bool debugStructural)
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            state int count = CreateCount();
            state int fast = useProjected(count, 10);
            state int slow = useProjected(count, 20);

            <TextBlock Text={ReadText(fast, slow)} />
            """;

        return Compile(component, RuntimeOwnerSource, debugStructural, out _);
    }

    private static Type Compile(
        string componentSource,
        string ownerSource,
        bool debugStructural,
        out string generatedSource)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(componentSource, ownerSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var semanticErrors = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot())
            .Where(diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            semanticErrors.Length == 0,
            string.Join(Environment.NewLine, semanticErrors.Select(
                diagnostic => diagnostic.Code + ": " + diagnostic.Message)));

        var generatedText = ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>(),
            mode: debugStructural
                ? ComponentGenerationMode.DebugStructural
                : ComponentGenerationMode.ReleaseDirect);
        generatedSource = generatedText.ToString();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (debugStructural)
        {
            parseOptions = parseOptions.WithPreprocessorSymbols("DEBUG");
        }

        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedText, parseOptions, "PlannerView.ComposableHooks.g.cs");
        var compilation = fixture.CSharpCompilation
            .AddSyntaxTrees(generatedTree)
            .WithAssemblyName("ComposableHookIntegration_" + Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.AsEnumerable()) + Environment.NewLine + generatedSource);

        using var assemblyStream = new MemoryStream();
        var emitted = compilation.Emit(assemblyStream);
        Assert.True(
            emitted.Success,
            string.Join(Environment.NewLine, emitted.Diagnostics) + Environment.NewLine + generatedSource);
        var assembly = Assembly.Load(assemblyStream.ToArray());
        return Assert.IsAssignableFrom<Type>(assembly.GetType("Demo.PlannerView"));
    }

    private static async Task OnDispatcher(Action test)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(test, CancellationToken.None);
    }

    private static AkburaControl CreateOwner(Type ownerType) =>
        Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));

    private static void Initialize(AkburaControl owner)
    {
        try
        {
            owner.GetType().GetMethod("InitializeForTest")!.Invoke(owner, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static T Get<T>(AkburaControl owner, string name) =>
        Assert.IsAssignableFrom<T>(owner.GetType().GetProperty(name)!.GetValue(owner));

    private static void Set(AkburaControl owner, string name, object value) =>
        owner.GetType().GetProperty(name)!.SetValue(owner, value);

    private const string GenericOwnerSource =
        """
        using System;
        using Akbura;
        using Akbura.CompilerAnotations;
        using Akbura.ComponentTree;
        using Akbura.Engine;
        using Akbura.Hooks;

        namespace Demo;

        public partial class PlannerView : AkburaControl
        {
            public PlannerView() : base(AkburaEngine.Empty) { }

            public void InitializeForTest() => base.OnInitialized();

        }

        public static class GenericHooks
        {
            [UseHook]
            public static State<TResult> useCombined<T, TResult>(
                [Self] this AkburaControl control,
                State<T> state,
                T value,
                Func<T, T, TResult> selector)
            {
                var source = state.Value;
                var result = control.useHookState<TResult>(() => selector(source, value));
                control.useEffect(
                    () => { result.Value = selector(source, value); },
                    [state, source, value]);
                return result;
            }
        }
        """;

    private const string RuntimeOwnerSource =
        """
        using System;
        using System.Threading;
        using Akbura;
        using Akbura.CompilerAnotations;
        using Akbura.ComponentTree;
        using Akbura.Engine;
        using Akbura.Hooks;

        namespace Demo;

        public partial class PlannerView : AkburaControl
        {
            public PlannerView() : base(AkburaEngine.Empty) { }

            public int OrdinaryInitializations { get; private set; }
            public int HookCalls { get; set; }
            public int EffectRuns { get; set; }
            public bool ThrowFromRender { get; set; }
            public bool EffectsSawAttachedStates { get; set; } = true;
            public State<int> PreparedFast { get; set; } = null!;
            public State<int> PreparedSlow { get; set; } = null!;
            public CancellationToken FastToken { get; set; }
            public int FastForTest => fast;
            public int SlowForTest => slow;

            public int CreateCount()
            {
                OrdinaryInitializations++;
                return 1;
            }

            public string ReadText(int first, int second)
            {
                if (ThrowFromRender)
                {
                    throw new InvalidOperationException("render failed");
                }

                return first + ":" + second;
            }

            public void InitializeForTest() => base.OnInitialized();

            public void AdoptStateForTest(State<int> state) =>
                CreateState(StateInfo<int>.FromState("adopted", _ => state));
        }

        public static class ProjectionHooks
        {
            [UseHook]
            public static State<int> useProjected(
                [Self] this AkburaControl control,
                State<int> source,
                int offset)
            {
                var owner = (PlannerView)control;
                owner.HookCalls++;
                var value = source.Value + offset;
                var result = control.useHookState(value);
                if (offset == 10)
                {
                    owner.PreparedFast = result;
                }
                else
                {
                    owner.PreparedSlow = result;
                }

                EffectHooks.useEffect(
                    control,
                    (Action<CancellationToken>)(cancellationToken =>
                    {
                        owner.EffectRuns++;
                        owner.EffectsSawAttachedStates &=
                            owner.PreparedFast.IsAttached && owner.PreparedSlow.IsAttached;
                        if (offset == 10)
                        {
                            owner.FastToken = cancellationToken;
                        }

                        result.Value = value;
                    }),
                    [source, source.Value]);

                return result;
            }
        }
        """;
}
