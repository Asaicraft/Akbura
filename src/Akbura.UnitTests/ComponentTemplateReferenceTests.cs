using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class ComponentTemplateReferenceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Generate_SourceComponentTemplateRootTakesPrecedenceOverImportedClrType(
        bool isDataTemplate,
        bool hasCodeBehind)
    {
        const string hostSource =
            """
            using Avalonia.Controls;
            using Demo.Components;
            using OtherTemplates;

            <ContentControl>
                <ContentControl.ContentTemplate>
                    <TemplateContent />
                </ContentControl.ContentTemplate>
            </ContentControl>
            """;
        const string childSource =
            """
            using Avalonia.Controls;

            <TextBlock Text="Source component" />
            """;
        const string dataTemplateSource =
            """
            namespace OtherTemplates;

            public sealed class TemplateContent : global::Avalonia.Controls.Templates.IDataTemplate
            {
                public bool Match(object? data) => true;

                public global::Avalonia.Controls.Control? Build(object? data) => null;
            }
            """;
        const string objectSource =
            """
            namespace OtherTemplates;

            public sealed class TemplateContent
            {
            }
            """;
        var csharpSource = isDataTemplate ? dataTemplateSource : objectSource;
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "ComponentTemplateReferenceTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(csharpSource, parseOptions)],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        if (hasCodeBehind)
        {
            const string codeBehind =
                """
                namespace Demo.Components;

                public partial class TemplateContent
                {
                }
                """;
            compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(codeBehind, parseOptions));
        }

        var projectDirectory = Path.Combine(Path.GetTempPath(), "ComponentTemplateReferenceTests");
        var hostTree = AkburaSyntaxTree.ParseText(
            hostSource,
            Path.Combine(projectDirectory, "Pages", "Host.akbura"));
        var childTree = AkburaSyntaxTree.ParseText(
            childSource,
            Path.Combine(projectDirectory, "Components", "TemplateContent.akbura"));
        var catalog = AkburaGenerationCatalogBuilder.Create(
            compilation,
            [hostTree, childTree],
            "Demo",
            projectDirectory);
        var host = Assert.Single(catalog.Components, input => input.Component.Name == "Host");

        Assert.DoesNotContain(
            host.SemanticModel.GetSemanticDiagnostics(hostTree.GetRoot()),
            static diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error);

        using var codeWriter = new CodeWriter("\r\n");
        using var componentWriter = new ComponentWriter(
            codeWriter,
            host.Component,
            host.SemanticModel,
            host.SourcePath,
            catalog.AkcssModuleTypeNames);
        ref readonly var plan = ref componentWriter.Plan;
        var child = Assert.Single(plan.Elements, element =>
            element.Syntax.StartTag?.Name.ToFullString().Trim() == "TemplateContent");

        Assert.Equal(
            "global::Demo.Components.TemplateContent",
            child.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.True(child.IsControl);
        Assert.True(child.IsLocal);
        var template = Assert.Single(plan.Templates);
        Assert.Equal(template.ScopeId, child.ScopeId);
        Assert.True(TemplateWriter.CanWrite(plan, template));

        foreach (var input in catalog.Components)
        {
            var generatedText = ComponentDocumentWriter.Generate(
                input.Component,
                input.SemanticModel,
                input.SourcePath,
                catalog.AkcssModuleTypeNames);
            var generatedTree = CSharpSyntaxTree.ParseText(generatedText, parseOptions);
            compilation = compilation.AddSyntaxTrees(generatedTree);
        }

        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    [Fact]
    public void Generate_NonControlTemplateRootRemainsInvalidWithoutLeakingLocalReferences()
    {
        const string componentSource =
            """
            using Avalonia.Controls;
            using Demo;

            <ContentControl>
                <ContentControl.ContentTemplate>
                    <Payload />
                </ContentControl.ContentTemplate>
            </ContentControl>
            """;
        const string csharpSource =
            """
            namespace Demo;

            public sealed class Payload
            {
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(componentSource, csharpSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var semanticDiagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Contains(semanticDiagnostics, static diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild);

        using var codeWriter = new CodeWriter("\r\n");
        using var componentWriter = new ComponentWriter(
            codeWriter,
            component,
            fixture.SemanticModel,
            "PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());
        ref readonly var plan = ref componentWriter.Plan;
        var payload = Assert.Single(plan.Elements, element =>
            element.Syntax.StartTag?.Name.ToFullString().Trim() == "Payload");

        Assert.False(payload.IsControl);
        Assert.Empty(plan.Templates);
        Assert.Empty(plan.PropertyContents);

        var generatedText = ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());
        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedText,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var diagnostics = fixture.CSharpCompilation.AddSyntaxTrees(generatedTree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }
}
