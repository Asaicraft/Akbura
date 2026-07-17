using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class SourceProjectReferenceTests
{
    [Fact]
    public void CompilationReference_InvalidatesOnlyChangedProjectAndDependents()
    {
        const string projectBSource =
            """
            namespace ProjectB;

            public sealed class AvaloniaOnlyControl : Avalonia.Controls.Control
            {
            }
            """;
        const string projectACSharpSource =
            """
            namespace ProjectA.Components;

            public sealed partial class LibraryCard : Akbura.AkburaControl
            {
                protected override Avalonia.Controls.Control Update() => this;
            }
            """;
        const string projectAComponentV1 =
            """
            namespace ProjectA.Components;

            param string Title;
            """;
        const string projectAComponentV2 =
            """
            namespace ProjectA.Components;

            param string Caption;
            """;
        const string projectAStylesV1 =
            """
            @using Avalonia.Controls;

            Control.shared {
                Width: 100;
            }
            """;
        const string projectAStylesV2 =
            """
            @using Avalonia.Controls;

            Control.shared {
                Width: 200;
            }
            """;
        const string projectCSourceV1 =
            """
            using ProjectA.Components;
            using ProjectA.Styles.Theme.akcss;

            <LibraryCard Title="C1" class="shared" />
            """;
        const string projectCSourceV2 =
            """
            using ProjectA.Components;
            using ProjectA.Styles.Theme.akcss;

            <LibraryCard Title="C2" class="shared" />
            """;
        const string projectCSourceV3 =
            """
            using ProjectA.Components;
            using ProjectA.Styles.Theme.akcss;

            <LibraryCard Caption="C3" class="shared" />
            """;

        var frameworkReferences = SymbolTests.CreateAvaloniaReferences();
        var projectB = CSharpCompilation.Create(
            "ProjectB",
            [CSharpSyntaxTree.ParseText(projectBSource)],
            frameworkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var projectBReference = projectB.ToMetadataReference();

        var projectACSharp = CSharpCompilation.Create(
            "ProjectA",
            [CSharpSyntaxTree.ParseText(projectACSharpSource)],
            frameworkReferences.Append(projectBReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var projectAComponentTreeV1 = AkburaSyntaxTree.ParseText(
            projectAComponentV1,
            "Components/LibraryCard.akbura");
        var projectAStylesTreeV1 = AkcssSyntaxTree.ParseText(
            projectAStylesV1,
            "Styles/Theme.akcss",
            "ProjectA.Styles.Theme.akcss");
        var projectA1 = new AkburaCompilation(
            projectACSharp,
            [projectAComponentTreeV1],
            [projectAStylesTreeV1],
            rootNamespace: "ProjectA");
        var projectAReference1 = projectA1.ToReference();

        var projectCSharp = CSharpCompilation.Create(
            "ProjectC",
            references: frameworkReferences
                .Append(projectBReference)
                .Append(projectAReference1.CSharpReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var projectCTreeV1 = AkburaSyntaxTree.ParseText(
            projectCSourceV1,
            "Views/MainView.akbura");
        var projectC1 = new AkburaCompilation(
            projectCSharp,
            [projectCTreeV1],
            ImmutableArray<AkcssSyntaxTree>.Empty,
            rootNamespace: "ProjectC",
            compilationReferences: [projectAReference1]);

        var projectAComponent1 = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            projectA1.GetSemanticModel(projectAComponentTreeV1)
                .GetDeclaredSymbol(projectAComponentTreeV1.GetRoot()));
        var projectCComponent1 = GetReferencedComponent(projectC1, projectCTreeV1);
        Assert.Same(projectAComponent1, projectCComponent1.AkburaComponent);
        Assert.Same(
            projectA1.GetSemanticModel(projectAComponentTreeV1),
            projectC1.GetSemanticModel(projectAComponentTreeV1));
        Assert.Equal("100", GetAppliedWidthExpression(projectC1, projectCTreeV1));
        Assert.Same(projectA1, Assert.Single(projectC1.CompilationReferences).Compilation);
        Assert.Contains(projectBReference, projectC1.CSharpCompilation.References);

        // Editing only C keeps both referenced project snapshots intact.
        var projectCTreeV2 = projectCTreeV1.WithChangedText(projectCSourceV2);
        var projectC2 = projectC1.ReplaceSyntaxTree(projectCTreeV1, projectCTreeV2);
        var projectCComponent2 = GetReferencedComponent(projectC2, projectCTreeV2);
        Assert.Same(projectC1.CSharpCompilation, projectC2.CSharpCompilation);
        Assert.Same(projectC1, projectC2.PreviousCompilation);
        Assert.Same(projectA1, Assert.Single(projectC2.CompilationReferences).Compilation);
        Assert.Same(projectAComponent1, projectCComponent2.AkburaComponent);
        Assert.Contains(projectBReference, projectC2.CSharpCompilation.References);
        Assert.Equal("100", GetAppliedWidthExpression(projectC2, projectCTreeV2));

        // Editing A creates a new A snapshot and invalidates C even though A's CLR API is unchanged.
        var projectAComponentTreeV2 = projectAComponentTreeV1.WithChangedText(projectAComponentV2);
        var projectAStylesTreeV2 = projectAStylesTreeV1.WithChangedText(projectAStylesV2);
        var projectA2 = projectA1
            .ReplaceSyntaxTree(projectAComponentTreeV1, projectAComponentTreeV2)
            .ReplaceAkcssSyntaxTree(projectAStylesTreeV1, projectAStylesTreeV2);
        var projectAReference2 = projectAReference1.WithCompilation(projectA2);
        var projectC3 = projectC2.WithCompilationReferences([projectAReference2]);
        var staleTitleAttribute = GetAttribute(projectCTreeV2, "Title");

        Assert.Same(projectA1.CSharpCompilation, projectA2.CSharpCompilation);
        Assert.Same(projectAReference1.CSharpReference, projectAReference2.CSharpReference);
        Assert.Contains(projectBReference, projectC3.CSharpCompilation.References);
        Assert.Same(projectC2.CSharpCompilation, projectC3.CSharpCompilation);
        Assert.Same(projectC2, projectC3.PreviousCompilation);
        Assert.Same(projectA2, Assert.Single(projectC3.CompilationReferences).Compilation);
        Assert.Contains(
            projectC3.GetSemanticModel(projectCTreeV2).GetSemanticDiagnostics(staleTitleAttribute),
            static diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound);
        Assert.Equal("200", GetAppliedWidthExpression(projectC3, projectCTreeV2));

        // Updating C against A's new public contract restores a valid binding.
        var projectCTreeV3 = projectCTreeV2.WithChangedText(projectCSourceV3);
        var projectC4 = projectC3.ReplaceSyntaxTree(projectCTreeV2, projectCTreeV3);
        var projectAComponent2 = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            projectA2.GetSemanticModel(projectAComponentTreeV2)
                .GetDeclaredSymbol(projectAComponentTreeV2.GetRoot()));
        var projectCComponent4 = GetReferencedComponent(projectC4, projectCTreeV3);
        var captionAttribute = GetAttribute(projectCTreeV3, "Caption");
        var captionOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            projectC4.GetSemanticModel(projectCTreeV3).GetOperation(captionAttribute));

        Assert.NotSame(projectAComponent1, projectAComponent2);
        Assert.Same(projectAComponent2, projectCComponent4.AkburaComponent);
        Assert.Equal("Caption", captionOperation.Property!.Parameter!.Name);
        Assert.True(projectC4.GetSemanticModel(projectCTreeV3)
            .GetSemanticDiagnostics(captionAttribute).IsEmpty);
        Assert.Equal("200", GetAppliedWidthExpression(projectC4, projectCTreeV3));
    }

    private static IMarkupComponentSymbol GetReferencedComponent(
        AkburaCompilation compilation,
        AkburaSyntaxTree syntaxTree)
    {
        var element = GetRootElement(syntaxTree);
        return Assert.IsAssignableFrom<IMarkupComponentSymbol>(
            compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(element).Symbol);
    }

    private static string GetAppliedWidthExpression(
        AkburaCompilation compilation,
        AkburaSyntaxTree syntaxTree)
    {
        var classAttribute = GetAttribute(syntaxTree, "class");
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            compilation.GetSemanticModel(syntaxTree).GetOperation(classAttribute));
        var style = Assert.Single(operation.AppliedAkcssSymbols);
        var setter = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(style.Operations));
        Assert.Equal("Width", setter.Property!.Name);
        return setter.Syntax.Expression.ToFullString().Trim();
    }

    private static MarkupAttributeSyntax GetAttribute(
        AkburaSyntaxTree syntaxTree,
        string name)
    {
        return Assert.Single(
            GetRootElement(syntaxTree).StartTag!.Attributes,
            attribute => string.Equals(
                AkburaSemanticModel.GetMarkupPropertyName(attribute),
                name,
                StringComparison.Ordinal));
    }

    private static MarkupElementSyntax GetRootElement(AkburaSyntaxTree syntaxTree)
    {
        return Assert.IsType<MarkupRootSyntax>(
            Assert.Single(
                syntaxTree.GetRoot().Members,
                static member => member is MarkupRootSyntax)).Element;
    }
}
