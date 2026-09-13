using System.Reflection;
using System.Runtime.ExceptionServices;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class UsefulHookIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllHooks_CompileFromShortDslCalls(bool debugStructural)
    {
        const string source =
            """
            using System;
            using System.Threading.Tasks;
            using Avalonia.Controls;
            using Akbura.Hooks;

            state int count = useState(1);
            state HookRef<int> reference = useRef(0);
            state HookRef<int> latest = useLatest(count);
            state string id = useId("counter");
            state int doubled = useSelect(count, x => x * 2);
            state int combined = useComputed(() => count + doubled, [count, doubled]);
            state int distinct = useDistinct(count);
            state int previous = usePrevious(count, -1);
            state int delayed = useDebounce(count, 300);
            state int throttled = useThrottle(count, 300);
            state int projected = useThrottle(count, x => x + 1, 300);
            state AsyncSnapshot<string> response = useAsync(
                token => Task.FromResult(count.ToString()), [count], keepPreviousValue: false);
            state int observed = useObservable(Stream, 0, error => HandleError(error));
            state int stored = useExternalStore(Subscribe, () => StoreValue, [StoreVersion]);
            state HookReducer<int, int> reducer = useReducer<int, int>((value, delta) => value + delta, 0);

            useOnChange(count, (before, after) => RecordChange(before, after));
            useUpdateEffect(() => RecordChange(count, doubled), [count]);
            useTimeout(() => RecordChange(count, doubled), (int?)null);
            useTimeout(token => Task.CompletedTask, (TimeSpan?)null, [count]);
            useInterval(() => RecordChange(count, doubled), (int?)null);
            useInterval(token => Task.CompletedTask, (TimeSpan?)null);
            useThrottle(() => RecordChange(count, doubled), 300, [count]);
            useEventListener<EventArgs>(
                handler => Changed += handler,
                handler => Changed -= handler,
                (sender, args) => RecordChange(count, doubled), [StoreVersion]);

            <TextBlock Text={combined.ToString()} />
            """;

        _ = Compile(source, SourcesOwner, debugStructural);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DerivedChainAndReducer_SettleTheRealGeneratedUi(bool debugStructural)
    {
        const string source =
            """
            using Avalonia.Controls;
            using Akbura.Hooks;

            state HookReducer<int, int> counter = useReducer<int, int>((value, delta) => value + delta, 1);
            state int count = useComputed(() => counter.Value, [counter.Value]);
            state int doubled = useSelect(count, x => x * 2);
            state int total = useComputed(() => count + doubled, [count, doubled]);
            state int previous = usePrevious(count, -1);
            state HookRef<int> latest = useLatest(count);
            state string id = useId("counter");

            useOnChange(count, (before, after) => RecordChange(before, after));

            <TextBlock Text={total.ToString()} />
            """;
        var ownerType = Compile(source, DerivedOwner, debugStructural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var states = owner.GetDiagnosticStates();
                var id = Read<string>(owner, "IdForTest");
                Assert.Equal("3", Assert.IsType<TextBlock>(owner.Child).Text);
                Assert.Equal(-1, Read<int>(owner, "PreviousForTest"));
                Assert.Equal(0, Read<int>(owner, "Changes"));

                Invoke(owner, "Add");

                Assert.Equal("6", Assert.IsType<TextBlock>(owner.Child).Text);
                Assert.Equal(2, Read<int>(owner, "CountForTest"));
                Assert.Equal(4, Read<int>(owner, "DoubleForTest"));
                Assert.Equal(1, Read<int>(owner, "PreviousForTest"));
                Assert.Equal(2, Read<int>(owner, "LatestForTest"));
                Assert.Equal(1, Read<int>(owner, "Changes"));
                Assert.Equal(id, Read<string>(owner, "IdForTest"));
                Assert.True(states == owner.GetDiagnosticStates());

                Invoke(owner, "Reset");

                Assert.Equal("3", Assert.IsType<TextBlock>(owner.Child).Text);
                Assert.Equal(2, Read<int>(owner, "PreviousForTest"));
                Assert.Equal(1, Read<int>(owner, "LatestForTest"));
                Assert.Equal(2, Read<int>(owner, "Changes"));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Type Compile(string source, string ownerSource, bool debugStructural)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, ownerSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var errors = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot())
            .Where(diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(
            diagnostic => diagnostic.Code + ": " + diagnostic.Message)));

        var generated = ComponentDocumentWriter.Generate(
            component, fixture.SemanticModel, "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>(),
            mode: debugStructural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (debugStructural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var tree = CSharpSyntaxTree.ParseText(generated, options, "PlannerView.UsefulHooks.g.cs");
        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(tree)
            .WithAssemblyName("UsefulHooks_" + Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics().Where(
            diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.AsEnumerable()) + Environment.NewLine + generated);
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.PlannerView"));
    }

    private static T Read<T>(AkburaControl owner, string property) =>
        (T)owner.GetType().GetProperty(property)!.GetValue(owner)!;

    private static void Invoke(AkburaControl owner, string method)
    {
        try
        {
            owner.GetType().GetMethod(method)!.Invoke(owner, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private const string DerivedOwner =
        """
        using Akbura;
        using Akbura.Engine;

        namespace Demo;

        public partial class PlannerView : AkburaControl
        {
            public PlannerView() : base(AkburaEngine.Empty) { }
            public int CountForTest => count;
            public int DoubleForTest => doubled;
            public int PreviousForTest => previous;
            public int LatestForTest => latest.Current;
            public string IdForTest => id;
            public int Changes { get; private set; }
            public void Add() => counter.Dispatch(1);
            public void Reset() => counter.Reset();
            public void RecordChange(int before, int after) => Changes++;
        }
        """;

    private const string SourcesOwner =
        """
        using System;
        using Akbura;
        using Akbura.Engine;

        namespace Demo;

        public partial class PlannerView : AkburaControl
        {
            public PlannerView() : base(AkburaEngine.Empty) { }
            public IObservable<int> Stream { get; } = new EmptyStream();
            public int StoreValue { get; set; }
            public int StoreVersion { get; set; }
            public event EventHandler<EventArgs> Changed { add { } remove { } }
            public IDisposable Subscribe(Action callback) => new Subscription();
            public void HandleError(Exception error) { }
            public void RecordChange(int before, int after) { }
            private sealed class Subscription : IDisposable { public void Dispose() { } }
            private sealed class EmptyStream : IObservable<int>
            {
                public IDisposable Subscribe(IObserver<int> observer) => new Subscription();
            }
        }
        """;
}
