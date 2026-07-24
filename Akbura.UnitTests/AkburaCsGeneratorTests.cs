using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Akbura.Furioso;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkburaCsGeneratorTests
{
    [Fact]
    public void Generator_EmitsCompilableComponentLifecycleAndDescriptors()
    {
        const string component =
            "using Avalonia.Controls;\n" +
            "\n" +
            "param int Initial = 2;\n" +
            "state int count = 0;\n" +
            "\n" +
            "<StackPanel x.Name=\"layout\">\n" +
            "    <TextBlock x.Name=\"label\" Text={$\"Count: {count}\"} />\n" +
            "    <Button Click={(sender, args) => count++}>+</Button>\n" +
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
        Assert.Contains("private global::Avalonia.Controls.TextBlock label", text, StringComparison.Ordinal);
        Assert.Contains("protected override global::Avalonia.Controls.Control FirstUpdate()", text, StringComparison.Ordinal);
        Assert.Contains("protected override global::Avalonia.Controls.Control Update()", text, StringComparison.Ordinal);
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
        Assert.Contains("AkcssUtilityActivator", componentText, StringComparison.Ordinal);
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
        Assert.DoesNotContain("Amx.DynamicResource", text, StringComparison.Ordinal);
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
