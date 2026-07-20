using Akbura.Language;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class UseHookSemanticTests
{
    private const string GenericHookSource =
        """
        using Akbura.CompilerAnotations;
        using Akbura.ComponentTree;
        using Avalonia;
        using Avalonia.Controls;

        namespace Hooks;

        public static class ControlHooks
        {
            [UseHook]
            public static State<double> useControlValue<T>(
                [Self] T control,
                AvaloniaProperty<double> property)
                where T : Control => null!;
        }
        """;

    [Fact]
    public void GenericSelf_IsInferredFromSyntheticComponent_AndPropertyIsSubstituted()
    {
        var (semanticModel, state) = CreateStateModel(
            "using Hooks;\nstate double width = useControlValue(Width);",
            GenericHookSource);

        var bound = Assert.IsType<BoundStateInitializer>(
            semanticModel.BindingSession.BindSemanticSyntax(state.Initializer));
        var invocation = Assert.IsType<BoundUseHookInvocation>(bound.UseHookInvocation);

        Assert.True(invocation.HasSyntheticSelf);
        Assert.True(invocation.HasPropertyArgumentSubstitution);
        Assert.Equal(UseHookSelfKind.Implicit, invocation.Hook.SelfKind);
        Assert.Equal("Counter", Assert.Single(invocation.TypeArguments).Name);
        Assert.Equal(2, invocation.EffectiveArguments.Length);
        Assert.Contains("WidthProperty", invocation.EffectiveInvocation.ToFullString());
        Assert.True(semanticModel.GetSemanticDiagnostics(state).IsEmpty);
    }

    [Fact]
    public void ExplicitSelf_IsPreserved_WhilePropertyIsSubstituted()
    {
        var (semanticModel, state) = CreateStateModel(
            "using Hooks;\nstate double width = useControlValue(this, Width);",
            GenericHookSource);

        var bound = Assert.IsType<BoundStateInitializer>(
            semanticModel.BindingSession.BindSemanticSyntax(state.Initializer));
        var invocation = Assert.IsType<BoundUseHookInvocation>(bound.UseHookInvocation);

        Assert.False(invocation.HasSyntheticSelf);
        Assert.True(invocation.HasPropertyArgumentSubstitution);
        Assert.Equal(UseHookSelfKind.Explicit, invocation.Hook.SelfKind);
        Assert.StartsWith("useControlValue(this", invocation.EffectiveInvocation.ToFullString());
        Assert.True(semanticModel.GetSemanticDiagnostics(state).IsEmpty);
    }

    [Fact]
    public void ExplicitOtherControlAndAvaloniaProperty_AreBoundWithoutSubstitution()
    {
        var (semanticModel, state) = CreateStateModel(
            "using Avalonia.Controls;\n" +
            "using Hooks;\n" +
            "state Control other = new Control();\n" +
            "state double width = useControlValue(other, Control.WidthProperty);",
            GenericHookSource,
            stateName: "width");

        var bound = Assert.IsType<BoundStateInitializer>(
            semanticModel.BindingSession.BindSemanticSyntax(state.Initializer));
        var invocation = Assert.IsType<BoundUseHookInvocation>(bound.UseHookInvocation);

        Assert.False(invocation.HasSyntheticSelf);
        Assert.False(invocation.HasPropertyArgumentSubstitution);
        Assert.Equal(UseHookSelfKind.Explicit, invocation.Hook.SelfKind);
        Assert.Equal("Control", Assert.Single(invocation.TypeArguments).Name);
        Assert.Contains("Control.WidthProperty", invocation.EffectiveInvocation.ToFullString());
        Assert.True(semanticModel.GetSemanticDiagnostics(state).IsEmpty);
    }

    [Fact]
    public void OrdinaryCSharpArgument_HasPriorityOverAvaloniaPropertySubstitution()
    {
        const string hookSource =
            """
            using Akbura;
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;
            using Avalonia;

            namespace Hooks;

            public static class WidthHooks
            {
                [UseHook]
                public static State<double> useWidth([Self] AkburaControl control, double value) => null!;

                [UseHook]
                public static State<double> useWidth(
                    [Self] AkburaControl control,
                    AvaloniaProperty<double> property) => null!;
            }
            """;
        var (semanticModel, state) = CreateStateModel(
            "using Hooks;\nstate double width = useWidth(Width);",
            hookSource);

        var bound = Assert.IsType<BoundStateInitializer>(
            semanticModel.BindingSession.BindSemanticSyntax(state.Initializer));
        var invocation = Assert.IsType<BoundUseHookInvocation>(bound.UseHookInvocation);

        Assert.False(invocation.HasPropertyArgumentSubstitution);
        Assert.Equal(SpecialType.System_Double, invocation.Hook.Method.Parameters[1].Type.SpecialType);
        Assert.True(semanticModel.GetSemanticDiagnostics(state).IsEmpty);
    }

    [Fact]
    public void AvaloniaPropertyEffect_RewritesPropertiesInsideCollection()
    {
        const string code =
            "using Akbura.Hooks;\n" +
            "useAvaloniaProperty(() => { }, [Width, Height]);";
        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree);
        var statement = Assert.IsType<CSharpStatementSyntax>(
            syntaxTree.GetRoot().Members.Last());

        var bound = Assert.IsType<BoundUseHookStatement>(
            semanticModel.BindingSession.BindSemanticSyntax(statement));
        var invocation = bound.Invocation;

        Assert.True(invocation.HasSyntheticSelf);
        Assert.True(invocation.HasPropertyArgumentSubstitution);
        Assert.Contains("WidthProperty", invocation.EffectiveInvocation.ToFullString());
        Assert.Contains("HeightProperty", invocation.EffectiveInvocation.ToFullString());
        var propertiesParameter = Assert.IsAssignableFrom<INamedTypeSymbol>(
            invocation.Hook.Method.Parameters[2].Type);
        Assert.Equal("ReadOnlySpan", propertiesParameter.Name);
        Assert.Equal("AvaloniaProperty", Assert.Single(propertiesParameter.TypeArguments).Name);

        var operation = Assert.IsAssignableFrom<IUseHookOperation>(
            semanticModel.GetOperation(statement));
        Assert.Same(invocation.Hook, operation.Hook);
        Assert.True(semanticModel.GetSemanticDiagnostics(statement).IsEmpty);
    }

    [Fact]
    public void ExternalCustomRenderHook_CanUsePublicRuntimePrimitive()
    {
        const string hookSource =
            """
            using Akbura;
            using Akbura.CompilerAnotations;
            using Akbura.Hooks;

            namespace Hooks;

            public static class CustomHooks
            {
                private static readonly UseHookKey s_key = new();

                [UseHook]
                public static void useCounter([Self] AkburaControl control)
                {
                    control.UseHook(
                        s_key,
                        0,
                        static _ => new object(),
                        static (_, _) => { });
                }
            }
            """;
        var csharpCompilation = CSharpCompilation.Create(
            "ExternalCustomHooks",
            references: SymbolTests.CreateAvaloniaReferences(),
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    hookSource,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)),
            ],
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(
            csharpCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var syntaxTree = AkburaSyntaxTree.ParseText(
            "using Hooks;\nuseCounter();",
            "Counter.akbura");
        var semanticModel = new AkburaCompilation(csharpCompilation, [syntaxTree])
            .GetSemanticModel(syntaxTree);
        var statement = Assert.IsType<CSharpStatementSyntax>(
            syntaxTree.GetRoot().Members.Last());

        Assert.IsType<BoundUseHookStatement>(
            semanticModel.BindingSession.BindSemanticSyntax(statement));
        Assert.True(semanticModel.GetSemanticDiagnostics(statement).IsEmpty);
    }

    [Fact]
    public void GlobalUsing_MakesShortHookNameVisible()
    {
        var (semanticModel, state) = CreateStateModel(
            "state double width = useControlValue(Width);",
            "global using Hooks;\n" + GenericHookSource);

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(
            semanticModel.GetSymbolInfo(state).Symbol);

        Assert.Equal("useControlValue", Assert.IsAssignableFrom<IUseHookSymbol>(symbol.UseHook).Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(state).IsEmpty);
    }

    [Fact]
    public void CurrentNamespace_MakesShortHookNameVisible()
    {
        var (semanticModel, state) = CreateStateModel(
            "namespace Hooks;\nstate double width = useControlValue(Width);",
            GenericHookSource);

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(
            semanticModel.GetSymbolInfo(state).Symbol);

        Assert.Equal("useControlValue", Assert.IsAssignableFrom<IUseHookSymbol>(symbol.UseHook).Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(state).IsEmpty);
    }

    [Fact]
    public void GenericSelfConstraintFailure_ProducesDiagnostic()
    {
        const string hookSource =
            """
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;
            using Avalonia;
            using Avalonia.Controls;

            namespace Hooks;

            public static class ButtonHooks
            {
                [UseHook]
                public static State<double> useButtonValue<T>(
                    [Self] T control,
                    AvaloniaProperty<double> property)
                    where T : Button => null!;
            }
            """;
        var (semanticModel, state) = CreateStateModel(
            "using Hooks;\nstate double width = useButtonValue(Width);",
            hookSource);

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(
            semanticModel.GetSymbolInfo(state).Symbol);
        var diagnostics = semanticModel.GetSemanticDiagnostics(state);

        Assert.Null(symbol.UseHook);
        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Message.Contains("Button", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidSelfDeclaration_ProducesUseHookDiagnostic()
    {
        const string hookSource =
            """
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;

            namespace Hooks;

            public static class InvalidHooks
            {
                [UseHook]
                public static State<int> useInvalid(
                    [Self] object first,
                    [Self] object second) => null!;
            }
            """;
        var (semanticModel, state) = CreateStateModel(
            "using Hooks;\nstate int value = useInvalid(this);",
            hookSource);

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(state));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_UseHookInvalidDeclaration, diagnostic.Code);
        Assert.Contains("only one parameter", diagnostic.Message);
    }

    [Fact]
    public void QualifiedUseHookInsideBlock_ProducesTopLevelDiagnosticWithoutUsing()
    {
        const string hookSource =
            """
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;

            namespace Hooks;

            public static class QualifiedHooks
            {
                [UseHook]
                public static State<int> useValue<T>([Self] object control, T value) => null!;
            }
            """;
        const string code =
            """
            if (true)
            {
                Hooks.QualifiedHooks.useValue(1);
            }
            """;
        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, hookSource);
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(
            Assert.Single(syntaxTree.GetRoot().Members));
        var hookStatement = Assert.IsType<CSharpStatementSyntax>(
            Assert.Single(ifStatement.Body!.Tokens));

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(hookStatement));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_UseHookMustBeTopLevel, diagnostic.Code);
        Assert.Contains("useValue", diagnostic.Message);
    }

    [Theory]
    [InlineData("public State<int> useInvalid([Self] object self) => null!;", "public and static")]
    [InlineData("private static State<int> useInvalid([Self] object self) => null!;", "public and static")]
    [InlineData("public static State<int> useInvalid(object value, [Self] object self) => null!;", "must be first")]
    [InlineData("public static State<int> useInvalid([Self] ref object self) => null!;", "cannot be ref")]
    public void InvalidHookDeclarationRules_ProduceDiagnostic(string method, string expectedMessage)
    {
        var hookSource =
            "using Akbura.CompilerAnotations;\n" +
            "using Akbura.ComponentTree;\n" +
            "namespace Hooks;\n" +
            "public sealed class InvalidHooks\n" +
            "{\n" +
            "    [UseHook]\n" +
            "    " + method + "\n" +
            "}";
        var syntaxTree = AkburaSyntaxTree.ParseText(
            "using Hooks;\nstate int value = useInvalid();",
            "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, hookSource);
        var state = Assert.IsType<StateDeclarationSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is StateDeclarationSyntax));

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(state));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_UseHookInvalidDeclaration, diagnostic.Code);
        Assert.Contains(expectedMessage, diagnostic.Message);
    }

    [Fact]
    public void AmbiguousImportedHooks_ProduceCSharpDiagnostic()
    {
        const string hookSource =
            """
            using Akbura;
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;

            namespace Hooks;

            public static class FirstHooks
            {
                [UseHook]
                public static State<int> useValue([Self] AkburaControl control, int value) => null!;
            }

            public static class SecondHooks
            {
                [UseHook]
                public static State<int> useValue([Self] AkburaControl control, int value) => null!;
            }
            """;
        var (semanticModel, state) = CreateStateModel(
            "using Hooks;\nstate int value = useValue(1);",
            hookSource);

        var diagnostics = semanticModel.GetSemanticDiagnostics(state);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(
        "using Hooks;\nstate int value = useRender();",
        "public static void useRender([Akbura.CompilerAnotations.Self] AkburaControl control) { }")]
    [InlineData(
        "using Hooks;\nuseState();",
        "public static Akbura.ComponentTree.State<int> useState([Akbura.CompilerAnotations.Self] AkburaControl control) => null!;")]
    public void HookReturnKind_MustMatchItsContext(string akburaCode, string method)
    {
        var hookSource =
            "using Akbura;\n" +
            "using Akbura.CompilerAnotations;\n" +
            "using Akbura.ComponentTree;\n" +
            "namespace Hooks;\n" +
            "public static class ContextHooks\n" +
            "{\n" +
            "    [UseHook]\n" +
            "    " + method + "\n" +
            "}";
        var syntaxTree = AkburaSyntaxTree.ParseText(akburaCode, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, hookSource);
        var semanticSyntax = syntaxTree.GetRoot().Members.Last();

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(semanticSyntax));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_UseHookInvalidContext, diagnostic.Code);
    }

    private static (AkburaSemanticModel SemanticModel, StateDeclarationSyntax State) CreateStateModel(
        string akburaCode,
        string csharpSource,
        string? stateName = null)
    {
        var syntaxTree = AkburaSyntaxTree.ParseText(akburaCode, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, csharpSource);
        var state = Assert.IsType<StateDeclarationSyntax>(
            syntaxTree.GetRoot().Members.Single(member =>
                member is StateDeclarationSyntax candidate &&
                (stateName == null || candidate.Name.Identifier.ValueText == stateName)));
        return (semanticModel, state);
    }

    private static AkburaSemanticModel CreateSemanticModel(
        AkburaSyntaxTree syntaxTree,
        params string[] csharpSources)
    {
        var csharpCompilation = CSharpCompilation.Create(
            "UseHookSemanticTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            syntaxTrees: csharpSources.Select(source => CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))));
        return new AkburaCompilation(csharpCompilation, [syntaxTree])
            .GetSemanticModel(syntaxTree);
    }
}
