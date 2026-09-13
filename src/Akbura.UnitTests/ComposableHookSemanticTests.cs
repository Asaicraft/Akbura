using Akbura.Language;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using AkburaOperation = Akbura.Language.Operations.IOperation;

namespace Akbura.UnitTests;

public sealed class ComposableHookSemanticTests
{
    private const string HooksSource =
        """
        using System;
        using Akbura;
        using Akbura.CompilerAnotations;
        using Akbura.ComponentTree;

        namespace Hooks;

        public static class ComposedHooks
        {
            [UseHook]
            public static State<T> useDebounce<T>(
                [Self] AkburaControl control,
                State<T> state,
                int milliseconds) => null!;

            [UseHook]
            public static State<TResult> useDebounce<T, TResult>(
                [Self] AkburaControl control,
                State<T> state,
                Func<T, TResult> selector,
                int milliseconds) => null!;

            [UseHook]
            public static State<T> useMixed<T>(
                [Self] AkburaControl control,
                State<T> state,
                T value) => null!;

            [UseHook]
            public static State<int> useOverload(
                [Self] AkburaControl control,
                int value) => null!;

            [UseHook]
            public static State<int> useOverload(
                [Self] AkburaControl control,
                State<int> value) => null!;

            [UseHook]
            public static void useObserve(
                [Self] AkburaControl control,
                State<int> value) { }

            [UseHook]
            public static State<int> useAmbiguous(
                [Self] AkburaControl control,
                State<int> first,
                int second) => null!;

            [UseHook]
            public static State<int> useAmbiguous(
                [Self] AkburaControl control,
                int first,
                State<int> second) => null!;
        }

        public sealed class Model
        {
            public int count { get; set; }
        }
        """;

    [Fact]
    public void StateArgument_BindsLiveSourceAndPreservesConstructedMethod()
    {
        var (model, state) = CreateStateModel(
            """
            using Hooks;

            state int count = 0;
            state int result = useDebounce(count, 300);
            """);

        var bound = GetInvocation(model, state);
        var argument = Assert.Single(bound.StateArguments);
        Assert.Equal(1, argument.ArgumentIndex);
        Assert.Equal("count", argument.State.Name);
        Assert.Same(model.GetDeclaredSymbol(argument.State.DeclarationSyntax), argument.State);
        Assert.True(bound.HasSyntheticSelf);
        Assert.Equal(SpecialType.System_Int32, Assert.Single(bound.TypeArguments).SpecialType);
        Assert.Equal("count", bound.EffectiveInvocation.ArgumentList.Arguments[1].Expression.ToString());
        Assert.Equal("useDebounce(count, 300)", bound.OriginalInvocation.ToString());
        Assert.Equal("State", bound.EffectiveArguments[1].Type!.Name);
        Assert.Empty(model.GetSemanticDiagnostics(state));

        var operation = Assert.IsAssignableFrom<IUseHookOperation>(model.GetOperation(state.Initializer));
        Assert.Same(bound.Hook, operation.Hook);
        Assert.Same(argument.State, Assert.Single(operation.StateArguments).State);
        Assert.Contains(Descendants(operation), candidate =>
            ReferenceEquals(candidate.TargetSymbol, argument.State));
    }

    [Theory]
    [InlineData("x => x + 1", SpecialType.System_Int32)]
    [InlineData("x => x.ToString()", SpecialType.System_String)]
    [InlineData("count => count + 1", SpecialType.System_Int32)]
    [InlineData("x => count + x", SpecialType.System_Int32)]
    public void GenericSelector_InfersBothTypesWithoutRewritingLambda(
        string selector,
        SpecialType resultType)
    {
        var typeName = resultType == SpecialType.System_Int32 ? "int" : "string";
        var (model, state) = CreateStateModel(
            "using Hooks;\n" +
            "state int count = 0;\n" +
            "state " + typeName + " result = useDebounce(count, " + selector + ", 300);");

        var bound = GetInvocation(model, state);
        Assert.Equal(1, Assert.Single(bound.StateArguments).ArgumentIndex);
        Assert.Collection(
            bound.TypeArguments,
            type => Assert.Equal(SpecialType.System_Int32, type.SpecialType),
            type => Assert.Equal(resultType, type.SpecialType));
        Assert.Equal(selector, bound.EffectiveInvocation.ArgumentList.Arguments[2].Expression.ToString());
        Assert.Empty(model.GetSemanticDiagnostics(state));

        var operation = Assert.IsAssignableFrom<IUseHookOperation>(model.GetOperation(state.Initializer));
        Assert.DoesNotContain(Descendants(operation), candidate =>
            candidate is ICSharpOperation { RoslynKind: Microsoft.CodeAnalysis.OperationKind.ParameterReference } &&
            candidate.TargetSymbol is IStateSymbol);
    }

    [Theory]
    [InlineData("useDebounce(this, count, 300)", 1)]
    [InlineData("useDebounce(milliseconds: 300, state: count)", 2)]
    [InlineData("useDebounce((count), 300)", 1)]
    public void ExplicitSelfNamedAndParenthesizedArguments_PreserveSourceBinding(
        string invocation,
        int expectedIndex)
    {
        var (model, state) = CreateStateModel(
            "using Hooks;\nstate int count = 0;\nstate int result = " + invocation + ";");

        var bound = GetInvocation(model, state);
        var argument = Assert.Single(bound.StateArguments);
        Assert.Equal(expectedIndex, argument.ArgumentIndex);
        Assert.Equal("count", argument.State.Name);
        Assert.Equal(invocation, bound.OriginalInvocation.ToString());
        Assert.Empty(model.GetSemanticDiagnostics(state));
    }

    [Fact]
    public void SameStateInValueParameter_RemainsValue()
    {
        var (model, state) = CreateStateModel(
            """
            using Hooks;
            state int count = 0;
            state int result = useMixed(count, count);
            """);

        var bound = GetInvocation(model, state);
        Assert.Equal(1, Assert.Single(bound.StateArguments).ArgumentIndex);
        Assert.Equal("State", bound.EffectiveArguments[1].Type!.Name);
        Assert.Equal(SpecialType.System_Int32, bound.EffectiveArguments[2].Type!.SpecialType);
        Assert.Empty(model.GetSemanticDiagnostics(state));
    }

    [Fact]
    public void NullableStateArgument_PreservesItsGenericTypeAnnotation()
    {
        var (model, state) = CreateStateModel(
            """
            using Hooks;
            state string? count = null;
            state string? result = useDebounce(count, 300);
            """);

        var bound = GetInvocation(model, state);
        Assert.Single(bound.StateArguments);
        var type = Assert.Single(bound.TypeArguments);
        Assert.Equal(SpecialType.System_String, type.SpecialType);
        Assert.Equal(NullableAnnotation.Annotated, type.NullableAnnotation);
        Assert.Empty(model.GetSemanticDiagnostics(state));
    }

    [Theory]
    [InlineData("count")]
    [InlineData("count + 1")]
    public void OrdinaryValueOverload_TakesPriority(string expression)
    {
        var (model, state) = CreateStateModel(
            "using Hooks;\nstate int count = 0;\nstate int result = useOverload(" + expression + ");");

        var bound = GetInvocation(model, state);
        Assert.Empty(bound.StateArguments);
        Assert.Equal(SpecialType.System_Int32, bound.Hook.Method.Parameters[1].Type.SpecialType);
        Assert.Equal(expression, bound.EffectiveInvocation.ArgumentList.Arguments[1].Expression.ToString());
        Assert.Empty(model.GetSemanticDiagnostics(state));
    }

    [Theory]
    [InlineData("count + 1")]
    [InlineData("other.count")]
    public void ValueExpressionAndForeignProperty_AreNotStateReferences(string expression)
    {
        var (model, state) = CreateStateModel(
            "using Hooks;\n" +
            "state int count = 0;\n" +
            "state Model other = new Model();\n" +
            "state int result = useDebounce(" + expression + ", 300);");

        Assert.NotEmpty(model.GetSemanticDiagnostics(state));
        Assert.Null(Assert.IsAssignableFrom<IStateSymbol>(model.GetDeclaredSymbol(state)).UseHook);
    }

    [Fact]
    public void ForwardStateReference_IsRejectedRegardlessOfBindingOrder()
    {
        var (model, state) = CreateStateModel(
            """
            using Hooks;
            state int result = useDebounce(count, 300);
            state int count = 0;
            """);
        var source = model.SyntaxTree.GetRoot().Members.OfType<StateDeclarationSyntax>().Last();
        Assert.NotNull(model.GetDeclaredSymbol(source));

        Assert.Contains(model.GetSemanticDiagnostics(state), diagnostic =>
            diagnostic.Message.Contains("count", StringComparison.Ordinal));
        Assert.Null(Assert.IsAssignableFrom<IStateSymbol>(model.GetDeclaredSymbol(state)).UseHook);
    }

    [Fact]
    public void StateAdaptationWithDifferentValidArgumentSets_ReportsAmbiguity()
    {
        var (model, state) = CreateStateModel(
            """
            using Hooks;
            state int count = 0;
            state int result = useAmbiguous(count, count);
            """);

        Assert.Contains(model.GetSemanticDiagnostics(state), diagnostic =>
            diagnostic.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RenderHook_UsesTheSameTypedStateAdaptation()
    {
        var tree = AkburaSyntaxTree.ParseText(
            "using Hooks;\nstate int count = 0;\nuseObserve(count);",
            "Counter.akbura");
        var model = CreateModel(tree);
        var statement = tree.GetRoot().Members.Last();
        var bound = Assert.IsType<BoundUseHookStatement>(model.BindingSession.BindSemanticSyntax(statement));

        Assert.Equal("count", Assert.Single(bound.Invocation.StateArguments).State.Name);
        Assert.Empty(model.GetSemanticDiagnostics(statement));
    }

    [Theory]
    [InlineData("useDebounce(count, 300)", "int")]
    [InlineData("useDebounce(count, count => count + 1, 300)", "int")]
    [InlineData("useDebounce(milliseconds: 300, state: count)", "int")]
    [InlineData("useDebounce(count, x => x.ToString(), 300)", "string")]
    public void ExtensionStateHook_PreservesOriginalMethodAndTypedArguments(
        string invocation,
        string resultType)
    {
        var (model, state) = CreateStateModel(
            "using Hooks;\nstate int count = 0;\nstate " + resultType + " result = " + invocation + ";",
            extensionHooks: true);

        var bound = GetInvocation(model, state);
        Assert.True(bound.Hook.Method.IsExtensionMethod);
        Assert.Equal("Hooks.ComposedHooks", bound.Hook.Method.ContainingType.ToDisplayString());
        Assert.Single(bound.StateArguments);
        Assert.Equal(invocation, bound.OriginalInvocation.ToString());
        var operation = Assert.IsAssignableFrom<IUseHookOperation>(model.GetOperation(state.Initializer));
        Assert.Same(bound.Hook.Method, operation.Method);
        Assert.Equal("Hooks.ComposedHooks", operation.Method.ContainingType.ToDisplayString());
        Assert.Empty(model.GetSemanticDiagnostics(state));
    }

    [Fact]
    public void ExtensionValueOverload_StillHasPriorityOverStateAdaptation()
    {
        var (model, state) = CreateStateModel(
            "using Hooks;\nstate int count = 0;\nstate int result = useOverload(count);",
            extensionHooks: true);

        var bound = GetInvocation(model, state);
        Assert.True(bound.Hook.Method.IsExtensionMethod);
        Assert.Empty(bound.StateArguments);
        Assert.Equal(SpecialType.System_Int32, bound.Hook.Method.Parameters[1].Type.SpecialType);
    }

    [Fact]
    public void ExtensionStateHook_PreservesNullableTypeArguments()
    {
        var (model, state) = CreateStateModel(
            "using Hooks;\nstate string? count = null;\nstate string? result = useDebounce(count, 300);",
            extensionHooks: true);

        var bound = GetInvocation(model, state);
        Assert.True(bound.Hook.Method.IsExtensionMethod);
        Assert.Equal(NullableAnnotation.Annotated, Assert.Single(bound.TypeArguments).NullableAnnotation);
        Assert.Single(bound.StateArguments);
    }

    [Fact]
    public void ImportedStaticValueOverload_HasPriorityOverExtensionStateOverload()
    {
        const string hooks =
            """
            using Akbura;
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;
            namespace Hooks;

            public static class ExtensionHooks
            {
                [UseHook]
                public static State<int> usePriority(
                    [Self] this AkburaControl control,
                    State<int> value) => null!;
            }

            public static class OrdinaryHooks
            {
                [UseHook]
                public static State<int> usePriority(
                    [Self] AkburaControl control,
                    int value) => null!;
            }
            """;
        var (model, state) = CreateStateModel(
            "using Hooks;\nstate int count = 0;\nstate int result = usePriority(count);",
            hookSource: hooks);

        var bound = GetInvocation(model, state);
        Assert.Equal("OrdinaryHooks", bound.Hook.Method.ContainingType.Name);
        Assert.Empty(bound.StateArguments);
    }

    [Fact]
    public void ExtensionStateHook_PreservesGenericSelfConstraints()
    {
        const string hooks =
            """
            using Akbura;
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;
            namespace Hooks;

            public static class GenericHooks
            {
                [UseHook]
                public static State<T> useGeneric<TOwner, T>(
                    [Self] this TOwner control,
                    State<T> value)
                    where TOwner : AkburaControl => null!;
            }
            """;
        var (model, state) = CreateStateModel(
            "using Hooks;\nstate int count = 0;\nstate int result = useGeneric(count);",
            hookSource: hooks);

        var bound = GetInvocation(model, state);
        Assert.True(bound.Hook.Method.IsExtensionMethod);
        Assert.Collection(
            bound.TypeArguments,
            owner => Assert.Equal("Counter", owner.Name),
            value => Assert.Equal(SpecialType.System_Int32, value.SpecialType));
        Assert.Single(bound.StateArguments);
    }

    private static BoundUseHookInvocation GetInvocation(
        AkburaSemanticModel model,
        StateDeclarationSyntax state)
    {
        Assert.Empty(model.GetSemanticDiagnostics(state));
        return Assert.IsType<BoundUseHookInvocation>(
            Assert.IsType<BoundStateInitializer>(model.BindingSession.BindSemanticSyntax(state.Initializer))
                .UseHookInvocation);
    }

    private static (AkburaSemanticModel Model, StateDeclarationSyntax State) CreateStateModel(
        string source,
        bool extensionHooks = false,
        string? hookSource = null)
    {
        var tree = AkburaSyntaxTree.ParseText(source, "Counter.akbura");
        return (
            CreateModel(tree, extensionHooks, hookSource),
            tree.GetRoot().Members.OfType<StateDeclarationSyntax>().Single(state =>
                state.Name.Identifier.ValueText == "result"));
    }

    private static AkburaSemanticModel CreateModel(
        AkburaSyntaxTree tree,
        bool extensionHooks = false,
        string? hookSource = null)
    {
        var compilation = CSharpCompilation.Create(
            "ComposableHookSemanticTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    hookSource ?? (extensionHooks
                        ? HooksSource.Replace("[Self] AkburaControl", "[Self] this AkburaControl")
                        : HooksSource),
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)),
            ]);
        return new AkburaCompilation(compilation, [tree]).GetSemanticModel(tree);
    }

    private static IEnumerable<AkburaOperation> Descendants(AkburaOperation operation)
    {
        yield return operation;
        foreach (var child in operation.Children)
        {
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
