using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ICommandHandlerBindingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NavButton_CommandBoundary_InvokesOwnerOnceAndPreservesDirectIdentity(bool structural, bool direct)
    {
        var type = CompileNavButton(structural, direct);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Activator.CreateInstance(type)!;
            var helper = type.Assembly.GetType("Demo.CommandRuntimeFixture")!;
            helper.GetMethod("Configure")!.Invoke(null, [owner]);
            var native = type.GetProperty("NavigateTo")!.GetValue(owner);
            var button = Assert.IsType<Avalonia.Controls.Button>(
                type.GetMethod("FirstForTest")!.Invoke(owner, null));
            Assert.Same(button, type.GetMethod("UpdateForTest")!.Invoke(owner, null));
            Assert.Null(helper.GetProperty("Seen")!.GetValue(null));

            var command = Assert.IsAssignableFrom<System.Windows.Input.ICommand>(button.Command);
            if (direct)
            {
                Assert.Same(native, command);
                Assert.Same(owner, button.CommandParameter);
            }
            else
            {
                Assert.NotSame(native, command);
            }

            Assert.Same(button, type.GetMethod("UpdateForTest")!.Invoke(owner, null));
            Assert.Same(command, button.Command);
            command.Execute(button.CommandParameter);
            Assert.Same(owner, helper.GetProperty("Seen")!.GetValue(null));
            Assert.Equal(1, helper.GetProperty("Calls")!.GetValue(null));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("() => Save()")]
    [InlineData("() => { Save(); }")]
    [InlineData("() => Compute()")]
    [InlineData("async () => await SaveAsync()")]
    [InlineData("async () => { await SaveAsync(); }")]
    [InlineData("() => SaveAsync()")]
    [InlineData("Save")]
    [InlineData("SaveAsync")]
    [InlineData("Callback")]
    [InlineData("() => LoadTaskAsync()")]
    [InlineData("() => LoadValueTaskAsync()")]
    [InlineData("SaveValueTaskAsync")]
    [InlineData("LoadTaskAsync")]
    [InlineData("LoadValueTaskAsync")]
    [InlineData("ResultCallback")]
    [InlineData("parameter => Handle(parameter)")]
    [InlineData("(string value) => HandleString(value)")]
    [InlineData("Handle")]
    [InlineData("HandleAsync")]
    [InlineData("ValueTaskHandler")]
    [InlineData("delegate { Save(); }")]
    public void ICommandProperty_SupportedHandlerFamilies_Compile(string handler)
    {
        var component =
            "using Avalonia.Controls; using System.Threading.Tasks; namespace Demo; " +
            "<Button Command={" + handler + "} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty)
                {
                }

                public System.Action Callback => Save;
                public void Save() { }
                public int Compute() => 42;
                public System.Threading.Tasks.Task SaveAsync() => System.Threading.Tasks.Task.CompletedTask;
                public System.Threading.Tasks.Task<int> LoadTaskAsync() => System.Threading.Tasks.Task.FromResult(42);
                public System.Threading.Tasks.ValueTask SaveValueTaskAsync() => System.Threading.Tasks.ValueTask.CompletedTask;
                public System.Threading.Tasks.ValueTask<int> LoadValueTaskAsync() => new(42);
                public System.Func<System.Threading.Tasks.Task<int>> ResultCallback => LoadTaskAsync;
                public void Handle(object? value) { }
                public System.Threading.Tasks.Task HandleAsync(object? value) => System.Threading.Tasks.Task.CompletedTask;
                public System.Threading.Tasks.ValueTask<int> ValueTaskHandler(object? value) => new(42);
                public void HandleString(string value) { }
            }
            """;
        AssertGeneratedCompilation(component, host, structural: false, expectAdapter: true);
    }

    [Fact]
    public void CustomClrICommandProperty_Lambda_Compiles()
    {
        const string component =
            "using Demo; namespace Demo; <CommandHost Action={() => Save()} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public void Save() { }
            }

            public sealed class CommandHost : Avalonia.Controls.Control
            {
                public System.Windows.Input.ICommand? Action { get; set; }
            }
            """;

        AssertGeneratedCompilation(
            component,
            host,
            structural: false,
            expectAdapter: true,
            propertyName: "Action");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteOnlyClrICommandProperty_Lambda_Compiles(bool structural)
    {
        const string component =
            "using Demo; namespace Demo; <CommandHost Action={() => Save()} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public void Save() { }
            }

            public sealed class CommandHost : Avalonia.Controls.Control
            {
                public System.Windows.Input.ICommand? Action
                {
                    set { }
                }
            }
            """;

        AssertGeneratedCompilation(
            component,
            host,
            structural,
            expectAdapter: true,
            propertyName: "Action");
    }

    [Fact]
    public void ICommandProperty_TaskReturningMethodGroup_SeparatesReturnAndLogicalResult()
    {
        const string component =
            "using Avalonia.Controls; namespace Demo; <Button Command={LoadAsync} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public System.Threading.Tasks.Task<int> LoadAsync() =>
                    System.Threading.Tasks.Task.FromResult(42);
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, host);
        var attribute = fixture.ComponentTree.GetRoot()
            .DescendantNodes()
            .OfType<Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single();
        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            fixture.SemanticModel.GetOperation(attribute));

        Assert.Equal("Task", operation.ReturnType.Name);
        Assert.Equal("Int32", operation.ResultType.Name);
        Assert.Equal(MarkupCommandResultMode.ReturnsResult, operation.ResultMode);
    }

    [Fact]
    public void ICommandProperty_ImplicitParameter_IsNullableObject()
    {
        const string component =
            "using Avalonia.Controls; namespace Demo; " +
            "<Button Command={parameter => Handle(parameter)} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public void Handle(object? value) { }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, host);
        var attribute = fixture.ComponentTree.GetRoot()
            .DescendantNodes()
            .OfType<Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single();
        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            fixture.SemanticModel.GetOperation(attribute));
        var parameterType = Assert.IsAssignableFrom<ITypeSymbol>(
            Assert.Single(operation.ParameterTypes).Symbol);

        Assert.Equal(SpecialType.System_Object, parameterType.SpecialType);
        Assert.Equal(NullableAnnotation.Annotated, parameterType.NullableAnnotation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComponentICommandParameter_Lambda_Compiles(bool structural)
    {
        const string parentSource =
            "using Demo; namespace Demo; <ActionChild Action={() => Save()} />";
        const string childSource =
            "using System.Windows.Input; using Avalonia.Controls; namespace Demo; " +
            "param ICommand Action; <Button Command={Action} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public void Save() { }
            }

            public partial class ActionChild : Akbura.AkburaControl
            {
                public ActionChild() : base(Akbura.Engine.AkburaEngine.Empty) { }
            }
            """;
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = CSharpCompilation.Create(
            "ICommandComponentParameter_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(host, options)],
            SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var parentTree = AkburaSyntaxTree.ParseText(parentSource, "PlannerView.akbura");
        var childTree = AkburaSyntaxTree.ParseText(childSource, "ActionChild.akbura");
        var compilation = new AkburaCompilation(
            csharpCompilation,
            [parentTree, childTree],
            rootNamespace: "Demo");
        var generated = new List<string>();
        foreach (var tree in new[] { parentTree, childTree })
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            Assert.Empty(semanticModel.GetSemanticDiagnostics(tree.GetRoot()));
            var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
                semanticModel.GetSymbolInfo(tree.GetRoot()).Symbol);
            generated.Add(ComponentDocumentWriter.Generate(
                symbol,
                semanticModel,
                tree.FilePath,
                new Dictionary<Language.Syntax.AkburaSyntax, string>(),
                mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect)
                .ToString());
        }

        var parentModel = compilation.GetSemanticModel(parentTree);
        var actionAttribute = parentTree.GetRoot()
            .DescendantNodes()
            .OfType<Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single();
        Assert.Equal(
            MarkupCommandTargetKind.ICommandProperty,
            Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
                parentModel.GetOperation(actionAttribute)).TargetKind);
        var emittedCompilation = csharpCompilation.AddSyntaxTrees(generated.Select(source =>
            CSharpSyntaxTree.ParseText(source, options)));
        var diagnostics = emittedCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.AsEnumerable()) +
            Environment.NewLine + string.Join(Environment.NewLine, generated));
    }

    [Theory]
    [InlineData("ExistingCommand")]
    [InlineData("GetExistingCommand()")]
    [InlineData("null")]
    public void ICommandProperty_DirectCompatibleValue_RemainsOrdinaryAssignment(string expression)
    {
        var component =
            "using Avalonia.Controls; namespace Demo; <Button Command={" + expression + "} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public System.Windows.Input.ICommand ExistingCommand { get; } = new TestCommand();
                public System.Windows.Input.ICommand GetExistingCommand() => ExistingCommand;
            }

            public sealed class TestCommand : System.Windows.Input.ICommand
            {
                public event System.EventHandler? CanExecuteChanged
                {
                    add { }
                    remove { }
                }
                public bool CanExecute(object? parameter) => true;
                public void Execute(object? parameter) { }
            }
            """;

        AssertGeneratedCompilation(component, host, structural: false, expectAdapter: false);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("Text")]
    [InlineData("SaveAsync()")]
    public void ICommandProperty_NonCallableFailedConversion_RemainsDiagnostic(string expression)
    {
        var component =
            "using Avalonia.Controls; namespace Demo; <Button Command={" + expression + "} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public string Text => "text";
                public System.Threading.Tasks.Task SaveAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, host);
        var attribute = fixture.ComponentTree.GetRoot()
            .DescendantNodes()
            .OfType<Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single();

        Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            fixture.SemanticModel.GetOperation(attribute));
        Assert.Contains(
            fixture.SemanticModel.GetSemanticDiagnostics(attribute),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert ||
                diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError);
    }

    [Fact]
    public void DerivedICommandContract_IsNotAdaptedUnsafely()
    {
        const string component =
            "using Demo; namespace Demo; <CommandHost Action={() => Save()} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public void Save() { }
            }

            public interface IMyCommand : System.Windows.Input.ICommand
            {
                void Reset();
            }

            public sealed class CommandHost : Avalonia.Controls.Control
            {
                public IMyCommand? Action { get; set; }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, host);
        var attribute = fixture.ComponentTree.GetRoot()
            .DescendantNodes()
            .OfType<Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single();

        Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            fixture.SemanticModel.GetOperation(attribute));
        Assert.NotEmpty(fixture.SemanticModel.GetSemanticDiagnostics(attribute));
    }

    [Theory]
    [InlineData("(first, second) => Save()")]
    [InlineData("(ref object value) => Save()")]
    public void ICommandProperty_UnsupportedHandlerSignature_ReportsDiagnostic(string handler)
    {
        var component =
            "using Avalonia.Controls; namespace Demo; <Button Command={" + handler + "} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public void Save() { }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, host);
        var attribute = fixture.ComponentTree.GetRoot()
            .DescendantNodes()
            .OfType<Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single();
        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            fixture.SemanticModel.GetOperation(attribute));

        Assert.True(operation.HasErrors);
        Assert.Contains(
            fixture.SemanticModel.GetSemanticDiagnostics(attribute),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupCommandHandlerSignatureMismatch);
    }

    [Fact]
    public void ICommandProperty_AmbiguousMethodGroup_RemainsDiagnostic()
    {
        const string component =
            "using Avalonia.Controls; namespace Demo; <Button Command={Save} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public void Save() { }
                public void Save(int value) { }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, host);
        var attribute = fixture.ComponentTree.GetRoot()
            .DescendantNodes()
            .OfType<Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single();

        Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            fixture.SemanticModel.GetOperation(attribute));
        Assert.NotEmpty(fixture.SemanticModel.GetSemanticDiagnostics(attribute));
    }

    [Fact]
    public void ICommandProperty_MarkupBinding_RemainsOrdinaryPropertyBinding()
    {
        const string component =
            "using Avalonia.Controls; namespace Demo; <Button Command=${Binding ExistingCommand} />";
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public System.Windows.Input.ICommand? ExistingCommand { get; set; }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, host);
        var attribute = fixture.ComponentTree.GetRoot()
            .DescendantNodes()
            .OfType<Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single();

        Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            fixture.SemanticModel.GetOperation(attribute));
        Assert.DoesNotContain(
            fixture.SemanticModel.GetSemanticDiagnostics(attribute),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentHandlers_InvokeTheMatchingNavButtonExactlyOnce(bool structural)
    {
        var type = CompileParentAndNavButton(structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(async () =>
        {
            var owner = Activator.CreateInstance(type)!;
            var root = Assert.IsType<Avalonia.Controls.StackPanel>(
                type.GetMethod("FirstForTest")!.Invoke(owner, null));
            var children = root.Children
                .Where(control => control.GetType().Name == "NavButton")
                .ToArray();
            Assert.Equal(3, children.Length);

            var commands = new List<System.Windows.Input.ICommand>();
            foreach (var child in children)
            {
                Assert.NotNull(child.GetType().GetProperty("Geometries")!.GetValue(child));
                var button = Assert.IsType<Avalonia.Controls.Button>(
                    child.GetType().GetMethod("FirstForTest")!.Invoke(child, null));
                var command = Assert.IsAssignableFrom<System.Windows.Input.ICommand>(button.Command);
                commands.Add(command);
                command.Execute(button.CommandParameter);
            }

            Assert.Equal(commands.Count, commands.Distinct(ReferenceEqualityComparer.Instance).Count());
            var fixture = type.Assembly.GetType("Demo.ParentCommandFixture")!;
            await Assert.IsAssignableFrom<Task>(
                fixture.GetProperty("AllCalled")!.GetValue(null));
            Assert.Equal(3, fixture.GetProperty("Calls")!.GetValue(null));
            var seen = Assert.IsAssignableFrom<System.Collections.IList>(
                fixture.GetProperty("Seen")!.GetValue(null));
            Assert.Same(children[0], seen[0]);
            Assert.Null(seen[1]);
            Assert.Same(children[2], seen[2]);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ButtonCommand_AsyncLambdaExecutingDeclaredCommand_Compiles(bool structural)
    {
        const string component =
            """
            using System.Collections.Generic;
            using Avalonia.Controls;
            using Avalonia.Media;

            namespace Demo;

            param bool IsActive = false;
            param IList<StreamGeometry> Geometries;
            command void NavigateTo(PlannerView button);

            <Button Command={async () => await NavigateTo.Execute(this)}>
                Navigate
            </Button>
            """;
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, host);
        var semanticModel = fixture.SemanticModel;
        Assert.Empty(semanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));

        var attribute = fixture.ComponentTree.GetRoot()
            .DescendantNodes()
            .OfType<Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single();
        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));
        Assert.Equal(MarkupCommandTargetKind.ICommandProperty, operation.TargetKind);

        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var generated = ComponentDocumentWriter.Generate(
            symbol,
            semanticModel,
            fixture.ComponentTree.FilePath,
            new Dictionary<Language.Syntax.AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect)
            .ToString();
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var compilation = fixture.CSharpCompilation
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, options))
            .WithAssemblyName("ICommandHandlerBinding_" + Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.AsEnumerable()) +
            Environment.NewLine + generated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavButton_WithoutPartialCSharpPart_Compiles(bool structural)
    {
        const string component =
            """
            using System.Collections.Generic;
            using Avalonia.Controls;
            using Avalonia.Media;

            namespace Demo;

            param bool IsActive = false;
            param IList<StreamGeometry> Geometries;
            command void NavigateTo(NavButton button);

            <Button Command={async () => await NavigateTo.Execute(this)}>
                Navigate
            </Button>
            """;
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = CSharpCompilation.Create(
            "ICommandNavButtonWithoutPartial_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var tree = AkburaSyntaxTree.ParseText(component, "NavButton.akbura");
        var compilation = new AkburaCompilation(
            csharpCompilation,
            [tree],
            rootNamespace: "Demo");
        var semanticModel = compilation.GetSemanticModel(tree);
        Assert.Empty(semanticModel.GetSemanticDiagnostics(tree.GetRoot()));
        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(tree.GetRoot()).Symbol);
        Assert.Empty(symbol.PartialTypes);
        var generated = ComponentDocumentWriter.Generate(
            symbol,
            semanticModel,
            tree.FilePath,
            new Dictionary<Language.Syntax.AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect)
            .ToString();
        var emittedCompilation = csharpCompilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(generated, options));
        var diagnostics = emittedCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.AsEnumerable()) +
            Environment.NewLine + generated);
    }

    [Theory]
    [InlineData(false, "async () => await NavigateTo.Execute(this)", false)]
    [InlineData(true, "async () => await NavigateTo.Execute(this)", false)]
    [InlineData(false, "async () => await this.NavigateTo.Execute(this)", false)]
    [InlineData(true, "async () => await this.NavigateTo.Execute(this)", false)]
    [InlineData(false, "NavigateTo", true)]
    [InlineData(true, "NavigateTo", true)]
    public void NavButton_PostGeneratedSemanticModel_DoesNotDuplicateDeclaredCommand(bool structural, string commandValue, bool hasCommandParameter)
    {
        var component =
            """
            using System.Collections.Generic;
            using Avalonia.Controls;
            using Avalonia.Media;

            namespace Demo;

            param bool IsActive = false;
            param IList<StreamGeometry> Geometries;
            command void NavigateTo(NavButton button);

            <Button Command={COMMAND_VALUE}COMMAND_PARAMETER>
                Navigate
            </Button>
            """
            .Replace("COMMAND_VALUE", commandValue, StringComparison.Ordinal)
            .Replace(
                "COMMAND_PARAMETER",
                hasCommandParameter ? " CommandParameter={this}" : string.Empty,
                StringComparison.Ordinal);
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = CSharpCompilation.Create(
            "ICommandPostGenerated_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var tree = AkburaSyntaxTree.ParseText(component, "NavButton.akbura");
        var sourceCompilation = new AkburaCompilation(
            csharpCompilation,
            [tree],
            rootNamespace: "Demo");
        var sourceSemanticModel = sourceCompilation.GetSemanticModel(tree);
        Assert.Empty(sourceSemanticModel.GetSemanticDiagnostics(tree.GetRoot()));

        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            sourceSemanticModel.GetSymbolInfo(tree.GetRoot()).Symbol);
        var generated = ComponentDocumentWriter.Generate(
            symbol,
            sourceSemanticModel,
            tree.FilePath,
            new Dictionary<Language.Syntax.AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect)
            .ToString();
        var generatedTree = CSharpSyntaxTree.ParseText(
            generated,
            options,
            ComponentDocumentWriter.GetHintName(symbol, tree.FilePath));
        var postGeneratedCompilation = sourceCompilation.WithCSharpCompilation(
            csharpCompilation.AddSyntaxTrees(generatedTree));
        var postGeneratedSemanticModel = postGeneratedCompilation.GetSemanticModel(tree);
        var diagnostics = postGeneratedSemanticModel.GetSemanticDiagnostics(tree.GetRoot());

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic =>
                diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavButton_StaleGeneratedCommandSignature_DoesNotOverrideCurrentSource(bool structural)
    {
        const string previousComponent =
            """
            using Avalonia.Controls;

            namespace Demo;

            command void NavigateTo(NavButton button);

            <Button Command={async () => await NavigateTo.Execute(this)} />
            """;
        const string currentComponent =
            """
            using Avalonia.Controls;

            namespace Demo;

            command void NavigateTo(string route);

            <Button Command={async () => await NavigateTo.Execute("home")} />
            """;

        var (semanticModel, tree) = CreateSemanticModelWithStaleGeneratedComponent(
            previousComponent,
            currentComponent,
            structural);

        Assert.Empty(semanticModel.GetSemanticDiagnostics(tree.GetRoot()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavButton_StaleGeneratedCommandName_DoesNotOverrideCurrentSource(bool structural)
    {
        const string previousComponent =
            """
            using Avalonia.Controls;

            namespace Demo;

            command void NavigateTo(NavButton button);

            <Button Command={async () => await NavigateTo.Execute(this)} />
            """;
        const string currentComponent =
            """
            using Avalonia.Controls;

            namespace Demo;

            command void OpenPage(NavButton button);

            <Button Command={async () => await OpenPage.Execute(this)} />
            """;

        var (semanticModel, tree) = CreateSemanticModelWithStaleGeneratedComponent(
            previousComponent,
            currentComponent,
            structural);

        Assert.Empty(semanticModel.GetSemanticDiagnostics(tree.GetRoot()));
    }

    [Fact]
    public void NavButton_DeletedCommand_DoesNotBindToStaleGeneratedProperty()
    {
        const string previousComponent =
            """
            using Avalonia.Controls;

            namespace Demo;

            command void NavigateTo(NavButton button);

            <Button Command={async () => await NavigateTo.Execute(this)} />
            """;
        const string currentComponent =
            """
            using Avalonia.Controls;

            namespace Demo;

            <Button Command={async () => await NavigateTo.Execute(this)} />
            """;

        var (semanticModel, tree) = CreateSemanticModelWithStaleGeneratedComponent(
            previousComponent,
            currentComponent,
            structural: false);
        var diagnostic = Assert.Single(
            semanticModel.GetSemanticDiagnostics(tree.GetRoot()),
            static diagnostic =>
                diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError);

        Assert.DoesNotContain("Ambiguity", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NavigateTo", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NavButton.cs")]
    [InlineData("OtherGenerator/Akbura.Component.NavButton.other.g.cs")]
    public void NavButton_NonAkburaPartialCommandConflict_RemainsDiagnostic(string csharpPath)
    {
        const string component =
            """
            using Avalonia.Controls;

            namespace Demo;

            command void NavigateTo(NavButton button);

            <Button Command={async () => await NavigateTo.Execute(this)} />
            """;
        const string userPartial =
            """
            // <auto-generated />

            namespace Demo;

            public partial class NavButton : Akbura.AkburaControl
            {
                public NavButton() : base(Akbura.Engine.AkburaEngine.Empty)
                {
                }

                public Akbura.IAkburaCommand<NavButton, object> NavigateTo { get; set; } = null!;
            }
            """;
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var csharpCompilation = CSharpCompilation.Create(
            "ICommandUserConflict_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(userPartial, options, csharpPath)],
            SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var tree = AkburaSyntaxTree.ParseText(component, "NavButton.akbura");
        var semanticModel = new AkburaCompilation(
                csharpCompilation,
                [tree],
                rootNamespace: "Demo")
            .GetSemanticModel(tree);

        Assert.Contains(
            semanticModel.GetSemanticDiagnostics(tree.GetRoot()),
            static diagnostic =>
                diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError &&
                diagnostic.Message.Contains("Ambiguity", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ButtonCommand_DirectDeclaredCommand_CompilesWithoutAdapter(bool structural)
    {
        const string component =
            """
            using Avalonia.Controls;

            namespace Demo;

            command void NavigateTo(PlannerView button);

            <Button Command={NavigateTo} CommandParameter={this} />
            """;
        const string host =
            """
            namespace Demo;

            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        AssertGeneratedCompilation(component, host, structural, expectAdapter: false);
    }

    private static void AssertGeneratedCompilation(string component, string host, bool structural, bool expectAdapter, string propertyName = "Command")
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, host);
        var semanticModel = fixture.SemanticModel;
        Assert.Empty(semanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));

        var commandAttribute = fixture.ComponentTree.GetRoot()
            .DescendantNodes()
            .OfType<Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single(attribute => attribute.Name.Identifier.ValueText == propertyName);
        var operation = semanticModel.GetOperation(commandAttribute);
        if (expectAdapter)
        {
            Assert.Equal(
                MarkupCommandTargetKind.ICommandProperty,
                Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(operation).TargetKind);
        }
        else
        {
            Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(operation);
        }

        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var generated = ComponentDocumentWriter.Generate(
            symbol,
            semanticModel,
            fixture.ComponentTree.FilePath,
            new Dictionary<Language.Syntax.AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect)
            .ToString();
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var compilation = fixture.CSharpCompilation
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, options))
            .WithAssemblyName("ICommandHandlerBinding_" + Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.AsEnumerable()) +
            Environment.NewLine + generated);
    }

    private static Type CompileNavButton(bool structural, bool direct)
    {
        var commandValue = direct
            ? "NavigateTo"
            : "async () => await NavigateTo.Execute(this)";
        var commandParameter = direct ? " CommandParameter={this}" : string.Empty;
        var component =
            """
            using System.Collections.Generic;
            using Avalonia.Controls;
            using Avalonia.Media;

            namespace Demo;

            param bool IsActive = false;
            param IList<StreamGeometry> Geometries;
            command void NavigateTo(NavButton button);

            <Button Command={COMMAND_VALUE}COMMAND_PARAMETER>
                Navigate
            </Button>
            """
            .Replace("COMMAND_VALUE", commandValue, StringComparison.Ordinal)
            .Replace("COMMAND_PARAMETER", commandParameter, StringComparison.Ordinal);
        const string host =
            """
            namespace Demo;

            public partial class NavButton : Akbura.AkburaControl
            {
                public NavButton() : base(Akbura.Engine.AkburaEngine.Empty)
                {
                }

                public Avalonia.Controls.Control FirstForTest() => FirstUpdate();
                public Avalonia.Controls.Control UpdateForTest() => Update();
            }
            """;
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            parseOptions = parseOptions.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = CSharpCompilation.Create(
            "ICommandNavButton_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(host, parseOptions)],
            SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var tree = AkburaSyntaxTree.ParseText(component, "NavButton.akbura");
        var compilation = new AkburaCompilation(csharpCompilation, [tree], rootNamespace: "Demo");
        var semanticModel = compilation.GetSemanticModel(tree);
        Assert.Empty(semanticModel.GetSemanticDiagnostics(tree.GetRoot()));
        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(tree.GetRoot()).Symbol);
        var generated = ComponentDocumentWriter.Generate(
            symbol,
            semanticModel,
            tree.FilePath,
            new Dictionary<Language.Syntax.AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect)
            .ToString();
        const string runtimeHelper =
            """
            namespace Demo;

            public static class CommandRuntimeFixture
            {
                public static object? Seen { get; private set; }
                public static int Calls { get; private set; }

                public static void Configure(NavButton owner)
                {
                    owner.Geometries = new System.Collections.Generic.List<Avalonia.Media.StreamGeometry>();
                    owner.NavigateTo = Akbura.Commands.AkburaCommandFactory.CreateAction<NavButton, object>(value =>
                    {
                        Seen = value;
                        Calls++;
                    });
                }
            }
            """;
        var emittedCompilation = csharpCompilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(generated, parseOptions),
            CSharpSyntaxTree.ParseText(runtimeHelper, parseOptions));
        var diagnostics = emittedCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.AsEnumerable()) +
            Environment.NewLine + generated);
        using var output = new MemoryStream();
        var emitted = emittedCompilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.AsEnumerable()));
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.NavButton"));
    }

    private static (AkburaSemanticModel SemanticModel, AkburaSyntaxTree Tree) CreateSemanticModelWithStaleGeneratedComponent(string previousComponent, string currentComponent, bool structural)
    {
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = CSharpCompilation.Create(
            "ICommandStaleGenerated_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var previousTree = AkburaSyntaxTree.ParseText(
            previousComponent,
            "NavButton.akbura");
        var previousCompilation = new AkburaCompilation(
            csharpCompilation,
            [previousTree],
            rootNamespace: "Demo");
        var previousSemanticModel = previousCompilation.GetSemanticModel(previousTree);
        Assert.Empty(previousSemanticModel.GetSemanticDiagnostics(previousTree.GetRoot()));
        var previousSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            previousSemanticModel.GetSymbolInfo(previousTree.GetRoot()).Symbol);
        var generated = ComponentDocumentWriter.Generate(
            previousSymbol,
            previousSemanticModel,
            previousTree.FilePath,
            new Dictionary<Language.Syntax.AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        var generatedTree = CSharpSyntaxTree.ParseText(
            generated,
            options,
            ComponentDocumentWriter.GetHintName(previousSymbol, previousTree.FilePath));
        var currentTree = AkburaSyntaxTree.ParseText(
            currentComponent,
            "NavButton.akbura");
        var currentCompilation = new AkburaCompilation(
            csharpCompilation.AddSyntaxTrees(generatedTree),
            [currentTree],
            rootNamespace: "Demo");
        var semanticModel = currentCompilation.GetSemanticModel(currentTree);

        var repeatedCompilation = currentCompilation.WithCSharpCompilation(
            csharpCompilation.AddSyntaxTrees(generatedTree));
        Assert.Empty(
            repeatedCompilation
                .GetSemanticModel(currentTree)
                .GetSemanticDiagnostics(currentTree.GetRoot())
                .Where(static diagnostic =>
                    diagnostic.Message.Contains("Ambiguity", StringComparison.OrdinalIgnoreCase)));

        return (semanticModel, currentTree);
    }

    private static Type CompileParentAndNavButton(bool structural)
    {
        const string parentSource =
            """
            using System;
            using System.Threading.Tasks;
            using Avalonia.Controls;
            using Avalonia.Media;

            namespace Demo;

            <StackPanel>
                <NavButton
                    Geometries={Array.Empty<StreamGeometry>()}
                    NavigateTo={button => {
                        ParentCommandFixture.Record(button);
                    }} />

                <NavButton
                    Geometries={Array.Empty<StreamGeometry>()}
                    NavigateTo={() => {
                        ParentCommandFixture.RecordIgnored();
                    }} />

                <NavButton
                    Geometries={Array.Empty<StreamGeometry>()}
                    NavigateTo={async button => {
                        await Task.Yield();
                        ParentCommandFixture.Record(button);
                    }} />
            </StackPanel>
            """;
        const string childSource =
            """
            using System.Collections.Generic;
            using Avalonia.Controls;
            using Avalonia.Media;

            namespace Demo;

            param bool IsActive = false;
            param IList<StreamGeometry> Geometries;
            command void NavigateTo(NavButton button);

            <Button Command={async () => await NavigateTo.Execute(this)}>
                Navigate
            </Button>
            """;
        const string host =
            """
            namespace Demo;

            public partial class MainView : Akbura.AkburaControl
            {
                public MainView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public Avalonia.Controls.Control FirstForTest() => FirstUpdate();
            }

            public partial class NavButton : Akbura.AkburaControl
            {
                public NavButton() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public Avalonia.Controls.Control FirstForTest() => FirstUpdate();
            }

            public static class ParentCommandFixture
            {
                private static readonly System.Threading.Tasks.TaskCompletionSource s_allCalled =
                    new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

                public static int Calls { get; private set; }
                public static System.Collections.Generic.IReadOnlyList<object?> Seen => s_seen;
                public static System.Threading.Tasks.Task AllCalled => s_allCalled.Task;
                private static readonly System.Collections.Generic.List<object?> s_seen = new();

                public static void Record(NavButton button) => Add(button);
                public static void RecordIgnored() => Add(null);

                private static void Add(object? value)
                {
                    s_seen.Add(value);
                    Calls++;
                    if (Calls == 3)
                    {
                        s_allCalled.TrySetResult();
                    }
                }
            }
            """;
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = CSharpCompilation.Create(
            "ICommandParentChild_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(host, options)],
            SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var parentTree = AkburaSyntaxTree.ParseText(parentSource, "MainView.akbura");
        var childTree = AkburaSyntaxTree.ParseText(childSource, "NavButton.akbura");
        var compilation = new AkburaCompilation(
            csharpCompilation,
            [parentTree, childTree],
            rootNamespace: "Demo");
        var generated = new List<string>();
        foreach (var tree in new[] { parentTree, childTree })
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            Assert.Empty(semanticModel.GetSemanticDiagnostics(tree.GetRoot()));
            var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
                semanticModel.GetSymbolInfo(tree.GetRoot()).Symbol);
            generated.Add(ComponentDocumentWriter.Generate(
                symbol,
                semanticModel,
                tree.FilePath,
                new Dictionary<Language.Syntax.AkburaSyntax, string>(),
                mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect)
                .ToString());
        }

        var emittedCompilation = csharpCompilation.AddSyntaxTrees(generated.Select(source =>
            CSharpSyntaxTree.ParseText(source, options)));
        var diagnostics = emittedCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.AsEnumerable()) +
            Environment.NewLine + string.Join(Environment.NewLine, generated));
        using var output = new MemoryStream();
        var emitted = emittedCompilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.AsEnumerable()));
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.MainView"));
    }
}
