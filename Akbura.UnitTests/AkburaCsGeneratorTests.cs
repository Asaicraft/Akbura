using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Akbura.Furioso;
using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.ComponentModel;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkburaCsGeneratorTests
{
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
}
