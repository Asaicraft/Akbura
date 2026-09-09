using Akbura.HotReload;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComponentStructuralHotReloadIntegrationTests
{
    private const string HostSource =
        """
        using Akbura;
        using Akbura.Engine;
        using Avalonia.Controls;
        using Avalonia.Interactivity;
        using Avalonia.Metadata;
        using System.Collections.Generic;

        namespace Demo;

        public partial class PlannerView : AkburaControl
        {
            public PlannerView()
                : base(AkburaEngine.Empty)
            {
            }

            public int PrimaryClickCount;

            public int SecondaryClickCount;

            public Control InvokeFirstUpdate() => FirstUpdate();

            public Control InvokeUpdate() => Update();

            private void OnPrimaryClick(object? sender, RoutedEventArgs eventArgs)
            {
                PrimaryClickCount++;
            }

            private void OnSecondaryClick(object? sender, RoutedEventArgs eventArgs)
            {
                SecondaryClickCount++;
            }
        }

        public sealed class CollectionContentHost : Control
        {
            [Content]
            public ICollection<Control> Items { get; } = new List<Control>();
        }
        """;

    [Fact]
    public async Task DebugStructuralRevision_InsertsChildAndPreservesExistingInstances()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <StackPanel Tag="Initial">
                <TextBlock Text="Alpha" />
                <Button Content="Omega" />
            </StackPanel>
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <StackPanel Tag="Initial">
                <Border Width="42" />
                <TextBlock Text="Alpha" />
                <Button Content="Omega" />
            </StackPanel>
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<StackPanel>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                Assert.Equal(2, root.Children.Count);

                var textBlock = Assert.IsType<TextBlock>(root.Children[0]);
                var button = Assert.IsType<Button>(root.Children[1]);
                root.Tag = "User root state";
                textBlock.Text = "User";

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                updatedRevision.SetRenderState(updatedOwner, renderState);

                var updatedRoot = Assert.IsType<StackPanel>(
                    updatedRevision.InvokeUpdate(updatedOwner));

                Assert.Same(root, updatedRoot);
                Assert.Equal(3, updatedRoot.Children.Count);

                var border = Assert.IsType<Border>(updatedRoot.Children[0]);
                Assert.Equal(42, border.Width);
                Assert.Same(textBlock, updatedRoot.Children[1]);
                Assert.Same(button, updatedRoot.Children[2]);
                Assert.Equal("User root state", root.Tag);
                Assert.Equal("User", textBlock.Text);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_RemovesScalarChildFromRetainedParent()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <Border Tag="Stable parent">
                <TextBlock Text="Removed child" />
            </Border>
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <Border Tag="Stable parent" />
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<Border>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                var child = Assert.IsType<TextBlock>(root.Child);

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                updatedRevision.SetRenderState(updatedOwner, renderState);

                var updatedRoot = Assert.IsType<Border>(
                    updatedRevision.InvokeUpdate(updatedOwner));

                Assert.Same(root, updatedRoot);
                Assert.Null(updatedRoot.Child);
                Assert.Null(child.Parent);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_ChangesOneConstantWithoutResettingUserValue()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBox Text="Generated" Width="100" />
            </StackPanel>
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBox Text="Generated" Width="200" />
            </StackPanel>
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<StackPanel>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                var textBox = Assert.IsType<TextBox>(Assert.Single(root.Children));
                textBox.Text = "User value";

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                updatedRevision.SetRenderState(updatedOwner, renderState);

                var updatedRoot = Assert.IsType<StackPanel>(
                    updatedRevision.InvokeUpdate(updatedOwner));
                var updatedTextBox = Assert.IsType<TextBox>(
                    Assert.Single(updatedRoot.Children));

                Assert.Same(root, updatedRoot);
                Assert.Same(textBox, updatedTextBox);
                Assert.Equal("User value", updatedTextBox.Text);
                Assert.Equal(200, updatedTextBox.Width);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_RemovesConstantAndRestoresOriginalBaseline()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBox Tag="Generated value" />
            </StackPanel>
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBox />
            </StackPanel>
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                Assert.Null(new TextBox().Tag);

                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<StackPanel>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                var textBox = Assert.IsType<TextBox>(
                    Assert.Single(root.Children));
                Assert.Equal("Generated value", textBox.Tag);

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                updatedRevision.SetRenderState(updatedOwner, renderState);

                var updatedRoot = Assert.IsType<StackPanel>(
                    updatedRevision.InvokeUpdate(updatedOwner));
                var updatedTextBox = Assert.IsType<TextBox>(
                    Assert.Single(updatedRoot.Children));

                Assert.Same(root, updatedRoot);
                Assert.Same(textBox, updatedTextBox);
                Assert.Null(updatedTextBox.Tag);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_UnchangedConstantContentPreservesUserValue()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <Button Width="100">Generated content</Button>
            </StackPanel>
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <Button Width="200">Generated content</Button>
            </StackPanel>
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<StackPanel>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                var button = Assert.IsType<Button>(
                    Assert.Single(root.Children));
                button.Content = "User content";

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                updatedRevision.SetRenderState(updatedOwner, renderState);

                var updatedRoot = Assert.IsType<StackPanel>(
                    updatedRevision.InvokeUpdate(updatedOwner));
                var updatedButton = Assert.IsType<Button>(
                    Assert.Single(updatedRoot.Children));

                Assert.Same(root, updatedRoot);
                Assert.Same(button, updatedButton);
                Assert.Equal("User content", updatedButton.Content);
                Assert.Equal(200, updatedButton.Width);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_RemovesConstantContentAndRestoresBaseline()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <Button>Generated content</Button>
            </StackPanel>
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <Button />
            </StackPanel>
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                Assert.Null(new Button().Content);

                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<StackPanel>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                var button = Assert.IsType<Button>(
                    Assert.Single(root.Children));
                Assert.Equal("Generated content", button.Content);

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                updatedRevision.SetRenderState(updatedOwner, renderState);

                var updatedRoot = Assert.IsType<StackPanel>(
                    updatedRevision.InvokeUpdate(updatedOwner));
                var updatedButton = Assert.IsType<Button>(
                    Assert.Single(updatedRoot.Children));

                Assert.Same(root, updatedRoot);
                Assert.Same(button, updatedButton);
                Assert.Null(updatedButton.Content);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_RemovesFirstSameTypeSiblingAndPreservesMatchingSecond()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBlock Text="Hello" />
                <TextBlock Text="Hi" />
            </StackPanel>
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBlock Text="Hi" />
            </StackPanel>
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<StackPanel>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                var hello = Assert.IsType<TextBlock>(root.Children[0]);
                var hi = Assert.IsType<TextBlock>(root.Children[1]);

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                updatedRevision.SetRenderState(updatedOwner, renderState);

                var updatedRoot = Assert.IsType<StackPanel>(
                    updatedRevision.InvokeUpdate(updatedOwner));
                var updatedHi = Assert.IsType<TextBlock>(
                    Assert.Single(updatedRoot.Children));

                Assert.Same(root, updatedRoot);
                Assert.Same(hi, updatedHi);
                Assert.NotSame(hello, updatedHi);
                Assert.Null(hello.Parent);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_ReordersNamedChildrenAndPreservesBothInstances()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBlock x.Name="first" Text="First" />
                <TextBlock x.Name="second" Text="Second" />
            </StackPanel>
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBlock x.Name="second" Text="Second" />
                <TextBlock x.Name="first" Text="First" />
            </StackPanel>
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<StackPanel>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                var first = Assert.IsType<TextBlock>(root.Children[0]);
                var second = Assert.IsType<TextBlock>(root.Children[1]);

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                updatedRevision.SetRenderState(updatedOwner, renderState);

                var updatedRoot = Assert.IsType<StackPanel>(
                    updatedRevision.InvokeUpdate(updatedOwner));

                Assert.Same(root, updatedRoot);
                Assert.Equal(2, updatedRoot.Children.Count);
                Assert.Same(second, updatedRoot.Children[0]);
                Assert.Same(first, updatedRoot.Children[1]);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_ReplacesOnlyIncompatibleChild()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBlock Text="Old child" />
            </StackPanel>
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <Button Content="New child" />
            </StackPanel>
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<StackPanel>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                var textBlock = Assert.IsType<TextBlock>(
                    Assert.Single(root.Children));

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                updatedRevision.SetRenderState(updatedOwner, renderState);

                var updatedRoot = Assert.IsType<StackPanel>(
                    updatedRevision.InvokeUpdate(updatedOwner));
                var button = Assert.IsType<Button>(
                    Assert.Single(updatedRoot.Children));

                Assert.Same(root, updatedRoot);
                Assert.NotSame(textBlock, button);
                Assert.Equal("New child", button.Content);
                Assert.Null(textBlock.Parent);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_ChangesAndRemovesEventOnRetainedNode()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <Button Click={OnPrimaryClick} />
            </StackPanel>
            """;
        const string changedComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <Button Click={OnSecondaryClick} />
            </StackPanel>
            """;
        const string removedComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <Button />
            </StackPanel>
            """;
        var originalRevision = CompileRevision(originalComponent);
        var changedRevision = CompileRevision(changedComponent);
        var removedRevision = CompileRevision(removedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<StackPanel>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                var button = Assert.IsType<Button>(
                    Assert.Single(root.Children));

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(
                    1,
                    originalRevision.GetInt32Field(
                        originalOwner,
                        "PrimaryClickCount"));

                var renderState = originalRevision.GetRenderState(originalOwner);
                var changedOwner = changedRevision.CreateOwner();
                changedRevision.SetRenderState(changedOwner, renderState);
                var changedRoot = Assert.IsType<StackPanel>(
                    changedRevision.InvokeUpdate(changedOwner));
                var changedButton = Assert.IsType<Button>(
                    Assert.Single(changedRoot.Children));

                Assert.Same(root, changedRoot);
                Assert.Same(button, changedButton);

                changedButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(
                    1,
                    originalRevision.GetInt32Field(
                        originalOwner,
                        "PrimaryClickCount"));
                Assert.Equal(
                    1,
                    changedRevision.GetInt32Field(
                        changedOwner,
                        "SecondaryClickCount"));

                var removedOwner = removedRevision.CreateOwner();
                removedRevision.SetRenderState(removedOwner, renderState);
                var removedRoot = Assert.IsType<StackPanel>(
                    removedRevision.InvokeUpdate(removedOwner));
                var removedButton = Assert.IsType<Button>(
                    Assert.Single(removedRoot.Children));

                Assert.Same(root, removedRoot);
                Assert.Same(button, removedButton);

                removedButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(
                    1,
                    originalRevision.GetInt32Field(
                        originalOwner,
                        "PrimaryClickCount"));
                Assert.Equal(
                    1,
                    changedRevision.GetInt32Field(
                        changedOwner,
                        "SecondaryClickCount"));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_RemovesBindingFromRetainedNode()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBox x.Name="source" Text="Initial" />
                <TextBlock Text=${Binding #source.Text} />
            </StackPanel>
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBox x.Name="source" Text="Initial" />
                <TextBlock />
            </StackPanel>
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var originalOwner = originalRevision.CreateOwner();
                var root = Assert.IsType<StackPanel>(
                    originalRevision.InvokeFirstUpdate(originalOwner));
                var source = Assert.IsType<TextBox>(root.Children[0]);
                var target = Assert.IsType<TextBlock>(root.Children[1]);

                source.Text = "Bound value";
                Assert.Equal("Bound value", target.Text);

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                updatedRevision.SetRenderState(updatedOwner, renderState);
                var updatedRoot = Assert.IsType<StackPanel>(
                    updatedRevision.InvokeUpdate(updatedOwner));
                var updatedSource = Assert.IsType<TextBox>(
                    updatedRoot.Children[0]);
                var updatedTarget = Assert.IsType<TextBlock>(
                    updatedRoot.Children[1]);

                Assert.Same(root, updatedRoot);
                Assert.Same(source, updatedSource);
                Assert.Same(target, updatedTarget);
                Assert.Null(updatedTarget.Text);

                updatedSource.Text = "Detached value";
                Assert.Null(updatedTarget.Text);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DebugStructuralRevision_RootReplacementTransfersOwnedDataContextBinding()
    {
        const string originalComponent =
            """
            using Avalonia.Controls;

            <Border />
            """;
        const string updatedComponent =
            """
            using Avalonia.Controls;

            <Button />
            """;
        var originalRevision = CompileRevision(originalComponent);
        var updatedRevision = CompileRevision(updatedComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var originalOwner = originalRevision.CreateOwner();
                var originalSource = Assert.IsAssignableFrom<StyledElement>(
                    originalOwner);
                originalSource.DataContext = "Original owner";
                var originalRoot = Assert.IsType<Border>(
                    originalRevision.InvokeFirstUpdate(originalOwner));

                Assert.Equal("Original owner", originalRoot.DataContext);

                var renderState = originalRevision.GetRenderState(originalOwner);
                var updatedOwner = updatedRevision.CreateOwner();
                var updatedSource = Assert.IsAssignableFrom<StyledElement>(
                    updatedOwner);
                updatedSource.DataContext = "Updated owner";
                updatedRevision.SetRenderState(updatedOwner, renderState);
                var updatedRoot = Assert.IsType<Button>(
                    updatedRevision.InvokeUpdate(updatedOwner));

                Assert.Equal("Updated owner", updatedRoot.DataContext);
                Assert.Null(originalRoot.DataContext);

                originalSource.DataContext = "Detached original owner";
                Assert.Null(originalRoot.DataContext);

                updatedSource.DataContext = "Updated owner changed";
                Assert.Equal("Updated owner changed", updatedRoot.DataContext);
            },
            CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DebugStructuralRevision_ValidRootToZeroOrMultipleRootsRequiresRestart(
        bool usesMultipleRoots)
    {
        const string validComponent =
            """
            using Avalonia.Controls;

            <Border />
            """;
        var fallbackComponent = usesMultipleRoots
            ? """
              using Avalonia.Controls;

              <Border />
              <Button />
              """
            : "PrimaryClickCount++;\r\n";
        var validRevision = CompileRevision(validComponent);
        var fallbackRevision = CompileRevision(fallbackComponent);

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var initialFallbackOwner = fallbackRevision.CreateOwner();
                var fallbackRoot = fallbackRevision.InvokeFirstUpdate(
                    initialFallbackOwner);

                Assert.NotNull(fallbackRoot);
                Assert.Same(
                    fallbackRoot,
                    fallbackRevision.InvokeUpdate(initialFallbackOwner));

                var validOwner = validRevision.CreateOwner();
                validRevision.InvokeFirstUpdate(validOwner);
                var changedOwner = fallbackRevision.CreateOwner();
                fallbackRevision.SetRenderState(
                    changedOwner,
                    validRevision.GetRenderState(validOwner));

                var invocationException = Assert.Throws<TargetInvocationException>(
                    () => fallbackRevision.InvokeUpdate(changedOwner));
                var restartException = Assert.IsType<InvalidOperationException>(
                    invocationException.InnerException);

                Assert.Contains(
                    "requires an application restart during Hot Reload",
                    restartException.Message,
                    StringComparison.Ordinal);
            },
            CancellationToken.None);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitialRender_ICollectionContentAddsChildrenWithoutChangingReleaseDirect(
        bool useDebugStructural)
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <CollectionContentHost>
                <TextBlock Text="First" />
                <Button Content="Second" />
            </CollectionContentHost>
            """;
        var generationMode = useDebugStructural
            ? ComponentGenerationMode.DebugStructural
            : ComponentGenerationMode.ReleaseDirect;
        var revision = CompileRevision(component, generationMode);

        if (generationMode == ComponentGenerationMode.ReleaseDirect)
        {
            Assert.DoesNotContain(
                "__akburaRenderState",
                revision.GeneratedSource,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                ".ReconcileCollection(",
                revision.GeneratedSource,
                StringComparison.Ordinal);
        }

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var owner = revision.CreateOwner();
                var root = revision.InvokeFirstUpdate(owner);
                var items = Assert.IsAssignableFrom<ICollection<Control>>(
                    root.GetType().GetProperty("Items")?.GetValue(root));

                Assert.True(
                    items.Count == 2,
                    revision.GeneratedSource);
                Assert.Collection(
                    items,
                    item => Assert.Equal(
                        "First",
                        Assert.IsType<TextBlock>(item).Text),
                    item => Assert.Equal(
                        "Second",
                        Assert.IsType<Button>(item).Content));
                Assert.Same(root, revision.InvokeUpdate(owner));
                Assert.Equal(2, items.Count);
            },
            CancellationToken.None);
    }

    private static RuntimeRevision CompileRevision(
        string componentSource,
        ComponentGenerationMode generationMode =
            ComponentGenerationMode.DebugStructural)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            componentSource,
            HostSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);
        var generatedText = ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>(),
            CancellationToken.None,
            generationMode);
        var generatedSource = generatedText.ToString();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(
            LanguageVersion.Preview);
        if (generationMode == ComponentGenerationMode.DebugStructural)
        {
            parseOptions = parseOptions.WithPreprocessorSymbols("DEBUG");
        }

        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedText,
            parseOptions,
            path: ComponentDocumentWriter.GetHintName(
                component,
                "Views/PlannerView.akbura"));
        var runtimeCompilation = fixture.CSharpCompilation
            .AddSyntaxTrees(generatedTree)
            .WithAssemblyName(
                "ComponentStructuralHotReloadIntegration_" +
                Guid.NewGuid().ToString("N"));
        var diagnostics = runtimeCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            generatedSource);

        using var assemblyStream = new MemoryStream();
        var emitResult = runtimeCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics) +
            Environment.NewLine +
            generatedSource);

        return new RuntimeRevision(
            Assembly.Load(assemblyStream.ToArray()),
            generatedSource,
            generationMode == ComponentGenerationMode.DebugStructural);
    }

    private sealed class RuntimeRevision
    {
        private readonly Type _ownerType;
        private readonly FieldInfo? _renderStateField;
        private readonly MethodInfo _firstUpdateMethod;
        private readonly MethodInfo _updateMethod;

        public RuntimeRevision(
            Assembly assembly,
            string generatedSource,
            bool usesStructuralHotReload)
        {
            GeneratedSource = generatedSource;
            _ownerType = assembly.GetType("Demo.PlannerView") ??
                throw new InvalidOperationException(
                    "Generated component type was not found.");
            _renderStateField = _ownerType.GetField(
                ComponentStructuralHotReloadWriter.RenderStateFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            _firstUpdateMethod = GetMethod("InvokeFirstUpdate");
            _updateMethod = GetMethod("InvokeUpdate");

            if (usesStructuralHotReload)
            {
                Assert.NotNull(_renderStateField);
                Assert.Equal(
                    typeof(AkburaRenderState),
                    _renderStateField.FieldType);
            }
            else
            {
                Assert.Null(_renderStateField);
            }
        }

        public string GeneratedSource { get; }

        public object CreateOwner()
        {
            var owner = Activator.CreateInstance(_ownerType);

            Assert.NotNull(owner);
            return owner;
        }

        public Control InvokeFirstUpdate(object owner)
        {
            return Assert.IsAssignableFrom<Control>(
                _firstUpdateMethod.Invoke(owner, parameters: null));
        }

        public Control InvokeUpdate(object owner)
        {
            return Assert.IsAssignableFrom<Control>(
                _updateMethod.Invoke(owner, parameters: null));
        }

        public AkburaRenderState GetRenderState(object owner)
        {
            Assert.NotNull(_renderStateField);
            return Assert.IsType<AkburaRenderState>(
                _renderStateField.GetValue(owner));
        }

        public void SetRenderState(
            object owner,
            AkburaRenderState renderState)
        {
            Assert.NotNull(_renderStateField);
            _renderStateField.SetValue(owner, renderState);
        }

        public int GetInt32Field(object owner, string name)
        {
            var field = _ownerType.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public) ??
                throw new InvalidOperationException(
                    "Generated runtime field was not found: " + name);

            return Assert.IsType<int>(field.GetValue(owner));
        }

        private MethodInfo GetMethod(string name)
        {
            return _ownerType.GetMethod(
                       name,
                       BindingFlags.Instance | BindingFlags.Public) ??
                throw new InvalidOperationException(
                    "Generated runtime method was not found: " + name);
        }
    }
}
