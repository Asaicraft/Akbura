using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComponentLifecycleRuntimeCoverageTests
{
    [Fact]
    public async Task DynamicChildParameter_FromTopLevelLocal_IsWrittenInBothLifecyclePhases()
    {
        const string component =
            """
            var current = GetCurrentValue();

            <Child Value={current} />
            """;
        const string childComponent =
            """
            param string Value = "";
            """;
        const string csharp =
            """
            using Akbura;
            using Akbura.ComponentTree;
            using Akbura.Engine;
            using Avalonia;
            using Avalonia.Controls;
            using System.Collections.Immutable;

            namespace Demo;

            public partial class PlannerView : AkburaControl
            {
                private string _currentValue = "Initial";

                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }

                public Control InvokeFirstUpdate() => FirstUpdate();

                public Control InvokeUpdate() => Update();

                public void SetTestValue(string value)
                {
                    _currentValue = value;
                }

                private string GetCurrentValue() => _currentValue;
            }

            public partial class Child : AkburaControl
            {
                private readonly Border _root = new();

                public Child()
                    : base(AkburaEngine.Empty)
                {
                }

                public string Value { get; set; } = string.Empty;

                protected override Control FirstUpdate() => _root;

                protected override Control Update() => _root;

                protected override ImmutableArray<Parameter> GetParameters() => [];

                protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];

                protected override ImmutableArray<InjectService> GetServices() => [];

                protected override ImmutableArray<State> GetStates() => [];
            }
            """;
        var fixture = CompileRuntimeFixture(component, csharp, childComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = fixture.CreateInstance("Demo.PlannerView");
                var child = fixture.Invoke<Control>(owner, "InvokeFirstUpdate");

                Assert.Equal(fixture.GetRuntimeType("Demo.Child"), child.GetType());
                Assert.Equal("Initial", fixture.GetProperty<string>(child, "Value"));

                fixture.Invoke(owner, "SetTestValue", "Changed");
                var updatedChild = fixture.Invoke<Control>(owner, "InvokeUpdate");

                Assert.Same(child, updatedChild);
                Assert.Equal("Changed", fixture.GetProperty<string>(child, "Value"));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task GeneratedUseHook_RunsInsideStableRuntimeFrames_WithSyntheticSelf()
    {
        const string component =
            """
            using Avalonia.Controls;

            useRuntimeHook();

            <Border />
            """;
        const string csharp =
            """
            using Akbura;
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;
            using Akbura.Engine;
            using Akbura.Hooks;
            using Avalonia;
            using Avalonia.Controls;
            using System;
            using System.Collections.Immutable;

            namespace Demo;

            public partial class PlannerView : AkburaControl
            {
                private static readonly UseHookKey s_runtimeHookKey = new();

                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }

                public int HookInvocationCount { get; private set; }

                public int HookStateCreationCount { get; private set; }

                public int HookApplicationCount { get; private set; }

                public void InitializeForTest() => base.OnInitialized();

                public void RenderAgain() => InvalidState();

                [UseHook]
                public static void useRuntimeHook([Self] PlannerView control)
                {
                    control.RecordSyntheticSelf(control);
                    control.UseHook(
                        s_runtimeHookKey,
                        control,
                        static current =>
                        {
                            current.HookStateCreationCount++;
                            return new RuntimeHookState();
                        },
                        static (state, current) =>
                        {
                            state.ApplicationCount++;
                            current.HookApplicationCount = state.ApplicationCount;
                        });
                }

                private void RecordSyntheticSelf(PlannerView self)
                {
                    if (!ReferenceEquals(this, self))
                    {
                        throw new InvalidOperationException("Synthetic self was not the component instance.");
                    }

                    HookInvocationCount++;
                }

                private sealed class RuntimeHookState
                {
                    public int ApplicationCount { get; set; }
                }
            }
            """;
        var fixture = CompileRuntimeFixture(component, csharp);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = fixture.CreateInstance("Demo.PlannerView");

                fixture.Invoke(owner, "InitializeForTest");
                AssertHookCounts(fixture, owner, invocations: 1, creations: 1, applications: 1);

                fixture.Invoke(owner, "RenderAgain");
                AssertHookCounts(fixture, owner, invocations: 2, creations: 1, applications: 2);

                fixture.Invoke(owner, "RenderAgain");
                AssertHookCounts(fixture, owner, invocations: 3, creations: 1, applications: 3);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task ExplicitRootDataContext_IsNotReplacedByOwnerDataContextBinding()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border DataContext={GetExplicitDataContext()} />
            """;
        const string csharp =
            """
            using Akbura;
            using Akbura.ComponentTree;
            using Akbura.Engine;
            using Avalonia;
            using Avalonia.Controls;
            using System.Collections.Immutable;

            namespace Demo;

            public partial class PlannerView : AkburaControl
            {
                private readonly object _explicitDataContext = new();

                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }

                public object ExplicitDataContext => _explicitDataContext;

                public Control InvokeFirstUpdate() => FirstUpdate();

                public Control InvokeUpdate() => Update();

                private object GetExplicitDataContext() => _explicitDataContext;
            }
            """;
        var fixture = CompileRuntimeFixture(component, csharp);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = fixture.CreateInstance("Demo.PlannerView");
                var ownerControl = Assert.IsAssignableFrom<Control>(owner);
                var root = fixture.Invoke<Control>(owner, "InvokeFirstUpdate");
                var explicitDataContext = fixture.GetProperty<object>(
                    owner,
                    "ExplicitDataContext");

                ownerControl.DataContext = new object();
                Assert.Same(root, fixture.Invoke<Control>(owner, "InvokeUpdate"));
                Assert.Same(explicitDataContext, root.DataContext);

                ownerControl.DataContext = new object();

                Assert.Same(explicitDataContext, root.DataContext);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task GeneratedLifecycle_WithoutMarkupRoot_ReusesFallbackControl()
    {
        const string component =
            """
            RenderCount++;
            """;
        const string csharp =
            """
            using Akbura;
            using Akbura.ComponentTree;
            using Akbura.Engine;
            using Avalonia;
            using Avalonia.Controls;
            using System.Collections.Immutable;

            namespace Demo;

            public partial class PlannerView : AkburaControl
            {
                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }

                public int RenderCount { get; private set; }

                public Control InvokeFirstUpdate() => FirstUpdate();

                public Control InvokeUpdate() => Update();
            }
            """;
        var fixture = CompileRuntimeFixture(component, csharp);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = fixture.CreateInstance("Demo.PlannerView");
                var root = fixture.Invoke<Control>(owner, "InvokeFirstUpdate");
                var firstUpdateRoot = fixture.Invoke<Control>(owner, "InvokeUpdate");
                var secondUpdateRoot = fixture.Invoke<Control>(owner, "InvokeUpdate");

                Assert.Same(root, firstUpdateRoot);
                Assert.Same(root, secondUpdateRoot);
                Assert.Equal(2, fixture.GetProperty<int>(owner, "RenderCount"));
            },
            CancellationToken.None);
    }

    private static RuntimeFixture CompileRuntimeFixture(
        string component,
        string csharp,
        string? childComponent = null)
    {
        var baseFixture = AkcssActivatorPlannerTests.CreateFixture(component, csharp);
        var semanticModel = baseFixture.SemanticModel;
        if (childComponent != null)
        {
            var childTree = AkburaSyntaxTree.ParseText(
                childComponent,
                "Child.akbura");
            var compilation = new AkburaCompilation(
                baseFixture.CSharpCompilation,
                [baseFixture.ComponentTree, childTree],
                rootNamespace: "Demo");
            semanticModel = compilation.GetSemanticModel(baseFixture.ComponentTree);
        }

        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(
                baseFixture.ComponentTree.GetRoot()).Symbol);
        using var codeWriter = new CodeWriter("\r\n");
        var componentWriter = new ComponentWriter(
            codeWriter,
            componentSymbol,
            semanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());

        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        if (componentWriter.WriteElementFields())
        {
            codeWriter.WriteLine();
        }

        if (componentWriter.WriteLifecycleFields())
        {
            codeWriter.WriteLine();
        }

        componentWriter.WriteLifecycleMembers();
        codeWriter.WriteLine();
        componentWriter.WriteDescriptorMembers();
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        var generatedSource = codeWriter.GetText().ToString();
        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "PlannerView.Lifecycle.RuntimeCoverage.g.cs");
        var runtimeCompilation = baseFixture.CSharpCompilation
            .AddSyntaxTrees(generatedTree)
            .WithAssemblyName(
                "ComponentLifecycleRuntimeCoverage_" +
                Guid.NewGuid().ToString("N"));
        var diagnostics = runtimeCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            generatedSource);

        using var assemblyStream = new MemoryStream();
        var emitResult = runtimeCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics) +
            Environment.NewLine +
            generatedSource);

        return new RuntimeFixture(Assembly.Load(assemblyStream.ToArray()));
    }

    private static void AssertHookCounts(
        RuntimeFixture fixture,
        object owner,
        int invocations,
        int creations,
        int applications)
    {
        Assert.Equal(
            invocations,
            fixture.GetProperty<int>(owner, "HookInvocationCount"));
        Assert.Equal(
            creations,
            fixture.GetProperty<int>(owner, "HookStateCreationCount"));
        Assert.Equal(
            applications,
            fixture.GetProperty<int>(owner, "HookApplicationCount"));
    }

    private sealed class RuntimeFixture
    {
        private readonly Assembly _assembly;

        public RuntimeFixture(Assembly assembly)
        {
            _assembly = assembly;
        }

        public Type GetRuntimeType(string name)
        {
            return _assembly.GetType(name) ??
                throw new InvalidOperationException(
                    "Generated runtime type was not found: " + name);
        }

        public object CreateInstance(string typeName)
        {
            var instance = Activator.CreateInstance(GetRuntimeType(typeName));

            Assert.NotNull(instance);
            return instance;
        }

        public object? Invoke(
            object target,
            string name,
            params object?[] arguments)
        {
            return GetMethod(target.GetType(), name).Invoke(target, arguments);
        }

        public T Invoke<T>(
            object target,
            string name,
            params object?[] arguments)
        {
            return Assert.IsAssignableFrom<T>(Invoke(target, name, arguments));
        }

        public T GetProperty<T>(object target, string name)
        {
            var value = target.GetType()
                .GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(target);

            return Assert.IsType<T>(value);
        }

        private static MethodInfo GetMethod(Type type, string name)
        {
            return type.GetMethod(
                       name,
                       BindingFlags.Public | BindingFlags.Instance) ??
                throw new InvalidOperationException(
                    "Generated runtime method was not found: " + name);
        }
    }
}
