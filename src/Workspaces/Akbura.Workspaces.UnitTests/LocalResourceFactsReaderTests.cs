using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class LocalResourceFactsReaderTests
{
    [Fact]
    public void MalformedApplicationStillProducesCompleteResourceFacts()
    {
        var snapshot = CreateSnapshot(
            "App.axaml",
            """
            <Application
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Application.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary.MergedDictionaries>
                            <ResourceInclude Source="Themes/Colors.axaml" />
                        </ResourceDictionary.MergedDictionaries>
                        <SolidColorBrush x:Key="CardBackground">#112233</SolidColorBrush>
            """);

        var applicationRoot = Assert.IsType<
            LocalApplicationResourceRootFact>(
                snapshot.Facts.ApplicationRoot);
        var scope = Assert.Single(snapshot.Facts.Scopes);
        var declaration = Assert.Single(snapshot.Facts.Declarations);
        var import = Assert.Single(snapshot.Facts.Imports);

        Assert.Equal(LocalResourceScopeKind.Application, scope.Kind);
        Assert.Equal(scope.Id, applicationRoot.ScopeId);
        Assert.Equal("CardBackground", declaration.Key);
        Assert.Equal("SolidColorBrush", declaration.TypeName);
        Assert.Equal("https://github.com/avaloniaui", declaration.TypeNamespace);
        Assert.Equal(scope.Id, declaration.ScopeId);
        Assert.Equal("Themes/Colors.axaml", import.Source);
        Assert.Equal(scope.Id, import.ScopeId);
    }

    [Fact]
    public void XamlNamespaceAliasDefinesResourceKey()
    {
        var snapshot = CreateSnapshot(
            "Resources/Colors.axaml",
            """
            <ResourceDictionary
                xmlns="https://github.com/avaloniaui"
                xmlns:meta="http://schemas.microsoft.com/winfx/2006/xaml">
                <Color meta:Key="Accent">#FF00FF</Color>
                <local:Unknown
                    xmlns:local="using:Example.Controls"
                    meta:Key="UnknownResource" />
            </ResourceDictionary>
            """);

        Assert.Collection(
            snapshot.Facts.Declarations,
            resource =>
            {
                Assert.Equal("Accent", resource.Key);
                Assert.Equal("Color", resource.TypeName);
                Assert.Equal(
                    "https://github.com/avaloniaui",
                    resource.TypeNamespace);
            },
            resource =>
            {
                Assert.Equal("UnknownResource", resource.Key);
                Assert.Equal("local:Unknown", resource.TypeName);
                Assert.Equal(
                    "using:Example.Controls",
                    resource.TypeNamespace);
            });
    }

    [Fact]
    public void KeyOutsideResourceScopeIsIgnored()
    {
        var snapshot = CreateSnapshot(
            "Views/MainView.axaml",
            """
            <Window
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Window.Resources>
                    <Color x:Key="VisibleResource">#010203</Color>
                    <Dictionary>
                        <Color x:Key="NotAResource">#040506</Color>
                    </Dictionary>
                </Window.Resources>
                <Grid>
                    <Dictionary>
                        <Color x:Key="AlsoNotAResource">#070809</Color>
                    </Dictionary>
                </Grid>
            </Window>
            """);

        var declaration = Assert.Single(snapshot.Facts.Declarations);
        Assert.Equal("VisibleResource", declaration.Key);
    }

    [Fact]
    public void NestedDictionaryValueDoesNotFlattenItsChildren()
    {
        var snapshot = CreateSnapshot(
            "Resources.axaml",
            """
            <ResourceDictionary
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary x:Key="NestedDictionary">
                    <Color x:Key="NestedColor">#010203</Color>
                </ResourceDictionary>
                <Color x:Key="TopLevelColor">#040506</Color>
            </ResourceDictionary>
            """);

        Assert.Equal(
            new[]
            {
                "NestedDictionary",
                "TopLevelColor",
            },
            snapshot.Facts.Declarations
                .Select(static resource => resource.Key));
    }

    [Fact]
    public void ThemeDictionaryNamesAreNotOrdinaryResourceKeys()
    {
        var snapshot = CreateSnapshot(
            "Themes/Theme.axaml",
            """
            <ResourceDictionary
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Light">
                        <Color x:Key="ThemeAccent">#FFFFFF</Color>
                    </ResourceDictionary>
                    <ResourceDictionary x:Key="Dark">
                        <Color x:Key="ThemeAccent">#000000</Color>
                    </ResourceDictionary>
                </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);

        Assert.Collection(
            snapshot.Facts.Declarations,
            resource =>
            {
                Assert.Equal("ThemeAccent", resource.Key);
                Assert.Equal("Light", resource.ThemeVariant);
            },
            resource =>
            {
                Assert.Equal("ThemeAccent", resource.Key);
                Assert.Equal("Dark", resource.ThemeVariant);
            });
        Assert.DoesNotContain(
            snapshot.Facts.Declarations,
            static resource => resource.Key is "Light" or "Dark");
    }

    [Fact]
    public void IncompleteComputedAndCommentedImportsAreIgnored()
    {
        var computed = CreateSnapshot(
            "App.axaml",
            """
            <Application
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Application.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary.MergedDictionaries>
                            <!-- <ResourceInclude Source="Commented.axaml" /> -->
                            <ResourceInclude Source="{Binding DictionaryPath}" />
                            <TextBlock Text="ResourceInclude Source='Text.axaml'" />
                        </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                </Application.Resources>
            </Application>
            """);
        var incomplete = CreateSnapshot(
            "Broken.axaml",
            """
            <ResourceDictionary
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.MergedDictionaries>
                    <ResourceInclude Source="Themes/Unfinished.axaml
            """);

        Assert.Empty(computed.Facts.Imports);
        Assert.Empty(incomplete.Facts.Imports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncrementalEditRemovesBrokenOrDeletedImport(bool deleteImport)
    {
        const string import =
            "<ResourceInclude Source=\"Themes/Colors.axaml\" />";
        const string source = """
            <ResourceDictionary
                xmlns="https://github.com/avaloniaui">
                <ResourceDictionary.MergedDictionaries>
                    <ResourceInclude Source="Themes/Colors.axaml" />
                </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """;
        var original = CreateSnapshot(
            "Themes/Main.axaml",
            source);
        var importStart = source.IndexOf(
            import,
            StringComparison.Ordinal);
        var change = deleteImport
            ? new TextChange(
                new TextSpan(importStart, import.Length),
                string.Empty)
            : new TextChange(
                new TextSpan(
                    source.IndexOf(
                        "\" />",
                        importStart,
                        StringComparison.Ordinal),
                    1),
                string.Empty);
        var changedText = original.Text.WithChanges(change);

        var changed = original.WithText(
            changedText,
            VersionStamp.Create(),
            changedText.GetChangeRanges(original.Text),
            CancellationToken.None);

        Assert.Equal(
            "Themes/Colors.axaml",
            Assert.Single(original.Facts.Imports).Source);
        Assert.Empty(changed.Facts.Imports);
    }

    [Fact]
    public void CustomPropertyNamespaceOrOwnerDoesNotActivateTraversal()
    {
        var snapshot = CreateSnapshot(
            "Resources.axaml",
            """
            <ResourceDictionary
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="using:Example.Controls">
                <local:ResourceDictionary.MergedDictionaries>
                    <ResourceInclude Source="Foreign.axaml" />
                </local:ResourceDictionary.MergedDictionaries>
                <local:ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Dark">
                        <Color x:Key="ForeignThemeColor">#010203</Color>
                    </ResourceDictionary>
                </local:ResourceDictionary.ThemeDictionaries>
                <local:ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceInclude Source="ForeignOwner.axaml" />
                    </ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary.ThemeDictionaries>
                        <ResourceDictionary x:Key="Light">
                            <Color x:Key="ForeignOwnerColor">#070809</Color>
                        </ResourceDictionary>
                    </ResourceDictionary.ThemeDictionaries>
                </local:ResourceDictionary>
                <Color x:Key="VisibleColor">#040506</Color>
            </ResourceDictionary>
            """);

        Assert.Empty(snapshot.Facts.Imports);
        Assert.Equal(
            "VisibleColor",
            Assert.Single(snapshot.Facts.Declarations).Key);
    }

    [Fact]
    public void AvaloniaIncludeKindsProduceLiteralImports()
    {
        var snapshot = CreateSnapshot(
            "App.axaml",
            """
            <Application
                xmlns="https://github.com/avaloniaui"
                xmlns:local="using:Example.Controls">
                <Application.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary.MergedDictionaries>
                            <ResourceInclude Source="Themes/Colors.axaml" />
                            <MergeResourceInclude Source="Themes/Controls.axaml" />
                            <local:ResourceInclude Source="Themes/Foreign.axaml" />
                        </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                </Application.Resources>
                <Application.Styles>
                    <StyleInclude Source="Themes/Fluent.axaml" />
                    <local:StyleInclude Source="Themes/ForeignStyle.axaml" />
                </Application.Styles>
            </Application>
            """);

        Assert.Equal(
            new[]
            {
                "Themes/Colors.axaml",
                "Themes/Controls.axaml",
                "Themes/Fluent.axaml",
            },
            snapshot.Facts.Imports
                .Select(static import => import.Source));
    }

    [Fact]
    public void RootStylesExposeStyleIncludesFromTheirRootScope()
    {
        var snapshot = CreateSnapshot(
            "Themes/Theme.axaml",
            """
            <Styles xmlns="https://github.com/avaloniaui">
                <StyleInclude Source="Controls.axaml" />
            </Styles>
            """);

        var scope = Assert.Single(snapshot.Facts.Scopes);
        var import = Assert.Single(snapshot.Facts.Imports);
        Assert.Equal(LocalResourceScopeKind.Style, scope.Kind);
        Assert.True(scope.IsDocumentRoot);
        Assert.Equal(scope.Id, import.ScopeId);
        Assert.True(
            snapshot.Facts.TryGetRootScopeId(out var rootScopeId));
        Assert.Equal(scope.Id, rootScopeId);
    }

    [Fact]
    public void CustomResourcesPropertyMustBelongToItsOwner()
    {
        var snapshot = CreateSnapshot(
            "Views/Widget.axaml",
            """
            <local:Widget
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="using:Example.Controls">
                <local:Foo.Resources>
                    <Color x:Key="WrongOwner">#010203</Color>
                </local:Foo.Resources>
                <local:Widget.Resources>
                    <Color x:Key="RightOwner">#040506</Color>
                </local:Widget.Resources>
            </local:Widget>
            """);

        var declaration = Assert.Single(
            snapshot.Facts.Declarations);
        Assert.Equal("RightOwner", declaration.Key);
        Assert.Single(snapshot.Facts.Scopes);
    }

    [Fact]
    public void NestedControlResourcesCreateChildScope()
    {
        var snapshot = CreateSnapshot(
            "Views/MainView.axaml",
            """
            <Window
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Window.Resources>
                    <Color x:Key="WindowColor">#010203</Color>
                </Window.Resources>
                <Grid>
                    <Border>
                        <Border.Resources>
                            <Color x:Key="BorderColor">#040506</Color>
                        </Border.Resources>
                        <TextBlock />
                    </Border>
                    <Border>
                        <Color x:Key="NotInResources">#070809</Color>
                    </Border>
                </Grid>
            </Window>
            """);

        Assert.Collection(
            snapshot.Facts.Scopes,
            windowScope =>
            {
                Assert.Equal(LocalResourceScopeKind.Element, windowScope.Kind);
                Assert.Null(windowScope.ParentScopeId);
                Assert.True(windowScope.IsDocumentRoot);
            },
            borderScope =>
            {
                Assert.Equal(LocalResourceScopeKind.Element, borderScope.Kind);
                Assert.Equal(0, borderScope.ParentScopeId);
                Assert.False(borderScope.IsDocumentRoot);
            });
        Assert.Equal(
            new[]
            {
                ("WindowColor", 0),
                ("BorderColor", 1),
            },
            snapshot.Facts.Declarations
                .Select(static resource =>
                    (resource.Key, resource.ScopeId)));
    }

    [Fact]
    public void SnapshotUpdateKeepsOldFactsAndRefreshesPathFacts()
    {
        const string oldSource = """
            <ResourceDictionary
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Color x:Key="OldKey">#010203</Color>
            </ResourceDictionary>
            """;
        var oldSnapshot = CreateSnapshot(
            "Themes\\Colors.axaml",
            oldSource);
        var oldText = oldSnapshot.Text;
        var start = oldSource.IndexOf("OldKey", StringComparison.Ordinal);
        var newText = oldText.WithChanges(
            new TextChange(
                new TextSpan(start, "OldKey".Length),
                "NewKey"));
        var changes = newText.GetChangeRanges(oldText);

        var newSnapshot = oldSnapshot.WithText(
            newText,
            VersionStamp.Create(),
            changes,
            CancellationToken.None);

        Assert.Equal(
            "OldKey",
            Assert.Single(oldSnapshot.Facts.Declarations).Key);
        Assert.Equal(
            "NewKey",
            Assert.Single(newSnapshot.Facts.Declarations).Key);
        Assert.Equal(
            "Themes/Colors.axaml",
            newSnapshot.Facts.Path.LogicalPath.Value);
        Assert.Equal(
            "Themes",
            newSnapshot.Facts.Path.LogicalDirectory);
        Assert.Equal(
            "Test.Resources",
            newSnapshot.Facts.Path.AssemblyIdentity.Name);
        Assert.Equal("project-a", newSnapshot.Facts.Path.ProjectKey);
        Assert.Equal("net10.0", newSnapshot.Facts.Path.TargetFramework);
        Assert.True(newSnapshot.Facts.TryGetRootScopeId(out var rootScopeId));
        Assert.True(newSnapshot.Facts.IsScopeVisibleFromRoot(rootScopeId));
    }

    private static ResourceDocumentSnapshot CreateSnapshot(string logicalPath, string source)
    {
        var physicalPath = Path.Combine(
            Path.GetTempPath(),
            "Akbura.Workspaces.Tests",
            logicalPath.Replace('/', Path.DirectorySeparatorChar));
        var input = new ResourceDocumentInput(
            new Uri(physicalPath),
            physicalPath,
            logicalPath,
            ResourceAssemblyIdentity.Create(
                new AssemblyIdentity("Test.Resources")),
            SourceText.From(source),
            VersionStamp.Create(),
            projectKey: "project-a",
            targetFramework: "net10.0");

        return ResourceDocumentSnapshot.Create(
            input,
            CancellationToken.None);
    }
}
