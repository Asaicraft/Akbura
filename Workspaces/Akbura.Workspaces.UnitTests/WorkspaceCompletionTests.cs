using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceCompletionTests
{
    [Fact]
    public void CompletionResult_DefaultValueHasEmptyItems()
    {
        var result = default(AkburaCompletionResult);

        Assert.Empty(result.Items);
        Assert.True(result.Items.Length == 0);
        Assert.True(result.IsEmpty);
    }

    private const string CardSource = """
        namespace Gallery;

        using Avalonia.Controls;

        param string Title;
        param bool Compact = false;

        <StackPanel/>
        """;

    [Theory]
    [InlineData("", "")]
    [InlineData("st", "st")]
    public void Completion_TopLevelOffersStateWithoutSemanticContext(
        string source,
        string expectedPrefix)
    {
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Counter.akbura");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        var item = Assert.Single(result.Items);
        Assert.Equal("state", item.DisplayText);
        Assert.Equal("state", item.InsertText);
        Assert.Equal(AkburaCompletionKind.Keyword, item.Kind);
        Assert.Equal(
            expectedPrefix,
            document.Text.ToString(result.ApplicableSpan));
        Assert.False(result.IsIncomplete);
    }

    [Fact]
    public void Completion_TopLevelFiltersStateByPrefix()
    {
        const string source = "par";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Counter.akbura");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        Assert.Empty(result.Items);
    }

    [Fact]
    public void Completion_TopLevelOffersStateBeforeExistingMarkup()
    {
        const string sourceWithCaret = """
            state int count = 0;

            stat|

            <StackPanel gap-3>
                <Button Click={count--}/>
            </StackPanel>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Counter.akbura");
        using var workspace = new AkburaWorkspace();

        Assert.True(document.TryGetCSharpCompletionContext(
            position,
            out var csharpContext));
        Assert.Equal(
            AkburaCSharpCompletionContextKind.Statement,
            csharpContext.Kind);

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                position);

        var item = Assert.Single(result.Items);
        Assert.Equal("state", item.DisplayText);
        Assert.Equal(
            "stat",
            document.Text.ToString(result.ApplicableSpan));
    }

    [Fact]
    public void Completion_UsingOffersFullAkcssModuleName()
    {
        const string sourceWithCaret =
            "using Gallery.Components.Sty|";
        const string stylesSource = """
            @utilities {
            }
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceCompletionTests),
            Guid.NewGuid().ToString("N"));
        var componentsDirectory = Path.Combine(
            directory,
            "Components");
        Directory.CreateDirectory(componentsDirectory);

        try
        {
            var projectContext = new ProjectContext(
                ProjectId.CreateNewId(),
                Path.Combine(directory, "Gallery.csproj"),
                directory,
                "Gallery",
                CreateCompilation(),
                ImmutableArray<ProjectReference>.Empty);
            using var workspace = new AkburaWorkspace(projectContext);
            workspace.OpenOrChangeDocumentContext(
                new Uri(Path.Combine(
                    componentsDirectory,
                    "Styles.akcss")),
                SourceText.From(stylesSource));
            var appPath = Path.Combine(directory, "App.akbura");
            var text = SourceText.From(source);
            var semanticContext =
                workspace.OpenOrChangeDocumentContext(
                    new Uri(appPath),
                    text);
            var syntacticDocument = AkburaSyntacticDocument.Parse(
                text,
                appPath);

            var result = workspace.LanguageServices.Completion
                .GetCompletions(
                    syntacticDocument,
                    semanticContext,
                    position);

            var item = Assert.Single(
                result.Items,
                static item => item.Kind ==
                    AkburaCompletionKind.AkcssModule);
            Assert.Equal(
                "Gallery.Components.Styles.akcss",
                item.DisplayText);
            Assert.Equal(item.DisplayText, item.InsertText);
            Assert.Equal(
                "Gallery.Components.Sty",
                syntacticDocument.Text.ToString(
                    result.ApplicableSpan));
            Assert.True(result.IsIncomplete);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Completion_ComponentNameUsesVisibleTypesAndComponents()
    {
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;

            <Sta
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "StackPanel" &&
                        item.Kind == AkburaCompletionKind.Component);
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "Card");
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "AbstractView");
                Assert.Equal(
                    "Sta",
                    syntacticDocument.Text.ToString(
                        result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_AttributeUsesAkburaParametersAndClrMembers()
    {
        const string source = """
            namespace Gallery;

            <Card 
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Title" &&
                        item.Kind == AkburaCompletionKind.Parameter &&
                        item.InsertText == "Title=\"\"" &&
                        item.CaretOffsetFromEnd == 1);
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Compact");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Width");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Loaded");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "x.Name");
            });
    }

    [Fact]
    public void Completion_AvaloniaPropertyUsesComponentType()
    {
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;

            <StackPanel Wid></StackPanel>
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.IndexOf(
                            "Wid",
                            StringComparison.Ordinal) + 3);

                var item = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "Width");
                Assert.Equal(
                    AkburaCompletionKind.Property,
                    item.Kind);
                Assert.Equal("Width=\"\"", item.InsertText);
                Assert.Equal(1, item.CaretOffsetFromEnd);
                Assert.Equal(
                    "Wid",
                    syntacticDocument.Text.ToString(
                        result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_ComponentRequestsMemberCompletionAfterInsert()
    {
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;

            <Sta
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                var item = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "StackPanel");
                Assert.True(item.TriggerCompletionAfterInsert);
            });
    }

    [Fact]
    public void Completion_AttributeExcludesExistingMember()
    {
        const string source = """
            namespace Gallery;

            <Card Title="Hello" Com
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "Title");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Compact");
                Assert.Equal(
                    "Com",
                    syntacticDocument.Text.ToString(
                        result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_PropertyElementUsesParentComponent()
    {
        const string source = """
            namespace Gallery;

            <Card>
                <Card.Ti
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Card.Title" &&
                        item.Kind ==
                            AkburaCompletionKind.PropertyElement);
            });
    }

    [Fact]
    public void Completion_ClosingTagDoesNotRequireSemantics()
    {
        const string source = "<Card></";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "App.akbura");

        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        var item = Assert.Single(result.Items);
        Assert.Equal("Card", item.InsertText);
        Assert.Equal(AkburaCompletionKind.ClosingTag, item.Kind);
        Assert.False(result.IsIncomplete);
    }

    [Fact]
    public void Completion_SemanticContextCanArriveAfterPopup()
    {
        const string source = "<Sta";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "App.akbura");

        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        Assert.Empty(result.Items);
        Assert.True(result.IsIncomplete);
    }

    [Fact]
    public void Completion_MarkupExtensionUsesDuckTypedVisibleTypes()
    {
        const string source = """
            namespace Gallery;

            using Akbura.Markup;
            using Gallery.Extensions;

            <Card Content=${
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Binding" &&
                        item.Kind ==
                            AkburaCompletionKind.MarkupExtension);
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "StaticResource");
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "DynamicResource");
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "md" &&
                        item.Suffix == "utility variant");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "sm");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "lg");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "xl");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "xxl");
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Custom" &&
                        item.InsertText == "Custom");
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "PlainMarkup");
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "InheritedProbe");
                Assert.DoesNotContain(
                    result.Items,
                    static item =>
                        item.DisplayText == "InvalidProbe");

                var generic = Assert.Single(
                    result.Items,
                    static item =>
                        item.DisplayText == "GenericProbe");
                Assert.Equal("GenericProbe<>", generic.InsertText);
                Assert.Equal(1, generic.CaretOffsetFromEnd);
                Assert.True(result.IsIncomplete);
            });
    }

    [Fact]
    public void Completion_MarkupExtensionRequiresOrdinaryUsing()
    {
        const string source = """
            namespace Gallery;

            <Card Content=${Cus
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Empty(result.Items);
                Assert.Equal(
                    "Cus",
                    syntacticDocument.Text.ToString(
                        result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_MarkupExtensionSupportsNamespaceAlias()
    {
        const string source = """
            namespace Gallery;

            using extensions = Gallery.Extensions;

            <Card Content=${extensions::Cus
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText ==
                            "extensions::Custom");
            });
    }

    [Fact]
    public void Completion_ComponentPrefixIsStrictAndSystemTypesAreHidden()
    {
        const string source = """
            namespace Gallery;

            using System;
            using Avalonia.Controls;

            <
            """;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "StackPanel");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Card");
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "Boolean");
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "Exception");
                var card = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "Card");
                var stackPanel = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "StackPanel");
                Assert.Equal(0, card.Priority);
                Assert.Equal("Akbura component", card.Suffix);
                Assert.Equal(10, stackPanel.Priority);
                Assert.Equal("Avalonia.Controls", stackPanel.Suffix);
                Assert.True(
                    result.Items.IndexOf(card) <
                    result.Items.IndexOf(stackPanel));
                Assert.True(result.IsIncomplete);
            });
    }

    [Fact]
    public void Completion_AttributeIncludesImportedAkcssUtilities()
    {
        const string stylesSource = """
            @utilities {
                StackPanel.gap-(double value) {
                }

                StackPanel.flow-horizontal {
                }

                TextBlock.text-2xl {
                }
            }
            """;
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;
            using Styles.akcss;

            <StackPanel gap-
            """;

        WithWorkspace(
            source,
            stylesSource,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                var utility = Assert.Single(
                    result.Items,
                    static item =>
                        item.DisplayText == "gap-(double value)");
                Assert.Equal(
                    AkburaCompletionKind.TailwindUtility,
                    utility.Kind);
                Assert.Equal("gap-", utility.InsertText);
                Assert.Equal("gap-", utility.FilterText);
                Assert.Equal("StackPanel", utility.Suffix);
                Assert.Equal(
                    "gap-",
                    syntacticDocument.Text.ToString(
                        result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_InsertsParameterlessUtilityName()
    {
        const string stylesSource = """
            @utilities {
                StackPanel.flow-horizontal {
                }
            }
            """;
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;
            using Styles.akcss;

            <StackPanel flow
            """;

        WithWorkspace(
            source,
            stylesSource,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                var utility = Assert.Single(
                    result.Items,
                    static item =>
                        item.DisplayText == "flow-horizontal");
                Assert.Equal(
                    AkburaCompletionKind.TailwindUtility,
                    utility.Kind);
                Assert.Equal("flow-horizontal", utility.InsertText);
            });
    }

    [Fact]
    public void Completion_UtilityPreservesTypedArgument()
    {
        const string stylesSource = """
            @utilities {
                StackPanel.gap-(double value) {
                }
            }
            """;
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;
            using Styles.akcss;

            <StackPanel gap-3
            """;

        WithWorkspace(
            source,
            stylesSource,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                var utility = Assert.Single(
                    result.Items,
                    static item =>
                        item.DisplayText == "gap-(double value)");
                Assert.Equal("gap-3", utility.InsertText);
            });
    }

    [Fact]
    public void Completion_UtilityFiltersIncompatibleTargetType()
    {
        const string stylesSource = """
            @utilities {
                TextBlock.text-2xl {
                }
            }
            """;
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;
            using Styles.akcss;

            <StackPanel text
            """;

        WithWorkspace(
            source,
            stylesSource,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.DoesNotContain(
                    result.Items,
                    static item =>
                        item.Kind == AkburaCompletionKind.TailwindUtility);
            });
    }

    [Fact]
    public void Completion_UsesAkcssUtilityFromProjectReference()
    {
        const string stylesSource = """
            @utilities {
                StackPanel.gap-(double value) {
                }
            }
            """;
        const string source = """
            namespace Application;

            using Avalonia.Controls;
            using Library.Styles.akcss;

            <StackPanel gap-
            """;

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceCompletionTests),
            Guid.NewGuid().ToString("N"));
        var libraryDirectory = Path.Combine(directory, "Library");
        var applicationDirectory = Path.Combine(
            directory,
            "Application");
        Directory.CreateDirectory(libraryDirectory);
        Directory.CreateDirectory(applicationDirectory);

        try
        {
            var libraryProjectId = ProjectId.CreateNewId("Library");
            var applicationProjectId = ProjectId.CreateNewId(
                "Application");
            var libraryCompilation = CreateCompilation()
                .WithAssemblyName("Library");
            var applicationCompilation = CSharpCompilation.Create(
                "Application",
                [CSharpSyntaxTree.ParseText(
                    "namespace Application { " +
                    "public partial class App : " +
                    "Akbura.AkburaControl { } }")],
                CreatePlatformReferences().Append(
                    libraryCompilation.ToMetadataReference()),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var libraryContext = new ProjectContext(
                libraryProjectId,
                Path.Combine(libraryDirectory, "Library.csproj"),
                libraryDirectory,
                "Library",
                libraryCompilation,
                ImmutableArray<ProjectReference>.Empty);
            var applicationContext = new ProjectContext(
                applicationProjectId,
                Path.Combine(
                    applicationDirectory,
                    "Application.csproj"),
                applicationDirectory,
                "Application",
                applicationCompilation,
                [new ProjectReference(libraryProjectId)]);

            using var workspace = new AkburaWorkspace();
            var libraryProject = workspace.AddOrUpdateProject(
                libraryContext);
            workspace.OpenOrChangeDocumentContext(
                libraryProject.Id,
                new Uri(Path.Combine(
                    libraryDirectory,
                    "Styles.akcss")),
                SourceText.From(stylesSource));
            var applicationProject = workspace.AddOrUpdateProject(
                applicationContext);

            const string importSource = "using Library.Sty";
            var importResult = GetCompletionResult(
                workspace,
                applicationProject.Id,
                Path.Combine(applicationDirectory, "App.akbura"),
                importSource);
            Assert.Contains(
                importResult.Items,
                static item =>
                    item.DisplayText == "Library.Styles.akcss" &&
                    item.InsertText == "Library.Styles.akcss" &&
                    item.Kind == AkburaCompletionKind.AkcssModule);

            var result = GetCompletionResult(
                workspace,
                applicationProject.Id,
                Path.Combine(applicationDirectory, "App.akbura"),
                source);

            Assert.Contains(
                result.Items,
                static item =>
                    item.DisplayText == "gap-(double value)" &&
                    item.Kind ==
                        AkburaCompletionKind.TailwindUtility);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Completion_UtilityUsesGlobalAkcssUsing()
    {
        const string stylesSource = """
            @utilities {
                StackPanel.gap-(double value) {
                }
            }
            """;
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;

            <StackPanel gap-
            """;

        WithWorkspace(
            source,
            stylesSource,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "gap-(double value)" &&
                        item.Kind ==
                            AkburaCompletionKind.TailwindUtility);
            },
            globalUsingsSource:
                "global using Styles.akcss;");
    }

    [Fact]
    public void Completion_UtilityWorksAfterMarkupExtensionPrefix()
    {
        const string stylesSource = """
            @utilities {
                StackPanel.gap-(double value) {
                }
            }
            """;
        const string source = """
            namespace Gallery;

            using Akbura.Markup;
            using Avalonia.Controls;
            using Styles.akcss;

            <StackPanel ${md}:ga
            """;

        WithWorkspace(
            source,
            stylesSource,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                var utility = Assert.Single(
                    result.Items,
                    static item =>
                        item.DisplayText == "gap-(double value)");
                Assert.Equal("gap-", utility.InsertText);
                Assert.Equal(
                    "ga",
                    syntacticDocument.Text.ToString(
                        result.ApplicableSpan));
            });
    }

    [Fact]
    public void CompletionItem_ComputesDetailedDescriptionOnDemand()
    {
        var calls = 0;
        var item = new AkburaCompletionItem(
            "Card",
            "Card",
            AkburaCompletionKind.Component,
            description: string.Empty,
            descriptionFactory: () =>
            {
                calls++;
                return "global::Gallery.Card";
            });

        Assert.Equal(0, calls);
        Assert.Equal("global::Gallery.Card", item.Description);
        Assert.Equal("global::Gallery.Card", item.Description);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Completion_UsesAkburaComponentFromProjectReference()
    {
        const string libraryCSharpSource = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public double Width { get; set; }
                }

                public sealed class StackPanel : Control
                {
                }
            }

            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control
                {
                }
            }
            """;
        const string libraryComponentSource = """
            namespace Library;

            using Avalonia.Controls;

            param string Title;

            <StackPanel/>
            """;
        const string applicationCSharpSource = """
            namespace Application
            {
                public partial class App : Akbura.AkburaControl
                {
                }
            }
            """;
        const string componentCompletionSource = """
            namespace Application;

            using Library;

            <Car
            """;
        const string parameterCompletionSource = """
            namespace Application;

            using Library;

            <Card 
            """;

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceCompletionTests),
            Guid.NewGuid().ToString("N"));
        var libraryDirectory = Path.Combine(directory, "Library");
        var applicationDirectory = Path.Combine(directory, "Application");
        Directory.CreateDirectory(libraryDirectory);
        Directory.CreateDirectory(applicationDirectory);

        try
        {
            var platformReferences = CreatePlatformReferences();
            var libraryProjectId = ProjectId.CreateNewId("Library");
            var applicationProjectId = ProjectId.CreateNewId("Application");
            var libraryCompilation = CSharpCompilation.Create(
                "Library",
                [CSharpSyntaxTree.ParseText(libraryCSharpSource)],
                platformReferences,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var applicationCompilation = CSharpCompilation.Create(
                "Application",
                [CSharpSyntaxTree.ParseText(applicationCSharpSource)],
                platformReferences.Append(
                    libraryCompilation.ToMetadataReference()),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var libraryContext = new ProjectContext(
                libraryProjectId,
                Path.Combine(libraryDirectory, "Library.csproj"),
                libraryDirectory,
                "Library",
                libraryCompilation,
                ImmutableArray<ProjectReference>.Empty);
            var applicationContext = new ProjectContext(
                applicationProjectId,
                Path.Combine(applicationDirectory, "Application.csproj"),
                applicationDirectory,
                "Application",
                applicationCompilation,
                [new ProjectReference(libraryProjectId)]);

            using var workspace = new AkburaWorkspace();
            var libraryProject = workspace.AddOrUpdateProject(libraryContext);
            workspace.OpenOrChangeDocumentContext(
                libraryProject.Id,
                new Uri(Path.Combine(libraryDirectory, "Card.akbura")),
                SourceText.From(libraryComponentSource));
            var applicationProject = workspace.AddOrUpdateProject(
                applicationContext);

            var componentResult = GetCompletionResult(
                workspace,
                applicationProject.Id,
                Path.Combine(applicationDirectory, "App.akbura"),
                componentCompletionSource);
            Assert.Contains(
                componentResult.Items,
                static item => item.DisplayText == "Card");
            var applicationReference = Assert.Single(
                workspace.CurrentSolution
                    .GetRequiredProject(applicationProject.Id)
                    .Compilation
                    .CompilationReferences);
            Assert.Equal(
                0,
                applicationReference.CachedComponentSymbolCount);

            var parameterResult = GetCompletionResult(
                workspace,
                applicationProject.Id,
                Path.Combine(applicationDirectory, "App.akbura"),
                parameterCompletionSource);
            Assert.Contains(
                parameterResult.Items,
                static item =>
                    item.DisplayText == "Title" &&
                    item.Kind == AkburaCompletionKind.Parameter);
            applicationReference = Assert.Single(
                workspace.CurrentSolution
                    .GetRequiredProject(applicationProject.Id)
                    .Compilation
                    .CompilationReferences);
            Assert.Equal(
                1,
                applicationReference.CachedComponentSymbolCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Completion_UsesAkburaParametersFromMetadataReference()
    {
        const string source = """
            namespace Application;

            using Library;

            <Card 
            """;
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceCompletionTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var libraryReference =
                CreateEmbeddedComponentReference(directory);
            var applicationCompilation = CSharpCompilation.Create(
                "Application",
                [CSharpSyntaxTree.ParseText(
                    "namespace Application { " +
                    "public partial class App : " +
                    "Akbura.AkburaControl { } }")],
                CreatePlatformReferences().Append(libraryReference),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var projectContext = new ProjectContext(
                ProjectId.CreateNewId(),
                Path.Combine(directory, "Application.csproj"),
                directory,
                "Application",
                applicationCompilation,
                ImmutableArray<ProjectReference>.Empty);

            using var workspace = new AkburaWorkspace(projectContext);
            var result = GetCompletionResult(
                workspace,
                workspace.CurrentSolution.Projects.Keys.Single(),
                Path.Combine(directory, "App.akbura"),
                source);

            Assert.Contains(
                result.Items,
                static item =>
                    item.DisplayText == "Title" &&
                    item.Kind == AkburaCompletionKind.Parameter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WithWorkspace(
        string source,
        Action<
            AkburaWorkspace,
            AkburaDocumentContext,
            AkburaSyntacticDocument> assertion)
    {
        WithWorkspace(
            source,
            stylesSource: null,
            assertion);
    }

    private static void WithWorkspace(
        string source,
        string? stylesSource,
        Action<
            AkburaWorkspace,
            AkburaDocumentContext,
            AkburaSyntacticDocument> assertion,
        string? globalUsingsSource = null)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceCompletionTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var projectContext = new ProjectContext(
                ProjectId.CreateNewId(),
                projectFilePath: string.Empty,
                projectDirectory: directory,
                rootNamespace: string.Empty,
                CreateCompilation(),
                ImmutableArray<ProjectReference>.Empty);
            using var workspace = new AkburaWorkspace(projectContext);
            workspace.OpenOrChangeDocumentContext(
                new Uri(Path.Combine(directory, "Card.akbura")),
                SourceText.From(CardSource));
            if (stylesSource != null)
            {
                workspace.OpenOrChangeDocumentContext(
                    new Uri(Path.Combine(directory, "Styles.akcss")),
                    SourceText.From(stylesSource));
            }

            if (globalUsingsSource != null)
            {
                workspace.OpenOrChangeDocumentContext(
                    new Uri(Path.Combine(
                        directory,
                        "GlobalUsings.akbura")),
                    SourceText.From(globalUsingsSource));
            }

            var appPath = Path.Combine(directory, "App.akbura");
            var text = SourceText.From(source);
            var semanticContext =
                workspace.OpenOrChangeDocumentContext(
                    new Uri(appPath),
                    text);
            var syntacticDocument =
                AkburaSyntacticDocument.Parse(text, appPath);

            assertion(
                workspace,
                semanticContext,
                syntacticDocument);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CSharpProjection_MapsExpressionAndPreservesVisibleScope()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            state string title = "Akbura";
            var length = title.Length;

            <StackPanel x.Name="panel">
                <StackPanel Width={panel.|}/>
            </StackPanel>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));

                var projectedText = projection.Root.ToFullString();
                Assert.Equal(
                    syntacticDocument.Text.ToString(
                        completionContext.HostSpan),
                    projectedText.Substring(
                        projection.ProjectedSpan.Start,
                        projection.ProjectedSpan.Length));
                Assert.True(projection.TryMapPositionToHost(
                    projection.ProjectedPosition,
                    out var mappedPosition));
                Assert.Equal(position, mappedPosition);
                Assert.True(projection.TryMapToHost(
                    projection.ProjectedSpan,
                    out var mappedSpan));
                Assert.Equal(
                    completionContext.HostSpan,
                    mappedSpan);
                Assert.False(projection.TryMapToHost(
                    new TextSpan(
                        projection.ProjectedSpan.Start - 1,
                        projection.ProjectedSpan.Length),
                    out _));
                Assert.False(projection.TryMapToHost(
                    new TextSpan(
                        projection.ProjectedSpan.Start,
                        projection.ProjectedSpan.Length + 1),
                    out _));

                var localNames = projection.Root
                    .DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
                    .Select(static variable =>
                        variable.Identifier.ValueText)
                    .ToArray();
                Assert.Contains("title", localNames);
                Assert.Contains("length", localNames);
                Assert.Contains("panel", localNames);
                Assert.True(projection.IsStateName("title"));
                Assert.False(projection.IsStateName("length"));
                Assert.False(projection.IsStateName("panel"));
                Assert.Equal(
                    1,
                    localNames.Count(static name =>
                        name == "length"));
            });
    }

    [Fact]
    public void CSharpProjection_UsesCurrentTextWithStaleSemanticDocument()
    {
        const string semanticSource = """
            namespace Gallery;
            using Avalonia.Controls;
            state int count = 0;
            <StackPanel Width={}/>
            """;
        const string currentSource = """
            namespace Gallery;
            using Avalonia.Controls;
            state int count = 0;
            <StackPanel Width={c}/>
            """;

        WithWorkspace(
            semanticSource,
            (_, semanticContext, _) =>
            {
                var position = currentSource.IndexOf(
                    "{c}",
                    StringComparison.Ordinal) + 2;
                var syntacticDocument =
                    AkburaSyntacticDocument.Parse(
                        SourceText.From(currentSource),
                        semanticContext.Document.FilePath);
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));

                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));

                var projectedText = projection.Root.ToFullString();
                Assert.Equal(
                    "c",
                    projectedText.Substring(
                        projection.ProjectedSpan.Start,
                        projection.ProjectedSpan.Length));
                Assert.Contains(
                    projection.Root
                        .DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>(),
                    static variable =>
                        variable.Identifier.ValueText == "count");
                Assert.True(projection.IsStateName("count"));

                var parseOptions = semanticContext.Project
                    .CSharpCompilation.SyntaxTrees
                    .Select(static tree => tree.Options)
                    .OfType<CSharpParseOptions>()
                    .FirstOrDefault() ?? CSharpParseOptions.Default;
                var projectionTree = CSharpSyntaxTree.Create(
                    projection.Root,
                    parseOptions);
                var projectionCompilation = semanticContext.Project
                    .CSharpCompilation.AddSyntaxTrees(projectionTree);
                var projectionModel = projectionCompilation
                    .GetSemanticModel(projectionTree);
                Assert.Contains(
                    projectionModel.LookupSymbols(
                        projection.ProjectedPosition,
                        name: "count"),
                    static symbol => symbol.Name == "count");

                var completionList = RoslynCompletionTestHost
                    .GetCompletionsAsync(
                        semanticContext.Project.CSharpCompilation,
                        projection.Root,
                        projection.ProjectedPosition,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Assert.NotNull(completionList);
                var countCompletion = Assert.Single(
                    completionList.ItemsList,
                    static item => item.DisplayText == "count");
                Assert.True(projection.TryMapToHost(
                    countCompletion.Span,
                    out var countHostSpan),
                    $"Completion span {countCompletion.Span} is outside " +
                    $"projection span {projection.ProjectedSpan}.");
                Assert.Equal(
                    new TextSpan(
                        currentSource.IndexOf(
                            "{c}",
                            StringComparison.Ordinal) + 1,
                        1),
                    countHostSpan);
                Assert.True(projection.TryMapPositionToHost(
                    projection.ProjectedPosition,
                    out var mappedPosition));
                Assert.Equal(position, mappedPosition);
                Assert.Equal(
                    semanticSource,
                    semanticContext.Document.Text.ToString());
            });
    }

    [Fact]
    public void CSharpProjection_StateAndParamInitializersUseRoslynCompletion()
    {
        const string stateSource = """
            namespace Gallery;

            using System;
            using Avalonia.Controls;

            state int count = 0;
            state double maximum = Math.|;

            <StackPanel/>
            """;
        WithCSharpProjection(
            stateSource,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Expression,
                    context.Kind);
                Assert.True(projection.IsStateName("count"));
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "Max");
            });

        const string emptyStateSource = """
            namespace Gallery;

            using Avalonia.Controls;

            state int count = 0;
            state int maximum = |;

            <StackPanel/>
            """;
        WithCSharpProjection(
            emptyStateSource,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Expression,
                    context.Kind);
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "count");
            });

        const string paramSource = """
            namespace Gallery;

            using Avalonia.Controls;

            param string Prefix = "Akbura";
            param string Title = Prefix.ToUpp|;

            <StackPanel/>
            """;
        WithCSharpProjection(
            paramSource,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Expression,
                    context.Kind);
                Assert.Contains(
                    projection.Root.DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax
                            .VariableDeclaratorSyntax>(),
                    static variable =>
                        variable.Identifier.ValueText == "Prefix");
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "ToUpper");
            });
    }

    [Fact]
    public void CSharpProjection_StatementPreservesStatesAndPrecedingLocals()
    {
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;

            state int count = 0;
            var first = count;
            var second = fir|;

            <StackPanel/>
            """;

        WithCSharpProjection(
            source,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Statement,
                    context.Kind);
                Assert.True(projection.IsStateName("count"));
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "first");
            });
    }

    [Fact]
    public void CSharpProjection_IncompleteAndBodyStatementsMapExactly()
    {
        const string incompleteSource = """
            namespace Gallery;

            using Avalonia.Controls;

            state int count = 0;
            var value = |;

            <StackPanel/>
            """;
        WithCSharpProjection(
            incompleteSource,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Statement,
                    context.Kind);
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "count");
            });

        const string bodySource = """
            namespace Gallery;

            using System;
            using Avalonia.Controls;

            void Update(int value)
            {
                if (value > Math.A|)
                {
                    value++;
                }
            }

            <StackPanel/>
            """;
        WithCSharpProjection(
            bodySource,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Statement,
                    context.Kind);
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "Abs");
            });
    }

    [Fact]
    public void CSharpProjection_MethodBodyPreservesMethodContext()
    {
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;

            state string title = "Akbura";

            T Convert<T>(T value)
                where T : class
            {
                var local = title;
                return val|;
            }

            <StackPanel/>
            """;

        WithCSharpProjection(
            source,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Statement,
                    context.Kind);
                var probeMethod = projection.Root
                    .DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax
                        .MethodDeclarationSyntax>()
                    .Single(static method =>
                        method.Identifier.ValueText ==
                            "__akbura_statement_probe");
                Assert.Contains(
                    probeMethod.TypeParameterList!.Parameters,
                    static parameter =>
                        parameter.Identifier.ValueText == "T");
                Assert.Single(probeMethod.ConstraintClauses);
                Assert.Contains(
                    probeMethod.ParameterList.Parameters,
                    static parameter =>
                        parameter.Identifier.ValueText == "value");
                Assert.Contains(
                    probeMethod.Body!.DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax
                            .VariableDeclaratorSyntax>(),
                    static variable =>
                        variable.Identifier.ValueText == "local");
                Assert.True(projection.IsStateName("title"));
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "value");
            });
    }

    [Fact]
    public void CSharpProjection_MethodBodyPreservesAsyncModifier()
    {
        const string source = """
            namespace Gallery;

            using System.Threading;
            using System.Threading.Tasks;
            using Avalonia.Controls;

            state string title = "Akbura";

            async Task LoadAsync(CancellationToken cancellationToken)
            {
                var delay = 10;
                await Task.Del|;
            }

            <StackPanel/>
            """;

        WithCSharpProjection(
            source,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Statement,
                    context.Kind);
                var probeMethod = projection.Root
                    .DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax
                        .MethodDeclarationSyntax>()
                    .Single(static method =>
                        method.Identifier.ValueText ==
                            "__akbura_statement_probe");
                Assert.Contains(
                    probeMethod.Modifiers,
                    static modifier =>
                        modifier.IsKind(SyntaxKind.AsyncKeyword));
                Assert.Contains(
                    probeMethod.ParameterList.Parameters,
                    static parameter =>
                        parameter.Identifier.ValueText ==
                            "cancellationToken");
                Assert.Contains(
                    probeMethod.Body!.DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax
                            .VariableDeclaratorSyntax>(),
                    static variable =>
                        variable.Identifier.ValueText == "delay");
                Assert.True(projection.IsStateName("title"));
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "Delay");
            });
    }

    [Fact]
    public void CSharpProjection_DeclarationContextsUseRoslynCompletion()
    {
        var cases = new[]
        {
            (
                Source: """
                    namespace Gallery;
                    using Avalonia.Controls;
                    state Car| current = null;
                    <StackPanel/>
                    """,
                Kind: AkburaCSharpCompletionContextKind.Type,
                Item: "Card"),
            (
                Source: """
                    namespace Gallery;
                    using Avalonia.Controls;
                    command System.Threading.Tasks.ValueTa| Save();
                    <StackPanel/>
                    """,
                Kind: AkburaCSharpCompletionContextKind.Type,
                Item: "ValueTask"),
            (
                Source: """
                    namespace Gallery;
                    using System.Collections.Gen|;
                    using Avalonia.Controls;
                    <StackPanel/>
                    """,
                Kind: AkburaCSharpCompletionContextKind
                    .UsingDirectiveName,
                Item: "Generic"),
            (
                Source: """
                    namespace Gallery;
                    using |
                    """,
                Kind: AkburaCSharpCompletionContextKind
                    .UsingDirectiveName,
                Item: "System"),
            (
                Source: """
                    namespace Gallery;
                    using Akbura.|
                    """,
                Kind: AkburaCSharpCompletionContextKind
                    .UsingDirectiveName,
                Item: "Markup"),
            (
                Source: """
                    global using System.Collections.Gen|;
                    namespace Gallery;
                    using Avalonia.Controls;
                    <StackPanel/>
                    """,
                Kind: AkburaCSharpCompletionContextKind
                    .UsingDirectiveName,
                Item: "Generic"),
            (
                Source: """
                    namespace Gallery;
                    using static System.Ma|;
                    using Avalonia.Controls;
                    <StackPanel/>
                    """,
                Kind: AkburaCSharpCompletionContextKind
                    .UsingDirectiveName,
                Item: "Math"),
            (
                Source: """
                    namespace Gallery;
                    using Alias = System.Collections.Gen|;
                    using Avalonia.Controls;
                    <StackPanel/>
                    """,
                Kind: AkburaCSharpCompletionContextKind
                    .UsingDirectiveName,
                Item: "Generic"),
            (
                Source: """
                    namespace Gallery;
                    using unsafe Alias = System.IntP|*;
                    using Avalonia.Controls;
                    <StackPanel/>
                    """,
                Kind: AkburaCSharpCompletionContextKind
                    .UsingDirectiveName,
                Item: "IntPtr"),
            (
                Source: """
                    namespace Gallery;
                    using Avalonia.Controls;
                    command void Save(Car| model);
                    <StackPanel/>
                    """,
                Kind: AkburaCSharpCompletionContextKind
                    .CommandParameterList,
                Item: "Card"),
        };

        foreach (var testCase in cases)
        {
            WithCSharpProjection(
                testCase.Source,
                (semanticContext, context, projection, _) =>
                {
                    Assert.Equal(testCase.Kind, context.Kind);
                    AssertCompletionContains(
                        semanticContext,
                        projection,
                        testCase.Item);
                });
        }
    }

    [Fact]
    public void CSharpProjection_StatementUsesCurrentTextWithStaleSemantics()
    {
        const string semanticSource = """
            namespace Gallery;
            using Avalonia.Controls;
            state int count = 0;
            var text = c;
            <StackPanel/>
            """;
        const string currentSource = """
            namespace Gallery;
            using Avalonia.Controls;
            state int count = 0;
            var text = co|;
            <StackPanel/>
            """;
        var position = currentSource.IndexOf('|');
        var currentText = currentSource.Remove(position, 1);

        WithWorkspace(
            semanticSource,
            (_, semanticContext, _) =>
            {
                var syntacticDocument =
                    AkburaSyntacticDocument.Parse(
                        SourceText.From(currentText),
                        semanticContext.Document.FilePath);
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Statement,
                    completionContext.Kind);
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));

                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "count");
                Assert.True(projection.TryMapPositionToHost(
                    projection.ProjectedPosition,
                    out var mappedPosition));
                Assert.Equal(position, mappedPosition);
            });
    }

    [Fact]
    public void CSharpCompletionChangeMapper_MapsAutoImportAndPreservesCrLf()
    {
        const string sourceWithCaret =
            "global using System;\r\n" +
            "using Avalonia.Controls;\r\n" +
            "using Gallery.Styles.akcss;\r\n" +
            "\r\n" +
            "state Observ| items = default;\r\n" +
            "<StackPanel/>\r\n";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));

                var projectedText = SourceText.From(
                    projection.Root.ToFullString());
                var replacementSpan = new TextSpan(
                    projection.ActiveMapping.ProjectedSpan.Start,
                    "Observ".Length);
                var replacement = new TextChange(
                    replacementSpan,
                    "ObservableCollection");
                const string importText =
                    "using System.Collections.ObjectModel;\r\n";
                var import = new TextChange(
                    new TextSpan(0, 0),
                    importText);
                var changes = ImmutableArray.Create(import, replacement);
                var newProjectedPosition =
                    replacementSpan.Start +
                    importText.Length +
                    "ObservableCollection".Length;
                var changedProjectedText = projectedText.WithChanges(changes);
                var completionChange = CompletionChange.Create(
                    new TextChange(
                        new TextSpan(0, projectedText.Length),
                        changedProjectedText.ToString()),
                    changes,
                    newProjectedPosition,
                    includesCommitCharacter: false);

                Assert.True(
                    AkburaCSharpCompletionChangeMapper
                        .TryMapCompletionChange(
                            SourceText.From(source),
                            projectedText,
                            projection,
                            completionChange,
                            out var mapped));

                var changedHostText = SourceText.From(source)
                    .WithChanges(mapped.Changes)
                    .ToString();
                const string expected =
                    "global using System;\r\n" +
                    "using Avalonia.Controls;\r\n" +
                    "using System.Collections.ObjectModel;\r\n" +
                    "using Gallery.Styles.akcss;\r\n" +
                    "\r\n" +
                    "state ObservableCollection items = default;\r\n" +
                    "<StackPanel/>\r\n";
                Assert.Equal(expected, changedHostText);
                Assert.Equal(
                    expected.IndexOf(
                        "ObservableCollection",
                        StringComparison.Ordinal) +
                    "ObservableCollection".Length,
                    mapped.NewHostPosition);
                Assert.False(mapped.IncludesCommitCharacter);
            });
    }

    [Fact]
    public void CSharpCompletionChangeMapper_RejectsWrapperChanges()
    {
        const string sourceWithCaret = """
            using Avalonia.Controls;

            state int count = 0;
            var text = cou|;

            <StackPanel/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));

                var projectedText = SourceText.From(
                    projection.Root.ToFullString());
                var wrapperNameStart = projectedText.ToString().IndexOf(
                    "__akbura_statement_probe",
                    StringComparison.Ordinal);
                Assert.True(wrapperNameStart >= 0);
                var directChange = new TextChange(
                    projection.ActiveMapping.ProjectedSpan,
                    "count;");
                var wrapperChange = new TextChange(
                    new TextSpan(wrapperNameStart, 1),
                    "X");
                var changes = ImmutableArray.Create(
                    wrapperChange,
                    directChange);
                var changedProjectedText = projectedText.WithChanges(changes);
                var completionChange = CompletionChange.Create(
                    new TextChange(
                        new TextSpan(0, projectedText.Length),
                        changedProjectedText.ToString()),
                    changes,
                    newPosition: null,
                    includesCommitCharacter: false);

                Assert.False(
                    AkburaCSharpCompletionChangeMapper
                        .TryMapCompletionChange(
                            SourceText.From(source),
                            projectedText,
                            projection,
                            completionChange,
                            out _));
            });
    }

    [Fact]
    public void CSharpCompletionChangeMapper_MapsRoslynTypeAutoImport()
    {
        const string sourceWithCaret = """
            using Avalonia.Controls;

            state ObservableCollec| items = default;

            <StackPanel/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));

                var completion = RoslynCompletionTestHost
                    .GetImportCompletionAsync(
                        semanticContext.Project.CSharpCompilation,
                        projection.Root,
                        projection.ProjectedPosition,
                        "ObservableCollection",
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Assert.NotNull(completion);
                Assert.Contains(
                    "System.Collections.ObjectModel",
                    completion.Value.Item.InlineDescription,
                    StringComparison.Ordinal);

                Assert.True(
                    AkburaCSharpCompletionChangeMapper
                        .TryMapCompletionChange(
                            SourceText.From(source),
                            completion.Value.ProjectedText,
                            projection,
                            completion.Value.Change,
                            out var mapped));
                var changedHostText = SourceText.From(source)
                    .WithChanges(mapped.Changes)
                    .ToString();
                Assert.Contains(
                    "using System.Collections.ObjectModel;",
                    changedHostText,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "state ObservableCollection items",
                    changedHostText,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void CSharpCompletionChangeMapper_DoesNotDuplicateExistingImport()
    {
        const string sourceWithCaret = """
            using System.Collections.ObjectModel;
            using Avalonia.Controls;

            state ObservableCollec| items = default;

            <StackPanel/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));

                var completion = RoslynCompletionTestHost
                    .GetCompletionChangeAsync(
                        semanticContext.Project.CSharpCompilation,
                        projection.Root,
                        projection.ProjectedPosition,
                        "ObservableCollection",
                        requireComplexTextEdit: false,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Assert.NotNull(completion);
                Assert.True(
                    AkburaCSharpCompletionChangeMapper
                        .TryMapCompletionChange(
                            SourceText.From(source),
                            completion.Value.ProjectedText,
                            projection,
                            completion.Value.Change,
                            out var mapped));

                var changedHostText = SourceText.From(source)
                    .WithChanges(mapped.Changes)
                    .ToString();
                Assert.Equal(
                    1,
                    CountOccurrences(
                        changedHostText,
                        "using System.Collections.ObjectModel;"));
                Assert.Contains(
                    "state ObservableCollection items",
                    changedHostText,
                    StringComparison.Ordinal);
            });
    }

    [Theory]
    [InlineData(
        "var value = JsonSerializ|;",
        "JsonSerializer",
        "System.Text.Json",
        "var value = JsonSerializer;")]
    [InlineData(
        "command void Save(CancellationTok| token);",
        "CancellationToken",
        "System.Threading",
        "command void Save(CancellationToken token);")]
    [InlineData(
        "state string name = \"\";\nvar result = name.SomeExtens|;",
        "SomeExtension",
        "Gallery.Extensions",
        "name.SomeExtension")]
    public void CSharpCompletionChangeMapper_MapsRoslynAutoImports(
        string fragmentWithCaret,
        string displayText,
        string expectedNamespace,
        string expectedFragment)
    {
        var sourceWithCaret =
            "using Avalonia.Controls;\n\n" +
            fragmentWithCaret +
            "\n\n<StackPanel/>\n";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));

                var completion = RoslynCompletionTestHost
                    .GetImportCompletionAsync(
                        semanticContext.Project.CSharpCompilation,
                        projection.Root,
                        projection.ProjectedPosition,
                        displayText,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Assert.NotNull(completion);
                Assert.True(
                    AkburaCSharpCompletionChangeMapper
                        .TryMapCompletionChange(
                            SourceText.From(source),
                            completion.Value.ProjectedText,
                            projection,
                            completion.Value.Change,
                            out var mapped));

                var changedHostText = SourceText.From(source)
                    .WithChanges(mapped.Changes)
                    .ToString();
                Assert.Contains(
                    $"using {expectedNamespace};",
                    changedHostText,
                    StringComparison.Ordinal);
                Assert.Contains(
                    expectedFragment,
                    changedHostText,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void CSharpProjection_MapsRoslynQuickInfoToHost()
    {
        const string sourceWithCaret = """
            using Avalonia.Controls;

            state string title = "Akbura";
            var length = title.Len|gth;

            <StackPanel/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));

                var quickInfo = RoslynCompletionTestHost
                    .GetQuickInfoAsync(
                        semanticContext.Project.CSharpCompilation,
                        projection.Root,
                        projection.ProjectedPosition,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Assert.NotNull(quickInfo);
                Assert.True(projection.TryMapToHost(
                    quickInfo!.Span,
                    out var hostSpan));
                Assert.Equal("Length", source.Substring(
                    hostSpan.Start,
                    hostSpan.Length));
                Assert.Contains(
                    "Length",
                    string.Concat(
                        quickInfo.Sections.SelectMany(static section =>
                            section.TaggedParts)
                        .Select(static part => part.Text)),
                    StringComparison.Ordinal);
            });
    }

    [Theory]
    [InlineData(
        "state string title = string.Emp|ty;",
        "Empty")]
    [InlineData(
        "param string title = string.Emp|ty;",
        "Empty")]
    [InlineData(
        "string Format(string value) { return value.Tri|m(); }",
        "Trim")]
    [InlineData(
        "command void Save(System.Threading.CancellationTok|en token);",
        "CancellationToken")]
    public void CSharpProjection_MapsRoslynQuickInfoAcrossContexts(
        string fragmentWithCaret,
        string expectedToken)
    {
        var sourceWithCaret =
            "using Avalonia.Controls;\n\n" +
            fragmentWithCaret +
            "\n\n<StackPanel/>\n";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));

                var quickInfo = RoslynCompletionTestHost
                    .GetQuickInfoAsync(
                        semanticContext.Project.CSharpCompilation,
                        projection.Root,
                        projection.ProjectedPosition,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Assert.NotNull(quickInfo);
                Assert.True(projection.TryMapToHost(
                    quickInfo!.Span,
                    out var hostSpan));
                Assert.Equal(
                    expectedToken,
                    source.Substring(
                        hostSpan.Start,
                        hostSpan.Length));
            });
    }

    private static void WithCSharpProjection(
        string sourceWithCaret,
        Action<
            AkburaDocumentContext,
            AkburaCSharpCompletionContext,
            AkburaCSharpProjection,
            int> assertion)
    {
        var position = sourceWithCaret.IndexOf('|');
        Assert.True(position >= 0);
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));
                Assert.Equal(
                    syntacticDocument.Text.ToString(
                        completionContext.HostSpan),
                    projection.Root.ToFullString().Substring(
                        projection.ProjectedSpan.Start,
                        projection.ProjectedSpan.Length));
                Assert.True(projection.TryMapPositionToHost(
                    projection.ProjectedPosition,
                    out var mappedPosition));
                Assert.Equal(position, mappedPosition);

                assertion(
                    semanticContext,
                    completionContext,
                    projection,
                    position);
            });
    }

    private static int CountOccurrences(
        string value,
        string search)
    {
        var count = 0;
        var position = 0;
        while ((position = value.IndexOf(
                   search,
                   position,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += search.Length;
        }

        return count;
    }

    private static void AssertCompletionContains(
        AkburaDocumentContext semanticContext,
        AkburaCSharpProjection projection,
        string displayText)
    {
        var completionList = RoslynCompletionTestHost
            .GetCompletionsAsync(
                semanticContext.Project.CSharpCompilation,
                projection.Root,
                projection.ProjectedPosition,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.NotNull(completionList);
        Assert.Contains(
            completionList.ItemsList,
            item => item.DisplayText == displayText);
    }

    private static AkburaCompletionResult GetCompletionResult(
        AkburaWorkspace workspace,
        AkburaProjectId projectId,
        string path,
        string source)
    {
        var text = SourceText.From(source);
        var semanticContext = workspace.OpenOrChangeDocumentContext(
            projectId,
            new Uri(path),
            text);
        var syntacticDocument = AkburaSyntacticDocument.Parse(text, path);
        return workspace.LanguageServices.Completion.GetCompletions(
            syntacticDocument,
            semanticContext,
            source.Length);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public double Width { get; set; }

                    public event System.EventHandler? Loaded;
                }

                public sealed class StackPanel : Control
                {
                    public double Spacing { get; set; }
                }

                public abstract class AbstractView : Control
                {
                }
            }

            namespace Avalonia.Data
            {
                public class Binding
                {
                }

                public class ReflectionBinding
                {
                }

                public class CompiledBinding
                {
                }
            }

            namespace Avalonia.Markup.Xaml.MarkupExtensions
            {
                public sealed class StaticResourceExtension
                {
                    public StaticResourceExtension(object key)
                    {
                    }

                    public object ProvideValue(
                        System.IServiceProvider services) => new();
                }

                public sealed class DynamicResourceExtension
                {
                    public DynamicResourceExtension(object key)
                    {
                    }

                    public object ProvideValue() => new();
                }
            }

            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control
                {
                }
            }

            namespace Akbura.Markup
            {
                [System.AttributeUsage(
                    System.AttributeTargets.Class)]
                public sealed class UtilityVariantAttribute :
                    System.Attribute
                {
                }

                public abstract class BreakpointExtensionBase
                {
                    public bool ProvideValue() => true;
                }

                [UtilityVariant]
                public sealed class smExtension :
                    BreakpointExtensionBase
                {
                }

                [UtilityVariant]
                public sealed class mdExtension :
                    BreakpointExtensionBase
                {
                }

                [UtilityVariant]
                public sealed class lgExtension :
                    BreakpointExtensionBase
                {
                }

                [UtilityVariant]
                public sealed class xlExtension :
                    BreakpointExtensionBase
                {
                }

                [UtilityVariant]
                public sealed class xxlExtension :
                    BreakpointExtensionBase
                {
                }
            }

            namespace Gallery.Extensions
            {
                public static class StringExtensions
                {
                    public static int SomeExtension(
                        this string value) => value.Length;
                }

                public sealed class CustomExtension
                {
                    public object ProvideValue() => new();
                }

                public sealed class PlainMarkup
                {
                    public object ProvideValue() => new();
                }

                public class ProbeExtensionBase
                {
                    public object ProvideValue() => new();
                }

                public sealed class InheritedProbeExtension :
                    ProbeExtensionBase
                {
                }

                public sealed class GenericProbeExtension<T>
                {
                    public T? ProvideValue() => default;
                }

                public sealed class InvalidProbeExtension
                {
                }
            }

            namespace Gallery
            {
                public partial class Card : Akbura.AkburaControl
                {
                }

                public partial class App : Akbura.AkburaControl
                {
                }
            }
            """;

        return CSharpCompilation.Create(
            "WorkspaceCompletionTests",
            [CSharpSyntaxTree.ParseText(source)],
            CreatePlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static PortableExecutableReference
        CreateEmbeddedComponentReference(string directory)
    {
        const string componentSource = """
            namespace Library;

            using Avalonia.Controls;

            param string Title;

            <StackPanel/>
            """;
        const string csharpSource = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                }

                public sealed class StackPanel : Control
                {
                }
            }

            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control
                {
                }
            }

            namespace Library
            {
                public partial class Card : Akbura.AkburaControl
                {
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "Library",
            [CSharpSyntaxTree.ParseText(csharpSource)],
            CreatePlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var manifest = AkburaModuleManifestBuilder.Build(
            "Library",
            "Library",
            [new AkburaModuleSourceText(
                "Card.akbura",
                componentSource)],
            compilation);
        using var manifestStream = new MemoryStream();
        AkburaModuleManifestSerializer.Write(
            manifestStream,
            manifest);

        var assemblyPath = Path.Combine(directory, "Library.dll");
        var emitResult = compilation.Emit(
            assemblyPath,
            manifestResources:
            [
                CreateResource(
                    AkburaModuleManifest.ResourceName,
                    manifestStream.ToArray()),
                CreateEmbeddedSourceResource(
                    "Card.akbura",
                    componentSource),
            ]);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromFile(assemblyPath);
    }

    private static ResourceDescription CreateEmbeddedSourceResource(
        string name,
        string content)
    {
        var preamble = System.Text.Encoding.Unicode.GetPreamble();
        var text = System.Text.Encoding.Unicode.GetBytes(content);
        var bytes = new byte[preamble.Length + text.Length];
        preamble.CopyTo(bytes, 0);
        text.CopyTo(bytes, preamble.Length);
        return CreateResource(name, bytes);
    }

    private static ResourceDescription CreateResource(
        string name,
        byte[] content)
    {
        return new ResourceDescription(
            name,
            () => new MemoryStream(content, writable: false),
            isPublic: true);
    }

    private static MetadataReference[] CreatePlatformReferences()
    {
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator) ?? [];
        return trustedPlatformAssemblies
            .Select(static path =>
                MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
