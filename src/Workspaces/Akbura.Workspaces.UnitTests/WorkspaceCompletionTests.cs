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
        command void Save();

        <StackPanel/>
        """;

    [Theory]
    [InlineData(".")]
    [InlineData(".cl")]
    public void Completion_SelectorSnippetReplacesLeadingDot(string source)
    {
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "Styles.akcss");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);
        var item = Assert.Single(
            result.Items,
            static candidate => candidate.DisplayText == ".class");
        var changedText = text.WithChanges(
            new TextChange(
                result.ApplicableSpan,
                item.InsertText));

        Assert.Equal(
            new TextSpan(0, source.Length),
            result.ApplicableSpan);
        Assert.StartsWith(
            ".class {",
            changedText.ToString(),
            StringComparison.Ordinal);
        Assert.False(
            changedText.ToString().StartsWith(
                "..",
                StringComparison.Ordinal),
            changedText.ToString());
    }

    [Fact]
    public void Completion_TopLevelOffersCatalogWithoutSemanticContext()
    {
        const string source = "";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Counter.akbura");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        foreach (var keyword in new[] { "state", "param", "inject", "command", })
        {
            var item = Assert.Single(
                result.Items,
                candidate => candidate.DisplayText == keyword);
            Assert.Equal(
                keyword + " ",
                item.InsertText);
            Assert.Equal(AkburaCompletionKind.Keyword, item.Kind);
            Assert.True(item.TriggerCompletionAfterInsert);
        }

        Assert.Equal(
            3,
            result.Items.Count(static item =>
                item.Kind == AkburaCompletionKind.Hook));
        Assert.False(result.IsIncomplete);
    }

    [Theory]
    [InlineData("st", "state")]
    [InlineData("par", "param")]
    [InlineData("inj", "inject")]
    [InlineData("comm", "command")]
    public void Completion_TopLevelFiltersCatalogByPrefix(string source, string expectedItem)
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
        Assert.Equal(expectedItem, item.DisplayText);
        Assert.Equal(
            expectedItem + " ",
            item.InsertText);
        Assert.Equal(
            source,
            document.Text.ToString(result.ApplicableSpan));
    }

    [Fact]
    public void Completion_UseEffectOffersIndentedSnippets()
    {
        const string source = "\n    useE";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Counter.akbura");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        var hooks = result.Items
            .Where(static item =>
                item.Kind == AkburaCompletionKind.Hook)
            .ToArray();
        Assert.Equal(3, hooks.Length);
        Assert.All(
            hooks,
            static item =>
                Assert.Equal("Akbura.Hooks", item.NamespaceImport));

        var everyUpdate = Assert.Single(
            hooks,
            static item => item.DisplayText == "useEffect");
        Assert.Equal(
            "useEffect(() =>\n    {\n        \n    });",
            everyUpdate.InsertText);
        Assert.Equal(
            everyUpdate.InsertText.IndexOf(
                "\n    });",
                StringComparison.Ordinal),
            everyUpdate.InsertText.Length -
                everyUpdate.CaretOffsetFromEnd);
    }

    [Theory]
    [InlineData("using Akbura.Hooks;\n\nuseE")]
    [InlineData("global using Akbura.Hooks;\n\nuseE")]
    [InlineData(
        "using static Akbura.Hooks.EffectHooks;\n\nuseE")]
    [InlineData("namespace Akbura.Hooks;\n\nuseE")]
    public void Completion_UseEffectDoesNotOfferRedundantImport(string source)
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

        var hooks = result.Items
            .Where(static item =>
                item.Kind == AkburaCompletionKind.Hook)
            .ToArray();
        Assert.Equal(3, hooks.Length);
        Assert.All(
            hooks,
            static item => Assert.Null(item.NamespaceImport));
    }

    [Fact]
    public void Completion_UseEffectAliasStillImportsHooksNamespace()
    {
        const string source =
            "using Hooks = Akbura.Hooks;\n\nuseE";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Counter.akbura");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        var hooks = result.Items
            .Where(static item =>
                item.Kind == AkburaCompletionKind.Hook)
            .ToArray();
        Assert.Equal(3, hooks.Length);
        Assert.All(
            hooks,
            static item =>
                Assert.Equal("Akbura.Hooks", item.NamespaceImport));
    }

    [Fact]
    public void Completion_UseEffectUsesProjectGlobalHooksImport()
    {
        const string source = "useE";

        WithWorkspace(
            source,
            stylesSource: null,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        source.Length);

                var hooks = result.Items
                    .Where(static item =>
                        item.Kind == AkburaCompletionKind.Hook)
                    .ToArray();
                Assert.Equal(3, hooks.Length);
                Assert.All(
                    hooks,
                    static item =>
                        Assert.Null(item.NamespaceImport));
            },
            globalUsingsSource: "using Akbura.Hooks;");
    }

    [Fact]
    public void Completion_UseEffectPreservesCrLfAndTabIndentation()
    {
        const string source = "\r\n\tuseE";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Counter.akbura");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        var everyUpdate = Assert.Single(
            result.Items,
            static item => item.DisplayText == "useEffect");
        Assert.Equal(
            "useEffect(() =>\r\n\t{\r\n\t\t\r\n\t});",
            everyUpdate.InsertText);
        Assert.Equal(
            everyUpdate.InsertText.IndexOf(
                "\r\n\t});",
                StringComparison.Ordinal),
            everyUpdate.InsertText.Length -
                everyUpdate.CaretOffsetFromEnd);
    }

    [Fact]
    public void Completion_StateInitializerOffersVisibleStateHook()
    {
        const string source = """
            namespace Gallery;

            using Akbura.Hooks;
            using Avalonia.Controls;

            state object brush = useStatic;

            <StackPanel/>
            """;
        var position = source.IndexOf(
            "useStatic",
            StringComparison.Ordinal) + "useStatic".Length;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                var item = Assert.Single(
                    result.Items,
                    static candidate =>
                        candidate.DisplayText == "useStaticResource");
                Assert.Equal(AkburaCompletionKind.Hook, item.Kind);
                Assert.Equal("useStaticResource", item.InsertText);
                Assert.Equal(
                    "<T>(object key, T fallback = default) → State<T>",
                    item.Suffix);
                Assert.DoesNotContain("AkburaControl", item.Suffix);
                Assert.Equal(
                    "useStatic",
                    syntacticDocument.Text.ToString(result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_StateInitializerOffersHookFromCurrentNamespace()
    {
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;

            state int value = useCust;

            <StackPanel/>
            """;
        var position = source.IndexOf(
            "useCust",
            StringComparison.Ordinal) + "useCust".Length;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static candidate =>
                        candidate.DisplayText == "useCustomState" &&
                        candidate.Kind == AkburaCompletionKind.Hook);
            });
    }

    [Fact]
    public void Completion_StateInitializerOffersHookFromReferencedAssembly()
    {
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;
            using ReferencedHooks;

            state int value = useRef;

            <StackPanel/>
            """;
        var position = source.IndexOf(
            "useRef",
            StringComparison.Ordinal) + "useRef".Length;

        WithWorkspace(
            source,
            stylesSource: null,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static candidate =>
                        candidate.DisplayText == "useReferencedState" &&
                        candidate.Kind == AkburaCompletionKind.Hook);
            },
            additionalReference: CreateReferencedHookReference());
    }

    [Fact]
    public void Completion_StateInitializerUsesProjectGlobalHookImport()
    {
        const string source = """
            namespace Gallery;

            using Avalonia.Controls;

            state object value = useStatic;

            <StackPanel/>
            """;
        var position = source.IndexOf(
            "useStatic",
            StringComparison.Ordinal) + "useStatic".Length;

        WithWorkspace(
            source,
            stylesSource: null,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static candidate =>
                        candidate.DisplayText == "useStaticResource" &&
                        candidate.Kind == AkburaCompletionKind.Hook);
            },
            globalUsingsSource: "global using Akbura.Hooks;");
    }

    [Fact]
    public void Completion_StateInitializerExcludesRenderHook()
    {
        const string source = """
            namespace Gallery;

            using Akbura.Hooks;
            using Avalonia.Controls;

            state object value = useRender;

            <StackPanel/>
            """;
        var position = source.IndexOf(
            "useRender",
            StringComparison.Ordinal) + "useRender".Length;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.DoesNotContain(
                    result.Items,
                    static candidate =>
                        candidate.DisplayText == "useRenderOnly");
            });
    }

    [Fact]
    public void Completion_NestedStateExpressionDoesNotOfferHook()
    {
        const string source = """
            namespace Gallery;

            using Akbura.Hooks;
            using Avalonia.Controls;

            state object value = Wrap(useStatic);

            <StackPanel/>
            """;
        var position = source.IndexOf(
            "useStatic",
            StringComparison.Ordinal) + "useStatic".Length;

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.DoesNotContain(
                    result.Items,
                    static candidate =>
                        candidate.Kind == AkburaCompletionKind.Hook);
            });
    }

    [Fact]
    public void Completion_AkcssOffersTopLevelKeywordsWithoutSemanticContext()
    {
        const string source = "@u";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Styles.akcss");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                source.Length);

        var item = Assert.Single(
            result.Items,
            static item => item.DisplayText == "@using");
        Assert.Equal("@using ;", item.InsertText);
        Assert.Equal(1, item.CaretOffsetFromEnd);
        Assert.True(item.TriggerCompletionAfterInsert);
        Assert.False(result.IsIncomplete);
    }

    [Fact]
    public void Completion_AkcssBodyOffersDirectivesWhileSemanticContextIsPending()
    {
        const string sourceWithCaret =
            ".card {\n    @a|\n}";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Styles.akcss");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                position);

        var item = Assert.Single(
            result.Items,
            static item => item.DisplayText == "@apply");
        Assert.Equal(
            "@a",
            source.Substring(
                result.ApplicableSpan.Start,
                result.ApplicableSpan.Length));
        Assert.Equal("@apply ;", item.InsertText);
        Assert.True(result.IsIncomplete);
    }

    [Fact]
    public void Completion_AkcssPropertyContextIsIncompleteWithoutSemanticModel()
    {
        const string sourceWithCaret =
            ".card {\n    Wid|\n}";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Styles.akcss");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                position);

        Assert.Empty(result.Items);
        Assert.True(result.IsIncomplete);
    }

    [Theory]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card {\n    Wid|\n}",
        "Width",
        "Width: ")]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card {\n    Heig|\n}",
        "Height",
        "Height: ")]
    [InlineData(
        "@using Gallery;\n" +
        "Options.card {\n    Na|\n}",
        "Name",
        "Name: ")]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card {\n    Grid.Ro|\n}",
        "Grid.Row",
        "Row: ")]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card {\n" +
        "    @if(true) {\n        Wid|\n    }\n" +
        "}",
        "Width",
        "Width: ")]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card {\n    Width: 10;\n    Wid|\n}",
        "Width",
        "Width: ")]
    public void Completion_AkcssOffersSemanticProperties(string sourceWithCaret, string expectedDisplayText, string expectedInsertText)
    {
        WithAkcssWorkspace(
            sourceWithCaret,
            importedStylesSource: null,
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                var item = Assert.Single(
                    result.Items,
                    item => item.DisplayText == expectedDisplayText);
                Assert.Equal(expectedInsertText, item.InsertText);
                Assert.Equal(AkburaCompletionKind.Property, item.Kind);
                Assert.True(item.TriggerCompletionAfterInsert);
                Assert.False(result.IsIncomplete);
            });
    }

    [Fact]
    public void Completion_AkcssApplyOffersCompatibleLocalDeclarations()
    {
        const string source = """
            @using Avalonia.Controls;
            @using Gallery;

            Control.base-card {
                Width: 10;
            }

            Options.incompatible {
                Name: "ignored";
            }

            @utilities {
                Control.gap-(double value) {
                    Width: value;
                }
            }

            Control.card {
                @apply |;
            }
            """;

        WithAkcssWorkspace(
            source,
            importedStylesSource: null,
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "base-card" &&
                        item.Kind == AkburaCompletionKind.AkcssStyle);
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "gap-(double value)" &&
                        item.Kind ==
                            AkburaCompletionKind.TailwindUtility);
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "card");
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText == "incompatible");
            });
    }

    [Fact]
    public void Completion_AkcssApplyUsesImportedResolutionLayer()
    {
        const string importedStyles = """
            @using Avalonia.Controls;

            Control.base-card {
                Width: 10;
            }

            @utilities {
                Control.gap-(double value) {
                    Width: value;
                }
            }
            """;
        const string source =
            "@using Avalonia.Controls;\n" +
            "@using Imported.akcss;\n\n" +
            "Control.card {\n    @apply base-card ga|;\n}";

        WithAkcssWorkspace(
            source,
            importedStyles,
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                var utility = Assert.Single(
                    result.Items,
                    static item => item.Kind ==
                        AkburaCompletionKind.TailwindUtility);
                Assert.Equal("gap-(double value)", utility.DisplayText);
                Assert.Equal("gap-", utility.InsertText);
                Assert.Equal(
                    "utility \u00B7 Imported.akcss",
                    utility.Suffix);
            });
    }

    [Fact]
    public void Completion_AkcssApplyPreservesTypedUtilityArgument()
    {
        const string importedStyles = """
            @using Avalonia.Controls;

            @utilities {
                Control.gap-(double value) {
                    Width: value;
                }
            }
            """;
        const string source =
            "@using Avalonia.Controls;\n" +
            "@using Imported.akcss;\n\n" +
            "Control.card {\n    @apply gap-12|;\n}";

        WithAkcssWorkspace(
            source,
            importedStyles,
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                var utility = Assert.Single(
                    result.Items,
                    static item => item.Kind ==
                        AkburaCompletionKind.TailwindUtility);
                Assert.Equal("gap-12", utility.InsertText);
            });
    }

    [Fact]
    public void Completion_AkcssUsingOffersFullModuleName()
    {
        const string source = "@using Imp|";
        WithAkcssWorkspace(
            source,
            ".imported { Width: 1; }",
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                var module = Assert.Single(
                    result.Items,
                    static item => item.Kind ==
                        AkburaCompletionKind.AkcssModule);
                Assert.Equal("Imported.akcss", module.DisplayText);
                Assert.Equal(module.DisplayText, module.InsertText);
                Assert.False(result.IsIncomplete);
            });
    }

    [Fact]
    public void Completion_AkcssUsingDoesNotOfferCurrentModule()
    {
        const string source = "@using |";
        WithAkcssWorkspace(
            source,
            ".imported { Width: 1; }",
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.DoesNotContain(
                    result.Items,
                    static item => item.Kind ==
                            AkburaCompletionKind.AkcssModule &&
                        item.DisplayText == "Styles.akcss");
                Assert.Contains(
                    result.Items,
                    static item => item.Kind ==
                            AkburaCompletionKind.AkcssModule &&
                        item.DisplayText == "Imported.akcss");
            });
    }

    [Theory]
    [InlineData("param |", "", 2)]
    [InlineData("param b|", "b", 1)]
    [InlineData("param ou|", "ou", 1)]
    public void Completion_ParamOffersBindingModifiers(string sourceWithCaret, string expectedPrefix, int expectedCount)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Counter.akbura");
        using var workspace = new AkburaWorkspace();

        var result = workspace.LanguageServices.Completion
            .GetCompletions(
                document,
                semanticContext: null,
                position);

        Assert.Equal(expectedCount, result.Items.Length);
        Assert.All(
            result.Items,
            static item =>
            {
                Assert.Contains(
                    item.DisplayText,
                    new[] { "bind", "out" });
                Assert.EndsWith(
                    " ",
                    item.InsertText,
                    StringComparison.Ordinal);
                Assert.True(item.TriggerCompletionAfterInsert);
            });
        Assert.Equal(
            expectedPrefix,
            document.Text.ToString(result.ApplicableSpan));
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
    public void Completion_InsideAliasedPageListResolvesAliasAndOffersPageTypes()
    {
        const string sourceWithCaret = """
            using Avalonia.Controls;
            using PageList = Avalonia.Collections.AvaloniaList<Avalonia.Controls.Page>;

            <PageList>
                <|
            </PageList>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, count: 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var semanticModel = semanticContext.Project.Compilation
                    .GetSemanticModel(semanticContext.Document.SyntaxTree);
                AssertDataType(
                    semanticModel,
                    "PageList",
                    "global::Avalonia.Collections.AvaloniaList<global::Avalonia.Controls.Page>");

                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "ContentPage" &&
                        item.Kind == AkburaCompletionKind.Component);
            });
    }

    [Theory]
    [InlineData("<|", "")]
    [InlineData("<Bord|", "Bord")]
    public void Completion_ComponentNameOffersAutoImportableBorder(string sourceWithCaret, string expectedPrefix)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, count: 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                var border = Assert.Single(
                    result.Items,
                    static item =>
                        item.DisplayText == "Border" &&
                        item.NamespaceImport ==
                            "Avalonia.Controls");

                Assert.Equal("Border", border.InsertText);
                Assert.Equal(
                    "Avalonia.Controls  (using)",
                    border.Suffix);
                Assert.True(border.TriggerCompletionAfterInsert);
                Assert.Equal(
                    expectedPrefix,
                    syntacticDocument.Text.ToString(
                        result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_VisibleBorderDoesNotRequestDuplicateImport()
    {
        const string sourceWithCaret = """
            using Avalonia.Controls;

            <Bord|
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, count: 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                var border = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "Border");

                Assert.Null(border.NamespaceImport);
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

                var loaded = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "Loaded");

                Assert.Equal(
                    AkburaCompletionKind.Event,
                    loaded.Kind);
                Assert.Equal(
                    "Loaded={}",
                    loaded.InsertText);
                Assert.Equal(
                    1,
                    loaded.CaretOffsetFromEnd);
                Assert.True(
                    loaded.TriggerCompletionAfterInsert);
                var save = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "Save");
                Assert.Equal(
                    AkburaCompletionKind.Command,
                    save.Kind);
                Assert.Equal(
                    "Save={}",
                    save.InsertText);
                Assert.Equal(
                    1,
                    save.CaretOffsetFromEnd);
                Assert.True(
                    save.TriggerCompletionAfterInsert);

                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "x.Name");
            });
    }

    [Theory]
    [InlineData("")]
    [InlineData("Grid")]
    [InlineData("Grid.")]
    [InlineData("Grid.Ro")]
    public void Completion_AttributeOffersVisibleAttachedProperty(string prefix)
    {
        var sourceWithCaret =
            "namespace Gallery;\n\n" +
            "using Avalonia.Controls;\n\n" +
            "<Button " +
            prefix +
            "|/>";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, count: 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                var row = Assert.Single(
                    result.Items,
                    static item =>
                        item.DisplayText == "Grid.Row");

                Assert.Equal("Grid.Row=\"\"", row.InsertText);
                Assert.Equal(1, row.CaretOffsetFromEnd);
                Assert.Equal(
                    prefix,
                    syntacticDocument.Text.ToString(
                        result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_AttributeIncludesCompleteAttachedPropertyCatalog()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <Button |/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, count: 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Grid.Row");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Grid.Column");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Grid.RowSpan");
                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText == "Grid.ColumnSpan");
            });
    }

    [Fact]
    public void Completion_AttributeWithEmptyPrefixPreservesCompleteCatalog()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <WideControl |
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.True(result.Items.Length > 50);
                var padding = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "Padding");
                Assert.Equal("Padding=\"\"", padding.InsertText);
                Assert.Empty(syntacticDocument.Text.ToString(
                    result.ApplicableSpan));
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
            @using Avalonia.Controls;

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
            @using Avalonia.Controls;

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
            @using Avalonia.Controls;

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
            @using Avalonia.Controls;

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
            @using Avalonia.Controls;

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
            @using Avalonia.Controls;

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
            @using Avalonia.Controls;

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

    [Fact]
    public void ProjectedCSharpService_CompletesAndResolvesState()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            state int count = 0;

            <StackPanel Width={cou|}/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.ProjectedCSharp
                    .GetCompletionsAsync(
                        syntacticDocument,
                        semanticContext,
                        position,
                        new AkburaProjectedCompletionTrigger(
                            IsExplicit: true,
                            IsIncomplete: false,
                            Character: '\0'))
                    .GetAwaiter()
                    .GetResult();

                Assert.NotNull(result);
                var item = Assert.Single(
                    result.Value.Items,
                    static item => item.DisplayText == "count");
                var resolution = workspace.LanguageServices.ProjectedCSharp
                    .ResolveCompletionAsync(
                        syntacticDocument,
                        semanticContext,
                        position,
                        item.ResolveKey)
                    .GetAwaiter()
                    .GetResult();

                Assert.NotNull(resolution);
                var changed = SourceText.From(source)
                    .WithChanges(resolution!.Change.Changes)
                    .ToString();
                Assert.Contains("Width={count}", changed);
            });
    }

    [Fact]
    public void ProjectedCSharpService_CompletesDataTypeInsideQuotedValue()
    {
        const string sourceWithCaret = """
            namespace Consumer;

            using Avalonia.Controls;
            using Gallery;

            <Border x.DataType="Opt|ions"/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var context));
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Type,
                    context.Kind);
                Assert.Equal(
                    "Options",
                    syntacticDocument.Text.ToString(context.HostSpan));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    context,
                    out var projection));
                var roslynCompletion = RoslynCompletionTestHost
                    .GetCompletionsAsync(
                        semanticContext.Project.CSharpCompilation,
                        projection.Root,
                        projection.ProjectedPosition,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Assert.NotNull(roslynCompletion);
                var rawItem = Assert.Single(
                    roslynCompletion.ItemsList,
                    static item => item.DisplayText == "Options");
                Assert.True(
                    projection.TryMapToHost(
                        rawItem.Span,
                        out _),
                    $"Completion span {rawItem.Span} is outside " +
                    $"projection span {projection.ProjectedSpan}.");

                var result = workspace.LanguageServices.ProjectedCSharp
                    .GetCompletionsAsync(
                        syntacticDocument,
                        semanticContext,
                        position,
                        new AkburaProjectedCompletionTrigger(
                            IsExplicit: true,
                            IsIncomplete: false,
                            Character: '\0'))
                    .GetAwaiter()
                    .GetResult();

                Assert.NotNull(result);
                var item = Assert.Single(
                    result.Value.Items,
                    static item => item.DisplayText == "Options");
                var resolution = workspace.LanguageServices.ProjectedCSharp
                    .ResolveCompletionAsync(
                        syntacticDocument,
                        semanticContext,
                        position,
                        item.ResolveKey)
                    .GetAwaiter()
                    .GetResult();

                Assert.NotNull(resolution);
                var changed = SourceText.From(source)
                    .WithChanges(resolution!.Change.Changes)
                    .ToString();
                Assert.Contains(
                    "x.DataType=\"Options\"",
                    changed,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "Optionsions",
                    changed,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void ProjectedCSharpService_DataTypeAutoImportPreservesCrLfAndUnicode()
    {
        const string sourceWithCaret =
            "namespace Consumer;\r\n" +
            "\r\n" +
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "// 😀\r\n" +
            "<Border x.DataType=\"Option|\"/>\r\n";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var context));
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    context,
                    out var projection));

                var completion = RoslynCompletionTestHost
                    .GetAutomaticImportCompletionAsync(
                        semanticContext.Project.CSharpCompilation,
                        projection.Root,
                        projection.ProjectedPosition,
                        'n',
                        displayText: "Options",
                        isIncompleteSession: true,
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
                var changed = SourceText.From(source)
                    .WithChanges(mapped.Changes)
                    .ToString();

                Assert.Contains(
                    "using Gallery;\r\n",
                    changed,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "x.DataType=\"Options\"",
                    changed,
                    StringComparison.Ordinal);
                Assert.Contains("// 😀\r\n", changed);
                Assert.DoesNotContain("\n", changed.Replace("\r\n", ""));
            });
    }

    [Theory]
    [InlineData(
        "<Border x.DataType='Opt|ions'/>",
        "x.DataType='Options'")]
    [InlineData(
        "<Border x.DataType=\"Opt|ions/>",
        "x.DataType=\"Options/>")]
    [InlineData(
        "<Border x.DataType=\"global::Gallery.Opt|ions\"/>",
        "x.DataType=\"global::Gallery.Options\"")]
    [InlineData(
        "<Border x.DataType=\"System.Collections.Generic.List<Gallery.Opt|ions>\"/>",
        "x.DataType=\"System.Collections.Generic.List<Gallery.Options>\"")]
    public void ProjectedCSharpService_DataTypeEditPreservesSupportedLiteralForm(string markupWithCaret, string expectedText)
    {
        var sourceWithCaret =
            "namespace Consumer;\r\n\r\n" +
            "using Avalonia.Controls;\r\n" +
            "using Gallery;\r\n\r\n" +
            markupWithCaret;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.ProjectedCSharp
                    .GetCompletionsAsync(
                        syntacticDocument,
                        semanticContext,
                        position,
                        new AkburaProjectedCompletionTrigger(
                            IsExplicit: true,
                            IsIncomplete: false,
                            Character: '\0'))
                    .GetAwaiter()
                    .GetResult();

                Assert.NotNull(result);
                var item = Assert.Single(
                    result.Value.Items,
                    static item =>
                        item.DisplayText == "Options");
                var resolution = workspace.LanguageServices.ProjectedCSharp
                    .ResolveCompletionAsync(
                        syntacticDocument,
                        semanticContext,
                        position,
                        item.ResolveKey)
                    .GetAwaiter()
                    .GetResult();

                Assert.NotNull(resolution);
                var changed = SourceText.From(source)
                    .WithChanges(resolution!.Change.Changes)
                    .ToString();
                Assert.Contains(
                    expectedText,
                    changed,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "Optionsions",
                    changed,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void ProjectedCSharpService_EmptyDataTypeOffersTypeItems()
    {
        const string sourceWithCaret = """
            namespace Consumer;

            using Avalonia.Controls;
            using Gallery;

            <Border x.DataType="|"/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.ProjectedCSharp
                    .GetCompletionsAsync(
                        syntacticDocument,
                        semanticContext,
                        position,
                        new AkburaProjectedCompletionTrigger(
                            IsExplicit: true,
                            IsIncomplete: false,
                            Character: '\0'))
                    .GetAwaiter()
                    .GetResult();

                Assert.NotNull(result);
                var items = result.Value.Items
                    .Where(static item =>
                        item.DisplayText == "Options")
                    .ToArray();
                Assert.NotEmpty(items);
                Assert.All(
                    items,
                    static item => Assert.Equal(
                        AkburaProjectedCompletionKind.Class,
                        item.Kind));
            });
    }

    [Fact]
    public void DataTypeResolver_UsesDocumentUsingsAliasesAndSupportedTypeForms()
    {
        const string source = """
            namespace Consumer;

            using Avalonia.Controls;
            using Gallery;
            using VM = Gallery.Options;
            using Generic = System.Collections.Generic;

            <Border x.DataType="Options"/>
            """;

        WithWorkspace(
            source,
            (_workspace, semanticContext, _syntacticDocument) =>
            {
                var semanticModel = semanticContext.Project.Compilation
                    .GetSemanticModel(
                        semanticContext.Document.SyntaxTree);

                AssertDataType(
                    semanticModel,
                    "Options",
                    "global::Gallery.Options");
                AssertDataType(
                    semanticModel,
                    "Gallery.Options",
                    "global::Gallery.Options");
                AssertDataType(
                    semanticModel,
                    "global::Gallery.Options",
                    "global::Gallery.Options");
                AssertDataType(
                    semanticModel,
                    "VM",
                    "global::Gallery.Options");
                AssertDataType(
                    semanticModel,
                    "Outer.Nested",
                    "global::Gallery.Outer.Nested");
                AssertDataType(
                    semanticModel,
                    "Generic.List<Options>",
                    "global::System.Collections.Generic.List<global::Gallery.Options>");
            });
    }

    [Fact]
    public void DataTypeResolver_UsesGlobalUsingFromCurrentProjectSnapshot()
    {
        const string source = """
            namespace Consumer;

            using Avalonia.Controls;

            <Border x.DataType="Options"/>
            """;

        WithWorkspace(
            source,
            stylesSource: null,
            (_workspace, semanticContext, _syntacticDocument) =>
            {
                var semanticModel = semanticContext.Project.Compilation
                    .GetSemanticModel(
                        semanticContext.Document.SyntaxTree);
                AssertDataType(
                    semanticModel,
                    "Options",
                    "global::Gallery.Options");
            },
            globalUsingsSource: "global using Gallery;");
    }

    [Fact]
    public void DataTypeResolver_DoesNotChooseRandomAmbiguousShortName()
    {
        const string source = """
            namespace Consumer;

            using Avalonia.Controls;
            using Gallery;
            using Other;

            <Border x.DataType="Options"/>
            """;

        var compilation = CreateCompilation().AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                "namespace Other { public sealed class Options { } }"));
        using var workspace = new AkburaWorkspace(
            new ProjectContext(
                ProjectId.CreateNewId("AmbiguousDataType"),
                projectFilePath: string.Empty,
                projectDirectory: Environment.CurrentDirectory,
                rootNamespace: "Consumer",
                compilation,
                ImmutableArray<ProjectReference>.Empty));
        var path = Path.GetFullPath(
            "AmbiguousDataType.akbura");
        var text = SourceText.From(source);
        var semanticContext = workspace.OpenOrChangeDocumentContext(
            new Uri(path),
            text);
        var semanticModel = semanticContext.Project.Compilation
            .GetSemanticModel(
                semanticContext.Document.SyntaxTree);

        Assert.False(
            semanticModel.TryBindMarkupDataTypeDirective(
                "Options",
                out _));
        AssertDataType(
            semanticModel,
            "Gallery.Options",
            "global::Gallery.Options");
        AssertDataType(
            semanticModel,
            "Other.Options",
            "global::Other.Options");
    }

    [Theory]
    [InlineData(
        "<Border Tag=${Binding Customer.Add|ress} x.DataType=\"MainViewModel\"/>",
        "Address",
        "Customer.Address")]
    [InlineData(
        "<Border Tag=${Binding Path=Customer.Add|ress} x.DataType=\"MainViewModel\"/>",
        "Address",
        "Path=Customer.Address")]
    [InlineData(
        "<Border Tag=${Binding Customer.Address.Ci|ty} x.DataType=\"MainViewModel\"/>",
        "City",
        "Customer.Address.City")]
    [InlineData(
        "<Border Tag=${Binding Customers[0].Na|me} x.DataType=\"MainViewModel\"/>",
        "Name",
        "Customers[0].Name")]
    [InlineData(
        "<Border Tag=${Binding CustomerList[0].Na|me} x.DataType=\"MainViewModel\"/>",
        "Name",
        "CustomerList[0].Name")]
    [InlineData(
        "<Border Tag=${Binding Customer?.Na|me} x.DataType=\"MainViewModel\"/>",
        "Name",
        "Customer?.Name")]
    [InlineData(
        "<Border Tag=${Binding ((Customer)Customer).Na|me} x.DataType=\"MainViewModel\"/>",
        "Name",
        "((Customer)Customer).Name")]
    [InlineData(
        "<Border Tag=${Binding Pending^.Na|me} x.DataType=\"MainViewModel\"/>",
        "Name",
        "Pending^.Name")]
    [InlineData(
        "<Border Tag=${Binding !IsA|ctive} x.DataType=\"MainViewModel\"/>",
        "IsActive",
        "!IsActive")]
    [InlineData(
        "<Border Tag=${Binding $self.Wi|dth} x.DataType=\"MainViewModel\"/>",
        "Width",
        "$self.Width")]
    [InlineData(
        "<StackPanel><Border Tag=${Binding $parent[StackPanel].Spa|cing} x.DataType=\"MainViewModel\"/></StackPanel>",
        "Spacing",
        "$parent[StackPanel].Spacing")]
    [InlineData(
        "<StackPanel><Border Tag=${Binding $parent.Spa|cing} x.DataType=\"MainViewModel\"/></StackPanel>",
        "Spacing",
        "$parent.Spacing")]
    [InlineData(
        "<StackPanel x.Name=\"named\"><Border Tag=${Binding #named.Spa|cing} x.DataType=\"MainViewModel\"/></StackPanel>",
        "Spacing",
        "#named.Spacing")]
    [InlineData(
        "<StackPanel x.Name=\"named\"><Border Tag=${CompiledBinding Spa|cing, ElementName=named} x.DataType=\"MainViewModel\"/></StackPanel>",
        "Spacing",
        "CompiledBinding Spacing")]
    [InlineData(
        "<StackPanel x.Name=\"named\"><Border Tag=${ReflectionBinding Spa|cing, ElementName=named} x.DataType=\"MainViewModel\"/></StackPanel>",
        "Spacing",
        "ReflectionBinding Spacing")]
    [InlineData(
        "<Border Tag=${Binding Spa|cing, RelativeSource=${RelativeSource FindAncestor, AncestorType=StackPanel}} x.DataType=\"MainViewModel\"/>",
        "Spacing",
        "Binding Spacing")]
    public void Completion_BindingPathUsesSemanticReceiverAndReplacesSuffix(string markupWithCaret, string expectedItem, string expectedPath)
    {
        var sourceWithCaret =
            "namespace Gallery;\n\n" +
            "using Avalonia.Controls;\n\n" +
            markupWithCaret;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var syntaxContext = syntacticDocument
                    .GetCompletionContext(position);
                Assert.Equal(
                    AkburaCompletionContextKind.BindingPath,
                    syntaxContext.Kind);
                var semanticModel = semanticContext.Project.Compilation
                    .GetSemanticModel(
                        semanticContext.Document.SyntaxTree);
                Assert.True(
                    semanticModel.TryBindMarkupDataTypeDirective(
                        "MainViewModel",
                        out _));

                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);
                var item = Assert.Single(
                    result.Items,
                    item => item.DisplayText == expectedItem);
                var changed = syntacticDocument.Text.WithChanges(
                    new TextChange(
                        result.ApplicableSpan,
                        item.InsertText));

                Assert.Contains(
                    expectedPath,
                    changed.ToString(),
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    expectedItem + expectedItem,
                    changed.ToString(),
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Completion_BindingSourceExpressionOverridesDataType()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <Border Tag=${Binding Na|me, Source={Customer}} x.DataType="MainViewModel"/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);
                var names = result.Items
                    .Select(static item => item.DisplayText)
                    .ToHashSet(StringComparer.Ordinal);

                Assert.Contains("Name", names);
                Assert.DoesNotContain("Inherited", names);
            });
    }

    [Fact]
    public void Completion_RelativeSourceSelfUsesElementType()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <Border Tag=${Binding Wi|dth, RelativeSource=Self} x.DataType="MainViewModel"/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);
                var names = result.Items
                    .Select(static item => item.DisplayText)
                    .ToHashSet(StringComparer.Ordinal);

                Assert.Contains("Width", names);
                Assert.DoesNotContain("Inherited", names);
            });
    }

    [Fact]
    public void Completion_BindingInsideItemTemplateUsesInferredItemType()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <ItemsControl x.DataType="MainViewModel" ItemsSource=${Binding Customers}>
                <ItemsControl.ItemTemplate>
                    <Border Tag=${Binding Na|me}/>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);
                var names = result.Items
                    .Select(static item => item.DisplayText)
                    .ToHashSet(StringComparer.Ordinal);

                Assert.Contains("Name", names);
                Assert.DoesNotContain("Inherited", names);
            });
    }

    [Fact]
    public void Completion_UnknownRelativeSourceDoesNotUseDataType()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <Border Tag=${Binding Inh|, RelativeSource=Unknown} x.DataType="MainViewModel"/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.DoesNotContain(
                    result.Items,
                    static item =>
                        item.DisplayText == "Inherited");
            });
    }

    [Fact]
    public void Completion_BindingInsideItemTemplateUsesExplicitItemType()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <ItemsControl x.DataType="MainViewModel" ItemsSource=${Binding Customers}>
                <ItemsControl.ItemTemplate x.DataType="Customer">
                    <Border Tag=${Binding Na|me}/>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);
                var names = result.Items
                    .Select(static item => item.DisplayText)
                    .ToHashSet(StringComparer.Ordinal);

                Assert.Contains("Name", names);
                Assert.DoesNotContain("Inherited", names);
            });
    }

    [Fact]
    public void Completion_BindingTemplateBoundaryDoesNotUseOuterDataType()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <ItemsControl x.DataType="MainViewModel">
                <ItemsControl.ItemTemplate>
                    <Border Tag=${Binding Inh|}/>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.DoesNotContain(
                    result.Items,
                    static item =>
                        item.DisplayText == "Inherited");
            });
    }

    [Fact]
    public void Completion_BindingUsesUpdatedRoslynCompilationWithoutBuild()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <Border Tag=${Binding Fre|} x.DataType="MainViewModel"/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var text = SourceText.From(source);
        var projectId = ProjectId.CreateNewId("Application");
        var compilation = CreateCompilation();
        var projectDirectory = Path.GetFullPath(
            Path.Combine(
                Path.GetTempPath(),
                nameof(WorkspaceCompletionTests),
                Guid.NewGuid().ToString("N")));
        var projectPath = Path.Combine(
            projectDirectory,
            "Application.csproj");
        var documentPath = Path.Combine(
            projectDirectory,
            "View.akbura");
        var initialProjectContext = new ProjectContext(
            projectId,
            projectPath,
            projectDirectory,
            "Gallery",
            compilation,
            ImmutableArray<ProjectReference>.Empty);

        using var workspace = new AkburaWorkspace(
            initialProjectContext);
        var initialContext = workspace.OpenOrChangeDocumentContext(
            new Uri(documentPath),
            text);
        var syntacticDocument = AkburaSyntacticDocument.Parse(
            text,
            documentPath);
        var initial = workspace.LanguageServices.Completion
            .GetCompletions(
                syntacticDocument,
                initialContext,
                position);

        Assert.DoesNotContain(
            initial.Items,
            static item =>
                item.DisplayText == "FreshFromUnsavedEdit");

        var oldTree = Assert.Single(compilation.SyntaxTrees);
        var oldSource = oldTree.GetText().ToString();
        var changedSource = oldSource.Replace(
            "public bool IsActive { get; }",
            "public bool IsActive { get; }\r\n\r\n" +
            "                    public string FreshFromUnsavedEdit " +
            "{ get; } = \"\";",
            StringComparison.Ordinal);
        Assert.NotEqual(oldSource, changedSource);
        var changedTree = CSharpSyntaxTree.ParseText(
            changedSource,
            (CSharpParseOptions)oldTree.Options,
            oldTree.FilePath);
        var changedCompilation = compilation.ReplaceSyntaxTree(
            oldTree,
            changedTree);

        workspace.AddOrUpdateProject(new ProjectContext(
            projectId,
            projectPath,
            projectDirectory,
            "Gallery",
            changedCompilation,
            ImmutableArray<ProjectReference>.Empty));
        var changedContext = workspace.OpenOrChangeDocumentContext(
            new Uri(documentPath),
            text);
        var changed = workspace.LanguageServices.Completion
            .GetCompletions(
                syntacticDocument,
                changedContext,
                position);

        Assert.Contains(
            changed.Items,
            static item =>
                item.DisplayText == "FreshFromUnsavedEdit");
        Assert.DoesNotContain(
            initial.Items,
            static item =>
                item.DisplayText == "FreshFromUnsavedEdit");
    }

    [Fact]
    public void Completion_BindingRootIncludesReadableInheritedMembersOnly()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <Border Tag=${Binding |} x.DataType="MainViewModel"/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var syntaxContext = syntacticDocument
                    .GetCompletionContext(position);
                Assert.Equal(
                    AkburaCompletionContextKind.BindingPath,
                    syntaxContext.Kind);
                var semanticModel = semanticContext.Project.Compilation
                    .GetSemanticModel(
                        semanticContext.Document.SyntaxTree);
                Assert.True(
                    semanticModel.TryBindMarkupDataTypeDirective(
                        "MainViewModel",
                        out _));

                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);
                var names = result.Items
                    .Select(static item => item.DisplayText)
                    .ToHashSet(StringComparer.Ordinal);

                Assert.Contains("Customer", names);
                Assert.Contains("Inherited", names);
                Assert.Contains("PublicField", names);
                Assert.Contains("$self", names);
                Assert.Contains("$parent", names);
                Assert.Contains("$templatedParent", names);
                Assert.DoesNotContain("WriteOnly", names);
                Assert.DoesNotContain("StaticMember", names);
                Assert.DoesNotContain("Hidden", names);
            });
    }

    [Fact]
    public void Completion_UnknownBindingReceiverFailsWithoutUnrelatedMembers()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            <Border Tag=${Binding Missing.|} x.DataType="MainViewModel"/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Empty(result.Items);
            });
    }

    [Theory]
    [InlineData(
        "<Border Tag=${Binding Customer.Name, Mo|} x.DataType=\"MainViewModel\"/>",
        "Mode",
        "Mode=")]
    [InlineData(
        "<Border Tag=${Binding Mode=Tw|oWay} x.DataType=\"MainViewModel\"/>",
        "TwoWay",
        "Mode=TwoWay")]
    [InlineData(
        "<Border Tag=${CompiledBinding Customer.Name, Ele|} x.DataType=\"MainViewModel\"/>",
        "ElementName",
        "ElementName=")]
    public void Completion_BindingArgumentsUseExtensionMetadata(string markupWithCaret, string expectedItem, string expectedText)
    {
        var sourceWithCaret =
            "namespace Gallery;\n\n" +
            "using Avalonia.Controls;\n\n" +
            markupWithCaret;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);
                var item = Assert.Single(
                    result.Items,
                    item => item.DisplayText == expectedItem);
                var changed = syntacticDocument.Text.WithChanges(
                    new TextChange(
                        result.ApplicableSpan,
                        item.InsertText));

                Assert.Contains(
                    expectedText,
                    changed.ToString(),
                    StringComparison.Ordinal);
            });
    }

    [Theory]
    [InlineData(
        "<Border Tag=${Custom 0, Mo|}/>",
        "Mode",
        "Mode=")]
    [InlineData(
        "<Border Tag=${Custom 0, Mode=Pr|imary}/>",
        "Primary",
        "Mode=Primary")]
    public void Completion_CommonMarkupExtensionArgumentsUsePropertyMetadata(string markupWithCaret, string expectedItem, string expectedText)
    {
        var sourceWithCaret =
            "namespace Gallery;\n\n" +
            "using Avalonia.Controls;\n" +
            "using Gallery.Extensions;\n\n" +
            markupWithCaret;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);
                var item = Assert.Single(
                    result.Items,
                    item => item.DisplayText == expectedItem);
                var changed = syntacticDocument.Text.WithChanges(
                    new TextChange(
                        result.ApplicableSpan,
                        item.InsertText));

                Assert.Contains(
                    expectedText,
                    changed.ToString(),
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task ProjectedCSharpService_UsesCompilationProjectReferences()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;
            using Library;

            state Person person = new();

            <TextBlock Text={person.Na|}/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var directory = Directory.CreateTempSubdirectory(
            "akbura-projected-reference-tests-");

        try
        {
            var libraryId = ProjectId.CreateNewId("Library");
            var applicationId = ProjectId.CreateNewId("Application");
            var libraryCompilation = CSharpCompilation.Create(
                "Library",
                [CSharpSyntaxTree.ParseText(
                    "namespace Library { " +
                    "public sealed class Person { " +
                    "public string Name { get; } = string.Empty; " +
                    "} }")],
                CreatePlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var applicationCompilation = CreateCompilation()
                .AddReferences(
                    libraryCompilation.ToMetadataReference());
            using var workspace = new AkburaWorkspace();
            workspace.AddOrUpdateProject(new ProjectContext(
                libraryId,
                Path.Combine(directory.FullName, "Library.csproj"),
                directory.FullName,
                "Library",
                libraryCompilation,
                ImmutableArray<ProjectReference>.Empty));
            var application = workspace.AddOrUpdateProject(
                new ProjectContext(
                    applicationId,
                    Path.Combine(
                        directory.FullName,
                        "Application.csproj"),
                    directory.FullName,
                    "Gallery",
                    applicationCompilation,
                    [new ProjectReference(libraryId)]));
            var path = Path.Combine(
                directory.FullName,
                "View.akbura");
            var semanticContext = workspace.OpenOrChangeDocumentContext(
                application.Id,
                new Uri(path),
                SourceText.From(source));
            var syntacticDocument = AkburaSyntacticDocument.Parse(
                SourceText.From(source),
                path);

            var result = await workspace.LanguageServices.ProjectedCSharp
                .GetCompletionsAsync(
                    syntacticDocument,
                    semanticContext,
                    position,
                    new AkburaProjectedCompletionTrigger(
                        IsExplicit: true,
                        IsIncomplete: false,
                        Character: '\0'));

            Assert.NotNull(result);
            Assert.Contains(
                result.Value.Items,
                static item => item.DisplayText == "Name");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
    [Fact]
    public void ProjectedCSharpService_MapsQuickInfoToHost()
    {
        const string sourceWithCaret = """
            namespace Gallery;

            using Avalonia.Controls;

            state int count = 0;

            <StackPanel Width={co|unt}/>
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var info = workspace.LanguageServices.ProjectedCSharp
                    .GetQuickInfoAsync(
                        syntacticDocument,
                        semanticContext,
                        position)
                    .GetAwaiter()
                    .GetResult();

                Assert.NotNull(info);
                Assert.Equal(
                    "count",
                    source.Substring(
                        info!.SourceSpan.Start,
                        info.SourceSpan.Length));
                Assert.Contains("int", info.Signature);
            });
    }
    private static void WithWorkspace(string source, Action<AkburaWorkspace, AkburaDocumentContext, AkburaSyntacticDocument> assertion)
    {
        WithWorkspace(
            source,
            stylesSource: null,
            assertion);
    }

    private static void WithAkcssWorkspace(string sourceWithCaret, string? importedStylesSource, Action<AkburaWorkspace, AkburaDocumentContext, AkburaSyntacticDocument, int> assertion)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = position >= 0
            ? sourceWithCaret.Remove(position, 1)
            : sourceWithCaret;
        if (position < 0)
        {
            position = source.Length;
        }

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
            if (importedStylesSource != null)
            {
                workspace.OpenOrChangeDocumentContext(
                    new Uri(Path.Combine(
                        directory,
                        "Imported.akcss")),
                    SourceText.From(importedStylesSource));
            }

            var filePath = Path.Combine(directory, "Styles.akcss");
            var text = SourceText.From(source);
            var semanticContext =
                workspace.OpenOrChangeDocumentContext(
                    new Uri(filePath),
                    text);
            var syntacticDocument = AkburaSyntacticDocument.Parse(
                text,
                filePath);

            assertion(
                workspace,
                semanticContext,
                syntacticDocument,
                position);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WithWorkspace(string source, string? stylesSource, Action<AkburaWorkspace, AkburaDocumentContext, AkburaSyntacticDocument> assertion, string? globalUsingsSource = null, MetadataReference? additionalReference = null)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(WorkspaceCompletionTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var compilation = CreateCompilation();
            if (additionalReference != null)
            {
                compilation = compilation.AddReferences(additionalReference);
            }

            var projectContext = new ProjectContext(
                ProjectId.CreateNewId(),
                projectFilePath: string.Empty,
                projectDirectory: directory,
                rootNamespace: string.Empty,
                compilation,
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

    [Theory]
    [InlineData(
        "new Border() { | }",
        "Width")]
    [InlineData(
        "count % 2 == 0 " +
        "? new Border() { Wid| } " +
        ": new Button() { }",
        "Width")]
    [InlineData(
        "count % 2 == 0 " +
        "? new Border() { } " +
        ": new Button() { Hei| }",
        "Height")]
    public void CSharpProjection_ObjectInitializersSupportCompletion(string expressionWithCaret, string expectedItem)
    {
        var source =
            "namespace Gallery;\n\n" +
            "using Avalonia.Controls;\n\n" +
            "state int count = 0;\n\n" +
            "<Border>\n" +
            "    {" +
            expressionWithCaret +
            "}\n" +
            "</Border>";

        WithCSharpProjection(
            source,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Expression,
                    context.Kind);
                Assert.Equal(
                    context.HostSpan.Length,
                    projection.ProjectedSpan.Length);
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    expectedItem);
            });
    }

    [Theory]
    [InlineData(
        "? new But|",
        "Button")]
    [InlineData(
        "? new Button() { Wid| }",
        "Width")]
    public void CSharpProjection_MultilineConditionalWithIndentedClosingBrace(string branchWithCaret, string expectedItem)
    {
        var source =
            "namespace Gallery;\r\n" +
            "\r\n" +
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "state int count = 0;\r\n" +
            "\r\n" +
            "<StackPanel>\r\n" +
            "\t<Border>\r\n" +
            "\t\t{count % 2 == 0\r\n" +
            "\t\t\t" +
            branchWithCaret +
            "\r\n" +
            "\t\t}\r\n" +
            "\t</Border>\r\n" +
            "</StackPanel>\r\n";

        WithCSharpProjection(
            source,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Expression,
                    context.Kind);
                Assert.Equal(
                    context.HostSpan.Length,
                    projection.ProjectedSpan.Length);
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    expectedItem);
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
                    state |
                    """,
                Kind: AkburaCSharpCompletionContextKind.Type,
                Item: "Card"),
            (
                Source: """
                    namespace Gallery;
                    using Avalonia.Controls;
                    param |
                    """,
                Kind: AkburaCSharpCompletionContextKind.Type,
                Item: "Card"),
            (
                Source: """
                    namespace Gallery;
                    using Avalonia.Controls;
                    param bind |
                    """,
                Kind: AkburaCSharpCompletionContextKind.Type,
                Item: "Card"),
            (
                Source: """
                    namespace Gallery;
                    using Avalonia.Controls;
                    param Car|
                    """,
                Kind: AkburaCSharpCompletionContextKind.Type,
                Item: "Card"),
            (
                Source: """
                    namespace Gallery;
                    using Avalonia.Controls;
                    inject |
                    """,
                Kind: AkburaCSharpCompletionContextKind.Type,
                Item: "Card"),
            (
                Source: """
                    namespace Gallery;
                    using Avalonia.Controls;
                    inject Car|
                    """,
                Kind: AkburaCSharpCompletionContextKind.Type,
                Item: "Card"),
            (
                Source: """
                    namespace Gallery;
                    using Avalonia.Controls;
                    inject Car|? Service;
                    <StackPanel/>
                    """,
                Kind: AkburaCSharpCompletionContextKind.Type,
                Item: "Card"),
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
            (
                Source: """
                    namespace Gallery;
                    using Avalonia.Controls;
                    command void Save(string name, Car| model);
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

    [Theory]
    [InlineData(
        "param System.Collections.Generic.IList<Avalonia.Media.StreamGeometry> |",
        "")]
    [InlineData(
        "param System.Collections.Generic.IList<Avalonia.Media.StreamGeometry> Ge|",
        "Ge")]
    public void CSharpProjection_DeclarationNamesUseRoslynCompletion(string declarationWithCaret, string expectedName)
    {
        WithCSharpProjection(
            declarationWithCaret,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.DeclarationName,
                    context.Kind);
                Assert.Equal(
                    expectedName,
                    projection.Root.ToFullString().Substring(
                        projection.ProjectedSpan.Start,
                        projection.ProjectedSpan.Length));

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
                    static item => item.DisplayText.Contains(
                        "geometr",
                        StringComparison.OrdinalIgnoreCase));
            });
    }

    [Fact]
    public void CSharpProjection_CommandOffersSyntheticMembers()
    {
        const string source = """
            namespace Gallery;
            using Avalonia.Controls;

            command void Save();
            var commandMember = Save.|;

            <StackPanel/>
            """;

        WithCSharpProjection(
            source,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Statement,
                    context.Kind);
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "Execute");
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "CanExecute");
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "IsExecuting");
            });
    }

    [Fact]
    public void CSharpProjection_UseEffectCallbackOffersComponentMembers()
    {
        const string source = """
            namespace Gallery;
            using System;
            using Avalonia.Controls;
            using Akbura.Hooks;

            state int count = 0;
            param string Title;
            inject IServiceProvider Services;
            command void Save();

            useEffect(() =>
            {
                |
            });

            <StackPanel/>
            """;

        WithCSharpProjection(
            source,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Statement,
                    context.Kind);
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "count");
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "Title");
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "Services");
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "Save");
            });
    }

    [Fact]
    public void CSharpProjection_UseEffectDependenciesOfferComponentMembers()
    {
        const string source = """
            namespace Gallery;
            using System;
            using Avalonia.Controls;
            using Akbura.Hooks;

            state int count = 0;
            param string Title;
            inject IServiceProvider Services;

            useEffect(() => { }, [|]);

            <StackPanel/>
            """;

        WithCSharpProjection(
            source,
            (semanticContext, context, projection, _) =>
            {
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Statement,
                    context.Kind);
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "count");
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "Title");
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "Services");
            });
    }

    [Theory]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "@using Avalonia.Layout;\n" +
        "Control.card { HorizontalAlignment: Cen|; }",
        "Center",
        AkburaCompletionKind.AkcssValue)]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card { Background: Dod|; }",
        "DodgerBlue",
        AkburaCompletionKind.AkcssColor)]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card { Tint: Dod|; }",
        "DodgerBlue",
        AkburaCompletionKind.AkcssColor)]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card { Padding: |; }",
        "(horizontal: 0, vertical: 0)",
        AkburaCompletionKind.AkcssValue)]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card { Padding: (hori|; }",
        "horizontal:",
        AkburaCompletionKind.AkcssValue)]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card { CornerRadius: new Cor|; }",
        "new CornerRadius(0)",
        AkburaCompletionKind.AkcssValue)]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card { Variant: Pri|; }",
        "Primary",
        AkburaCompletionKind.AkcssValue)]
    public void Completion_AkcssOffersExpectedTypeValues(string sourceWithCaret, string expectedDisplayText, AkburaCompletionKind expectedKind)
    {
        WithAkcssWorkspace(
            sourceWithCaret,
            importedStylesSource: null,
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var akcssContext = syntacticDocument
                    .GetAkcssCompletionContext(position);
                Assert.True(
                    akcssContext.Kind ==
                        AkcssCompletionContextKind.PropertyValue,
                    $"Prefix '{akcssContext.Prefix}', property " +
                    $"'{akcssContext.PropertyName}', owner " +
                    $"{akcssContext.OwnerSpan}, declaration " +
                    $"{akcssContext.ContainingDeclarationSpan}.");
                var semanticModel = semanticContext.Project.Compilation
                    .GetSemanticModel(semanticContext.Document.SyntaxTree);
                Assert.True(
                    semanticModel.TryGetAkcssValueCompletionInfo(
                        akcssContext.ContainingDeclarationSpan,
                        akcssContext.PropertyName,
                        out _),
                    $"Property '{akcssContext.PropertyName}', " +
                    $"declaration {akcssContext.ContainingDeclarationSpan}.");
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                var item = Assert.Single(
                    result.Items,
                    item => item.DisplayText == expectedDisplayText);
                Assert.Equal(expectedKind, item.Kind);
                Assert.False(result.IsIncomplete);
            });
    }

    [Fact]
    public void Completion_AkcssIfOffersAttachedPropertyShorthand()
    {
        const string source =
            "@using Avalonia.Controls;\n" +
            "Control.card { @if(Grid.Ro| > 0) { Width: 10; } }";

        WithAkcssWorkspace(
            source,
            importedStylesSource: null,
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var context = syntacticDocument
                    .GetAkcssCompletionContext(position);
                Assert.Equal(
                    AkcssCompletionContextKind
                        .AttachedPropertyExpression,
                    context.Kind);
                Assert.Equal("Grid", context.Qualifier);
                Assert.Equal("Ro", context.Prefix);

                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);
                var item = Assert.Single(
                    result.Items,
                    static item => item.DisplayText == "RowSpan");

                Assert.Equal("RowSpan", item.InsertText);
                Assert.Equal(
                    AkburaCompletionKind.Property,
                    item.Kind);
                Assert.Equal("Ro", syntacticDocument.Text.ToString(
                    result.ApplicableSpan));
            });
    }

    [Fact]
    public void Completion_AkcssQualifiedPropertyNameOffersOnlyAttachedProperties()
    {
        const string source =
            "@using Avalonia.Controls;\n" +
            "Control.card { Grid.R| }";

        WithAkcssWorkspace(
            source,
            importedStylesSource: null,
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var context = syntacticDocument
                    .GetAkcssCompletionContext(position);
                Assert.Equal(
                    AkcssCompletionContextKind.PropertyName,
                    context.Kind);
                Assert.Equal("Grid", context.Qualifier);
                Assert.Equal("R", context.Prefix);

                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Grid.Row" &&
                        item.InsertText == "Row: ");
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Grid.RowSpan" &&
                        item.InsertText == "RowSpan: ");
                Assert.DoesNotContain(
                    result.Items,
                    static item =>
                        item.DisplayText == "Grid.ShowGridLines");
                Assert.False(result.IsIncomplete);
            });
    }

    [Fact]
    public void Completion_AkcssPropertyNameOffersAttachedOwnerBeforeDot()
    {
        const string source =
            "@using Avalonia.Controls;\n" +
            "Control.card { Gr| }";

        WithAkcssWorkspace(
            source,
            importedStylesSource: null,
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var context = syntacticDocument
                    .GetAkcssCompletionContext(position);
                Assert.Equal(
                    AkcssCompletionContextKind.PropertyName,
                    context.Kind);
                Assert.Equal(string.Empty, context.Qualifier);
                Assert.Equal("Gr", context.Prefix);

                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Grid.Row" &&
                        item.InsertText == "Grid.Row: ");
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Grid.RowSpan" &&
                        item.InsertText == "Grid.RowSpan: ");
                Assert.DoesNotContain(
                    result.Items,
                    static item =>
                        item.DisplayText == "Grid.ShowGridLines");
                Assert.False(result.IsIncomplete);
            });
    }

    [Fact]
    public void Completion_AkcssPropertyCatalogSurvivesPrefixReplacement()
    {
        const string source =
            "@using Avalonia.Controls;\n" +
            "Control.card { H| }";

        WithAkcssWorkspace(
            source,
            importedStylesSource: null,
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Height" &&
                        item.InsertText == "Height: ");
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Grid.Row" &&
                        item.InsertText == "Grid.Row: ");
                Assert.False(result.IsIncomplete);
            });
    }

    [Fact]
    public void Completion_InlineAkcssOffersSemanticPropertyCatalog()
    {
        const string sourceWithCaret =
            "using Avalonia.Controls;\n\n" +
            "@akcss {\n" +
            "    Control.card { H| }\n" +
            "}\n\n" +
            "<Border/>\n";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                var context = syntacticDocument
                    .GetAkcssCompletionContext(position);
                Assert.Equal(
                    AkcssCompletionContextKind.PropertyName,
                    context.Kind);

                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Height" &&
                        item.InsertText == "Height: ");
                Assert.Contains(
                    result.Items,
                    static item =>
                        item.DisplayText == "Grid.Row" &&
                        item.InsertText == "Grid.Row: ");
                Assert.False(result.IsIncomplete);
            });
    }

    [Fact]
    public void Completion_InlineAkcssMatchesStandalonePropertyItems()
    {
        const string standaloneSource =
            "@using Avalonia.Controls;\n" +
            "Control.card { H }\n";
        const string inlineSourceWithCaret =
            "using Avalonia.Controls;\n\n" +
            "@akcss {\n" +
            "    Control.card { H| }\n" +
            "}\n\n" +
            "<Border/>\n";
        var inlinePosition = inlineSourceWithCaret.IndexOf('|');
        var inlineSource = inlineSourceWithCaret.Remove(
            inlinePosition,
            1);

        WithWorkspace(
            inlineSource,
            standaloneSource,
            (workspace, inlineSemanticContext, inlineDocument) =>
            {
                var stylesPath = Path.Combine(
                    Path.GetDirectoryName(
                        inlineSemanticContext.Document.FilePath)!,
                    "Styles.akcss");
                var standaloneText = SourceText.From(
                    standaloneSource);
                var standaloneSemanticContext =
                    workspace.OpenOrChangeDocumentContext(
                        new Uri(stylesPath),
                        standaloneText);
                var standaloneDocument =
                    AkburaSyntacticDocument.Parse(
                        standaloneText,
                        stylesPath);
                var standalonePosition =
                    standaloneSource.IndexOf(
                        "H }",
                        StringComparison.Ordinal) +
                    1;

                var standaloneResult =
                    workspace.LanguageServices.Completion
                        .GetCompletions(
                            standaloneDocument,
                            standaloneSemanticContext,
                            standalonePosition);
                var inlineResult =
                    workspace.LanguageServices.Completion
                        .GetCompletions(
                            inlineDocument,
                            inlineSemanticContext,
                            inlinePosition);

                Assert.Equal(
                    standaloneResult.Items.Select(
                        static item => (
                            item.DisplayText,
                            item.InsertText,
                            item.Kind,
                            item.NamespaceImport)),
                    inlineResult.Items.Select(
                        static item => (
                            item.DisplayText,
                            item.InsertText,
                            item.Kind,
                            item.NamespaceImport)));
                Assert.Equal(
                    standaloneResult.IsIncomplete,
                    inlineResult.IsIncomplete);
            });
    }

    [Fact]
    public void Completion_InlineAkcssCommentDoesNotFallBackToMarkup()
    {
        const string sourceWithCaret =
            "using Avalonia.Controls;\n\n" +
            "@akcss {\n" +
            "    // <Bor|\n" +
            "}\n\n" +
            "<Border/>\n";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetAkcssCompletionRegion(
                        position,
                        out _));
                Assert.True(
                    syntacticDocument.GetAkcssCompletionContext(
                        position).IsDefault);

                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Empty(result.Items);
            });
    }

    [Fact]
    public void Completion_AkcssCornerRadiusDoesNotOfferThicknessTuple()
    {
        const string source =
            "@using Avalonia.Controls;\n" +
            "Control.card { CornerRadius: |; }";

        WithAkcssWorkspace(
            source,
            importedStylesSource: null,
            (workspace, semanticContext, syntacticDocument, position) =>
            {
                var context = syntacticDocument
                    .GetAkcssCompletionContext(position);
                var semanticModel = semanticContext.Project.Compilation
                    .GetSemanticModel(semanticContext.Document.SyntaxTree);
                Assert.True(
                    semanticModel.TryGetAkcssValueCompletionInfo(
                        context.ContainingDeclarationSpan,
                        context.PropertyName,
                        out var info),
                    $"Kind {context.Kind}, property " +
                    $"'{context.PropertyName}'.");
                Assert.NotNull(info.ExpectedType);
                Assert.True(
                    semanticModel.IsAvaloniaCornerRadiusType(
                        info.ExpectedType!),
                    info.ExpectedType!.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat));
                var result = workspace.LanguageServices.Completion
                    .GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position);

                Assert.Contains(
                    result.Items,
                    static item => item.DisplayText ==
                        "new CornerRadius(0)");
                Assert.DoesNotContain(
                    result.Items,
                    static item => item.DisplayText ==
                        "(0, 0, 0, 0)");
            });
    }

    [Theory]
    [InlineData(
        "@using System;\n@using Avalonia.Controls;\n" +
        "StackPanel.card { Width: Math.Ro|; }",
        AkburaCSharpCompletionContextKind.Expression,
        "Round")]
    [InlineData(
        "@using System;\n@using Avalonia.Controls;\n" +
        "@utilities { " +
        "StackPanel.gap-(double value) { " +
        "Spacing: value.ToStr|; } }",
        AkburaCSharpCompletionContextKind.Expression,
        "ToString")]
    [InlineData(
        "@using System;\n@using Avalonia.Controls;\n" +
        "StackPanel.card { Width: Wid|; }",
        AkburaCSharpCompletionContextKind.Expression,
        "Width")]
    [InlineData(
        "@using System;\nStackP|.card { }",
        AkburaCSharpCompletionContextKind.Type,
        "StackPanel")]
    [InlineData(
        "@using System;\n@utilities { " +
        "StackPanel.gap-(dou| value) { } }",
        AkburaCSharpCompletionContextKind.Type,
        "double")]
    [InlineData(
        "@using System;\n@using Avalonia.Controls;\n" +
        "Control.card { @if(string.IsNullOrEmp|ty(\"\")) { } }",
        AkburaCSharpCompletionContextKind.Expression,
        "IsNullOrEmpty")]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "@utilities { StackP|.gap { } }",
        AkburaCSharpCompletionContextKind.Type,
        "StackPanel")]
    [InlineData(
        "@using Avalonia.Controls;\n@intercept StackP|;",
        AkburaCSharpCompletionContextKind.Type,
        "StackPanel")]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card { Width: |; }",
        AkburaCSharpCompletionContextKind.Expression,
        "Width")]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "Control.card { @if(|) { } }",
        AkburaCSharpCompletionContextKind.Expression,
        "false")]
    [InlineData(
        "@using System.Collections.Gen|;\n.card { }",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "Generic")]
    [InlineData(
        "@using |;\n.card { }",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "System")]
    public void CSharpProjection_AkcssContextsUseRoslynCompletion(string sourceWithCaret, AkburaCSharpCompletionContextKind expectedKind, string expectedItem)
    {
        WithAkcssWorkspace(
            sourceWithCaret,
            importedStylesSource: null,
            (_, semanticContext, syntacticDocument, position) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.Equal(expectedKind, completionContext.Kind);
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
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    expectedItem);
            });
    }

    [Fact]
    public void CSharpProjection_AkcssUsingCompletionSpanMapsToHost()
    {
        const string source =
            "@using System.Collections.Gen|;\n.card { }";
        WithAkcssWorkspace(
            source,
            importedStylesSource: null,
            (_, semanticContext, syntacticDocument, position) =>
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

                var completionList = RoslynCompletionTestHost
                    .GetCompletionsAsync(
                        semanticContext.Project.CSharpCompilation,
                        projection.Root,
                        projection.ProjectedPosition,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Assert.NotNull(completionList);
                var item = Assert.Single(
                    completionList.ItemsList,
                    static item => item.DisplayText == "Generic");

                Assert.True(projection.TryMapToHost(
                    item.Span,
                    out var itemHostSpan));
                Assert.True(
                    completionContext.HostSpan.Contains(itemHostSpan));
            });
    }

    [Fact]
    public void CSharpProjection_EmptyAkcssUsingCommitsNamespace()
    {
        const string sourceWithCaret = "@using |;\n.card { }";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithAkcssWorkspace(
            sourceWithCaret,
            importedStylesSource: null,
            (_, semanticContext, syntacticDocument, _) =>
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
                        "System",
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
                Assert.Equal("@using System;\n.card { }", changedHostText);
            });
    }

    [Fact]
    public void CSharpProjection_AkcssUsesCurrentTextWithStaleSemantics()
    {
        const string semanticSource =
            "@using System;\n" +
            "@using Avalonia.Controls;\n" +
            "Control.card { Width: Math.R; }";
        const string currentSourceWithCaret =
            "@using System;\n" +
            "@using Avalonia.Controls;\n" +
            "Control.card { Width: Math.Ro|; }";
        var position = currentSourceWithCaret.IndexOf('|');
        var currentSource = currentSourceWithCaret.Remove(position, 1);

        WithAkcssWorkspace(
            semanticSource,
            importedStylesSource: null,
            (_, semanticContext, _, _) =>
            {
                var syntacticDocument = AkburaSyntacticDocument.Parse(
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

                Assert.Equal(
                    "Math.Ro",
                    projection.Root.ToFullString().Substring(
                        projection.ProjectedSpan.Start,
                        projection.ProjectedSpan.Length));
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    "Round");
            });
    }

    [Theory]
    [InlineData(
        "@akcss { @utilities { " +
        "StackPanel.gap-(double value) { " +
        "Spacing: value.ToStr|; } } }",
        AkburaCSharpCompletionContextKind.Expression,
        "ToString")]
    [InlineData(
        "@akcss { @using System.Collections.Gen|; .card { } }",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "Generic")]
    [InlineData(
        "@akcss { StackP|.card { } }",
        AkburaCSharpCompletionContextKind.Type,
        "StackPanel")]
    public void CSharpProjection_InlineAkcssUsesRoslynCompletion(string inlineAkcssWithCaret, AkburaCSharpCompletionContextKind expectedKind, string expectedItem)
    {
        var sourceWithCaret =
            "using Avalonia.Controls;\n\n" +
            inlineAkcssWithCaret +
            "\n\n<StackPanel/>\n";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithWorkspace(
            source,
            (_, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.Equal(expectedKind, completionContext.Kind);
                Assert.True(AkburaCSharpProjectionFactory.TryCreate(
                    syntacticDocument,
                    semanticContext,
                    completionContext,
                    out var projection));
                Assert.Equal(
                    AkburaCSharpImportSyntaxKind.InlineAkcssBlock,
                    projection.ImportContext.SyntaxKind);
                AssertCompletionContains(
                    semanticContext,
                    projection,
                    expectedItem);
            });
    }

    [Theory]
    [InlineData(
        "@using System;\n" +
        "@using Avalonia.Controls;\n" +
        "Control.card { Width: Math.Ma|x(1, 2); }",
        "Max")]
    [InlineData(
        "@using Avalonia.Controls;\n" +
        "@utilities { StackPanel.gap-(double value) { " +
        "Spacing: val|ue; } }",
        "value")]
    public void CSharpProjection_AkcssMapsRoslynQuickInfoToHost(string sourceWithCaret, string expectedToken)
    {
        WithAkcssWorkspace(
            sourceWithCaret,
            importedStylesSource: null,
            (_, semanticContext, syntacticDocument, position) =>
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
                    syntacticDocument.Text.ToString(hostSpan));
            });
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
    public void CSharpCompletionChangeMapper_MapsAkcssAutoImportBeforeModules()
    {
        const string sourceWithCaret =
            "@using Avalonia.Controls;\r\n" +
            "@using Imported.akcss;\r\n" +
            "\r\n" +
            "Control.card {\r\n" +
            "    Tag: new ObservableCollec|;\r\n" +
            "}\r\n";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithAkcssWorkspace(
            sourceWithCaret,
            importedStylesSource: ".imported { Width: 1; }",
            (_workspace, semanticContext, syntacticDocument, _) =>
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
                    AkburaCSharpImportSyntaxKind.AkcssDocument,
                    projection.ImportContext.SyntaxKind);

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
                    "@using Avalonia.Controls;\r\n" +
                    "@using System.Collections.ObjectModel;\r\n" +
                    "@using Imported.akcss;",
                    changedHostText,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "new ObservableCollection",
                    changedHostText,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "\r\nusing System.Collections.ObjectModel;",
                    changedHostText,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void CSharpCompletionChangeMapper_InsertsFirstAkcssImportWithLf()
    {
        const string sourceWithCaret =
            "@using Avalonia.Controls;\n" +
            "@using Imported.akcss;\n" +
            "\n" +
            "Control.card {\n" +
            "    Tag: new ObservableCollec|;\n" +
            "}\n";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithAkcssWorkspace(
            sourceWithCaret,
            importedStylesSource: ".imported { Width: 1; }",
            (_, semanticContext, syntacticDocument, _) =>
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
                Assert.StartsWith(
                    "@using Avalonia.Controls;\n" +
                    "@using System.Collections.ObjectModel;\n" +
                    "@using Imported.akcss;",
                    changedHostText,
                    StringComparison.Ordinal);
                Assert.DoesNotContain('\r', changedHostText);
            });
    }

    [Fact]
    public void CSharpCompletionChangeMapper_DoesNotDuplicateAkcssImport()
    {
        const string sourceWithCaret =
            "@using System.Collections.ObjectModel;\n" +
            "@using Avalonia.Controls;\n" +
            "\n" +
            "Control.card {\n" +
            "    Tag: new ObservableCollec|;\n" +
            "}\n";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);

        WithAkcssWorkspace(
            sourceWithCaret,
            importedStylesSource: null,
            (_, semanticContext, syntacticDocument, _) =>
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
                    "new ObservableCollec".Length);
                var replacement = new TextChange(
                    replacementSpan,
                    "new ObservableCollection");
                var duplicateImport = new TextChange(
                    new TextSpan(0, 0),
                    "using System.Collections.ObjectModel;\n");
                var changes = ImmutableArray.Create(
                    duplicateImport,
                    replacement);
                var changedProjectedText = projectedText.WithChanges(changes);
                var completion = CompletionChange.Create(
                    new TextChange(
                        new TextSpan(0, projectedText.Length),
                        changedProjectedText.ToString()),
                    changes,
                    newPosition: null,
                    includesCommitCharacter: false);
                Assert.True(
                    AkburaCSharpCompletionChangeMapper
                        .TryMapCompletionChange(
                            SourceText.From(source),
                            projectedText,
                            projection,
                            completion,
                            out var mapped));

                var changedHostText = SourceText.From(source)
                    .WithChanges(mapped.Changes)
                    .ToString();
                Assert.Equal(
                    1,
                    CountOccurrences(
                        changedHostText,
                        "@using System.Collections.ObjectModel;"));
                Assert.Contains(
                    "new ObservableCollection",
                    changedHostText,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void CSharpCompletionChangeMapper_MapsInlineAkcssAutoImportInsideBlock()
    {
        const string sourceWithCaret =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "@akcss {\r\n" +
            "    Control.card {\r\n" +
            "        Tag: new ObservableCollec|;\r\n" +
            "    }\r\n" +
            "}\r\n" +
            "\r\n" +
            "<Border/>\r\n";
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
                Assert.Equal(
                    AkburaCSharpImportSyntaxKind.InlineAkcssBlock,
                    projection.ImportContext.SyntaxKind);
                Assert.Equal(
                    "    ",
                    projection.ImportContext.Indentation);

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
                    "@akcss {\r\n" +
                    "    @using System.Collections.ObjectModel;\r\n",
                    changedHostText,
                    StringComparison.Ordinal);
                Assert.Equal(
                    1,
                    CountOccurrences(
                        changedHostText,
                        "@using System.Collections.ObjectModel;"));
                Assert.DoesNotContain(
                    "\r\nusing System.Collections.ObjectModel;",
                    changedHostText,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "new ObservableCollection",
                    changedHostText,
                    StringComparison.Ordinal);
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
    public void CSharpCompletionChangeMapper_RejectsUnknownTriviaChanges()
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
                var triviaChange = new TextChange(
                    new TextSpan(wrapperNameStart, 0),
                    " ");
                var changes = ImmutableArray.Create(
                    triviaChange,
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

    [Theory]
    [InlineData("B", 'B', false)]
    [InlineData("Brus", 's', true)]
    public void CSharpCompletion_AutomaticallyImportsBrushes(string prefix, char triggerCharacter, bool isIncompleteSession)
    {
        const string sourceTemplate = """
            using Avalonia.Controls;

            <Border>
                {new Border()
                {
                    Background = PREFIX|
                }}
            </Border>
            """;
        var sourceWithCaret = sourceTemplate.Replace(
            "PREFIX",
            prefix,
            StringComparison.Ordinal);
        var position = sourceWithCaret.IndexOf(
            '|',
            StringComparison.Ordinal);
        var source = sourceWithCaret.Remove(
            position,
            count: 1);

        WithWorkspace(
            source,
            (_workspace, semanticContext, syntacticDocument) =>
            {
                Assert.True(
                    syntacticDocument.TryGetCSharpCompletionContext(
                        position,
                        out var completionContext));
                Assert.Equal(
                    AkburaCSharpCompletionContextKind.Expression,
                    completionContext.Kind);
                Assert.True(
                    AkburaCSharpProjectionFactory.TryCreate(
                        syntacticDocument,
                        semanticContext,
                        completionContext,
                        out var projection));

                var completion = RoslynCompletionTestHost
                    .GetAutomaticImportCompletionAsync(
                        semanticContext.Project.CSharpCompilation,
                        projection.Root,
                        projection.ProjectedPosition,
                        triggerCharacter,
                        displayText: "Brushes",
                        isIncompleteSession,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Assert.NotNull(completion);
                Assert.Equal(
                    "Brushes",
                    completion.Value.Item.DisplayText);
                Assert.True(
                    completion.Value.Item.IsComplexTextEdit);
                Assert.Contains(
                    "Avalonia.Media",
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
                var changedText = SourceText.From(source)
                    .WithChanges(mapped.Changes)
                    .ToString();

                Assert.Contains(
                    "using Avalonia.Media;",
                    changedText,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "Background = Brushes",
                    changedText,
                    StringComparison.Ordinal);
                Assert.Equal(
                    1,
                    CountOccurrences(
                        changedText,
                        "using Avalonia.Media;"));
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
        "param object Items = new ObservableCollec|();",
        "ObservableCollection",
        "System.Collections.ObjectModel",
        "param object Items = new ObservableCollection();")]
    [InlineData(
        "inject CancellationTok| CancellationToken;",
        "CancellationToken",
        "System.Threading",
        "inject CancellationToken CancellationToken;")]
    [InlineData(
        "command Tas| Save();",
        "Task",
        "System.Threading.Tasks",
        "command Task Save();")]
    [InlineData(
        "state string name = \"\";\nvar result = name.SomeExtens|;",
        "SomeExtension",
        "Gallery.Extensions",
        "name.SomeExtension")]
    public void CSharpCompletionChangeMapper_MapsRoslynAutoImports(string fragmentWithCaret, string displayText, string expectedNamespace, string expectedFragment)
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
    public void CSharpProjection_MapsRoslynQuickInfoAcrossContexts(string fragmentWithCaret, string expectedToken)
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

    private static void WithCSharpProjection(string sourceWithCaret, Action<AkburaDocumentContext, AkburaCSharpCompletionContext, AkburaCSharpProjection, int> assertion)
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

    private static int CountOccurrences(string value, string search)
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

    private static void AssertCompletionContains(AkburaDocumentContext semanticContext, AkburaCSharpProjection projection, string displayText)
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

    private static void AssertDataType(AkburaSemanticModel semanticModel, string typeText, string expectedType)
    {
        Assert.True(
            semanticModel.TryBindMarkupDataTypeDirective(
                typeText,
                out var dataType),
            $"Expected '{typeText}' to resolve.");
        Assert.Equal(
            expectedType,
            dataType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static AkburaCompletionResult GetCompletionResult(AkburaWorkspace workspace, AkburaProjectId projectId, string path, string source)
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

                    public double Height { get; set; }

                    public object? Tag { get; set; }

                    public Avalonia.Layout.HorizontalAlignment
                        HorizontalAlignment { get; set; }

                    public Avalonia.Media.IBrush? Background { get; set; }

                    public Avalonia.Media.Color Tint { get; set; }

                    public Avalonia.Thickness Padding { get; set; }

                    public Avalonia.CornerRadius CornerRadius { get; set; }

                    public Gallery.Variant Variant { get; set; }

                    public event System.EventHandler? Loaded;
                }

                public sealed class StackPanel : Control
                {
                    public double Spacing { get; set; }
                }

                public sealed class Border : Control
                {
                }

                public sealed class Button : Control
                {
                }

                public class Page : Control
                {
                }

                public sealed class ContentPage : Page
                {
                }

                public sealed class ItemsControl : Control
                {
                    public object? ItemsSource { get; set; }

                    [Avalonia.Metadata.InheritDataTypeFromItems(
                        "ItemsSource")]
                    public object? ItemTemplate { get; set; }
                }

                public abstract class AbstractView : Control
                {
                }

                public sealed class WideControl : Control
                {
                    public object A00 { get; set; }
                    public object A01 { get; set; }
                    public object A02 { get; set; }
                    public object A03 { get; set; }
                    public object A04 { get; set; }
                    public object A05 { get; set; }
                    public object A06 { get; set; }
                    public object A07 { get; set; }
                    public object A08 { get; set; }
                    public object A09 { get; set; }
                    public object A10 { get; set; }
                    public object A11 { get; set; }
                    public object A12 { get; set; }
                    public object A13 { get; set; }
                    public object A14 { get; set; }
                    public object A15 { get; set; }
                    public object A16 { get; set; }
                    public object A17 { get; set; }
                    public object A18 { get; set; }
                    public object A19 { get; set; }
                    public object A20 { get; set; }
                    public object A21 { get; set; }
                    public object A22 { get; set; }
                    public object A23 { get; set; }
                    public object A24 { get; set; }
                    public object A25 { get; set; }
                    public object A26 { get; set; }
                    public object A27 { get; set; }
                    public object A28 { get; set; }
                    public object A29 { get; set; }
                    public object A30 { get; set; }
                    public object A31 { get; set; }
                    public object A32 { get; set; }
                    public object A33 { get; set; }
                    public object A34 { get; set; }
                    public object A35 { get; set; }
                    public object A36 { get; set; }
                    public object A37 { get; set; }
                    public object A38 { get; set; }
                    public object A39 { get; set; }
                    public object A40 { get; set; }
                    public object A41 { get; set; }
                    public object A42 { get; set; }
                    public object A43 { get; set; }
                    public object A44 { get; set; }
                    public object A45 { get; set; }
                    public object A46 { get; set; }
                    public object A47 { get; set; }
                    public object A48 { get; set; }
                    public object A49 { get; set; }
                    public object A50 { get; set; }
                    public object A51 { get; set; }
                    public object A52 { get; set; }
                    public object A53 { get; set; }
                    public object A54 { get; set; }
                }

                public sealed class Grid : Control
                {
                    public bool ShowGridLines { get; set; }

                    public static readonly
                        Avalonia.AttachedProperty<int> RowProperty = new();

                    public static readonly
                        Avalonia.AttachedProperty<int> ColumnProperty = new();

                    public static readonly
                        Avalonia.AttachedProperty<int> RowSpanProperty = new();

                    public static readonly
                        Avalonia.AttachedProperty<int> ColumnSpanProperty = new();

                    public static int GetRow(Control control) => 0;

                    public static int GetColumn(Control control) => 0;

                    public static int GetRowSpan(Control control) => 0;

                    public static int GetColumnSpan(Control control) => 0;

                    public static void SetRow(
                        Control control,
                        int value)
                    {
                    }

                    public static void SetColumn(
                        Control control,
                        int value)
                    {
                    }

                    public static void SetRowSpan(
                        Control control,
                        int value)
                    {
                    }

                    public static void SetColumnSpan(
                        Control control,
                        int value)
                    {
                    }
                }
            }

            namespace Avalonia.Collections
            {
                public class AvaloniaList<T> :
                    System.Collections.Generic.List<T>
                {
                }
            }

            namespace Avalonia.Metadata
            {
                [System.AttributeUsage(
                    System.AttributeTargets.Property)]
                public sealed class InheritDataTypeFromItemsAttribute :
                    System.Attribute
                {
                    public InheritDataTypeFromItemsAttribute(
                        string propertyName)
                    {
                    }

                    public System.Type? AncestorType { get; set; }
                }
            }

            namespace Avalonia
            {
                public readonly struct Thickness
                {
                }

                public readonly struct CornerRadius
                {
                    public CornerRadius(double uniformRadius)
                    {
                    }

                    public CornerRadius(
                        double topLeft,
                        double topRight,
                        double bottomRight,
                        double bottomLeft)
                    {
                    }

                }

                public class AvaloniaProperty
                {
                }

                public class AvaloniaProperty<T> : AvaloniaProperty
                {
                }

                public class AttachedProperty<T> :
                    AvaloniaProperty<T>
                {
                }
            }

            namespace Avalonia.Layout
            {
                public enum HorizontalAlignment
                {
                    Left,
                    Center,
                    Right,
                    Stretch,
                }
            }

            namespace Avalonia.Media
            {
                public sealed class StreamGeometry
                {
                }

                public interface IBrush
                {
                }

                public static class Brushes
                {
                    public static IBrush Red => default!;
                }

                public readonly struct Color
                {
                }

                public static class Colors
                {
                    public static Color AliceBlue => default;

                    public static Color Black => default;

                    public static Color DodgerBlue => default;

                    public static Color White => default;
                }
            }

            namespace Avalonia.Data
            {
                public enum BindingMode
                {
                    OneWay,
                    TwoWay,
                }

                public class Binding
                {
                    public string? Path { get; set; }

                    public BindingMode Mode { get; set; }

                    public bool IsAsync { get; set; }

                    public string? ElementName { get; set; }

                    public object? Source { get; set; }

                    public object? RelativeSource { get; set; }
                }

                public class ReflectionBinding : Binding
                {
                }

                public class CompiledBinding : Binding
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

            namespace Gallery
            {
                public enum Variant
                {
                    Primary,
                    Secondary,
                }
            }

            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control
                {
                }
            }

            namespace Akbura.CompilerAnotations
            {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class UseHookAttribute : System.Attribute
                {
                }

                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class SelfAttribute : System.Attribute
                {
                }
            }

            namespace Akbura.ComponentTree
            {
                public readonly struct State<T>
                {
                }
            }

            namespace Akbura.Hooks
            {
                public static class ResourceHooks
                {
                    [Akbura.CompilerAnotations.UseHook]
                    public static Akbura.ComponentTree.State<T>
                        useStaticResource<T>(
                            [Akbura.CompilerAnotations.Self]
                            this Akbura.AkburaControl control,
                            object key,
                            T fallback = default!) => default;

                    [Akbura.CompilerAnotations.UseHook]
                    public static Akbura.ComponentTree.State<T>
                        useStaticResource<T>(
                            [Akbura.CompilerAnotations.Self]
                            this Akbura.AkburaControl control,
                            object key,
                            T fallback,
                            bool required) => default;

                    [Akbura.CompilerAnotations.UseHook]
                    public static void useRenderOnly(
                        [Akbura.CompilerAnotations.Self]
                        this Akbura.AkburaControl control)
                    {
                    }
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
                    public CustomExtension(int seed)
                    {
                    }

                    public Gallery.Variant Mode { get; set; }

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
                public static class CustomHooks
                {
                    [Akbura.CompilerAnotations.UseHook]
                    public static Akbura.ComponentTree.State<int>
                        useCustomState() => default;
                }

                public class ViewModelBase
                {
                    public string Inherited { get; } = "";

                    protected string Hidden { get; } = "";

                    public static string StaticMember { get; } = "";
                }

                public sealed class MainViewModel : ViewModelBase
                {
                    public Customer Customer { get; } = new();

                    public Customer[] Customers { get; } = [];

                    public System.Collections.Generic.List<Customer>
                        CustomerList { get; } = [];

                    public System.Threading.Tasks.Task<Customer> Pending
                        { get; } = System.Threading.Tasks.Task.FromResult(
                            new Customer());

                    public bool IsActive { get; }

                    public string PublicField = "";

                    public string WriteOnly
                    {
                        set { }
                    }
                }

                public sealed class Customer
                {
                    public string Name { get; } = "";

                    public Address Address { get; } = new();
                }

                public sealed class Address
                {
                    public string City { get; } = "";
                }

                public sealed class Options
                {
                    public string Name { get; set; } = "";
                }

                public sealed class Outer
                {
                    public sealed class Nested
                    {
                    }
                }

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

    private static PortableExecutableReference CreateEmbeddedComponentReference(string directory)
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

    private static PortableExecutableReference CreateReferencedHookReference()
    {
        const string source = """
            namespace Akbura.CompilerAnotations
            {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class UseHookAttribute : System.Attribute
                {
                }

                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class SelfAttribute : System.Attribute
                {
                }
            }

            namespace Akbura.ComponentTree
            {
                public readonly struct State<T>
                {
                }
            }

            namespace Akbura
            {
                public class AkburaControl
                {
                }
            }

            namespace ReferencedHooks
            {
                public static class StateHooks
                {
                    [Akbura.CompilerAnotations.UseHook]
                    public static Akbura.ComponentTree.State<int>
                        useReferencedState(
                            [Akbura.CompilerAnotations.Self]
                            this Akbura.AkburaControl control) => default;
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "ReferencedHooks",
            [CSharpSyntaxTree.ParseText(source)],
            CreatePlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static ResourceDescription CreateEmbeddedSourceResource(string name, string content)
    {
        var preamble = System.Text.Encoding.Unicode.GetPreamble();
        var text = System.Text.Encoding.Unicode.GetBytes(content);
        var bytes = new byte[preamble.Length + text.Length];
        preamble.CopyTo(bytes, 0);
        text.CopyTo(bytes, preamble.Length);
        return CreateResource(name, bytes);
    }

    private static ResourceDescription CreateResource(string name, byte[] content)
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
