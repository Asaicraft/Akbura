using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Akbura.Furioso;
using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkburaCsGeneratorTests
{
    [Fact]
    public void Generator_UsesInstantiableMarkupTypeWhenStaticTypeHasSameName()
    {
        const string component =
            "using Avalonia.Controls;\n" +
            "using Avalonia.Controls.Shapes;\n" +
            "\n" +
            "<Button>\n" +
            "    <Path />\n" +
            "</Button>\n";
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedPathContentTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    "global using System.IO;",
                    parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "IconButton.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources);
        var text = generated.SourceText.ToString();
        Assert.Contains(
            "private global::Avalonia.Controls.Shapes.Path __element1",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.Controls.ContentControl.ContentProperty, __element1",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_UsesControlCandidateForAmbiguousAkcssTargetType()
    {
        const string akcss =
            "@using Avalonia.Controls.Shapes;\n" +
            "@using Avalonia.Media;\n" +
            "\n" +
            "Path.icon {\n" +
            "    Width: 18d;\n" +
            "    Fill: Brushes.White;\n" +
            "}\n";
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedPathStyleTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    "global using System.IO;",
                    parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "Icon.akcss");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(akcss)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources);
        var text = generated.SourceText.ToString();
        Assert.Contains(
            "global::Avalonia.Controls.Shapes.Path",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.Controls.Shapes.Shape.FillProperty",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.Media.Brushes.White",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_EmitsCompilableComponentLifecycleAndDescriptors()
    {
        const string component =
            "using Avalonia.Controls;\n" +
            "\n" +
             "param int Initial = 2;\n" +
             "state int count = 0;\n" +
             "\n" +
             "void Increment(int delta)\n" +
             "{\n" +
             "    count += delta;\n" +
             "}\n" +
             "\n" +
             "<StackPanel x.Name=\"layout\">\n" +
             "    <TextBlock x.Name=\"label\" Text={$\"Count: {count}\"} />\n" +
             "    <Button Click={() => Increment(1)}>+</Button>\n" +
             "</StackPanel>\n";
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedComponentTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(Environment.CurrentDirectory, "Counter.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts: [new TestAdditionalText(sourcePath, SourceText.From(component))],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        var generated = Assert.Single(result.GeneratedSources);
        Assert.StartsWith("Akbura.Component.Counter.akbura.", generated.HintName, StringComparison.Ordinal);
        var text = generated.SourceText.ToString();
        Assert.Contains("partial class Counter : global::Akbura.AkburaControl", text, StringComparison.Ordinal);
        Assert.Contains("Parameter<Counter, int> InitialProperty", text, StringComparison.Ordinal);
        Assert.Contains("StateInfo<int> s_stateInfo_count", text, StringComparison.Ordinal);
        Assert.Contains("void Increment(int delta)", text, StringComparison.Ordinal);
        Assert.Contains("private global::Avalonia.Controls.TextBlock label", text, StringComparison.Ordinal);
        Assert.Contains("protected override global::Avalonia.Controls.Control FirstUpdate()", text, StringComparison.Ordinal);
        Assert.Contains("protected override global::Avalonia.Controls.Control Update()", text, StringComparison.Ordinal);
        Assert.Contains("__eventArgument0", text, StringComparison.Ordinal);
        Assert.Contains("__eventArgument1", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_EmitsNonVoidComponentFunction()
    {
        const string component =
            """
            using Akbura;
            using Avalonia;
            using Avalonia.Controls;

            StyledElement? FindAncestor(StyledElement? current)
            {
                while (current != null)
                {
                    if (current is AkburaControl)
                    {
                        return current;
                    }

                    current = current.Parent;
                }

                return null;
            }

            var ancestor = FindAncestor(Parent);

            <TextBlock Text={ancestor?.GetType().Name ?? ""}/>
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedNonVoidFunctionTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "AncestorLink.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(
            Assert.Single(driver.GetRunResult().Results)
                .GeneratedSources);
        Assert.Contains(
            "StyledElement? FindAncestor(StyledElement? current)",
            generated.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_ResolvesInheritedPropertyOnSourceComponent()
    {
        const string child =
            """
            using Avalonia.Controls;

            <TextBlock Text="Child"/>
            """;
        const string host =
            """
            using Avalonia.Controls;

            <Child IsVisible={false}/>
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedInheritedPropertyTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    Path.Combine(Environment.CurrentDirectory, "Child.akbura"),
                    SourceText.From(child)),
                new TestAdditionalText(
                    Path.Combine(Environment.CurrentDirectory, "Host.akbura"),
                    SourceText.From(host)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Generator_UpdatesInlineExpressionContentAfterStateChange()
    {
        const string component =
            """
            using Avalonia.Controls;

            state bool isHighlighted = false;

            <Button Click={() => isHighlighted = !isHighlighted}>
                {isHighlighted
                    ? "Disable highlight"
                    : "Enable highlight"}
            </Button>
            """;
        const string csharp =
            """
            public partial class ReactiveContent
            {
                public ReactiveContent()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedReactiveContentTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(csharp, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "ReactiveContent.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(
            Assert.Single(driver.GetRunResult().Results)
                .GeneratedSources);
        Assert.Contains(
            "ContentControl.ContentProperty, isHighlighted",
            generated.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var componentControl = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(
                        assembly.GetType("ReactiveContent")!));
                var window = new Window
                {
                    Content = componentControl,
                };

                window.Show();

                var button = Assert.IsType<Button>(
                    componentControl.Child);
                Assert.Equal("Enable highlight", button.Content);

                button.RaiseEvent(
                    new Avalonia.Interactivity.RoutedEventArgs(
                        Button.ClickEvent));

                Assert.Equal("Disable highlight", button.Content);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public void Generator_EmitsPropertyElementRawStringExpressionWithSemicolon()
    {
        const string featureView =
            """
            using Avalonia.Controls;

            param string Code = "";

            <ContentControl />
            """;
        const string app =
            """"
            using Avalonia.Controls;

            <FeatureView>
                <FeatureView.Code>
                    {"""
                    state int count = 0;

                    <TextBlock Text={$"Current value: {count}"} />
                    """;}
                </FeatureView.Code>
            </FeatureView>
            """";
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedRawStringPropertyElementTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var featureViewPath = Path.Combine(Environment.CurrentDirectory, "FeatureView.akbura");
        var appPath = Path.Combine(Environment.CurrentDirectory, "App.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts:
            [
                new TestAdditionalText(featureViewPath, SourceText.From(featureView)),
                new TestAdditionalText(appPath, SourceText.From(app)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSources = Assert.Single(driver.GetRunResult().Results).GeneratedSources;
        var appSource = Assert.Single(
            generatedSources,
            static source => source.HintName.Contains("App.akbura", StringComparison.Ordinal));

        Assert.Contains(
            "state int count = 0;",
            appSource.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_NormalizesIndentedInterpolatedContent()
    {
        const string component =
            """
        using Avalonia.Controls;

        state int count = 0;

        <Button>
            Increment to {count+1}
        </Button>
        """;

        var generatedSource = GenerateWhitespaceComponent(component);

        Assert.Contains(
            "$\"Increment to {count+1}\"",
            generatedSource,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "$\"    Increment to {count+1}\"",
            generatedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_PreservesIndentedInterpolatedContent_WhenXmlSpaceIsPreserve()
    {
        const string component =
            """
        using Avalonia.Controls;

        state int count = 0;

        <Button xml.space="preserve">
            Increment to {count+1}
        </Button>
        """;

        var generatedSource = GenerateWhitespaceComponent(component);

        Assert.Contains(
            "$\"    Increment to {count+1}\"",
            generatedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generator_UsesContentParameterAsLogicalContent()
    {
        const string wrapper =
            "using Avalonia.Controls;\n" +
            "using Avalonia.Controls.Presenters;\n" +
            "\n" +
            "param object Content;\n" +
            "param Button Submit;\n" +
            "\n" +
            "<StackPanel>\n" +
            "    <ContentPresenter Content={Content} />\n" +
            "    <ContentPresenter Content={Submit} />\n" +
            "</StackPanel>\n";
        const string consumer =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<MegaWrapper>\n" +
            "    <MegaWrapper.Submit>\n" +
            "        <Button Content=\"Submit\" />\n" +
            "    </MegaWrapper.Submit>\n" +
            "    <TextBlock Text=\"Hello Akbura!\" />\n" +
            "</MegaWrapper>\n";
        const string csharp =
            "public partial class MegaWrapper\n" +
            "{\n" +
            "    public MegaWrapper() : base(global::Akbura.Engine.AkburaEngine.Empty) { }\n" +
            "}\n" +
            "\n" +
            "public partial class MyAkbura\n" +
            "{\n" +
            "    public MyAkbura() : base(global::Akbura.Engine.AkburaEngine.Empty) { }\n" +
            "}\n";
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedLogicalContentTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(csharp, parseOptions)],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var wrapperPath = Path.Combine(Environment.CurrentDirectory, "MegaWrapper.akbura");
        var consumerPath = Path.Combine(Environment.CurrentDirectory, "MyAkbura.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts:
            [
                new TestAdditionalText(wrapperPath, SourceText.From(wrapper)),
                new TestAdditionalText(consumerPath, SourceText.From(consumer)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSources = Assert.Single(driver.GetRunResult().Results).GeneratedSources;
        Assert.Equal(2, generatedSources.Length);
        var wrapperSource = Assert.Single(
            generatedSources,
            static source => source.HintName.Contains("MegaWrapper.akbura", StringComparison.Ordinal));
        Assert.Contains(
            "[global::Avalonia.Metadata.Content]",
            wrapperSource.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var component = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(assembly.GetType("MyAkbura")!));
                var window = new Window { Content = component };
                window.Show();

                var wrapperControl = Assert.IsAssignableFrom<AkburaControl>(component.Child);
                var content = Assert.IsType<TextBlock>(
                    wrapperControl.GetType().GetProperty("Content")!.GetValue(wrapperControl));
                Assert.IsType<Button>(
                    wrapperControl.GetType().GetProperty("Submit")!.GetValue(wrapperControl));
                Assert.Same(wrapperControl, ((ILogical)content).LogicalParent);
                Assert.Same(content, Assert.Single(((ILogical)wrapperControl).LogicalChildren));
                Assert.NotSame(
                    wrapperControl,
                    ((ILogical)Assert.IsType<StackPanel>(wrapperControl.Child)).LogicalParent);

                var replacement = new Border();
                wrapperControl.GetType().GetProperty("Content")!.SetValue(
                    wrapperControl,
                    replacement);
                Assert.Null(((ILogical)content).LogicalParent);
                Assert.Same(wrapperControl, ((ILogical)replacement).LogicalParent);
                Assert.Same(
                    replacement,
                    Assert.Single(((ILogical)wrapperControl).LogicalChildren));

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_UsesObservableCollectionForIListContent()
    {
        const string wrapper =
            "using Avalonia.Controls;\n" +
            "using System.Collections.Generic;\n" +
            "\n" +
            "param IList<Control> Content;\n" +
            "\n" +
            "<StackPanel />\n";
        const string consumer =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<CollectionWrapper>\n" +
            "    <TextBlock Text=\"First\" />\n" +
            "    <Button Content=\"Second\" />\n" +
            "</CollectionWrapper>\n";
        const string emptyConsumer = "<CollectionWrapper />\n";
        const string csharp =
            "public partial class CollectionWrapper\n" +
            "{\n" +
            "    public CollectionWrapper() : base(global::Akbura.Engine.AkburaEngine.Empty) { }\n" +
            "}\n" +
            "\n" +
            "public partial class CollectionConsumer\n" +
            "{\n" +
            "    public CollectionConsumer() : base(global::Akbura.Engine.AkburaEngine.Empty) { }\n" +
            "}\n" +
            "\n" +
            "public partial class EmptyCollectionConsumer\n" +
            "{\n" +
            "    public EmptyCollectionConsumer() : base(global::Akbura.Engine.AkburaEngine.Empty) { }\n" +
            "}\n";
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedCollectionContentTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(csharp, parseOptions)],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var wrapperPath = Path.Combine(Environment.CurrentDirectory, "CollectionWrapper.akbura");
        var consumerPath = Path.Combine(Environment.CurrentDirectory, "CollectionConsumer.akbura");
        var emptyConsumerPath = Path.Combine(
            Environment.CurrentDirectory,
            "EmptyCollectionConsumer.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts:
            [
                new TestAdditionalText(wrapperPath, SourceText.From(wrapper)),
                new TestAdditionalText(consumerPath, SourceText.From(consumer)),
                new TestAdditionalText(emptyConsumerPath, SourceText.From(emptyConsumer)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSources = Assert.Single(driver.GetRunResult().Results).GeneratedSources;
        var wrapperSource = Assert.Single(
            generatedSources,
            static source => source.HintName.Contains("CollectionWrapper.akbura", StringComparison.Ordinal));
        var wrapperText = wrapperSource.SourceText.ToString();
        Assert.Contains(
            "ReadOnlyParameter<CollectionWrapper, global::System.Collections.Generic.IList<global::Avalonia.Controls.Control>>",
            wrapperText,
            StringComparison.Ordinal);
        Assert.Contains(
            "ObservableCollection<global::Avalonia.Controls.Control>",
            wrapperText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var component = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(assembly.GetType("CollectionConsumer")!));
                var window = new Window { Content = component };
                window.Show();

                var wrapperControl = Assert.IsAssignableFrom<AkburaControl>(component.Child);
                var content = Assert.IsType<ObservableCollection<Control>>(
                    wrapperControl.GetType().GetProperty("Content")!.GetValue(wrapperControl));
                Assert.Equal(2, content.Count);
                Assert.Equal(2, ((ILogical)wrapperControl).LogicalChildren.Count);
                Assert.DoesNotContain(
                    wrapperControl.Child!,
                    ((ILogical)wrapperControl).LogicalChildren);

                var added = new Border();
                content.Add(added);
                Assert.Same(wrapperControl, ((ILogical)added).LogicalParent);
                Assert.Equal(3, ((ILogical)wrapperControl).LogicalChildren.Count);

                content.Remove(added);
                Assert.Null(((ILogical)added).LogicalParent);
                Assert.Equal(2, ((ILogical)wrapperControl).LogicalChildren.Count);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_UsesObservableCollectionForNamedIListParameter()
    {
        const string owner =
            """
            using System.Collections.Generic;
            using Demo;

            param IList<TestPage> Pages;

            <Avalonia.Controls.StackPanel />
            """;
        const string consumer =
            """
            using Demo;

            <NamedCollectionOwner>
                <NamedCollectionOwner.Pages>
                    <TestPage />
                    <TestPage />
                </NamedCollectionOwner.Pages>
            </NamedCollectionOwner>
            """;
        const string csharp =
            """
            namespace Demo
            {
                public sealed class TestPage : global::Avalonia.Controls.Control
                {
                }
            }

            public partial class NamedCollectionOwner
            {
                public NamedCollectionOwner()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }

            public partial class NamedCollectionConsumer
            {
                public NamedCollectionConsumer()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedNamedCollectionParameterTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(csharp, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var ownerPath = Path.Combine(
            Environment.CurrentDirectory,
            "NamedCollectionOwner.akbura");
        var consumerPath = Path.Combine(
            Environment.CurrentDirectory,
            "NamedCollectionConsumer.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    ownerPath,
                    SourceText.From(owner)),
                new TestAdditionalText(
                    consumerPath,
                    SourceText.From(consumer)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSources = Assert.Single(
            driver.GetRunResult().Results).GeneratedSources;
        var ownerSource = Assert.Single(
            generatedSources,
            static source => source.HintName.Contains(
                "NamedCollectionOwner.akbura",
                StringComparison.Ordinal));
        var ownerText = ownerSource.SourceText.ToString();
        Assert.Contains(
            "ReadOnlyParameter<NamedCollectionOwner, global::System.Collections.Generic.IList<global::Demo.TestPage>>",
            ownerText,
            StringComparison.Ordinal);
        Assert.Contains(
            "private readonly global::System.Collections.ObjectModel.ObservableCollection<global::Demo.TestPage> __collection_Pages = [];",
            ownerText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[global::Avalonia.Metadata.Content]",
            ownerText,
            StringComparison.Ordinal);

        var consumerSource = Assert.Single(
            generatedSources,
            static source => source.HintName.Contains(
                "NamedCollectionConsumer.akbura",
                StringComparison.Ordinal));
        Assert.Contains(
            "__element0.__AkburaAddCollection_Pages(",
            consumerSource.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var consumerControl = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(
                        assembly.GetType("NamedCollectionConsumer")!));
                var window = new Window
                {
                    Content = consumerControl,
                };

                window.Show();

                var ownerControl = Assert.IsAssignableFrom<AkburaControl>(
                    consumerControl.Child);
                var pagesProperty = ownerControl.GetType().GetProperty("Pages")!;
                Assert.False(pagesProperty.CanWrite);
                var pages = Assert.IsAssignableFrom<System.Collections.IList>(
                    pagesProperty.GetValue(ownerControl));
                Assert.Equal(2, pages.Count);
                Assert.Equal(
                    "ObservableCollection`1",
                    pages.GetType().Name);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_EmitsQualifiedUseEffectAndStabilizesAfterStateChange()
    {
        const string component =
            """
            using Akbura.Hooks;
            using Avalonia.Controls;
            using Avalonia.Controls.Presenters;

            param Control? Content = null;

            state Control? content = null;

            useEffect(() =>
            {
                content = new Border();
            }, []);

            Content = content;

            <ContentPresenter Content={Content} />
            """;
        const string csharp =
            """
            public partial class EffectView
            {
                public EffectView()
                    : base(
                        new global::Akbura.Engine.AkburaEngineExtensions
                            .AkburaEngineBuilder()
                            .WithMaxUpdatesPerBatch(3)
                            .Build())
                {
                }
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedUseEffectTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(csharp, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "EffectView.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources);
        Assert.Contains(
            "global::Akbura.Hooks.EffectHooks.useEffect(",
            generated.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "__element0.UpdateChild();",
            generated.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var componentControl = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(
                        assembly.GetType("EffectView")!));
                var window = new Window
                {
                    Content = componentControl,
                };

                window.Show();

                var presenter = Assert.IsType<
                    Avalonia.Controls.Presenters.ContentPresenter>(
                    componentControl.Child);
                var border = Assert.IsType<Border>(presenter.Content);
                Assert.Same(
                    presenter,
                    border.GetVisualParent());

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_DataTemplatePreviewEffectBuildsAndDisplaysComponent()
    {
        const string featureView =
            """
            using Akbura.Hooks;
            using Avalonia.Controls;
            using Avalonia.Controls.Presenters;
            using Avalonia.Controls.Templates;

            param IDataTemplate View;

            state Control? preview = null;

            useEffect(() =>
            {
                preview = View.Build(null);
            }, [View]);

            <TabControl>
                <TabItem Header="Preview">
                    <ContentPresenter Content={preview} />
                </TabItem>
            </TabControl>
            """;
        const string previewChild =
            """
            using Avalonia.Controls;

            <TextBlock Text="Visible preview" />
            """;
        const string host =
            """
            using Avalonia.Controls;
            using Avalonia.Controls.Templates;

            <FeatureView>
                <FeatureView.View>
                    <PreviewChild />
                </FeatureView.View>
            </FeatureView>
            """;
        const string csharp =
            """
            public partial class FeatureView : global::Akbura.AkburaControl
            {
                public FeatureView()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }

            public partial class PreviewChild : global::Akbura.AkburaControl
            {
                public PreviewChild()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }

            public partial class PreviewHost : global::Akbura.AkburaControl
            {
                public PreviewHost()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedDataTemplatePreviewTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(csharp, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var projectDirectory = Environment.CurrentDirectory;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    Path.Combine(projectDirectory, "FeatureView.akbura"),
                    SourceText.From(featureView)),
                new TestAdditionalText(
                    Path.Combine(projectDirectory, "PreviewChild.akbura"),
                    SourceText.From(previewChild)),
                new TestAdditionalText(
                    Path.Combine(projectDirectory, "PreviewHost.akbura"),
                    SourceText.From(host)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var featureViewSource = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources,
            static source => source.HintName.Contains(
                "FeatureView.akbura",
                StringComparison.Ordinal));
        Assert.Contains(
            "__element2.UpdateChild();",
            featureViewSource.SourceText.ToString(),
            StringComparison.Ordinal);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                Avalonia.Application.Current!.Styles.Add(
                    new Avalonia.Themes.Fluent.FluentTheme());
                var hostControl = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(
                        assembly.GetType("PreviewHost")!));
                var window = new Window
                {
                    Content = hostControl,
                };

                window.Show();
                window.UpdateLayout();

                var featureControl = Assert.IsAssignableFrom<AkburaControl>(
                    hostControl.Child);
                var tabs = Assert.IsType<TabControl>(
                    featureControl.Child);
                var previewTab = Assert.IsType<TabItem>(
                    Assert.Single(tabs.Items));
                var presenter = Assert.IsType<
                    Avalonia.Controls.Presenters.ContentPresenter>(
                    previewTab.Content);
                var previewControl = Assert.IsAssignableFrom<AkburaControl>(
                    presenter.Content);
                Assert.Equal(
                    "PreviewChild",
                    previewControl.GetType().Name);
                Assert.Same(
                    presenter,
                    previewControl.GetVisualParent());
                Assert.True(previewControl.IsInitialized);
                Assert.Equal(
                    "Visible preview",
                    Assert.IsType<TextBlock>(
                        previewControl.Child).Text);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_SourceComponentParameterWorksWithAvaloniaPropertyHook()
    {
        const string router =
            """
            namespace Components;

            using Avalonia.Controls;

            param bind string Url = "";

            <Control />
            """;
        const string link =
            """
            namespace Components;

            using Akbura.Hooks;
            using Avalonia.Controls;

            param Router Router;

            state string currentUrl =
                useAvaloniaProperty(
                    Router,
                    global::Components.Router.UrlProperty);

            <TextBlock Text={currentUrl} />
            """;
        const string app =
            """
            namespace Components;

            using Avalonia.Controls;

            <StackPanel>
                <Link Router={Router} />
                <Router x.Name="Router" />
            </StackPanel>
            """;
        const string csharp =
            """
            namespace Components;

            public partial class Router : global::Akbura.AkburaControl
            {
                public Router()
                    : base(
                        global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }

            public partial class Link : global::Akbura.AkburaControl
            {
                public Link()
                    : base(
                        global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }

            public partial class App : global::Akbura.AkburaControl
            {
                public App()
                    : base(
                        global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedSourceComponentHookTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    csharp,
                    parseOptions),
            ],
            references:
                SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver =
            CSharpGeneratorDriver.Create(
                generators:
                [
                    new AkburaCsGenerator()
                        .AsSourceGenerator(),
                ],
                additionalTexts:
                [
                    new TestAdditionalText(
                        Path.Combine(
                            Environment.CurrentDirectory,
                            "Components",
                            "Router.akbura"),
                        SourceText.From(router)),
                    new TestAdditionalText(
                        Path.Combine(
                            Environment.CurrentDirectory,
                            "Components",
                            "Link.akbura"),
                        SourceText.From(link)),
                    new TestAdditionalText(
                        Path.Combine(
                            Environment.CurrentDirectory,
                            "Components",
                            "App.akbura"),
                        SourceText.From(app)),
                ],
                parseOptions: parseOptions);

        driver =
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var updatedCompilation,
                out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error);
        var result = Assert.Single(
            driver.GetRunResult().Results);
        var linkSource = Assert.Single(
            result.GeneratedSources,
            static source =>
                source.HintName.Contains(
                    "Link.akbura",
                    StringComparison.Ordinal));
        var linkText =
            linkSource.SourceText.ToString();
        Assert.Contains(
            "Parameter<Link, global::Components.Router>",
            linkText,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.Hooks.AvaloniaPropertyHooks.useAvaloniaProperty<global::Components.Router, string>",
            linkText,
            StringComparison.Ordinal);

        using var assemblyStream =
            new MemoryStream();
        var emitResult =
            updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(
                Environment.NewLine,
                emitResult.Diagnostics));
        var assembly =
            Assembly.Load(assemblyStream.ToArray());

        using var session =
            HeadlessUnitTestSession.StartNew(
                typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var routerControl =
                    Assert.IsAssignableFrom<
                        AkburaControl>(
                        Activator.CreateInstance(
                            assembly.GetType(
                                "Components.Router")!));
                var linkControl =
                    Assert.IsAssignableFrom<
                        AkburaControl>(
                        Activator.CreateInstance(
                            assembly.GetType(
                                "Components.Link")!));
                var routerType =
                    routerControl.GetType();
                var urlProperty =
                    routerType.GetProperty("Url")!;
                urlProperty.SetValue(
                    routerControl,
                    "/first");
                linkControl.GetType()
                    .GetProperty("Router")!
                    .SetValue(
                        linkControl,
                        routerControl);
                var window = new Window
                {
                    Content = linkControl,
                };

                window.Show();

                var textBlock =
                    Assert.IsType<TextBlock>(
                        linkControl.Child);
                Assert.Equal(
                    "/first",
                    textBlock.Text);

                urlProperty.SetValue(
                    routerControl,
                    "/second");

                Assert.Equal(
                    "/second",
                    textBlock.Text);

                window.Close();

                var appControl =
                    Assert.IsAssignableFrom<
                        AkburaControl>(
                        Activator.CreateInstance(
                            assembly.GetType(
                                "Components.App")!));
                var appWindow = new Window
                {
                    Content = appControl,
                };

                appWindow.Show();

                var panel =
                    Assert.IsType<StackPanel>(
                        appControl.Child);
                var generatedLink =
                    Assert.IsAssignableFrom<
                        AkburaControl>(
                        panel.Children[0]);
                var generatedRouter =
                    Assert.IsAssignableFrom<
                        AkburaControl>(
                        panel.Children[1]);
                var generatedText =
                    Assert.IsType<TextBlock>(
                        generatedLink.Child);
                var generatedUrl =
                    generatedRouter.GetType()
                        .GetProperty("Url")!;

                Assert.Equal(
                    string.Empty,
                    generatedText.Text);

                generatedUrl.SetValue(
                    generatedRouter,
                    "/generated");

                Assert.Equal(
                    "/generated",
                    generatedText.Text);

                appWindow.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_BindAttributePropagatesSourceComponentParameterChangesToParentState()
    {
        const string router =
            """
            namespace Components;

            using Avalonia.Controls;

            param bind string Url = "";

            <Control />
            """;
        const string app =
            """
            namespace Components;

            using Avalonia.Controls;

            state string url = "/first";

            <StackPanel>
                <TextBlock Text={url} />
                <Router bind:Url={url} x.Name="Router" />
            </StackPanel>
            """;
        const string csharp =
            """
            namespace Components;

            public partial class Router : global::Akbura.AkburaControl
            {
                public Router()
                    : base(
                        global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }

            public partial class App : global::Akbura.AkburaControl
            {
                public App()
                    : base(
                        global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedBindAttributeTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    csharp,
                    parseOptions),
            ],
            references:
                SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver =
            CSharpGeneratorDriver.Create(
                generators:
                [
                    new AkburaCsGenerator()
                        .AsSourceGenerator(),
                ],
                additionalTexts:
                [
                    new TestAdditionalText(
                        Path.Combine(
                            Environment.CurrentDirectory,
                            "Components",
                            "Router.akbura"),
                        SourceText.From(router)),
                    new TestAdditionalText(
                        Path.Combine(
                            Environment.CurrentDirectory,
                            "Components",
                            "App.akbura"),
                        SourceText.From(app)),
                ],
                parseOptions: parseOptions);

        driver =
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var updatedCompilation,
                out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error);
        var result = Assert.Single(
            driver.GetRunResult().Results);
        var appSource = Assert.Single(
            result.GeneratedSources,
            static source =>
                source.HintName.Contains(
                    "App.akbura",
                    StringComparison.Ordinal));
        var appText =
            appSource.SourceText.ToString();
        Assert.Contains(
            "Router.PropertyChanged += ",
            appText,
            StringComparison.Ordinal);
        Assert.Contains(
            "url = (string)__bindingChange_",
            appText,
            StringComparison.Ordinal);

        using var assemblyStream =
            new MemoryStream();
        var emitResult =
            updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(
                Environment.NewLine,
                emitResult.Diagnostics));
        var assembly =
            Assembly.Load(assemblyStream.ToArray());

        using var session =
            HeadlessUnitTestSession.StartNew(
                typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var appControl =
                    Assert.IsAssignableFrom<
                        AkburaControl>(
                        Activator.CreateInstance(
                            assembly.GetType(
                                "Components.App")!));
                var window = new Window
                {
                    Content = appControl,
                };

                window.Show();

                var panel =
                    Assert.IsType<StackPanel>(
                        appControl.Child);
                var textBlock =
                    Assert.IsType<TextBlock>(
                        panel.Children[0]);
                var routerControl =
                    Assert.IsAssignableFrom<
                        AkburaControl>(
                        panel.Children[1]);
                var urlProperty =
                    routerControl.GetType()
                        .GetProperty("Url")!;

                Assert.Equal(
                    "/first",
                    textBlock.Text);
                Assert.Equal(
                    "/first",
                    urlProperty.GetValue(
                        routerControl));

                urlProperty.SetValue(
                    routerControl,
                    "/second");

                Assert.Equal(
                    "/second",
                    textBlock.Text);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public void Generator_SourceComponentNameCanBeReferencedBeforeItsMarkupDeclaration()
    {
        const string router =
            """
            namespace Components;

            using Avalonia.Controls;

            param bind string Url = "";

            <ContentControl />
            """;
        const string link =
            """
            namespace Components;

            using Avalonia.Controls;

            param Router Router;

            <Button />
            """;
        const string app =
            """
            namespace Components;

            using Avalonia.Controls;

            <StackPanel>
                <Link Router={Router} />
                <Router x.Name="Router" />
            </StackPanel>
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedForwardComponentNameTests",
            references:
                SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver =
            CSharpGeneratorDriver.Create(
                generators:
                [
                    new AkburaCsGenerator()
                        .AsSourceGenerator(),
                ],
                additionalTexts:
                [
                    new TestAdditionalText(
                        Path.Combine(
                            Environment.CurrentDirectory,
                            "Components",
                            "Router.akbura"),
                        SourceText.From(router)),
                    new TestAdditionalText(
                        Path.Combine(
                            Environment.CurrentDirectory,
                            "Components",
                            "Link.akbura"),
                        SourceText.From(link)),
                    new TestAdditionalText(
                        Path.Combine(
                            Environment.CurrentDirectory,
                            "Components",
                            "App.akbura"),
                        SourceText.From(app)),
                ],
                parseOptions: parseOptions);

        driver =
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var updatedCompilation,
                out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error);
        var result = Assert.Single(
            driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        var appSource = Assert.Single(
            result.GeneratedSources,
            static source =>
                source.HintName.Contains(
                    "App.akbura",
                    StringComparison.Ordinal));
        var appText = appSource.SourceText.ToString();
        Assert.Contains(
            "private global::Components.Router Router",
            appText,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Router = Router;",
            appText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generator_DeferredComponentBecomesContentPresenterVisualChild()
    {
        const string child =
            """
            using Avalonia.Controls;

            <Border Width="100" Height="40">
                <TextBlock Text="Visible child" />
            </Border>
            """;
        const string router =
            """
            using Akbura.Hooks;
            using Avalonia.Controls;
            using Avalonia.Controls.Presenters;
            using Avalonia.Markup.Xaml.Templates;
            using System.Collections.Generic;

            param IList<DeferredPage> Pages;

            state Control? content = null;

            useEffect(() =>
            {
                content = TemplateContent
                    .Load<Control>(Pages[0].Content)
                    .Result;
            }, [Pages]);

            <ContentPresenter Content={content} />
            """;
        const string host =
            """
            using Avalonia.Controls;

            <DrawerPage Header="Deferred content">
                <Border Padding="24">
                    <ScrollViewer>
                        <DeferredRouter>
                            <DeferredRouter.Pages>
                                <DeferredPage>
                                    <DeferredChild />
                                </DeferredPage>
                            </DeferredRouter.Pages>
                        </DeferredRouter>
                    </ScrollViewer>
                </Border>
            </DrawerPage>
            """;
        const string csharp =
            """
            using Avalonia.Metadata;

            public sealed class DeferredPage
            {
                [Content]
                [TemplateContent]
                public object Content { get; set; } = null!;
            }

            public partial class DeferredChild : global::Akbura.AkburaControl
            {
                public DeferredChild()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }

            public partial class DeferredRouter : global::Akbura.AkburaControl
            {
                public DeferredRouter()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }

            public partial class DeferredHost : global::Akbura.AkburaControl
            {
                public DeferredHost()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaDeferredComponentPresenterTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    csharp,
                    parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    Path.Combine(
                        Environment.CurrentDirectory,
                        "DeferredChild.akbura"),
                    SourceText.From(child)),
                new TestAdditionalText(
                    Path.Combine(
                        Environment.CurrentDirectory,
                        "DeferredRouter.akbura"),
                    SourceText.From(router)),
                new TestAdditionalText(
                    Path.Combine(
                        Environment.CurrentDirectory,
                        "DeferredHost.akbura"),
                    SourceText.From(host)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult =
            updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(
                Environment.NewLine,
                emitResult.Diagnostics));
        var assembly =
            Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                Avalonia.Application.Current!.Styles.Add(
                    new Avalonia.Themes.Fluent.FluentTheme());
                var hostControl =
                    Assert.IsAssignableFrom<AkburaControl>(
                        Activator.CreateInstance(
                            assembly.GetType(
                                "DeferredHost")!));
                var window = new Window
                {
                    Width = 300,
                    Height = 200,
                    Content = hostControl,
                };
                window.Show();

                var drawer =
                    Assert.IsType<DrawerPage>(
                        hostControl.Child);
                var border =
                    Assert.IsType<Border>(
                        drawer.Content);
                var scrollViewer =
                    Assert.IsType<ScrollViewer>(
                        border.Child);
                var routerControl =
                    Assert.IsAssignableFrom<AkburaControl>(
                        scrollViewer.Content);
                var presenter = Assert.IsType<
                    Avalonia.Controls.Presenters.ContentPresenter>(
                    routerControl.Child);
                var childControl =
                    Assert.IsAssignableFrom<AkburaControl>(
                        presenter.Content);
                Assert.Same(
                    presenter,
                    childControl.GetVisualParent());
                Assert.IsType<Border>(
                    childControl.Child);
                var textBlock = Assert.Single(
                    childControl
                        .GetVisualDescendants()
                        .OfType<TextBlock>());
                Assert.True(
                    presenter.Bounds.Width > 0 &&
                    presenter.Bounds.Height > 0);
                Assert.True(
                    childControl.Bounds.Width > 0 &&
                    childControl.Bounds.Height > 0);
                Assert.True(
                    textBlock.Bounds.Width > 0 &&
                    textBlock.Bounds.Height > 0);

                window.Close();
            },
            CancellationToken.None);
    }

    [Theory]
    [InlineData(
        "System.Collections.IList",
        "ObservableCollection<global::System.Object>",
        true)]
    [InlineData(
        "System.Collections.Generic.IList<Avalonia.Controls.Control>",
        "ObservableCollection<global::Avalonia.Controls.Control>",
        true)]
    [InlineData(
        "System.Collections.Generic.ICollection<Avalonia.Controls.Control>",
        "ObservableCollection<global::Avalonia.Controls.Control>",
        true)]
    [InlineData(
        "System.Collections.ObjectModel.ObservableCollection<Avalonia.Controls.Control>",
        "ObservableCollection<global::Avalonia.Controls.Control>",
        true)]
    [InlineData(
        "System.Collections.Generic.List<Avalonia.Controls.Control>",
        "List<global::Avalonia.Controls.Control>",
        false)]
    public void Generator_EmitsRequestedContentCollectionShape(
        string parameterType,
        string backingType,
        bool observesChanges)
    {
        var component =
            "param " + parameterType + " Content;\n" +
            "\n" +
            "<Avalonia.Controls.StackPanel />\n";
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedContentShapeTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(Environment.CurrentDirectory, "ContentShape.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts: [new TestAdditionalText(sourcePath, SourceText.From(component))],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources);
        var text = generated.SourceText.ToString();
        Assert.Contains("[global::Avalonia.Metadata.Content]", text, StringComparison.Ordinal);
        Assert.Contains(backingType, text, StringComparison.Ordinal);
        Assert.Equal(
            observesChanges,
            text.Contains(".CollectionChanged +=", StringComparison.Ordinal));
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Generator_EmitsAndAppliesCompiledBinding()
    {
        const string component =
            "using Avalonia.Controls;\n" +
            "using Demo;\n" +
            "\n" +
            "<TextBlock x.DataType=\"Demo.ViewModel\" Text=${Binding Name, Mode=OneWay} />\n";
        const string csharp =
            "namespace Demo\n" +
            "{\n" +
            "    public sealed class ViewModel\n" +
            "    {\n" +
            "        public string Name { get; set; } = \"Akbura\";\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "public partial class BindingView\n" +
            "{\n" +
            "    public BindingView() : base(global::Akbura.Engine.AkburaEngine.Empty) { }\n" +
            "}\n";
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedBindingTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(csharp, parseOptions)],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(Environment.CurrentDirectory, "BindingView.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts: [new TestAdditionalText(sourcePath, SourceText.From(component))],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(Assert.Single(driver.GetRunResult().Results).GeneratedSources);
        var text = generated.SourceText.ToString();
        Assert.Contains(".Bind(", text, StringComparison.Ordinal);
        Assert.Contains(
            "CompiledBinding.Create<global::Demo.ViewModel, string>(static __source => __source.Name",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var componentType = assembly.GetType("BindingView");
                var viewModelType = assembly.GetType("Demo.ViewModel");
                Assert.NotNull(componentType);
                Assert.NotNull(viewModelType);
                var component = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(componentType));
                component.DataContext = Activator.CreateInstance(viewModelType);
                var window = new Window { Content = component };
                window.Show();

                var textBlock = Assert.IsType<TextBlock>(component.Child);
                Assert.Equal("Akbura", textBlock.Text);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_EmitsFuncDataTemplateWithInferredItemTypeAndItemName()
    {
        const string component =
            "using Avalonia.Controls;\n" +
            "using Demo;\n" +
            "\n" +
            "inject ViewModel Vm;\n" +
            "\n" +
            "<ItemsControl ItemsSource={Vm.Items}>\n" +
            "    <ItemsControl.ItemTemplate x.ItemName=\"item\">\n" +
            "        <TextBlock Text={item.Name} />\n" +
            "    </ItemsControl.ItemTemplate>\n" +
            "</ItemsControl>\n";
        const string csharp =
            "namespace Demo\n" +
            "{\n" +
            "    public sealed class Item\n" +
            "    {\n" +
            "        public string Name { get; set; } = \"Template item\";\n" +
            "    }\n" +
            "\n" +
            "    public sealed class ViewModel\n" +
            "    {\n" +
            "        public System.Collections.Generic.IEnumerable<Item> Items { get; } =\n" +
            "            new Item[] { new Item() };\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "public partial class TemplateView\n" +
            "{\n" +
            "    public TemplateView(Akbura.Engine.AkburaEngine engine) : base(engine) { }\n" +
            "}\n";
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedTemplateTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(csharp, parseOptions)],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(Environment.CurrentDirectory, "TemplateView.akbura");
        var componentTree = Akbura.Language.ComponentSyntaxTree.ParseText(
            SourceText.From(component),
            sourcePath);
        var semanticModel = new Akbura.Language.AkburaCompilation(
            compilation,
            [componentTree]).GetSemanticModel(componentTree);
        Assert.NotNull(semanticModel.GetDeclaredSymbol(componentTree.GetRoot()));
        var templateElement = componentTree.GetRoot().DescendantNodes()
            .OfType<Akbura.Language.Syntax.MarkupElementSyntax>()
            .Single(element => element.StartTag?.Name.ToFullString().Trim() == "ItemsControl.ItemTemplate");
        Assert.True(
            semanticModel.BindingSession.MarkupDataTypes.TryGetDataType(templateElement, out var itemDataType));
        Assert.Equal("global::Demo.Item", itemDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.True(
            semanticModel.BindingSession.MarkupDataTypes.TryCreateItemSymbol(templateElement, out var itemSymbol));
        Assert.NotNull(itemSymbol);
        var textAttribute = componentTree.GetRoot().DescendantNodes()
            .OfType<Akbura.Language.Syntax.MarkupPlainAttributeSyntax>()
            .Single(attribute => attribute.Name.Identifier.ValueText == "Text");
        Assert.Empty(semanticModel.GetSemanticDiagnostics(textAttribute));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts: [new TestAdditionalText(sourcePath, SourceText.From(component))],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(Assert.Single(driver.GetRunResult().Results).GeneratedSources);
        var text = generated.SourceText.ToString();
        Assert.Contains(
            "FuncDataTemplate<global::Demo.Item>((item, __nameScope) =>",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var componentType = assembly.GetType("TemplateView");
                var itemType = assembly.GetType("Demo.Item");
                Assert.NotNull(componentType);
                Assert.NotNull(itemType);
                var viewModel = Activator.CreateInstance(assembly.GetType("Demo.ViewModel")!);
                var engine = new Akbura.Engine.AkburaEngine(new ConstantServiceProvider(viewModel));
                var component = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(componentType, engine));
                var window = new Window { Content = component };
                window.Show();

                var itemsControl = Assert.IsType<ItemsControl>(component.Child);
                Assert.NotNull(itemsControl.ItemTemplate);
                var item = Activator.CreateInstance(itemType);
                var textBlock = Assert.IsType<TextBlock>(itemsControl.ItemTemplate.Build(item));
                Assert.Equal("Template item", textBlock.Text);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public void Generator_EmitsFuncDataTemplateForAkburaDataTemplateParameter()
    {
        const string router =
            """
            using Avalonia.Controls;
            using Avalonia.Controls.Templates;

            param IDataTemplate NotFound;

            <ContentControl />
            """;
        const string app =
            """
            using Avalonia.Controls;

            <Router>
                <Router.NotFound x.DataType="string" x.ItemName="url">
                    <TextBlock Text={$"Page '{url}' not found"} />
                </Router.NotFound>
            </Router>
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaDataTemplateParameterGeneratorTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var projectDirectory = Environment.CurrentDirectory;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    Path.Combine(
                        projectDirectory,
                        "Router.akbura"),
                    SourceText.From(router)),
                new TestAdditionalText(
                    Path.Combine(
                        projectDirectory,
                        "App.akbura"),
                    SourceText.From(app)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        var appSource = Assert.Single(
            result.GeneratedSources,
            static generated =>
                generated.SourceText.ToString().Contains(
                    "partial class App",
                    StringComparison.Ordinal))
            .SourceText
            .ToString();
        Assert.Contains(
            "FuncDataTemplate<",
            appSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "(url, __nameScope) =>",
            appSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Generator_AssignsExplicitDataTemplateAndDefersItsContent()
    {
        const string component =
            """
            using System.Collections.Immutable;
            using Avalonia.Controls;
            using Avalonia.Data;
            using Avalonia.Markup.Xaml.Templates;

            state ImmutableArray<(string Name, int Age)> persons =
            [
                ("Allice", 18),
                ("Bob", 19)
            ];

            <ItemsControl ItemsSource={persons}>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel>
                            <TextBlock Text=${Binding Name, StringFormat="Name {0}"}/>
                            <TextBlock Text=${Binding Age, StringFormat="Age {0}"}/>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        const string csharp =
            """
            public partial class DataTemplates
            {
                public DataTemplates()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaExplicitDataTemplateTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(csharp, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "DataTemplates.akbura");
        var componentTree =
            Akbura.Language.ComponentSyntaxTree.ParseText(
                SourceText.From(component),
                sourcePath);
        var semanticModel = new Akbura.Language.AkburaCompilation(
            compilation,
            [componentTree]).GetSemanticModel(componentTree);
        Assert.NotNull(
            semanticModel.GetDeclaredSymbol(
                componentTree.GetRoot()));

        var elements = componentTree.GetRoot().DescendantNodes()
            .OfType<Akbura.Language.Syntax.MarkupElementSyntax>()
            .ToArray();
        var itemTemplateElement = Assert.Single(
            elements,
            static element =>
                element.StartTag?.Name.ToFullString().Trim() ==
                "ItemsControl.ItemTemplate");
        var dataTemplateElement = Assert.Single(
            elements,
            static element =>
                element.StartTag?.Name.ToFullString().Trim() ==
                "DataTemplate");
        Assert.NotNull(
            semanticModel.GetSymbolInfo(dataTemplateElement).Symbol);
        var itemTemplateOperation =
            Assert.IsAssignableFrom<IMarkupContentOperation>(
                semanticModel.GetOperation(itemTemplateElement));
        Assert.False(itemTemplateOperation.HasErrors);
        Assert.Equal(
            "global::Avalonia.Controls.Templates.IDataTemplate",
            itemTemplateOperation.ContentModel.AllowedChildType
                .ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat));
        var dataTemplateOperation =
            Assert.IsAssignableFrom<IMarkupContentOperation>(
                semanticModel.GetOperation(dataTemplateElement));
        Assert.False(dataTemplateOperation.HasErrors);
        Assert.True(dataTemplateOperation.IsDeferred);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error);
        var generated = Assert.Single(
            Assert.Single(driver.GetRunResult().Results)
                .GeneratedSources);
        var text = generated.SourceText.ToString();
        Assert.Contains(
            "new global::Avalonia.Markup.Xaml.Templates.DataTemplate();",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            ".DataType = typeof(",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "CreateDeferredContent<global::Avalonia.Controls.Control>",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "ItemsControl.ItemTemplateProperty",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "stringFormat: \"Name {0}\"",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "stringFormat: \"\\\"Name {0}\\\"\"",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FuncDataTemplate<",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult =
            updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(
                Environment.NewLine,
                emitResult.Diagnostics));
        var assembly =
            Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var componentType =
                    assembly.GetType("DataTemplates");
                Assert.NotNull(componentType);
                var component =
                    Assert.IsAssignableFrom<AkburaControl>(
                        Activator.CreateInstance(componentType));
                var window = new Window
                {
                    Content = component,
                };
                window.Show();

                var itemsControl =
                    Assert.IsType<ItemsControl>(component.Child);
                Assert.NotNull(itemsControl.ItemTemplate);
                var templateType =
                    itemsControl.ItemTemplate.GetType();
                Assert.Equal(
                    "Avalonia.Markup.Xaml.Templates.DataTemplate",
                    templateType.FullName);
                Assert.Equal(
                    typeof((string, int)),
                    templateType.GetProperty("DataType")!
                        .GetValue(itemsControl.ItemTemplate));
                var templateContent =
                    Assert.IsType<StackPanel>(
                        itemsControl.ItemTemplate.Build(
                            ("Allice", 18)));
                templateContent.DataContext = ("Allice", 18);
                Assert.Equal(2, templateContent.Children.Count);
                Assert.Equal(
                    "Name Allice",
                    Assert.IsType<TextBlock>(
                        templateContent.Children[0]).Text);
                Assert.Equal(
                    "Age 18",
                    Assert.IsType<TextBlock>(
                        templateContent.Children[1]).Text);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public void Generator_ReportsMissingUsingForDataTemplate()
    {
        const string component =
            """
            using Avalonia.Controls;

            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <TextBlock />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaMissingDataTemplateUsingTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "MissingDataTemplateUsing.akbura");
        var componentTree =
            Akbura.Language.ComponentSyntaxTree.ParseText(
                SourceText.From(component),
                sourcePath);
        var semanticModel = new Akbura.Language.AkburaCompilation(
            compilation,
            [componentTree]).GetSemanticModel(componentTree);
        var dataTemplateElement = componentTree.GetRoot()
            .DescendantNodes()
            .OfType<Akbura.Language.Syntax.MarkupElementSyntax>()
            .Single(
                static element =>
                    element.StartTag?.Name.ToFullString().Trim() ==
                    "DataTemplate");

        var symbolInfo =
            semanticModel.GetSymbolInfo(dataTemplateElement);
        var semanticDiagnostic = Assert.Single(
            semanticModel.GetSemanticDiagnostics(dataTemplateElement),
            static diagnostic =>
                diagnostic.Code ==
                ErrorCodes.AKBURA_SEMANTIC_MarkupComponentNotFound);
        Assert.Null(symbolInfo.Symbol);
        Assert.Equal(
            Akbura.Language.Symbols.CandidateReason.NotFound,
            symbolInfo.CandidateReason);
        Assert.Contains(
            "DataTemplate",
            semanticDiagnostic.Message,
            StringComparison.Ordinal);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var generatorDiagnostics);

        var generatorDiagnostic = Assert.Single(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Id ==
                ErrorCodes.AKBURA_SEMANTIC_MarkupComponentNotFound);
        Assert.Equal(
            DiagnosticSeverity.Error,
            generatorDiagnostic.Severity);
        Assert.Contains(
            "DataTemplate",
            generatorDiagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ReportsUnknownDeclaredTypeAndMissingContentPresenterUsing()
    {
        const string component =
            """
            using Avalonia.Controls;

            param MissingType Content;
            state MissingStateType? content = null;

            <ContentPresenter Content={Content} />
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaInvalidRouterDiagnosticsTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "InvalidRouter.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.Contains(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Id ==
                    ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError &&
                diagnostic.GetMessage().Contains(
                    "MissingType",
                    StringComparison.Ordinal));
        Assert.Contains(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Id ==
                    ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError &&
                diagnostic.GetMessage().Contains(
                    "MissingStateType",
                    StringComparison.Ordinal));
        Assert.Contains(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Id ==
                    ErrorCodes.AKBURA_SEMANTIC_MarkupComponentNotFound &&
                diagnostic.GetMessage().Contains(
                    "ContentPresenter",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Id == "CS0246");
    }

    [Fact]
    public void ModuleManifestBuilder_RecoversUntilSemanticDiagnosticsAreReported()
    {
        const string component =
            """
            using Avalonia.Controls;

            param MissingType Content;

            <Control />
            """;
        var compilation = CSharpCompilation.Create(
            "AkburaInvalidManifestRecoveryTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));

        var manifest = Akbura.Language.AkburaModuleManifestBuilder.Build(
            "AkburaInvalidManifestRecoveryTests",
            string.Empty,
            [
                new Akbura.Language.AkburaModuleSourceText(
                    "InvalidManifest.akbura",
                    SourceText.From(component)),
            ],
            compilation);

        var source = Assert.Single(manifest.Sources);
        var declaration = Assert.Single(source.Declarations);
        Assert.NotNull(declaration.Component);
        var parameter = Assert.Single(
            declaration.Component.Parameters);
        Assert.Equal(
            "global::System.Object",
            parameter.TypeName);
    }

    [Fact]
    public void Generator_EmitsExecutableTopLevelStatementsInsideUpdate()
    {
        const string component =
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Avalonia.Controls;
            using Avalonia.Controls.Presenters;
            using Avalonia.Controls.Templates;

            param Control? Content = null;
            param IList<Page> Pages;
            param bind string Url = "";
            param IDataTemplate NotFound;

            state Control? content = null;

            void ClearContent()
            {
                content = null;
            }

            var page = Pages.FirstOrDefault(page =>
                string.Equals(
                    page.Uri,
                    Url,
                    StringComparison.OrdinalIgnoreCase));

            if (content == null)
            {
                content = NotFound.Build(page);
            }

            Content = content;

            <ContentPresenter Content={Content} />
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var pageSyntaxTree = CSharpSyntaxTree.ParseText(
            """
            public sealed class Page
            {
                public string Uri { get; } = string.Empty;
            }
            """,
            parseOptions);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedRouterTests",
            syntaxTrees:
            [
                pageSyntaxTree,
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "Router.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        var generated = Assert.Single(result.GeneratedSources);
        var generatedTree = CSharpSyntaxTree.ParseText(
            generated.SourceText,
            parseOptions);
        var generatedRoot = generatedTree.GetCompilationUnitRoot();
        var generatedClass = Assert.Single(
            generatedRoot.DescendantNodes()
                .OfType<CSharp.ClassDeclarationSyntax>());
        Assert.Contains(
            generatedClass.Members.OfType<CSharp.MethodDeclarationSyntax>(),
            static method =>
                method.Identifier.ValueText == "ClearContent");

        var update = Assert.Single(
            generatedClass.Members
                .OfType<CSharp.MethodDeclarationSyntax>(),
            static method =>
                method.Identifier.ValueText == "Update");
        Assert.NotNull(update.Body);
        Assert.IsType<CSharp.LocalDeclarationStatementSyntax>(
            update.Body.Statements[0]);
        Assert.IsType<CSharp.IfStatementSyntax>(
            update.Body.Statements[1]);
        var assignment = Assert.IsType<CSharp.ExpressionStatementSyntax>(
            update.Body.Statements[2]);
        Assert.Equal(
            "Content = content",
            assignment.Expression.ToString());
        Assert.DoesNotContain(
            generatedTree.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_InitializesComponentParameterFromPrecedingRawStringLocal()
    {
        const string child =
            """
            using Avalonia.Controls;

            param string Code;

            <TextBlock Text={Code}/>
            """;
        const string host =
            "using Avalonia.Controls;\n" +
            "\n" +
            "string counterCode =\n" +
            "\"\"\"\n" +
            "<Button Content=\"Increment\"/>\n" +
            "\"\"\";\n" +
            "\n" +
            "<CodeView Code={counterCode}/>";
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedRawStringLocalTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    Path.Combine(Environment.CurrentDirectory, "CodeView.akbura"),
                    SourceText.From(child)),
                new TestAdditionalText(
                    Path.Combine(Environment.CurrentDirectory, "RawStringHost.akbura"),
                    SourceText.From(host)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSources = Assert.Single(driver.GetRunResult().Results)
            .GeneratedSources;
        var hostSource = Assert.Single(
            generatedSources,
            static generated =>
                generated.HintName.Contains(
                    "RawStringHost",
                    StringComparison.Ordinal));
        var generatedRoot = hostSource.SyntaxTree.GetCompilationUnitRoot();
        var generatedClass = Assert.Single(
            generatedRoot.DescendantNodes()
                .OfType<CSharp.ClassDeclarationSyntax>());
        var firstUpdate = Assert.Single(
            generatedClass.Members
                .OfType<CSharp.MethodDeclarationSyntax>(),
            static method =>
                method.Identifier.ValueText == "FirstUpdate");
        var update = Assert.Single(
            generatedClass.Members
                .OfType<CSharp.MethodDeclarationSyntax>(),
            static method =>
                method.Identifier.ValueText == "Update");

        Assert.Contains(
            firstUpdate.Body!.Statements,
            static statement =>
                statement is CSharp.LocalDeclarationStatementSyntax declaration &&
                declaration.Declaration.Variables.Any(
                    static variable =>
                        variable.Identifier.ValueText == "counterCode"));
        Assert.Contains(
            update.Body!.Statements,
            static statement =>
                statement is CSharp.LocalDeclarationStatementSyntax declaration &&
                declaration.Declaration.Variables.Any(
                    static variable =>
                        variable.Identifier.ValueText == "counterCode"));
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Generator_EmitsAndAppliesInlineAkcssClassesAndUtilities()
    {
        const string component =
            "using Avalonia.Controls;\n" +
            "\n" +
            "param double WidthValue = 40;\n" +
            "param bool ApplyWidth = true;\n" +
            "\n" +
            "@akcss {\n" +
            "    @using Avalonia.Controls;\n" +
            "\n" +
            "    .primary {\n" +
            "        Height: 25;\n" +
            "    }\n" +
            "\n" +
            "    @utilities {\n" +
            "        Control.w-(double width) {\n" +
            "            Width: width;\n" +
            "        }\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "<Border class=\"primary\" {ApplyWidth}:w-{WidthValue} />\n";
        const string csharp =
            "public partial class StyledView\n" +
            "{\n" +
            "    public StyledView() : base(global::Akbura.Engine.AkburaEngine.Empty) { }\n" +
            "}\n";
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedInlineAkcssTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(csharp, parseOptions)],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(Environment.CurrentDirectory, "StyledView.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts: [new TestAdditionalText(sourcePath, SourceText.From(component))],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSources = Assert.Single(driver.GetRunResult().Results).GeneratedSources;
        Assert.Equal(2, generatedSources.Length);
        var generatedComponent = Assert.Single(
            generatedSources,
            static source => source.HintName.StartsWith("Akbura.Component.", StringComparison.Ordinal));
        var componentText = generatedComponent.SourceText.ToString();
        Assert.Contains("AkcssClassActivator", componentText, StringComparison.Ordinal);
        Assert.Contains(
            "AkcssUtilityCandidateActivator",
            componentText,
            StringComparison.Ordinal);
        Assert.Contains("ExecuteAkcssStyles", componentText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var componentType = assembly.GetType("StyledView");
                Assert.NotNull(componentType);
                var component = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(componentType));
                var window = new Window { Content = component };
                window.Show();

                var border = Assert.IsType<Border>(component.Child);
                Assert.Equal(25d, border.Height);
                Assert.Equal(40d, border.Width);

                componentType.GetProperty("ApplyWidth")!.SetValue(component, false);
                Assert.True(double.IsNaN(border.Width));
                componentType.GetProperty("WidthValue")!.SetValue(component, 72d);
                Assert.True(double.IsNaN(border.Width));
                componentType.GetProperty("ApplyWidth")!.SetValue(component, true);
                Assert.Equal(72d, border.Width);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_ResolvesUtilityConflictsPerPropertyOperation()
    {
        const string component =
            "using Avalonia.Controls;\n" +
            "using Demo;\n" +
            "\n" +
            "@akcss {\n" +
            "    @using Akbura;\n" +
            "    @using Avalonia;\n" +
            "    @using Avalonia.Controls;\n" +
            "    @using Avalonia.Media;\n" +
            "    @using Demo;\n" +
            "\n" +
            "    @utilities {\n" +
            "        ProbeControl.my-w-(double value) {\n" +
            "            Width: ProbeLog.Value(\"first-width\", Amx.DynamicResource<double>(\"--spacing\") * value);\n" +
            "            Background: ProbeLog.Brush(\"first-background\", Brushes.Red);\n" +
            "            Padding: new Thickness(ProbeLog.Value(\"first-padding\", value * 5));\n" +
            "        }\n" +
            "\n" +
            "        ProbeControl.square-(double value) {\n" +
            "            Width: ProbeLog.Value(\"second-width\", value);\n" +
            "            @if(IsEnabled == false) {\n" +
            "                Width: ProbeLog.Value(\"disabled-width\", value * 2);\n" +
            "            }\n" +
            "            Height: ProbeLog.Value(\"second-height\", value);\n" +
            "        }\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "<ProbeControl my-w-10 square-15 />\n";
        const string csharp =
            "using Avalonia.Controls;\n" +
            "using Avalonia.Media;\n" +
            "using System.Collections.Generic;\n" +
            "\n" +
            "namespace Demo\n" +
            "{\n" +
            "    public sealed class ProbeControl : Border { }\n" +
            "\n" +
            "    public static class ProbeLog\n" +
            "    {\n" +
            "        public static List<string> Events { get; } = new();\n" +
            "        public static double Value(string name, double value)\n" +
            "        {\n" +
            "            Events.Add(name);\n" +
            "            return value;\n" +
            "        }\n" +
            "\n" +
            "        public static IBrush Brush(string name, IBrush value)\n" +
            "        {\n" +
            "            Events.Add(name);\n" +
            "            return value;\n" +
            "        }\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "public partial class OperationPriorityView\n" +
            "{\n" +
            "    public OperationPriorityView()\n" +
            "        : base(global::Akbura.Engine.AkburaEngine.Empty) { }\n" +
            "}\n";
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedOperationPriorityTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(csharp, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "OperationPriorityView.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSources = Assert.Single(
            driver.GetRunResult().Results).GeneratedSources;
        var generatedAkcss = Assert.Single(
            generatedSources,
            static source =>
                source.HintName.StartsWith(
                    "Akbura.Akcss.",
                    StringComparison.Ordinal));
        Assert.Contains(
            "AkcssUtilityOperation",
            generatedAkcss.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(
                Environment.NewLine,
                emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var componentType = assembly.GetType(
                    "OperationPriorityView");
                Assert.NotNull(componentType);
                var component = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(componentType));
                var window = new Window
                {
                    Content = component,
                };

                window.Show();

                var control = Assert.IsAssignableFrom<Border>(
                    component.Child);
                Assert.Equal(15d, control.Width);
                Assert.Equal(15d, control.Height);
                Assert.Equal(new Thickness(50d), control.Padding);
                Assert.Same(
                    Avalonia.Media.Brushes.Red,
                    control.Background);

                var logType = assembly.GetType("Demo.ProbeLog");
                Assert.NotNull(logType);
                var events = Assert.IsAssignableFrom<IEnumerable<string>>(
                    logType.GetProperty("Events")!.GetValue(null));
                Assert.DoesNotContain("first-width", events);
                Assert.Contains("first-background", events);
                Assert.Contains("first-padding", events);
                Assert.Contains("second-width", events);
                Assert.Contains("second-height", events);

                control.Resources["--spacing"] = 2d;
                Assert.Equal(15d, control.Width);
                Assert.DoesNotContain("first-width", events);

                control.IsEnabled = false;
                Assert.Equal(30d, control.Width);
                Assert.Contains("disabled-width", events);

                control.IsEnabled = true;
                Assert.Equal(15d, control.Width);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_AkcssUtilityMarkupExtension_RecreatesDynamicValueOnUpdate()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            param double Spacing = 4;

            @akcss {
                @using Avalonia.Controls;

                @utilities {
                    Control.p-(double value) {
                        Width: value;
                    }
                }
            }

            <Border p-${GalleryPadding {Spacing + 1}} />
            """;
        const string csharp =
            """
            namespace Demo.Extensions
            {
                public sealed class GalleryPaddingExtension
                {
                    private readonly double _value;

                    public GalleryPaddingExtension(double value)
                    {
                        _value = value;
                        CreationCount++;
                    }

                    public static int CreationCount { get; private set; }

                    public double ProvideValue(
                        System.IServiceProvider services)
                        => _value;
                }
            }

            public partial class MarkupUtilityView
            {
                public MarkupUtilityView()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedUtilityMarkupExtensionTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(csharp, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "MarkupUtilityView.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var result = Assert.Single(driver.GetRunResult().Results);
        var generated = Assert.Single(
            result.GeneratedSources,
            static source =>
                source.HintName.StartsWith(
                    "Akbura.Component.",
                    StringComparison.Ordinal));
        var generatedText = generated.SourceText.ToString();

        Assert.Contains(
            "AkcssUtilityCandidateActivator",
            generatedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "AkcssUtilityValueSource.Create<double>",
            generatedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "RegisterAttached<MarkupUtilityView",
            generatedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "__CreateAkcssMarkupExtension_",
            generatedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "#line (",
            generatedText,
            StringComparison.Ordinal);
        Assert.Contains(
            sourcePath,
            generatedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(
                Environment.NewLine,
                emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var componentType =
                    assembly.GetType("MarkupUtilityView");
                Assert.NotNull(componentType);
                var component =
                    Assert.IsAssignableFrom<AkburaControl>(
                        Activator.CreateInstance(componentType));
                var window = new Window
                {
                    Content = component,
                };

                window.Show();

                var border =
                    Assert.IsType<Border>(component.Child);
                var extensionType = assembly.GetType(
                    "Demo.Extensions.GalleryPaddingExtension");
                Assert.NotNull(extensionType);
                var creationCount =
                    extensionType.GetProperty("CreationCount");
                Assert.NotNull(creationCount);

                Assert.Equal(5d, border.Width);
                var beforeUpdate =
                    Assert.IsType<int>(
                        creationCount.GetValue(null));

                componentType
                    .GetProperty("Spacing")!
                    .SetValue(component, 10d);

                Assert.Equal(11d, border.Width);
                Assert.True(
                    Assert.IsType<int>(
                        creationCount.GetValue(null)) >
                    beforeUpdate);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_AkcssUtilityMarkupExtensions_SupportReactiveValuesBindingsAndLifecycle()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            @akcss {
                @using Avalonia.Controls;

                @utilities {
                    Control.typed-(double value) {
                        Width: value;
                    }

                    Control.object-(double value) {
                        Height: value;
                    }

                    Control.bound-(double value) {
                        MinWidth: value;
                    }

                    Control.variant-(double value) {
                        MaxWidth: value;
                    }
                }
            }

            <Border
                typed-${TypedSignal}
                object-${ObjectSignal}
                bound-${BoundValue}
                variant-1
                ${ReactiveVariant}:variant-2 />
            """;
        const string csharp =
            """
            using Akbura.Markup;
            using Avalonia;
            using Avalonia.Data;
            using System;
            using System.Collections.Generic;

            namespace Demo.Extensions
            {
                public sealed class TestSignal<T> : IObservable<T>
                {
                    private readonly List<IObserver<T>> _observers = new();
                    private T _value;

                    public TestSignal(T value)
                    {
                        _value = value;
                    }

                    public int SubscriberCount => _observers.Count;

                    public IDisposable Subscribe(IObserver<T> observer)
                    {
                        _observers.Add(observer);
                        observer.OnNext(_value);
                        return new Subscription(_observers, observer);
                    }

                    public void Emit(T value)
                    {
                        _value = value;
                        foreach (var observer in _observers.ToArray())
                        {
                            observer.OnNext(value);
                        }
                    }

                    private sealed class Subscription : IDisposable
                    {
                        private readonly List<IObserver<T>> _observers;
                        private IObserver<T>? _observer;

                        public Subscription(
                            List<IObserver<T>> observers,
                            IObserver<T> observer)
                        {
                            _observers = observers;
                            _observer = observer;
                        }

                        public void Dispose()
                        {
                            if (_observer is { } observer)
                            {
                                _observers.Remove(observer);
                                _observer = null;
                            }
                        }
                    }
                }

                public static class TestSources
                {
                    public static TestSignal<double> Typed { get; } = new(12d);

                    public static TestSignal<object> Object { get; } = new(25d);

                    public static TestSignal<bool> Variant { get; } = new(false);
                }

                public sealed class TypedSignalExtension
                {
                    public IObservable<double> ProvideValue(IServiceProvider services)
                        => TestSources.Typed;
                }

                public sealed class ObjectSignalExtension
                {
                    public IObservable<object> ProvideValue(IServiceProvider services)
                        => TestSources.Object;
                }

                public sealed class BoundValueExtension
                {
                    public BindingBase ProvideValue(IServiceProvider services)
                        => new Binding(nameof(BindingModel.Value));
                }

                [UtilityVariant(
                    10d,
                    ConflictGroup = "Tests",
                    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
                public sealed class ReactiveVariantExtension
                {
                    public IObservable<bool> ProvideValue(IServiceProvider services)
                        => TestSources.Variant;
                }

                public sealed class BindingModel : AvaloniaObject
                {
                    public static readonly StyledProperty<double> ValueProperty =
                        AvaloniaProperty.Register<BindingModel, double>(
                            nameof(Value));

                    public double Value
                    {
                        get => GetValue(ValueProperty);
                        set => SetValue(ValueProperty, value);
                    }
                }
            }

            public partial class MarkupUtilitySourcesView
            {
                public MarkupUtilitySourcesView()
                    : base(global::Akbura.Engine.AkburaEngine.Empty)
                {
                }
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedUtilityMarkupExtensionSourceTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(csharp, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "MarkupUtilitySourcesView.akbura");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var result = Assert.Single(driver.GetRunResult().Results);
        var generated = Assert.Single(
            result.GeneratedSources,
            static source =>
                source.HintName.StartsWith(
                    "Akbura.Component.",
                    StringComparison.Ordinal));
        var generatedText = generated.SourceText.ToString();

        Assert.Contains(
            "AkcssUtilityValueSource.CreateObservable<double, double>",
            generatedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "AkcssUtilityValueSource.CreateObservableObject<double>",
            generatedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "AkcssUtilityValueSource.CreateBinding<double>",
            generatedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "variant:",
            generatedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(
                Environment.NewLine,
                emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var componentType =
                    assembly.GetType("MarkupUtilitySourcesView");
                var modelType =
                    assembly.GetType("Demo.Extensions.BindingModel");
                var sourcesType =
                    assembly.GetType("Demo.Extensions.TestSources");
                Assert.NotNull(componentType);
                Assert.NotNull(modelType);
                Assert.NotNull(sourcesType);

                var component =
                    Assert.IsAssignableFrom<AkburaControl>(
                        Activator.CreateInstance(componentType));
                var model = Assert.IsAssignableFrom<AvaloniaObject>(
                    Activator.CreateInstance(modelType));
                modelType.GetProperty("Value")!.SetValue(model, 73d);
                component.DataContext = model;

                var window = new Window
                {
                    Content = component,
                };
                window.Show();

                var border =
                    Assert.IsType<Border>(component.Child);
                var typed = sourcesType
                    .GetProperty("Typed")!
                    .GetValue(null)!;
                var objectSource = sourcesType
                    .GetProperty("Object")!
                    .GetValue(null)!;
                var variant = sourcesType
                    .GetProperty("Variant")!
                    .GetValue(null)!;

                Assert.Equal(12d, border.Width);
                Assert.Equal(25d, border.Height);
                Assert.Equal(73d, border.MinWidth);
                Assert.Equal(1d, border.MaxWidth);
                Assert.Equal(
                    1,
                    typed.GetType()
                        .GetProperty("SubscriberCount")!
                        .GetValue(typed));
                Assert.Equal(
                    1,
                    objectSource.GetType()
                        .GetProperty("SubscriberCount")!
                        .GetValue(objectSource));
                Assert.Equal(
                    1,
                    variant.GetType()
                        .GetProperty("SubscriberCount")!
                        .GetValue(variant));

                typed.GetType()
                    .GetMethod("Emit")!
                    .Invoke(typed, [24d]);
                objectSource.GetType()
                    .GetMethod("Emit")!
                    .Invoke(objectSource, [44d]);
                variant.GetType()
                    .GetMethod("Emit")!
                    .Invoke(variant, [true]);
                modelType.GetProperty("Value")!.SetValue(model, 91d);

                Assert.Equal(24d, border.Width);
                Assert.Equal(44d, border.Height);
                Assert.Equal(91d, border.MinWidth);
                Assert.Equal(2d, border.MaxWidth);

                window.Content = null;
                Assert.Equal(
                    0,
                    typed.GetType()
                        .GetProperty("SubscriberCount")!
                        .GetValue(typed));
                Assert.Equal(
                    0,
                    objectSource.GetType()
                        .GetProperty("SubscriberCount")!
                        .GetValue(objectSource));
                Assert.Equal(
                    0,
                    variant.GetType()
                        .GetProperty("SubscriberCount")!
                        .GetValue(variant));

                typed.GetType()
                    .GetMethod("Emit")!
                    .Invoke(typed, [31d]);
                objectSource.GetType()
                    .GetMethod("Emit")!
                    .Invoke(objectSource, [45d]);
                variant.GetType()
                    .GetMethod("Emit")!
                    .Invoke(variant, [false]);

                window.Content = component;

                Assert.Equal(31d, border.Width);
                Assert.Equal(45d, border.Height);
                Assert.Equal(91d, border.MinWidth);
                Assert.Equal(1d, border.MaxWidth);
                Assert.Equal(
                    1,
                    typed.GetType()
                        .GetProperty("SubscriberCount")!
                        .GetValue(typed));
                Assert.Equal(
                    1,
                    objectSource.GetType()
                        .GetProperty("SubscriberCount")!
                        .GetValue(objectSource));
                Assert.Equal(
                    1,
                    variant.GetType()
                        .GetProperty("SubscriberCount")!
                        .GetValue(variant));

                window.Close();
                Assert.Equal(
                    0,
                    typed.GetType()
                        .GetProperty("SubscriberCount")!
                        .GetValue(typed));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_AkcssApplyExpandsImportedParameterizedUtilities()
    {
        const string component =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Border card />\n";
        const string componentAkcss =
            "@using Avalonia.Controls;\n" +
            "@using Styles.akcss;\n" +
            "\n" +
            "@utilities {\n" +
            "    Border.card {\n" +
            "        @apply p-4 bg-red-500 rounded-xl;\n" +
            "    }\n" +
            "}\n";
        const string sharedAkcss =
            "@using Avalonia;\n" +
            "@using Avalonia.Controls;\n" +
            "@using Avalonia.Controls.Primitives;\n" +
            "\n" +
            "@utilities {\n" +
            "    Decorator.p-(double value) { Padding: new Thickness(value); }\n" +
            "    TemplatedControl.p-(double value) { Padding: new Thickness(value * 2); }\n" +
            "    Border.bg-(string color)-(int shade) { Opacity: shade / 1000d; }\n" +
            "    Border.rounded-xl { CornerRadius: new CornerRadius(12); }\n" +
            "}\n";
        const string csharp =
            "public partial class CardView\n" +
            "{\n" +
            "    public CardView() : base(global::Akbura.Engine.AkburaEngine.Empty) { }\n" +
            "}\n";

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedApplyTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(csharp, parseOptions)],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var projectDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "AkcssApplyProject");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts:
            [
                new TestAdditionalText(
                    Path.Combine(projectDirectory, "CardView.akbura"),
                    SourceText.From(component)),
                new TestAdditionalText(
                    Path.Combine(projectDirectory, "CardView.akcss"),
                    SourceText.From(componentAkcss)),
                new TestAdditionalText(
                    Path.Combine(projectDirectory, "Styles.akcss"),
                    SourceText.From(sharedAkcss)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSources = Assert.Single(
            driver.GetRunResult().Results).GeneratedSources;
        var generatedCardStyles = Assert.Single(
            generatedSources,
            static source => source.HintName.Contains(
                "CardView.akcss",
                StringComparison.Ordinal));
        var generatedText = generatedCardStyles.SourceText.ToString();
        Assert.Contains("double value = 4;", generatedText, StringComparison.Ordinal);
        Assert.Contains("string color = \"red\";", generatedText, StringComparison.Ordinal);
        Assert.Contains("int shade = 500;", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "invalid AKCSS operation",
            generatedText,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "could not be emitted",
            generatedText,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var componentType = assembly.GetType("CardView");
                Assert.NotNull(componentType);
                var componentControl = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(componentType));
                var window = new Window
                {
                    Content = componentControl,
                };

                window.Show();

                var border = Assert.IsType<Border>(componentControl.Child);
                Assert.Equal(new Thickness(4), border.Padding);
                Assert.Equal(0.5d, border.Opacity);
                Assert.Equal(new CornerRadius(12), border.CornerRadius);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_EmitsExternalAkcssWithoutComponentTree()
    {
        const string akcss =
            "@using Akbura;\n" +
            "@using Avalonia.Controls;\n" +
            "\n" +
            "@utilities {\n" +
            "    Control.w-(double width) {\n" +
            "        Width: width < 100\n" +
            "            ? width * Amx.DynamicResource<double>(\"--spacing\")\n" +
            "            : 100;\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "Button.primary {\n" +
            "    Width: 120;\n" +
            "    Grid.Column: 2;\n" +
            "}\n";
        var compilation = CSharpCompilation.Create(
            "AkburaCsGeneratorTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(Environment.CurrentDirectory, "Styles.akcss");
        var additionalText = new TestAdditionalText(sourcePath, SourceText.From(akcss));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts: [additionalText],
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatorResult = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(generatorResult.Exception);
        var generatedSource = Assert.Single(generatorResult.GeneratedSources);
        Assert.Equal(
            "Akbura.Akcss.Styles.akcss.6f172a6a.g.cs",
            generatedSource.HintName);

        var text = generatedSource.SourceText.ToString();
        Assert.Contains("AkcssUtility<double>", text, StringComparison.Ordinal);
        Assert.Contains("ResourceNodeExtensions.GetResourceObservable", text, StringComparison.Ordinal);
        Assert.Contains("converter: __resourceValue =>", text, StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.Amx.DynamicResource<global::System.Double>",
            text,
            StringComparison.Ordinal);
        Assert.Contains("global::Avalonia.Layout.Layoutable.WidthProperty", text, StringComparison.Ordinal);
        Assert.Contains("global::Avalonia.Controls.Grid.SetColumn", text, StringComparison.Ordinal);
        Assert.Contains("ClearValue", text, StringComparison.Ordinal);
        var guardIndex = text.IndexOf("if (__target is", StringComparison.Ordinal);
        var lineDirectiveIndex = AssertEnhancedLineDirective(
            text,
            "(6,16)-(8,18)",
            sourcePath);
        var bindingIndex = text.IndexOf("TrackSubscription(__target", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0);
        Assert.True(bindingIndex >= 0);
        var lineDefaultIndex = text.IndexOf("#line default", bindingIndex, StringComparison.Ordinal);
        Assert.True(guardIndex < lineDirectiveIndex);
        Assert.True(lineDirectiveIndex < bindingIndex);
        Assert.True(bindingIndex < lineDefaultIndex);

        var mappedStatementTokens = generatedSource.SyntaxTree.GetRoot()
            .DescendantTokens()
            .Where(token => token.SpanStart >= bindingIndex &&
                            token.Span.End <= lineDefaultIndex)
            .ToArray();
        AssertMappedLocation(
            mappedStatementTokens.First(static token => token.ValueText == "width"),
            sourcePath,
            new LinePosition(5, 15),
            new LinePosition(5, 20));
        AssertMappedLocation(
            mappedStatementTokens.Last(static token => token.ValueText == "100"),
            sourcePath,
            new LinePosition(7, 14),
            new LinePosition(7, 17));
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var moduleType = assembly.GetType(
            "Akbura.Generated.__AkburaAkcssModule_6f172a6a");
        Assert.NotNull(moduleType);
        AssertGeneratedModuleContract(moduleType, "Styles.akcss");
        var utilityType = moduleType.GetNestedType(
            "Style_0",
            BindingFlags.NonPublic);
        Assert.NotNull(utilityType);
        var utility = Assert.IsAssignableFrom<AkcssUtility<double>>(
            Activator.CreateInstance(utilityType, nonPublic: true));

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var target = new Border();
                target.Resources["--spacing"] = 4d;

                utility.Update(target, 2d);

                Assert.Equal(8d, target.Width);
                target.Resources["--spacing"] = 6d;
                Assert.Equal(12d, target.Width);

                utility.Reset(target);
                Assert.True(double.IsNaN(target.Width));
                target.Resources["--spacing"] = 8d;
                Assert.True(double.IsNaN(target.Width));
            },
            CancellationToken.None);
    }

    [Fact]
    public void Generator_AkcssMetadataCarriersSurviveReferenceAssembly()
    {
        const string akcss =
            "@using System;\n" +
            "@using Avalonia;\n" +
            "@using Avalonia.Controls;\n" +
            "@using Akbura;\n" +
            "@using TestStyles;\n" +
            "\n" +
            "Border.surface { Opacity: 0.5; }\n" +
            "\n" +
            "Border.card {\n" +
            "    @apply surface;\n" +
            "    @if(IsEnabled) {\n" +
            "        Padding: new Thickness(Math.Pow(2, 3));\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "Button.special { @intercept InterceptStyle; }\n" +
            "\n" +
            "@utilities {\n" +
            "    Control.w-(double value) { Width: value * Amx.DynamicResource<double>(\"--spacing\"); }\n" +
            "}\n";
        const string libraryCSharp =
            "namespace TestStyles;\n" +
            "public sealed class InterceptStyle : global::Akbura.Akcss.AkcssClass\n" +
            "{\n" +
            "    public override void Update(object target) { }\n" +
            "}\n";
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var references = SymbolTests.CreateAvaloniaReferences();
        var libraryCompilation = CSharpCompilation.Create(
            "AkcssMetadataLibrary",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(libraryCSharp, parseOptions),
            ],
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "LibraryStyles.akcss");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts:
            [
                new TestAdditionalText(
                    sourcePath,
                    SourceText.From(akcss)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            libraryCompilation,
            out var generatedLibrary,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            generatedLibrary.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedSource = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources)
            .SourceText
            .ToString();
        Assert.Contains(
            "AkcssModuleReferenceAttribute",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static class __AkcssMetadata_0",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static class __AkcssMetadata_3",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "private sealed class Style_0",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains("global::System.Math.Pow(2, 3)", generatedSource, StringComparison.Ordinal);

        using var referenceAssemblyStream = new MemoryStream();
        var emitResult = generatedLibrary.Emit(
            referenceAssemblyStream,
            options: new Microsoft.CodeAnalysis.Emit.EmitOptions(
                metadataOnly: true,
                includePrivateMembers: false));
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));

        var libraryReference = MetadataReference.CreateFromImage(
            referenceAssemblyStream.ToArray());
        var consumerCSharpCompilation = CSharpCompilation.Create(
            "AkcssMetadataConsumer",
            references: references.Append(libraryReference),
            options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary)
                .WithMetadataImportOptions(MetadataImportOptions.Public));
        var referencedAssembly = Assert.IsAssignableFrom<IAssemblySymbol>(
            consumerCSharpCompilation.GetAssemblyOrModuleSymbol(
                libraryReference));
        var moduleReferenceAttribute = Assert.Single(
            referencedAssembly.GetAttributes(),
            static attribute =>
                attribute.AttributeClass?.ToDisplayString() ==
                "Akbura.CompilerAnotations.AkcssModuleReferenceAttribute");
        var moduleType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            Assert.Single(moduleReferenceAttribute.ConstructorArguments).Value);
        var runtimeStyle = Assert.Single(moduleType.GetTypeMembers("Style_0"));
        Assert.Equal(Accessibility.Private, runtimeStyle.DeclaredAccessibility);

        var carriers = moduleType.GetTypeMembers()
            .Where(static type =>
                type.Name.StartsWith(
                    "__AkcssMetadata_",
                    StringComparison.Ordinal))
            .OrderBy(static type => type.Name)
            .ToArray();
        Assert.Equal(4, carriers.Length);
        Assert.All(
            carriers,
            static carrier =>
                Assert.Equal(
                    Accessibility.Public,
                    carrier.DeclaredAccessibility));
        Assert.All(
            carriers,
            static carrier =>
                Assert.Contains(
                    carrier.GetAttributes(),
                    attribute =>
                        attribute.AttributeClass?.ToDisplayString() ==
                        "Akbura.CompilerAnotations.AkcssSymbolAttribute"));
        var utilityCarrier = Assert.Single(
            carriers,
            static carrier =>
                carrier.GetAttributes().Any(
                    attribute =>
                        attribute.AttributeClass?.ToDisplayString() ==
                        "Akbura.CompilerAnotations.AkcssUtilityParameterAttribute"));
        Assert.Contains(
            utilityCarrier.GetAttributes(),
            static attribute =>
                attribute.AttributeClass?.ToDisplayString() ==
                "Akbura.CompilerAnotations.AkcssOperationAttribute");

        const string component =
            "using Avalonia.Controls;\n" +
            "using LibraryStyles.akcss;\n" +
            "\n" +
            "<Border class=\"card\" w-4 />\n";
        var componentTree = ComponentSyntaxTree.ParseText(
            component,
            "Consumer.akbura");
        var akburaCompilation = new AkburaCompilation(
            consumerCSharpCompilation,
            [componentTree]);
        var semanticModel = akburaCompilation.GetSemanticModel(componentTree);
        var root = Assert.IsType<Akbura.Language.Syntax.MarkupRootSyntax>(
            Assert.Single(
                componentTree.GetRoot().Members,
                static member => member is Akbura.Language.Syntax.MarkupRootSyntax));
        var attributes = root.Element.StartTag!.Attributes;

        var classOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attributes[0]));
        var style = Assert.Single(classOperation.AppliedAkcssSymbols);
        var metadataStyle = Assert.IsAssignableFrom<IMetadataAkcssSymbol>(style);
        Assert.Equal(1, metadataStyle.RuntimeStyleIndex);
        Assert.Contains("IsEnabled", metadataStyle.ObservedProperties);
        Assert.Collection(
            style.Operations,
            operation =>
            {
                var apply = Assert.IsAssignableFrom<IAkcssApplyOperation>(operation);
                var metadataApply = Assert.IsAssignableFrom<IMetadataAkcssApplyOperation>(apply);
                Assert.Equal("surface", Assert.Single(apply.Items));
                Assert.Equal("Border.surface", Assert.Single(apply.AppliedSymbols).MetadataName);
                var expandedSetter = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
                    Assert.Single(metadataApply.ExpandedOperations));
                Assert.Equal("Opacity", expandedSetter.Property!.Name);
                Assert.Null(expandedSetter.Syntax);
            },
            operation =>
            {
                var condition = Assert.IsAssignableFrom<IAkcssIfOperation>(operation);
                var metadataCondition = Assert.IsAssignableFrom<IMetadataAkcssOperation>(condition);
                Assert.Contains("__target", metadataCondition.Expression, StringComparison.Ordinal);
                Assert.Equal(
                    SpecialType.System_Boolean,
                    Assert.IsAssignableFrom<ITypeSymbol>(
                        condition.ConditionType.Symbol).SpecialType);
                var styleSetter = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
                    Assert.Single(condition.Operations));
                var metadataStyleSetter = Assert.IsAssignableFrom<IMetadataAkcssOperation>(
                    styleSetter);
                Assert.Null(styleSetter.Syntax);
                Assert.NotNull(styleSetter.Property);
                Assert.Equal("Padding", styleSetter.Property.Name);
                Assert.Equal(PropertyAccessKind.AvaloniaProperty, styleSetter.Property.WriteKind);
                Assert.True(styleSetter.ValueConversion.IsIdentity);
                Assert.Equal(
                    "new global::Avalonia.Thickness(global::System.Math.Pow(2, 3))",
                    metadataStyleSetter.Expression);
                Assert.EndsWith(
                    "LibraryStyles.akcss",
                    metadataStyleSetter.SourcePath,
                    StringComparison.OrdinalIgnoreCase);
                Assert.True(metadataStyleSetter.SourceSpan.Length > 0);
            });

        var utilityOperation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attributes[1]));
        var utility = Assert.Single(utilityOperation.Utilities);
        var metadataUtility = Assert.IsAssignableFrom<IMetadataAkcssSymbol>(utility);
        Assert.Equal(3, metadataUtility.RuntimeStyleIndex);
        var parameter = Assert.Single(utility.Parameters);
        Assert.Equal("value", parameter.Name);
        Assert.Equal(
            SpecialType.System_Double,
            Assert.IsAssignableFrom<ITypeSymbol>(parameter.Type.Symbol).SpecialType);
        var utilitySetter = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(utility.Operations));
        var metadataUtilitySetter = Assert.IsAssignableFrom<IMetadataAkcssOperation>(
            utilitySetter);
        Assert.Equal("Width", utilitySetter.Property!.Name);
        Assert.Null(utilitySetter.Syntax);
        Assert.Equal(
            "((global::System.Double)__arguments[0]) * global::Akbura.Amx.DynamicResource<global::System.Double>(\"--spacing\")",
            metadataUtilitySetter.Expression);
        Assert.False(metadataUtility.OperationAttributes.IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(root.Element).IsEmpty);

        var metadataModule = Assert.Single(
            akburaCompilation.GetAkcssModuleSymbolsByLogicalName(
                "LibraryStyles.akcss"));
        var interceptStyle = Assert.Single(
            metadataModule.AkcssSymbols,
            static symbol => symbol.Name == "special");
        var intercept = Assert.IsAssignableFrom<IAkcssInterceptOperation>(
            Assert.Single(interceptStyle.Operations));
        Assert.Null(intercept.Syntax);
        Assert.Equal(
            "global::TestStyles.InterceptStyle",
            intercept.InterceptType.Symbol?.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat));

        const string localComponent =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Border class=\"local\" />\n";
        const string localAkcss =
            "@using Avalonia.Controls;\n" +
            "@using LibraryStyles.akcss;\n" +
            "\n" +
            "Border.local { @apply card w-4; }\n";
        var consumerDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "AkcssMetadataConsumer");
        GeneratorDriver consumerDriver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts:
            [
                new TestAdditionalText(
                    Path.Combine(consumerDirectory, "Consumer.akbura"),
                    SourceText.From(localComponent)),
                new TestAdditionalText(
                    Path.Combine(consumerDirectory, "Consumer.akcss"),
                    SourceText.From(localAkcss)),
            ],
            parseOptions: parseOptions);
        consumerDriver = consumerDriver.RunGeneratorsAndUpdateCompilation(
            consumerCSharpCompilation,
            out var generatedConsumer,
            out var consumerGeneratorDiagnostics);
        Assert.DoesNotContain(
            consumerGeneratorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            generatedConsumer.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var localModuleSource = Assert.Single(
            Assert.Single(consumerDriver.GetRunResult().Results).GeneratedSources,
            static source => source.HintName.Contains(
                "Consumer.akcss",
                StringComparison.Ordinal));
        var localModuleText = localModuleSource.SourceText.ToString();
        Assert.Contains("OpacityProperty", localModuleText, StringComparison.Ordinal);
        Assert.Contains("IsEnabled", localModuleText, StringComparison.Ordinal);
        Assert.Contains("PaddingProperty", localModuleText, StringComparison.Ordinal);
        Assert.Contains("WidthProperty", localModuleText, StringComparison.Ordinal);
        Assert.Contains("GetResourceObservable", localModuleText, StringComparison.Ordinal);
        Assert.Contains(
            "[global::Akbura.CompilerAnotations.ObservesPropertyAttribute(\"IsEnabled\")]",
            localModuleText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencedAkcssAnnotationsTakePriorityOverMalformedManifest()
    {
        const string source =
            "[assembly: global::Akbura.CompilerAnotations.AkcssModuleReferenceAttribute(" +
            "typeof(global::TestStyles.GeneratedStyles))]\n" +
            "namespace TestStyles;\n" +
            "[global::Akbura.CompilerAnotations.AkcssModuleAttribute(" +
            "\"Broken.akcss\", MetadataName = \"TestStyles.Broken.akcss\", FormatVersion = 4)]\n" +
            "public static class GeneratedStyles\n" +
            "{\n" +
            "    [global::Akbura.CompilerAnotations.AkcssSymbolAttribute(" +
            "Name = \"card\", MetadataName = \"Border.card\", " +
            "Kind = global::Akbura.CompilerAnotations.AkcssSymbolKind.Style, " +
            "TargetType = typeof(global::Avalonia.Controls.Border), " +
            "ClassName = \"card\", RuntimeStyleIndex = 0)]\n" +
            "    public static class __AkcssMetadata_0 { }\n" +
            "}\n";
        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(ReferencedAkcssAnnotationsTakePriorityOverMalformedManifest),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var assemblyPath = Path.Combine(directory, "AnnotatedStyles.dll");

        try
        {
            var parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview);
            var references = SymbolTests.CreateAvaloniaReferences();
            var library = CSharpCompilation.Create(
                "AnnotatedStyles",
                [CSharpSyntaxTree.ParseText(source, parseOptions)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var malformedManifest = new ResourceDescription(
                AkburaModuleManifest.ResourceName,
                static () => new MemoryStream(
                    new byte[] { 1, 2, 3 },
                    writable: false),
                isPublic: true);
            var emitResult = library.Emit(
                assemblyPath,
                manifestResources: [malformedManifest]);
            Assert.True(
                emitResult.Success,
                string.Join(Environment.NewLine, emitResult.Diagnostics));

            var libraryReference = MetadataReference.CreateFromFile(assemblyPath);
            var consumer = CSharpCompilation.Create(
                "Consumer",
                references: references.Append(libraryReference),
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var compilation = new AkburaCompilation(
                consumer,
                ImmutableArray<AkburaSyntaxTree>.Empty);

            var module = Assert.Single(
                compilation.GetAkcssModuleSymbolsByLogicalName("Broken.akcss"));
            var style = Assert.Single(module.AkcssSymbols);
            Assert.Equal("card", style.Name);
            Assert.Equal("Border.card", style.MetadataName);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Generator_ObservesTargetPropertiesUsedByAkcssConditions()
    {
        const string akcss =
            "@using Avalonia.Controls;\n" +
            "\n" +
            "Button.reactive-button {\n" +
            "    @if(IsEnabled) {\n" +
            "        Opacity: 0.25;\n" +
            "    }\n" +
            "}\n";
        var compilation = CSharpCompilation.Create(
            "AkburaGeneratedObservedAkcssTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(Environment.CurrentDirectory, "Reactive.akcss");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts: [new TestAdditionalText(sourcePath, SourceText.From(akcss))],
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedSource = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources);
        var generatedText = generatedSource.SourceText.ToString();
        Assert.Contains(
            "[global::Akbura.CompilerAnotations.ObservesPropertyAttribute(\"IsEnabled\")]",
            generatedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "((global::Avalonia.Controls.Button)__target).IsEnabled",
            generatedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var moduleType = Assert.Single(
            assembly.GetTypes(),
            static type => type.GetCustomAttribute<AkcssModuleAttribute>() != null);
        var styleType = moduleType.GetNestedType("Style_0", BindingFlags.NonPublic);
        Assert.NotNull(styleType);
        var style = Assert.IsAssignableFrom<AkcssClass>(
            Activator.CreateInstance(styleType, nonPublic: true));

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var button = new Button();
                AkburaControl.SetAkcssStyles(
                    button,
                    [new AkcssClassActivator(style)]);

                Assert.Equal(0.25d, button.Opacity);

                button.IsEnabled = false;
                Assert.Equal(1d, button.Opacity);

                button.IsEnabled = true;
                Assert.Equal(0.25d, button.Opacity);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Generator_GuardsMixedClassPropertiesByTargetCompatibility()
    {
        const string csharp =
            "namespace Data;\n" +
            "public sealed class MyClass\n" +
            "{\n" +
            "    public int Age { get; set; }\n" +
            "}\n";
        const string akcss =
            "@using Data;\n" +
            "@using Avalonia.Controls;\n" +
            "\n" +
            ".myStyle {\n" +
            "    MyClass.Age: 10;\n" +
            "    Padding: 10;\n" +
            "}\n";
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaCsGeneratorCustomClassTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(csharp, parseOptions)],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var sourcePath = Path.Combine(Environment.CurrentDirectory, "Styles.akcss");
        var additionalText = new TestAdditionalText(sourcePath, SourceText.From(akcss));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaCsGenerator().AsSourceGenerator()],
            additionalTexts: [additionalText],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatorResult = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(generatorResult.Exception);
        var generatedSource = Assert.Single(generatorResult.GeneratedSources);
        var text = generatedSource.SourceText.ToString();
        Assert.Contains("__target is global::Data.MyClass", text, StringComparison.Ordinal);
        Assert.Contains("__target is global::Avalonia.AvaloniaObject", text, StringComparison.Ordinal);
        AssertEnhancedLineDirective(text, "(5,18)-(5,20)", sourcePath);
        AssertEnhancedLineDirective(text, "(6,14)-(6,16)", sourcePath);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var customType = assembly.GetType("Data.MyClass");
        Assert.NotNull(customType);
        var moduleType = assembly.GetType(
            "Akbura.Generated.__AkburaAkcssModule_6f172a6a");
        Assert.NotNull(moduleType);
        var styleType = moduleType.GetNestedType(
            "Style_0",
            BindingFlags.NonPublic);
        Assert.NotNull(styleType);
        var style = Assert.IsAssignableFrom<AkcssClass>(
            Activator.CreateInstance(styleType, nonPublic: true));

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var customTarget = Activator.CreateInstance(customType);
                Assert.NotNull(customTarget);

                style.Update(customTarget);

                Assert.Equal(10, customType.GetProperty("Age")!.GetValue(customTarget));
                style.Reset(customTarget);

                var button = new Button();
                style.Update(button);

                Assert.Equal(10d, button.Padding.Left);
                Assert.Equal(10d, button.Padding.Top);
                style.Reset(button);
                Assert.Equal(0d, button.Padding.Left);
                Assert.Equal(0d, button.Padding.Top);
            },
            CancellationToken.None);
    }

    [Fact]
    public void Generator_DoesNotEmitAkcssImportAsCSharpUsingDirective()
    {
        const string component =
            """
            using Akbura.Styles.akcss;
            using Avalonia.Controls;

            <Button />
            """;

        var generatedSource =
            GenerateWhitespaceComponent(component);

        Assert.DoesNotContain(
            "using Akbura.Styles.akcss;",
            generatedSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "using Avalonia.Controls;",
            generatedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_GlobalAkburaUsingsAreAppliedWithoutGeneratingAComponent()
    {
        const string globalUsings =
            """
            using System.Text;
            using Avalonia.Controls.Presenters;
            """;
        const string component =
            """
            state StringBuilder text = new();

            <ContentPresenter />
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGlobalUsingsGeneratorTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var projectDirectory = Environment.CurrentDirectory;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    Path.Combine(
                        projectDirectory,
                        "Configuration",
                        GlobalUsings.ComponentFileName),
                    SourceText.From(globalUsings)),
                new TestAdditionalText(
                    Path.Combine(
                        projectDirectory,
                        "Views",
                        "Counter.akbura"),
                    SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        var generated = Assert.Single(result.GeneratedSources);
        var generatedText = generated.SourceText.ToString();
        Assert.Contains(
            "using System.Text;",
            generatedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "using Avalonia.Controls.Presenters;",
            generatedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "class GlobalUsings",
            generatedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_GlobalAkcssUsingsAreAppliedWithoutGeneratingAModule()
    {
        const string globalUsings =
            "@using Avalonia.Controls;";
        const string styles =
            """
            Control.example {
                Width: 10;
            }
            """;
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaGlobalAkcssUsingsGeneratorTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var projectDirectory = Environment.CurrentDirectory;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    Path.Combine(
                        projectDirectory,
                        "Configuration",
                        GlobalUsings.AkcssFileName),
                    SourceText.From(globalUsings)),
                new TestAdditionalText(
                    Path.Combine(
                        projectDirectory,
                        "Styles.akcss"),
                    SourceText.From(styles)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        var generated = Assert.Single(result.GeneratedSources);
        Assert.Contains(
            "using Avalonia.Controls;",
            generated.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_GlobalUsingsFilesRejectNonUsingMembers()
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaInvalidGlobalUsingsGeneratorTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var projectDirectory = Environment.CurrentDirectory;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    Path.Combine(
                        projectDirectory,
                        GlobalUsings.ComponentFileName),
                    SourceText.From(
                        """
                        using System;
                        <object />
                        """)),
                new TestAdditionalText(
                    Path.Combine(
                        projectDirectory,
                        GlobalUsings.AkcssFileName),
                    SourceText.From(".invalid { }")),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var generatorDiagnostics);

        Assert.Equal(
            2,
            generatorDiagnostics.Count(
                static diagnostic =>
                    diagnostic.Id ==
                    ErrorCodes
                        .AKBURA_SEMANTIC_GlobalUsingsFileContainsNonUsing));
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ModuleManifest_GlobalUsingsAffectSignaturesButAreNotExported()
    {
        var compilation = CSharpCompilation.Create(
            "AkburaGlobalUsingsManifestTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var manifest = AkburaModuleManifestBuilder.Build(
            "AkburaGlobalUsingsManifestTests",
            string.Empty,
            [
                new AkburaModuleSourceText(
                    Path.Combine(
                        "Configuration",
                        GlobalUsings.ComponentFileName),
                    SourceText.From("using System.Text;")),
                new AkburaModuleSourceText(
                    "Counter.akbura",
                    SourceText.From(
                        """
                        param StringBuilder Text;

                        <object />
                        """)),
                new AkburaModuleSourceText(
                    Path.Combine(
                        "Configuration",
                        GlobalUsings.AkcssFileName),
                    SourceText.From("@using Avalonia.Controls;")),
                new AkburaModuleSourceText(
                    "Styles.akcss",
                    SourceText.From(".example { }")),
            ],
            compilation);

        Assert.Equal(2, manifest.Sources.Length);
        var componentSource = Assert.Single(
            manifest.Sources,
            static source =>
                source.Kind == AkburaModuleSourceKind.Component);
        var component = Assert.Single(
            componentSource.Declarations).Component;
        Assert.NotNull(component);
        Assert.Equal(
            "global::System.Text.StringBuilder",
            Assert.Single(component.Parameters).TypeName);
        Assert.DoesNotContain(
            manifest.Sources,
            static source =>
                Path.GetFileName(source.SourceCodePath)
                    .StartsWith(
                        "GlobalUsings.",
                        StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateWhitespaceComponent(string component)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);

        var compilation = CSharpCompilation.Create(
            "AkburaWhitespaceGeneratorTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));

        var sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "WhitespaceComponent.akbura");

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                sourcePath,
                SourceText.From(component)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        var result = Assert.Single(driver.GetRunResult().Results);

        Assert.Null(result.Exception);

        var generated = Assert.Single(result.GeneratedSources);

        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        return generated.SourceText.ToString();
    }

    private static int AssertEnhancedLineDirective(
        string generatedSource,
        string sourceSpan,
        string sourcePath)
    {
        var prefix = $"#line {sourceSpan} ";
        var directiveIndex = generatedSource.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(directiveIndex >= 0);

        var lineEnd = generatedSource.IndexOf('\n', directiveIndex);
        Assert.True(lineEnd >= 0);
        var directive = generatedSource
            .Substring(directiveIndex, lineEnd - directiveIndex)
            .TrimEnd('\r');
        var pathSuffix = " \"" + sourcePath + "\"";
        Assert.EndsWith(pathSuffix, directive, StringComparison.Ordinal);

        var offsetStart = prefix.Length;
        var offsetLength = directive.Length - prefix.Length - pathSuffix.Length;
        Assert.True(offsetLength > 0);
        Assert.True(int.TryParse(
            directive.Substring(offsetStart, offsetLength),
            out var characterOffset));
        Assert.True(characterOffset >= 0);
        return directiveIndex;
    }

    private static void AssertMappedLocation(
        SyntaxToken token,
        string sourcePath,
        LinePosition expectedStart,
        LinePosition expectedEnd)
    {
        var mappedSpan = token.GetLocation().GetMappedLineSpan();
        Assert.Equal(sourcePath, mappedSpan.Path);
        Assert.Equal(expectedStart, mappedSpan.StartLinePosition);
        Assert.Equal(expectedEnd, mappedSpan.EndLinePosition);
    }

    private static void AssertGeneratedModuleContract(Type moduleType, string sourcePath)
    {
        Assert.True(moduleType.IsPublic);
        AssertHiddenFromEditor(moduleType);
        var moduleAttribute = Assert.IsType<AkcssModuleAttribute>(
            moduleType.GetCustomAttribute<AkcssModuleAttribute>());
        Assert.Equal(sourcePath, moduleAttribute.Path);

        var metadataNameField = moduleType.GetField(
            "MetadataName",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(metadataNameField);
        Assert.True(metadataNameField.IsLiteral);
        Assert.Equal("Styles.akcss", metadataNameField.GetRawConstantValue());
        AssertHiddenFromEditor(metadataNameField);

        var sourcePathField = moduleType.GetField(
            "SourcePath",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(sourcePathField);
        Assert.True(sourcePathField.IsLiteral);
        Assert.Equal(sourcePath, sourcePathField.GetRawConstantValue());
        AssertHiddenFromEditor(sourcePathField);

        var stylesField = moduleType.GetField(
            "Styles",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(stylesField);
        Assert.True(stylesField.IsInitOnly);
        AssertHiddenFromEditor(stylesField);
    }

    private static void AssertHiddenFromEditor(MemberInfo member)
    {
        var editorBrowsable = Assert.IsType<EditorBrowsableAttribute>(
            member.GetCustomAttribute<EditorBrowsableAttribute>());
        Assert.Equal(EditorBrowsableState.Never, editorBrowsable.State);
        var browsable = Assert.IsType<BrowsableAttribute>(
            member.GetCustomAttribute<BrowsableAttribute>());
        Assert.False(browsable.Browsable);
    }

    private sealed class TestAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public TestAdditionalText(string path, SourceText text)
        {
            Path = path;
            _text = text;
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return _text;
        }
    }

    private sealed class ConstantServiceProvider : Akbura.Engine.IAkburaServiceProvider
    {
        private readonly object? _service;

        public ConstantServiceProvider(object? service)
        {
            _service = service;
        }

        public object? GetService(ref readonly Akbura.Engine.InjectionInfo injectionInfo)
        {
            return _service != null && injectionInfo.RequestedService.IsInstanceOfType(_service)
                ? _service
                : null;
        }
    }
}
