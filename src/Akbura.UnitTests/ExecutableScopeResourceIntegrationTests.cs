using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akbura.Language;
using Xunit;
using System.Reflection;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ExecutableScopeResourceIntegrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NestedOutVariable_BindsAndRunsWithoutDuplicatingEnclosingIf(
        bool structural, bool useIntermediateLocal)
    {
        var assignment = useIntermediateLocal
            ? "var brush = found as IBrush; activeColor = brush;"
            : "activeColor = found as IBrush;";
        var source = """
            using Avalonia.Controls;
            using Avalonia.Media;
            state IBrush? activeColor = null;
            if (activeColor == null)
            {
                if (TryColor(out var found))
                {
                    ASSIGNMENT
                }
            }
            <Border Background={activeColor}/>
            """.Replace("ASSIGNMENT", assignment);
        var ownerSource = """
            using Akbura;
            using Akbura.Engine;
            using Avalonia.Media;
            namespace Demo;
            public partial class PlannerView : AkburaControl
            {
                public PlannerView() : base(AkburaEngine.Empty) { }
                public int Calls { get; private set; }
                public bool TryColor(out object? value)
                {
                    Calls++;
                    value = Brushes.Red;
                    return true;
                }
            }
            """;
        var type = Compile(source, ownerSource, structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var border = Assert.IsType<Border>(owner.Child);
                Assert.Same(Avalonia.Media.Brushes.Red, border.Background);
                Assert.Equal(1, (int)type.GetProperty("Calls")!.GetValue(owner)!);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualTryFindResource_AndBothHookNamesCompile(bool structural)
    {
        const string source = """
            using Akbura.Hooks;
            using Avalonia.Controls;
            using Avalonia.Media;
            state IBrush? activeColor = null;
            state IBrush? staticColor = useStaticResource<IBrush?>("--color-teal-300");
            state IBrush? dynamicColor = useDynamicResource<IBrush?>("--color-teal-300");
            if (activeColor == null)
            {
                if (this.TryFindResource("--color-teal-300", this.ActualThemeVariant, out var found))
                {
                    activeColor = found as IBrush;
                }
            }
            <Border Background={dynamicColor}/>
            """;
        _ = Compile(source, EmptyOwner, structural);
    }

    [Theory]
    [InlineData("state object? value = null; if (true) { if (int.TryParse(\"1\", out var inner)) { value = inner; } } value = inner;")]
    [InlineData("state object? value = null; if (true) { if (false && int.TryParse(\"1\", out var found)) { } value = found; }")]
    public void MissingScopeOrDefiniteAssignment_IsStillDiagnosed(string source)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; " + source + " <Border/>", EmptyOwner);
        var errors = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot())
            .Where(d => d.Severity == AkburaDiagnosticSeverity.Error).ToArray();
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryIfPatternVariable_RemainsInsideItsDefiniteAssignmentBranch(bool structural)
    {
        const string source = """
            using Avalonia.Controls;
            state object input = "hello";
            state string output = "";
            if (input is string text)
            {
                var result = text.ToUpperInvariant();
                output = result;
            }
            <TextBlock Text={output}/>
            """;
        _ = Compile(source, EmptyOwner, structural);
    }

    [Fact]
    public void ObjectResult_IsNotSilentlyConvertedToBrush()
    {
        const string source = """
            using Avalonia.Controls;
            using Avalonia.Media;
            state IBrush? activeColor = null;
            if (activeColor == null)
            {
                if (this.TryFindResource("x", this.ActualThemeVariant, out var found))
                {
                    activeColor = found;
                }
            }
            <Border/>
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, EmptyOwner);
        var errors = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot())
            .Where(d => d.Severity == AkburaDiagnosticSeverity.Error).ToArray();
        Assert.NotEmpty(errors);
        Assert.DoesNotContain(errors, d => d.Message.Contains("does not exist in the current context"));
    }

    private static Type Compile(string source, string ownerSource, bool structural)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, ownerSource);
        var root = fixture.ComponentTree.GetRoot();
        Assert.Empty(root.GetDiagnostics());
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(root).Symbol);
        var errors = fixture.SemanticModel.GetSemanticDiagnostics(root)
            .Where(d => d.Severity == AkburaDiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine,
            errors.Select(d => d.Code + ": " + d.Message)));
        var generated = ComponentDocumentWriter.Generate(component, fixture.SemanticModel,
            "Views/PlannerView.akbura", new Dictionary<AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural) options = options.WithPreprocessorSymbols("DEBUG");
        var compilation = fixture.CSharpCompilation
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, options, "Scope.g.cs"))
            .WithAssemblyName("ScopeResources_" + Guid.NewGuid().ToString("N"));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics) + "\n" + generated);
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.PlannerView"));
    }

    private const string EmptyOwner = """
        using Akbura;
        using Akbura.Engine;
        namespace Demo;
        public partial class PlannerView : AkburaControl
        {
            public PlannerView() : base(AkburaEngine.Empty) { }
        }
        """;
}
