using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Projects;
using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class ResourceCompletionIndexTests
{
    private const string AvaloniaNamespace =
        "https://github.com/avaloniaui";
    private const string XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly PortableExecutableReference
        ResourceMarkupExtensionsReference = EmitReference(
            "Avalonia.Markup.Xaml",
            """
            namespace Avalonia.Markup.Xaml.MarkupExtensions;

            public sealed class StaticResourceExtension
            {
                public StaticResourceExtension(object key)
                {
                }

                public object ProvideValue() => new();
            }

            public sealed class DynamicResourceExtension
            {
                public DynamicResourceExtension(object key)
                {
                }

                public object ProvideValue() => new();
            }
            """);

    [Fact]
    public void ApplicationImportsTraverseLocalDictionariesAndCycles()
    {
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<ResourceInclude Source=\"/Resources/A.axaml\" />")),
            ("Resources/A.axaml", Dictionary(
                "<SolidColorBrush x:Key=\"LocalAccent\" />" +
                "<ResourceInclude Source=\"B.axaml\" />")),
            ("Resources/B.axaml", Dictionary(
                "<x:String x:Key=\"SharedText\">value</x:String>" +
                "<ResourceInclude Source=\"A.axaml\" />")),
            ("Resources/Unused.axaml", Dictionary(
                "<SolidColorBrush x:Key=\"NotImported\" />")));

        var candidates = GetCandidates(workspace);

        Assert.Contains(candidates, candidate =>
            candidate.Key == "LocalAccent");
        Assert.Contains(candidates, candidate =>
            candidate.Key == "SharedText");
        Assert.DoesNotContain(candidates, candidate =>
            candidate.Key == "NotImported");
        Assert.Single(candidates, candidate =>
            candidate.Key == "LocalAccent");
    }

    [Fact]
    public void LibraryExportsRequireAnExactImportedEntryPoint()
    {
        var attributeReference = EmitReference(
            "Akbura",
            """
            namespace Akbura.CompilerAnotations;

            [System.AttributeUsage(
                System.AttributeTargets.Assembly,
                AllowMultiple = true)]
            public sealed class ExportResourceForAkburaCompletionAttribute
                : System.Attribute
            {
                public ExportResourceForAkburaCompletionAttribute(
                    string dictionaryPath,
                    string key,
                    System.Type resourceType)
                {
                }
            }
            """);
        var themeReference = EmitReference(
            "Acme.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "ThemeAccent",
                typeof(string))]
            [assembly: ExportResourceForAkburaCompletion(
                "Themes/Compact.axaml",
                "CompactSpacing",
                typeof(double))]
            """,
            attributeReference);
        var compilation = CreateApplicationCompilation(
            attributeReference,
            themeReference);
        using var workspace = CreateWorkspace(compilation);

        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(string.Empty)));
        Assert.DoesNotContain(
            GetCandidates(workspace),
            candidate => candidate.Key == "ThemeAccent");

        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<ResourceInclude " +
                "Source=\"avares://Acme.Theme/Styles.axaml\" />")));
        var candidates = GetCandidates(workspace);
        Assert.Contains(candidates, candidate =>
            candidate.Key == "ThemeAccent");
        Assert.DoesNotContain(candidates, candidate =>
            candidate.Key == "CompactSpacing");

        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(string.Empty)));
        Assert.DoesNotContain(
            GetCandidates(workspace),
            candidate => candidate.Key == "ThemeAccent");
    }

    [Fact]
    public void SameDictionaryPathInAnotherAssemblyDoesNotActivateItsExports()
    {
        var contract = CreateExportContractReference();
        var firstTheme = EmitReference(
            "First.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "FirstThemeKey",
                typeof(string))]
            """,
            contract);
        var secondTheme = EmitReference(
            "Second.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "SecondThemeKey",
                typeof(string))]
            """,
            contract);
        var compilation = CreateApplicationCompilation(
            contract,
            firstTheme,
            secondTheme);
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<ResourceInclude " +
                "Source=\"avares://First.Theme/Styles.axaml\" />")));

        var candidates = GetCandidates(workspace);

        Assert.Contains(
            candidates,
            static candidate => candidate.Key == "FirstThemeKey");
        Assert.DoesNotContain(
            candidates,
            static candidate => candidate.Key == "SecondThemeKey");
    }

    [Fact]
    public void ApplicationCanReachLibraryExportsThroughLocalDictionary()
    {
        var contract = CreateExportContractReference();
        var theme = EmitReference(
            "Acme.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "LibraryAccent",
                typeof(string))]
            """,
            contract);
        var compilation = CreateApplicationCompilation(contract, theme);
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<ResourceInclude " +
                "Source=\"Resources/Bridge.axaml\" />")),
            ("Resources/Bridge.axaml", Dictionary(
                "<ResourceInclude " +
                "Source=\"avares://Acme.Theme/Styles.axaml\" />")));

        Assert.Contains(
            GetCandidates(workspace),
            static candidate => candidate.Key == "LibraryAccent");
    }

    [Fact]
    public void RemovingOneImportRouteKeepsExportsFromAnotherRoute()
    {
        var contract = CreateExportContractReference();
        var theme = EmitReference(
            "Acme.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "SharedLibraryKey",
                typeof(string))]
            """,
            contract);
        var compilation = CreateApplicationCompilation(contract, theme);
        using var workspace = CreateWorkspace(compilation);
        var index = new ResourceCompletionIndex();
        const string libraryImport =
            "<ResourceInclude " +
            "Source=\"avares://Acme.Theme/Styles.axaml\" />";

        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<ResourceInclude Source=\"Resources/A.axaml\" />" +
                "<ResourceInclude Source=\"Resources/B.axaml\" />")),
            ("Resources/A.axaml", Dictionary(libraryImport)),
            ("Resources/B.axaml", Dictionary(libraryImport)));
        Assert.Contains(
            GetCandidates(workspace, index),
            static candidate => candidate.Key == "SharedLibraryKey");

        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<ResourceInclude Source=\"Resources/B.axaml\" />")),
            ("Resources/B.axaml", Dictionary(libraryImport)));
        Assert.Contains(
            GetCandidates(workspace, index),
            static candidate => candidate.Key == "SharedLibraryKey");

        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(string.Empty)));
        Assert.DoesNotContain(
            GetCandidates(workspace, index),
            static candidate => candidate.Key == "SharedLibraryKey");
    }

    [Fact]
    public void WrapperEntryPointExportsOnlyItsDeclaredPublicSurface()
    {
        var contract = CreateExportContractReference();
        var theme = EmitReference(
            "Acme.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Public.axaml",
                "ReexportedKey",
                typeof(string))]
            [assembly: ExportResourceForAkburaCompletion(
                "Internal.axaml",
                "HiddenInternalKey",
                typeof(string))]
            """,
            contract);
        var compilation = CreateApplicationCompilation(contract, theme);
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<ResourceInclude " +
                "Source=\"avares://Acme.Theme/Public.axaml\" />")));

        var candidates = GetCandidates(workspace);

        Assert.Contains(
            candidates,
            static candidate => candidate.Key == "ReexportedKey");
        Assert.DoesNotContain(
            candidates,
            static candidate => candidate.Key == "HiddenInternalKey");
    }

    [Fact]
    public void ReplacingSameNamedReferenceInvalidatesCachedExports()
    {
        var contract = CreateExportContractReference();
        var firstTheme = EmitReference(
            "Acme.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "OldThemeKey",
                typeof(string))]
            """,
            contract);
        var firstCompilation = CreateApplicationCompilation(
            contract,
            firstTheme);
        using var workspace = CreateWorkspace(firstCompilation);
        SynchronizeResources(
            workspace,
            firstCompilation,
            ("App.axaml", Application(
                "<ResourceInclude " +
                "Source=\"avares://Acme.Theme/Styles.axaml\" />")));
        var index = new ResourceCompletionIndex();

        Assert.Contains(
            GetCandidates(workspace, index),
            static candidate => candidate.Key == "OldThemeKey");

        var secondTheme = EmitReference(
            "Acme.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "NewThemeKey",
                typeof(string))]
            """,
            contract);
        var secondCompilation = CreateApplicationCompilation(
            contract,
            secondTheme);
        var original = workspace.CurrentSolution.GetRequiredProject(
            workspace.DefaultProjectId).Context;
        workspace.AddOrUpdateProject(new ProjectContext(
            original.RoslynProjectId,
            original.ProjectFilePath,
            original.ProjectDirectory,
            original.RootNamespace,
            secondCompilation,
            original.ProjectReferences));

        var candidates = GetCandidates(workspace, index);

        Assert.Contains(
            candidates,
            static candidate => candidate.Key == "NewThemeKey");
        Assert.DoesNotContain(
            candidates,
            static candidate => candidate.Key == "OldThemeKey");
    }

    [Fact]
    public void ResourceCompletionReplacesOnlyTheKeyContent()
    {
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<SolidColorBrush x:Key=\"AccentBrush\" />")));
        const string marked =
            "<Border Background=${StaticResource \"Accent|Bru\"} />";
        var position = marked.IndexOf('|');
        var source = marked.Remove(position, 1);
        var text = SourceText.From(source);
        var semanticContext = workspace.OpenOrChangeDocumentContext(
            workspace.DefaultProjectId,
            new Uri("C:/Project/MainView.akbura"),
            text);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "C:/Project/MainView.akbura");

        var result = workspace.LanguageServices.Completion.GetCompletions(
            document,
            semanticContext,
            position);
        var item = Assert.Single(
            result.Items,
            candidate => candidate.DisplayText == "AccentBrush");
        Assert.Equal("AccentBru", text.ToString(result.ApplicableSpan));

        var change = workspace.LanguageServices.Completion
            .GetCompletionChange(
                document,
                semanticContext,
                position,
                item);
        Assert.Equal(
            "<Border Background=${StaticResource \"AccentBrush\"} />",
            text.WithChanges(change.Changes).ToString());
    }

    [Theory]
    [InlineData(
        "<Border Background=${StaticResourceExtension Accent|} />")]
    [InlineData(
        "<Border Background=${Avalonia.Markup.Xaml.MarkupExtensions.StaticResource Accent|} />")]
    public void ResourceCompletionAcceptsBinderSupportedExtensionNames(string marked)
    {
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<SolidColorBrush x:Key=\"AccentBrush\" />")));

        var result = GetCompletion(workspace, marked);

        Assert.Contains(
            result.Items,
            static candidate => candidate.DisplayText == "AccentBrush");
    }

    [Fact]
    public void CustomSameNamedExtensionDoesNotActivateResourceCompletion()
    {
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<SolidColorBrush x:Key=\"AccentBrush\" />")));

        var result = GetCompletion(
            workspace,
            "<Border Background=${Gallery.StaticResource Acc|} />");

        Assert.DoesNotContain(
            result.Items,
            static candidate => candidate.DisplayText == "AccentBrush");
    }

    [Fact]
    public void ResourceExtensionIdentityRejectsForeignSourceCompilation()
    {
        const string metadataName =
            "Avalonia.Markup.Xaml.MarkupExtensions." +
            "StaticResourceExtension";
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);
        var text = SourceText.From("<Border />");
        var context = workspace.OpenOrChangeDocumentContext(
            workspace.DefaultProjectId,
            new Uri("C:/Project/Identity.akbura"),
            text);
        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        var canonicalType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName(metadataName));
        var foreignCompilation = CSharpCompilation.Create(
            "MyApp",
            [
                CSharpSyntaxTree.ParseText(
                    """
                    namespace Avalonia.Markup.Xaml.MarkupExtensions;

                    public sealed class StaticResourceExtension
                    {
                    }
                    """),
            ],
            [GetPlatformReference()],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var foreignType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            foreignCompilation.GetTypeByMetadataName(metadataName));
        using var foreignWorkspace = CreateWorkspace(foreignCompilation);
        var foreignContext =
            foreignWorkspace.OpenOrChangeDocumentContext(
                foreignWorkspace.DefaultProjectId,
                new Uri("C:/Project/ForeignIdentity.akbura"),
                text);
        var foreignSemanticModel =
            foreignContext.Project.Compilation.GetSemanticModel(
                foreignContext.Document.SyntaxTree);

        Assert.True(
            semanticModel
                .IsAvaloniaResourceMarkupExtensionTypeForCompletion(
                    canonicalType));
        Assert.False(
            foreignSemanticModel
                .IsAvaloniaResourceMarkupExtensionTypeForCompletion(
                    foreignType));
    }

    [Fact]
    public void LocalClrNamespaceTypeUsesItsExplicitAssembly()
    {
        var resourceTypes = EmitReference(
            "Acme.Types",
            """
            namespace Acme.Resources;

            public sealed class Brush
            {
            }
            """);
        var compilation = CreateApplicationCompilation(resourceTypes);
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            (
                "App.axaml",
                "<Application xmlns=\"" + AvaloniaNamespace + "\" " +
                "xmlns:x=\"" + XamlNamespace + "\" " +
                "xmlns:theme=\"clr-namespace:Acme.Resources;" +
                "assembly=Acme.Types\">" +
                "<Application.Resources><ResourceDictionary>" +
                "<theme:Brush x:Key=\"QualifiedBrush\" />" +
                "</ResourceDictionary></Application.Resources>" +
                "</Application>"));

        var candidate = Assert.Single(
            GetCandidates(workspace),
            static candidate => candidate.Key == "QualifiedBrush");
        var origin = Assert.Single(candidate.Origins);

        Assert.Equal(
            "Acme.Types",
            origin.ResourceType?.ContainingAssembly.Name);
        Assert.Equal(
            "Acme.Resources.Brush",
            origin.ResourceType?.ToDisplayString());
    }

    [Fact]
    public void LargeCatalogIsIncompleteAndCanBeRefiltered()
    {
        var resources = new StringBuilder();
        for (var index = 0; index < 75; index++)
        {
            resources.Append("<x:String x:Key=\"Key");
            resources.Append(index.ToString("D2"));
            resources.Append("\">value</x:String>");
        }

        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(resources.ToString())));

        var empty = GetCompletion(
            workspace,
            "<Border Tag=${StaticResource |} />");
        Assert.Equal(50, empty.Items.Length);
        Assert.True(empty.IsIncomplete);

        var filtered = GetCompletion(
            workspace,
            "<Border Tag=${StaticResource Key74|} />");
        Assert.False(filtered.IsIncomplete);
        Assert.Contains(filtered.Items, item =>
            item.DisplayText == "Key74");
    }

    [Theory]
    [InlineData(
        "<Border Tag=${StaticResource Icon.Ho|me} />",
        "Icon.Home",
        "<Border Tag=${StaticResource Icon.Home} />")]
    [InlineData(
        "<Border Tag=${DynamicResource --color-|slate-300} />",
        "--color-slate-300",
        "<Border Tag=${DynamicResource --color-slate-300} />")]
    [InlineData(
        "<Border Tag=${StaticResource \"Key |With\"} />",
        "Key With Space",
        "<Border Tag=${StaticResource \"Key With Space\"} />")]
    [InlineData(
        "<Border Tag=${DynamicResource Key|} />",
        "Key With Space",
        "<Border Tag=${DynamicResource \"Key With Space\"} />")]
    public void ResourceExtensionsRoundTripDomainKeys(string marked, string key, string expected)
    {
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<x:String x:Key=\"Icon.Home\">A</x:String>" +
                "<x:String x:Key=\"--color-slate-300\">B</x:String>" +
                "<x:String x:Key=\"Key With Space\">C</x:String>")));
        var position = marked.IndexOf('|');
        var source = marked.Remove(position, 1);
        var text = SourceText.From(source);
        var uri = new Uri("C:/Project/RoundTrip.akbura");
        var context = workspace.OpenOrChangeDocumentContext(
            workspace.DefaultProjectId,
            uri,
            text);
        var document = AkburaSyntacticDocument.Parse(
            text,
            uri.LocalPath);
        var result = workspace.LanguageServices.Completion.GetCompletions(
            document,
            context,
            position);
        var item = Assert.Single(
            result.Items,
            candidate => candidate.DisplayText == key);
        var change = workspace.LanguageServices.Completion
            .GetCompletionChange(
                document,
                context,
                position,
                item);

        Assert.Equal(
            expected,
            text.WithChanges(change.Changes).ToString());
    }

    [Fact]
    public void CompatibleResourceTypeRanksAheadOfLexicalOrder()
    {
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<x:String x:Key=\"AString\">A</x:String>" +
                "<SolidColorBrush x:Key=\"ZBrush\" />")));
        const string source =
            "using Avalonia.Controls; " +
            "<Border Background=${StaticResource } />";
        var position = source.IndexOf(
            "}",
            StringComparison.Ordinal);
        var text = SourceText.From(source);
        var uri = new Uri("C:/Project/Ranking.akbura");
        var context = workspace.OpenOrChangeDocumentContext(
            workspace.DefaultProjectId,
            uri,
            text);
        var document = AkburaSyntacticDocument.Parse(
            text,
            uri.LocalPath);
        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        var attribute = Assert.Single(
            context.Document.SyntaxTree
                .GetRootSyntax()
                .DescendantNodes()
                .OfType<Akbura.Language.Syntax.MarkupAttributeSyntax>());
        var property = Assert.IsAssignableFrom<
            Akbura.Language.Symbols.IPropertySymbol>(
                semanticModel.GetSymbolInfo(attribute).Symbol);
        Assert.Equal(
            "Avalonia.Media.SolidColorBrush?",
            property.Type.Symbol?.ToDisplayString());
        var indexed = new ResourceCompletionIndex().GetCandidates(
            context.Project,
            CancellationToken.None);
        var brushType = Assert.Single(
            Assert.Single(
                indexed,
                static candidate => candidate.Key == "ZBrush")
                .Origins).ResourceType;
        var stringType = Assert.Single(
            Assert.Single(
                indexed,
                static candidate => candidate.Key == "AString")
                .Origins).ResourceType;
        Assert.NotNull(brushType);
        Assert.NotNull(stringType);

        var result = workspace.LanguageServices.Completion.GetCompletions(
            document,
            context,
            position);

        var brush = Assert.Single(
            result.Items,
            static item => item.DisplayText == "ZBrush");
        var textResource = Assert.Single(
            result.Items,
            static item => item.DisplayText == "AString");
        Assert.Equal("ZBrush", result.Items[0].DisplayText);
        Assert.True(brush.Priority < textResource.Priority);
        Assert.True(StringComparer.Ordinal.Compare(
            brush.SortText,
            textResource.SortText) < 0);
    }

    [Fact]
    public void ScopeRankingPrefersLocalThenImportBeforeOuterAndApplication()
    {
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<SolidColorBrush x:Key=\"AApplication\" />")),
            ("Resources/Scoped.axaml", Dictionary(
                "<SolidColorBrush x:Key=\"AImported\" />" +
                "<SolidColorBrush x:Key=\"SharedScope\" />")));
        const string marked = """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Styling;
            using Avalonia.Media;

            <Border>
                <Border.Resources>
                    <SolidColorBrush x.key="ZOuter" />
                </Border.Resources>
                <Border>
                    <Border.Resources>
                        <SolidColorBrush x.key="ZLocal" />
                        <SolidColorBrush x.key="SharedScope" />
                        <ResourceInclude Source="Resources/Scoped.axaml" />
                    </Border.Resources>
                    <Border Tag=${StaticResource |} />
                </Border>
            </Border>
            """;
        var position = marked.IndexOf('|');
        var text = SourceText.From(marked.Remove(position, 1));
        var uri = new Uri("C:/Project/ScopeRanking.akbura");
        var context = workspace.OpenOrChangeDocumentContext(
            workspace.DefaultProjectId,
            uri,
            text);
        var index = new ResourceCompletionIndex();

        var candidates = index.GetCandidates(
            context,
            position,
            CancellationToken.None);

        var local = Assert.Single(
            candidates,
            static candidate => candidate.Key == "ZLocal");
        var imported = Assert.Single(
            candidates,
            static candidate => candidate.Key == "AImported");
        var outer = Assert.Single(
            candidates,
            static candidate => candidate.Key == "ZOuter");
        var application = Assert.Single(
            candidates,
            static candidate => candidate.Key == "AApplication");
        var builtIn = Assert.Single(
            candidates,
            static candidate => candidate.Key == "SystemAccentColor");
        Assert.True(local.Priority < imported.Priority);
        Assert.True(imported.Priority < outer.Priority);
        Assert.True(outer.Priority < application.Priority);
        Assert.True(application.Priority < builtIn.Priority);

        var ambiguous = Assert.Single(
            candidates,
            static candidate => candidate.Key == "SharedScope");
        Assert.Equal(2, ambiguous.Origins.Length);
        Assert.Contains(
            ambiguous.Origins,
            static origin => origin.Description.Contains(
                "ScopeRanking.akbura",
                StringComparison.Ordinal));
        Assert.Contains(
            ambiguous.Origins,
            static origin => origin.Description.Contains(
                "Resources/Scoped.axaml",
                StringComparison.Ordinal));

        var document = AkburaSyntacticDocument.Parse(
            text,
            uri.LocalPath);
        var result = workspace.LanguageServices.Completion.GetCompletions(
            document,
            context,
            position);
        var displayed = result.Items
            .Select(static item => item.DisplayText)
            .ToArray();
        Assert.True(
            Array.IndexOf(displayed, "ZLocal") <
            Array.IndexOf(displayed, "AImported"));
        Assert.True(
            Array.IndexOf(displayed, "AImported") <
            Array.IndexOf(displayed, "ZOuter"));
        Assert.True(
            Array.IndexOf(displayed, "ZOuter") <
            Array.IndexOf(displayed, "AApplication"));
        Assert.True(
            Array.IndexOf(displayed, "AApplication") <
            Array.IndexOf(displayed, "SystemAccentColor"));
        var sortTexts = result.Items.ToDictionary(
            static item => item.DisplayText,
            static item => item.SortText,
            StringComparer.Ordinal);
        Assert.True(StringComparer.Ordinal.Compare(
            sortTexts["ZLocal"],
            sortTexts["AImported"]) < 0);
        Assert.True(StringComparer.Ordinal.Compare(
            sortTexts["AImported"],
            sortTexts["ZOuter"]) < 0);
        Assert.True(StringComparer.Ordinal.Compare(
            sortTexts["ZOuter"],
            sortTexts["AApplication"]) < 0);
        Assert.True(StringComparer.Ordinal.Compare(
            sortTexts["AApplication"],
            sortTexts["SystemAccentColor"]) < 0);
    }

    [Fact]
    public void OrdinaryDictionaryPropertyNamedResourcesDoesNotCreateResourceScope()
    {
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);

        var result = GetCompletion(
            workspace,
            """
            using Avalonia.Controls;
            using Avalonia.Media;

            <OrdinaryDictionaryOwner>
                <OrdinaryDictionaryOwner.Resources>
                    <SolidColorBrush x.key="OrdinaryDictionaryKey" />
                </OrdinaryDictionaryOwner.Resources>
                <Border Tag=${StaticResource |} />
            </OrdinaryDictionaryOwner>
            """);

        Assert.DoesNotContain(
            result.Items,
            static item =>
                item.DisplayText == "OrdinaryDictionaryKey");
    }

    [Fact]
    public void AkburaThemeDictionariesIndexEntriesButNotVariantKeys()
    {
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);

        const string marked = """
            using Avalonia.Controls;
            using Avalonia.Media;

            <Border>
                <Border.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary.ThemeDictionaries>
                            <ResourceDictionary x.key="Light">
                                <SolidColorBrush x.key="ThemeAccent" />
                            </ResourceDictionary>
                            <ResourceDictionary x.key="Dark">
                                <SolidColorBrush x.key="ThemeAccent" />
                                <SolidColorBrush x.key="DarkOnly" />
                            </ResourceDictionary>
                        </ResourceDictionary.ThemeDictionaries>
                    </ResourceDictionary>
                </Border.Resources>
                <Border Tag=${StaticResource |} />
            </Border>
            """;
        var position = marked.IndexOf('|');
        var text = SourceText.From(marked.Remove(position, 1));
        var uri = new Uri("C:/Project/ThemeResources.akbura");
        var context = workspace.OpenOrChangeDocumentContext(
            workspace.DefaultProjectId,
            uri,
            text);
        var document = AkburaSyntacticDocument.Parse(
            text,
            uri.LocalPath);
        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        var properties = context.Document.SyntaxTree.GetRootSyntax()
            .DescendantNodes()
            .OfType<Akbura.Language.Syntax.MarkupElementSyntax>()
            .Where(static element =>
                element.StartTag?.Name.ToFullString().Contains('.') == true)
            .ToArray();
        var resourcesProperty = Assert.IsAssignableFrom<
            Akbura.Language.Symbols.IPropertySymbol>(
                semanticModel.GetSymbolInfo(
                    Assert.Single(
                        properties,
                        property => property.StartTag!.Name
                            .ToFullString()
                            .Contains(".Resources"))).Symbol);
        Assert.True(
            semanticModel
                .CreateMarkupPropertyElementContentModel(
                    resourcesProperty)
                .IsDictionary);
        Assert.Equal(
            "Avalonia.Controls.IResourceDictionary",
            resourcesProperty.Type.Symbol?.ToDisplayString());
        Assert.IsAssignableFrom<Akbura.Language.Symbols.IPropertySymbol>(
            semanticModel.GetSymbolInfo(
                Assert.Single(
                    properties,
                    property => property.StartTag!.Name
                        .ToFullString()
                        .Contains(".ThemeDictionaries"))).Symbol);
        var indexed = new ResourceCompletionIndex().GetCandidates(
            context,
            position,
            CancellationToken.None);
        Assert.Contains(
            indexed,
            static candidate => candidate.Key == "ThemeAccent");
        var result = workspace.LanguageServices.Completion.GetCompletions(
            document,
            context,
            position);

        var themeAccent = Assert.Single(
            result.Items,
            static item => item.DisplayText == "ThemeAccent");
        Assert.Contains("Light theme", themeAccent.Description);
        Assert.Contains("Dark theme", themeAccent.Description);
        Assert.Contains(
            result.Items,
            static item => item.DisplayText == "DarkOnly");
        Assert.DoesNotContain(
            result.Items,
            static item => item.DisplayText is "Light" or "Dark");
    }

    [Fact]
    public void AkburaDocumentEditsReuseProjectResourceCandidates()
    {
        var compilation = CreateApplicationCompilation();
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<x:String x:Key=\"Shared\">A</x:String>")));
        var index = new ResourceCompletionIndex();
        var originalProject = workspace.CurrentSolution.GetRequiredProject(
            workspace.DefaultProjectId);
        var original = index.GetCandidates(
            originalProject,
            CancellationToken.None);
        var uri = new Uri("C:/Project/MainView.akbura");

        workspace.OpenOrChangeDocumentContext(
            workspace.DefaultProjectId,
            uri,
            SourceText.From("<Border />"));
        var afterOpen = index.GetCandidates(
            workspace.CurrentSolution.GetRequiredProject(
                workspace.DefaultProjectId),
            CancellationToken.None);

        workspace.OpenOrChangeDocumentContext(
            workspace.DefaultProjectId,
            uri,
            SourceText.From("<Grid />"));
        var afterEdit = index.GetCandidates(
            workspace.CurrentSolution.GetRequiredProject(
                workspace.DefaultProjectId),
            CancellationToken.None);

        Assert.True(original == afterOpen);
        Assert.True(afterOpen == afterEdit);

        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<x:String x:Key=\"Changed\">B</x:String>")));
        var afterResourceEdit = index.GetCandidates(
            workspace.CurrentSolution.GetRequiredProject(
                workspace.DefaultProjectId),
            CancellationToken.None);

        Assert.False(afterEdit == afterResourceEdit);
        Assert.Contains(
            afterResourceEdit,
            static candidate => candidate.Key == "Changed");
    }

    [Fact]
    public void AkburaStylesImportSupportsLocalOverrideAndRemoval()
    {
        var avalonia = CreateAvaloniaReference();
        var akbura = EmitReference(
            "Akbura",
            """
            using System;

            [assembly:
                Akbura.CompilerAnotations.ExportResourceForAkburaCompletion(
                    "Styles.axaml",
                    "--color-slate-300",
                    typeof(global::Avalonia.Media.SolidColorBrush))]

            namespace Akbura.CompilerAnotations;

            [AttributeUsage(
                AttributeTargets.Assembly,
                AllowMultiple = true,
                Inherited = false)]
            public sealed class ExportResourceForAkburaCompletionAttribute
                : Attribute
            {
                public ExportResourceForAkburaCompletionAttribute(
                    string dictionaryPath,
                    string key,
                    Type resourceType)
                {
                }
            }
            """,
            avalonia);
        var compilation = CSharpCompilation.Create(
            "MyApp",
            [CSharpSyntaxTree.ParseText(string.Empty)],
            [GetPlatformReference(), avalonia, akbura],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        using var workspace = CreateWorkspace(compilation);
        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(
                "<ResourceInclude " +
                "Source=\"avares://Akbura/Styles.axaml\" />")));

        var imported = Assert.Single(
            GetCompletion(
                workspace,
                "using Avalonia.Controls; " +
                "<Border Background=${StaticResource " +
                "--color-slate-|} />").Items,
            static item => item.DisplayText == "--color-slate-300");
        Assert.Equal("SolidColorBrush", imported.Suffix);
        Assert.Contains("Exported by Akbura", imported.Description);

        var overridden = Assert.Single(
            GetCompletion(
                workspace,
                """
                using Avalonia.Controls;
                using Avalonia.Media;

                <Border>
                    <Border.Resources>
                        <SolidColorBrush x.key="--color-slate-300" />
                    </Border.Resources>
                    <Border Background=${StaticResource --color-slate-|} />
                </Border>
                """).Items,
            static item => item.DisplayText == "--color-slate-300");
        Assert.Contains("Local resource", overridden.Description);
        Assert.Contains("Exported by Akbura", overridden.Description);

        var afterOverrideRemoval = Assert.Single(
            GetCompletion(
                workspace,
                "using Avalonia.Controls; " +
                "<Border Background=${StaticResource " +
                "--color-slate-|} />").Items,
            static item => item.DisplayText == "--color-slate-300");
        Assert.DoesNotContain(
            "Local resource",
            afterOverrideRemoval.Description);
        Assert.Contains(
            "Exported by Akbura",
            afterOverrideRemoval.Description);

        SynchronizeResources(
            workspace,
            compilation,
            ("App.axaml", Application(string.Empty)));
        Assert.DoesNotContain(
            GetCompletion(
                workspace,
                "using Avalonia.Controls; " +
                "<Border Background=${StaticResource " +
                "--color-slate-|} />").Items,
            static item => item.DisplayText == "--color-slate-300");
    }

    private static AkburaCompletionResult GetCompletion(AkburaWorkspace workspace, string marked)
    {
        var position = marked.IndexOf('|');
        var source = marked.Remove(position, 1);
        var text = SourceText.From(source);
        var uri = new Uri("C:/Project/Completion.akbura");
        var context = workspace.OpenOrChangeDocumentContext(
            workspace.DefaultProjectId,
            uri,
            text);
        var document = AkburaSyntacticDocument.Parse(
            text,
            uri.LocalPath);
        return workspace.LanguageServices.Completion.GetCompletions(
            document,
            context,
            position);
    }

    private static ImmutableArray<ResourceCompletionCandidate> GetCandidates(AkburaWorkspace workspace)
    {
        return GetCandidates(workspace, new ResourceCompletionIndex());
    }

    private static ImmutableArray<ResourceCompletionCandidate> GetCandidates(AkburaWorkspace workspace, ResourceCompletionIndex index)
    {
        var project = workspace.CurrentSolution.GetRequiredProject(
            workspace.DefaultProjectId);
        return index.GetCandidates(
            project,
            CancellationToken.None);
    }

    private static void SynchronizeResources(AkburaWorkspace workspace, CSharpCompilation compilation, params (string Path, string Text)[] resources)
    {
        var identity = ResourceAssemblyIdentity.Create(
            compilation.Assembly.Identity);
        var inputs = resources.Select(resource =>
        {
            var physicalPath = "C:/Project/" + resource.Path;
            return new ResourceDocumentInput(
                new Uri(physicalPath),
                physicalPath,
                resource.Path,
                identity,
                SourceText.From(resource.Text),
                VersionStamp.Create(),
                "project",
                "net10.0");
        }).ToImmutableArray();

        workspace.SynchronizeProjectResourceDocuments(
            workspace.DefaultProjectId,
            inputs);
    }

    private static AkburaWorkspace CreateWorkspace(CSharpCompilation compilation)
    {
        var context = new ProjectContext(
            ProjectId.CreateNewId(),
            "C:/Project/MyApp.csproj",
            "C:/Project",
            "MyApp",
            compilation,
            ImmutableArray<ProjectReference>.Empty);
        return new AkburaWorkspace(context);
    }

    private static CSharpCompilation CreateApplicationCompilation(params MetadataReference[] references)
    {
        const string source = """
            namespace Avalonia
            {
                public class Application { }
            }

            namespace Avalonia.Media
            {
                public struct Color { }
                public class SolidColorBrush { }
            }

            namespace Avalonia.Controls
            {
                public interface IResourceDictionary
                    : System.Collections.Generic.IDictionary<object, object?>
                {
                }

                public sealed class ResourceDictionary
                    : System.Collections.Generic.Dictionary<object, object?>,
                      IResourceDictionary
                {
                    public System.Collections.Generic.IDictionary<
                        object,
                        IResourceDictionary> ThemeDictionaries
                    {
                        get;
                    } = new System.Collections.Generic.Dictionary<
                        object,
                        IResourceDictionary>();
                }

                public class Border
                {
                    public IResourceDictionary Resources
                    {
                        get;
                    } = new ResourceDictionary();

                    public Avalonia.Media.SolidColorBrush? Background
                    {
                        get;
                        set;
                    }

                    public object? Tag { get; set; }
                }

                public sealed class OrdinaryDictionaryOwner
                {
                    public System.Collections.Generic.IDictionary<
                        object,
                        object?> Resources
                    {
                        get;
                    } = new System.Collections.Generic.Dictionary<
                        object,
                        object?>();
                }
            }

            namespace Avalonia.Markup.Xaml.Styling
            {
                public sealed class ResourceInclude
                {
                    public string? Source { get; set; }
                }
            }

            namespace Gallery
            {
                public sealed class StaticResourceExtension
                {
                    public StaticResourceExtension(object key)
                    {
                    }

                    public object ProvideValue() => new();
                }
            }
            """;
        return CSharpCompilation.Create(
            "MyApp",
            [CSharpSyntaxTree.ParseText(source)],
            [
                GetPlatformReference(),
                ResourceMarkupExtensionsReference,
                .. references,
            ],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static PortableExecutableReference CreateExportContractReference()
    {
        return EmitReference(
            "Akbura",
            """
            namespace Akbura.CompilerAnotations;

            [System.AttributeUsage(
                System.AttributeTargets.Assembly,
                AllowMultiple = true,
                Inherited = false)]
            public sealed class ExportResourceForAkburaCompletionAttribute
                : System.Attribute
            {
                public ExportResourceForAkburaCompletionAttribute(
                    string dictionaryPath,
                    string key,
                    System.Type resourceType)
                {
                }
            }
            """);
    }

    private static PortableExecutableReference CreateAvaloniaReference()
    {
        return EmitReference(
            "Avalonia.Markup.Xaml",
            """
            namespace Avalonia
            {
                public class Application { }
            }

            namespace Avalonia.Media
            {
                public sealed class SolidColorBrush { }
            }

            namespace Avalonia.Controls
            {
                public interface IResourceDictionary
                    : System.Collections.Generic.IDictionary<object, object?>
                {
                }

                public sealed class ResourceDictionary
                    : System.Collections.Generic.Dictionary<object, object?>,
                      IResourceDictionary
                {
                }

                public class Border
                {
                    public IResourceDictionary Resources
                    {
                        get;
                    } = new ResourceDictionary();

                    public Avalonia.Media.SolidColorBrush? Background
                    {
                        get;
                        set;
                    }
                }
            }

            namespace Avalonia.Markup.Xaml.MarkupExtensions
            {
                public sealed class StaticResourceExtension
                {
                    public StaticResourceExtension(object key)
                    {
                    }

                    public object ProvideValue() => new();
                }

                public sealed class DynamicResourceExtension
                {
                    public DynamicResourceExtension(object key)
                    {
                    }

                    public object ProvideValue() => new();
                }
            }
            """);
    }

    private static PortableExecutableReference EmitReference(string assemblyName, string source, params MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [GetPlatformReference(), .. references],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(
            stream,
            options: new EmitOptions(
                metadataOnly: true,
                includePrivateMembers: false));
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference GetPlatformReference()
    {
        return MetadataReference.CreateFromFile(
            typeof(object).Assembly.Location);
    }

    private static string Application(string resources)
    {
        return "<Application xmlns=\"" + AvaloniaNamespace +
            "\" xmlns:x=\"" + XamlNamespace + "\">" +
            "<Application.Resources><ResourceDictionary>" +
            resources +
            "</ResourceDictionary></Application.Resources>" +
            "</Application>";
    }

    private static string Dictionary(string resources)
    {
        return "<ResourceDictionary xmlns=\"" + AvaloniaNamespace +
            "\" xmlns:x=\"" + XamlNamespace + "\">" +
            resources +
            "</ResourceDictionary>";
    }
}
