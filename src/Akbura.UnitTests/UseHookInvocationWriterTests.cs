using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

public sealed class UseHookInvocationWriterTests
{
    private const string ComponentSource =
        """
        namespace Demo;

        public partial class PlannerView : Akbura.AkburaControl
        {
            public PlannerView()
                : base(Akbura.Engine.AkburaEngine.Empty)
            {
            }
        }
        """;

    [Fact]
    public void Generate_InferredGenericHooksPreserveSelfAndPropertyBinding()
    {
        const string component =
            """
            using Avalonia.Controls;
            using HookLibrary;

            state double width = useControlValue(
                Width);

            useObserve(
                this,
                Width);

            <Border Width={width} />
            """;

        const string hooks =
            """
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;
            using Avalonia;
            using Avalonia.Controls;

            namespace HookLibrary;

            public static class ControlHooks
            {
                [UseHook]
                public static State<double> useControlValue<T>(
                    [Self] T owner,
                    AvaloniaProperty<double> property)
                    where T : Control => null!;

                [UseHook]
                public static State<string?> useControlValue<T>(
                    [Self] T owner,
                    AvaloniaProperty<string?> property)
                    where T : Control => null!;

                [UseHook]
                public static void useObserve<T>(
                    [Self] T owner,
                    AvaloniaProperty<double> property)
                    where T : Control
                {
                }

                [UseHook]
                public static void useObserve<T>(
                    [Self] T owner,
                    AvaloniaProperty<string?> property)
                    where T : Control
                {
                }
            }
            """;

        var output = GenerateAndCompile(component, hooks);

        foreach (var methodName in new[] { "useControlValue", "useObserve" })
        {
            var invocation = GetInvocation(output.Tree, methodName);
            var method = GetMethod(output, invocation);

            Assert.Equal("HookLibrary.ControlHooks", method.ContainingType.ToDisplayString());
            Assert.Equal("Demo.PlannerView", Assert.Single(method.TypeArguments).ToDisplayString());
            Assert.Equal(
                SpecialType.System_Double,
                Assert.Single(Assert.IsAssignableFrom<INamedTypeSymbol>(method.Parameters[1].Type).TypeArguments)
                    .SpecialType);

            Assert.Equal(2, invocation.ArgumentList.Arguments.Count);
            Assert.IsType<ThisExpressionSyntax>(invocation.ArgumentList.Arguments[0].Expression);
            Assert.Contains(
                "WidthProperty",
                invocation.ArgumentList.Arguments[1].ToString(),
                StringComparison.Ordinal);
            Assert.StartsWith(
                "global::HookLibrary.ControlHooks." + methodName + "<global::Demo.PlannerView>",
                invocation.Expression.ToString(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Generate_ExplicitNullableGenericHooksPreserveNamedArguments()
    {
        const string component =
            """
            using Avalonia.Controls;
            using HookLibrary;

            state string? message = useValue<string?>(value: null);

            useObserve<string?>(
                value: message);

            <TextBlock Text={message} />
            """;

        const string hooks =
            """
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;
            using Avalonia.Controls;

            namespace HookLibrary;

            public static class ValueHooks
            {
                [UseHook]
                public static State<T> useValue<T>([Self] Control owner, T value) => null!;

                [UseHook]
                public static void useObserve<T>([Self] Control owner, T value)
                {
                }
            }
            """;

        var output = GenerateAndCompile(component, hooks);

        foreach (var methodName in new[] { "useValue", "useObserve" })
        {
            var invocation = GetInvocation(output.Tree, methodName);
            var method = GetMethod(output, invocation);
            var typeArgument = Assert.Single(method.TypeArguments);

            Assert.Equal("HookLibrary.ValueHooks", method.ContainingType.ToDisplayString());
            Assert.Equal(SpecialType.System_String, typeArgument.SpecialType);
            Assert.Equal(NullableAnnotation.Annotated, typeArgument.NullableAnnotation);
            Assert.Equal(2, invocation.ArgumentList.Arguments.Count);
            Assert.IsType<ThisExpressionSyntax>(invocation.ArgumentList.Arguments[0].Expression);
            Assert.Equal("value", invocation.ArgumentList.Arguments[1].NameColon?.Name.Identifier.ValueText);
            Assert.Equal(
                "global::HookLibrary.ValueHooks." + methodName + "<string?>",
                invocation.Expression.ToString());
        }
    }

    private static GeneratedOutput GenerateAndCompile(string componentSource, string hooks)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            componentSource,
            ComponentSource);

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var hookTree = CSharpSyntaxTree.ParseText(hooks, parseOptions);
        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(hookTree);
        var akburaCompilation = new Akbura.Language.AkburaCompilation(
            compilation,
            [fixture.ComponentTree],
            rootNamespace: "Demo");

        var semanticModel = akburaCompilation.GetSemanticModel(fixture.ComponentTree);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);

        var generatedText = ComponentDocumentWriter.Generate(
            component,
            semanticModel,
            "PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>(),
            CancellationToken.None);

        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedText,
            parseOptions,
            path: "PlannerView.g.cs");

        var outputCompilation = compilation.AddSyntaxTrees(generatedTree);
        var diagnostics = outputCompilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedText.ToString());

        return new GeneratedOutput(outputCompilation, generatedTree);
    }

    private static InvocationExpressionSyntax GetInvocation(SyntaxTree tree, string methodName)
    {
        return Assert.Single(
            tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>(),
            invocation => invocation.Expression is MemberAccessExpressionSyntax member &&
                member.Name.Identifier.ValueText == methodName);
    }

    private static IMethodSymbol GetMethod(GeneratedOutput output, InvocationExpressionSyntax invocation)
    {
        return Assert.IsAssignableFrom<IMethodSymbol>(
            output.Compilation.GetSemanticModel(output.Tree).GetSymbolInfo(invocation).Symbol);
    }

    private readonly record struct GeneratedOutput(CSharpCompilation Compilation, SyntaxTree Tree);
}
