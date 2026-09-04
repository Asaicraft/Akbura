using Akbura;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComponentMemberIntegrationTests
{
    [Fact]
    public void GeneratedMemberSource_CompilesWithoutManualDescriptorStubs()
    {
        const string component =
            """
            using Demo;

            param string Title = "Default";
            inject IService service;
            state int count = 1;
            command int Add(int left, int right);

            int Double(int value)
            {
                return value * 2;
            }
            """;
        const string csharp =
            """
            using Akbura;
            using Akbura.Engine;
            using Avalonia.Controls;

            namespace Demo;

            public interface IService
            {
            }

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
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, csharp);
        var generatedSource = GenerateMembers(fixture, out var memberPlan);

        Assert.Single(memberPlan.Parameters);
        Assert.Single(memberPlan.Services);
        Assert.Single(memberPlan.States);
        Assert.Single(memberPlan.Commands);
        Assert.Single(memberPlan.UserMembers);
        Assert.Contains("GetParameters()", generatedSource, StringComparison.Ordinal);
        Assert.Contains("GetCommands()", generatedSource, StringComparison.Ordinal);
        Assert.Contains("GetServices()", generatedSource, StringComparison.Ordinal);
        Assert.Contains("GetStates()", generatedSource, StringComparison.Ordinal);

        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "PlannerView.Members.g.cs");
        var diagnostics = fixture.CSharpCompilation
            .AddSyntaxTrees(generatedTree)
            .GetDiagnostics()
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
    }

    [Fact]
    public async Task GeneratedMembers_PreserveRuntimeContracts()
    {
        const string component =
            """
            using Demo;

            param string Title = "Default";
            inject IRequiredService requiredService;
            inject IOptionalService? optionalService;
            state int count = CreateInitialCount();
            command int Sum(int left, int right);

            int CreateInitialCount()
            {
                InitializerCallCount++;
                return 41;
            }
            """;
        const string csharp =
            """
            using Akbura;
            using Akbura.ComponentTree;
            using Akbura.Engine;
            using Avalonia.Controls;
            using System;
            using System.Threading.Tasks;
            using System.Windows.Input;

            namespace Demo;

            public interface IRequiredService
            {
            }

            public interface IOptionalService
            {
            }

            public sealed class RequiredService : IRequiredService
            {
            }

            public sealed class TestServiceProvider : IAkburaServiceProvider
            {
                private readonly RequiredService _service = new();

                public object? GetService(ref readonly InjectionInfo injectionInfo)
                {
                    return injectionInfo.RequestedService == typeof(IRequiredService)
                        ? _service
                        : null;
                }
            }

            public sealed class SumCommand : IAkburaCommand<int, int, int>
            {
                event EventHandler? ICommand.CanExecuteChanged
                {
                    add
                    {
                    }
                    remove
                    {
                    }
                }

                bool ICommand.CanExecute(object? parameter) => true;

                void ICommand.Execute(object? parameter)
                {
                }

                IObservable<bool> IAkburaCommand.IsExecuting => null!;

                IObservable<bool> IAkburaCommand.CanExecute => null!;

                public ValueTask<int> Execute(int left, int right) =>
                    ValueTask.FromResult(left + right);
            }

            public partial class PlannerView : AkburaControl
            {
                private readonly Border _root = new();

                public PlannerView()
                    : base(new AkburaEngine(new TestServiceProvider()))
                {
                }

                public int InitializerCallCount { get; set; }

                public int UpdateCount { get; private set; }

                public int CountForTest
                {
                    get => count;
                    set => count = value;
                }

                public IRequiredService RequiredServiceForTest => requiredService;

                public IOptionalService? OptionalServiceForTest => optionalService;

                public int ParameterDescriptorCount => GetParameters().Length;

                public int CommandDescriptorCount => GetCommands().Length;

                public int ServiceDescriptorCount => GetServices().Length;

                public int StateDescriptorCount => GetStates().Length;

                public bool DescriptorArraysAreStable() =>
                    GetParameters() == GetParameters() &&
                    GetCommands() == GetCommands() &&
                    GetServices() == GetServices() &&
                    GetStates() == GetStates();

                public void InitializeForTest() => base.OnInitialized();

                public int ExecuteSumForTest(int left, int right)
                {
                    Sum = new SumCommand();
                    return Sum.Execute(left, right).GetAwaiter().GetResult();
                }

                protected override Control FirstUpdate() => _root;

                protected override Control Update()
                {
                    UpdateCount++;
                    return _root;
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, csharp);
        var generatedSource = GenerateMembers(fixture, out _);
        var assembly = CompileGeneratedMembers(fixture, generatedSource);
        var ownerType = assembly.GetType("Demo.PlannerView");

        Assert.NotNull(ownerType);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = Assert.IsAssignableFrom<object>(
                    Activator.CreateInstance(ownerType!));

                Invoke(ownerType!, owner, "InitializeForTest");

                Assert.Equal("Default", GetProperty<string>(ownerType, owner, "Title"));
                Assert.Equal(41, GetProperty<int>(ownerType, owner, "CountForTest"));
                Assert.Equal(1, GetProperty<int>(ownerType, owner, "InitializerCallCount"));
                Assert.Equal(1, GetProperty<int>(ownerType, owner, "UpdateCount"));
                Assert.NotNull(GetProperty<object>(ownerType, owner, "RequiredServiceForTest"));
                Assert.Null(ownerType.GetProperty("OptionalServiceForTest")!.GetValue(owner));
                Assert.Equal(1, GetProperty<int>(ownerType, owner, "ParameterDescriptorCount"));
                Assert.Equal(1, GetProperty<int>(ownerType, owner, "CommandDescriptorCount"));
                Assert.Equal(2, GetProperty<int>(ownerType, owner, "ServiceDescriptorCount"));
                Assert.Equal(1, GetProperty<int>(ownerType, owner, "StateDescriptorCount"));
                Assert.True(Assert.IsType<bool>(Invoke(
                    ownerType,
                    owner,
                    "DescriptorArraysAreStable")));

                ownerType.GetProperty("CountForTest")!.SetValue(owner, 42);

                Assert.Equal(42, GetProperty<int>(ownerType, owner, "CountForTest"));
                Assert.Equal(1, GetProperty<int>(ownerType, owner, "InitializerCallCount"));
                Assert.Equal(2, GetProperty<int>(ownerType, owner, "UpdateCount"));
                Assert.Equal(
                    7,
                    Assert.IsType<int>(Invoke(
                        ownerType,
                        owner,
                        "ExecuteSumForTest",
                        3,
                        4)));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task GeneratedContentParameter_UpdatesLogicalChildren()
    {
        const string component =
            """
            using Avalonia.Controls;

            param Control? Content;
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            CreateRuntimeOwnerSource());
        var generatedSource = GenerateMembers(fixture, out _);
        var assembly = CompileGeneratedMembers(fixture, generatedSource);
        var ownerType = assembly.GetType("Demo.PlannerView");

        Assert.NotNull(ownerType);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(ownerType!));
                var logicalOwner = Assert.IsAssignableFrom<ILogical>(owner);
                var contentProperty = ownerType!.GetProperty("Content");
                var first = new TextBlock();
                var replacement = new Border();

                Assert.NotNull(contentProperty);
                contentProperty!.SetValue(owner, first);

                Assert.Same(owner, ((ILogical)first).LogicalParent);
                Assert.Same(first, Assert.Single(logicalOwner.LogicalChildren));

                contentProperty.SetValue(owner, replacement);

                Assert.Null(((ILogical)first).LogicalParent);
                Assert.Same(owner, ((ILogical)replacement).LogicalParent);
                Assert.Same(
                    replacement,
                    Assert.Single(logicalOwner.LogicalChildren));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task GeneratedCollectionContent_SynchronizesLogicalChildren()
    {
        const string component =
            """
        using Avalonia.Controls;
        using System.Collections.Generic;

        param IList<Control> Content;
        """;

        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            CreateRuntimeOwnerSource());

        var generatedSource = GenerateMembers(fixture, out _);
        var assembly = CompileGeneratedMembers(fixture, generatedSource);
        var ownerType = assembly.GetType("Demo.PlannerView");

        Assert.NotNull(ownerType);

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var owner = Assert.IsType<AkburaControl>(
                    Activator.CreateInstance(ownerType!), exactMatch: false);
                var logicalOwner = Assert.IsType<ILogical>(owner, exactMatch: false);
                var first = new TextBlock();
                var second = new Border();

                Invoke(
                    ownerType!,
                    owner,
                    "__AkburaAddCollection_Content",
                    first);

                Invoke(
                    ownerType,
                    owner,
                    "__AkburaAddCollection_Content",
                    second);

                var contentProperty = ownerType.GetProperty("Content");

                Assert.NotNull(contentProperty);

                var content = Assert.IsAssignableFrom<IList<Control>>(
                    contentProperty!.GetValue(owner));

                Assert.Same(content, contentProperty.GetValue(owner));
                Assert.Equal(2, logicalOwner.LogicalChildren.Count);
                Assert.Contains(first, logicalOwner.LogicalChildren);
                Assert.Contains(second, logicalOwner.LogicalChildren);

                content.Remove(first);

                Assert.Null(((ILogical)first).LogicalParent);
                Assert.Same(
                    second,
                    Assert.Single(logicalOwner.LogicalChildren));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task GeneratedRequiredParameter_PreservesInitializationFailure()
    {
        const string component = "param string Required;";
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            CreateRuntimeOwnerSource());
        var generatedSource = GenerateMembers(fixture, out _);
        var assembly = CompileGeneratedMembers(fixture, generatedSource);
        var ownerType = assembly.GetType("Demo.PlannerView");

        Assert.NotNull(ownerType);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(ownerType!));
                var exception = Assert.Throws<TargetInvocationException>(
                    () => Invoke(ownerType!, owner, "InitializeForTest"));

                Assert.IsType<AkburaParameterNotSettedException>(
                    exception.InnerException);
            },
            CancellationToken.None);
    }

    private static string GenerateMembers(
        AkcssActivatorPlannerTests.PlannerFixture fixture,
        out ComponentMemberPlan memberPlan)
    {
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);
        using var codeWriter = new CodeWriter("\r\n");
        var componentWriter = new ComponentWriter(
            codeWriter,
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());

        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        Assert.True(componentWriter.WriteComponentMembers());
        codeWriter.WriteLine();
        componentWriter.WriteDescriptorMembers();
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        memberPlan = componentWriter.MemberPlan;
        return codeWriter.GetText().ToString();
    }

    private static Assembly CompileGeneratedMembers(
        AkcssActivatorPlannerTests.PlannerFixture fixture,
        string generatedSource)
    {
        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "PlannerView.Members.Runtime.g.cs");
        var compilation = fixture.CSharpCompilation
            .AddSyntaxTrees(generatedTree)
            .WithAssemblyName(
                "ComponentMemberIntegration_" +
                Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics()
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
        var emitResult = compilation.Emit(assemblyStream);

        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics) +
            Environment.NewLine +
            generatedSource);
        return Assembly.Load(assemblyStream.ToArray());
    }

    private static string CreateRuntimeOwnerSource()
    {
        return
            """
            using Akbura;
            using Akbura.Engine;
            using Avalonia.Controls;

            namespace Demo;

            public partial class PlannerView : AkburaControl
            {
                private readonly Border _root = new();

                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }

                public void InitializeForTest() => base.OnInitialized();

                protected override Control FirstUpdate() => _root;

                protected override Control Update() => _root;
            }
            """;
    }

    private static object? Invoke(
        Type type,
        object target,
        string methodName,
        params object?[]? arguments)
    {
        var method = type.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        return method!.Invoke(target, arguments);
    }

    private static T GetProperty<T>(
        Type type,
        object target,
        string propertyName)
    {
        var property = type.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property!.GetValue(target));
    }
}
