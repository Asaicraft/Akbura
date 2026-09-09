using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;

namespace Akbura.UnitTests;

public sealed class AkburaBlackSilenceGeneratorTests
{
    private const string SyntaxTreesTrackingName = "BlackSilence.SyntaxTrees";
    private const string SourceTextsTrackingName = "BlackSilence.SourceTexts";
    private const string GeneratedComponentsTrackingName = "BlackSilence.GeneratedComponents";
    private const string GeneratedExternalAkcssTrackingName = "BlackSilence.GeneratedExternalAkcss";
    private const string GeneratedInlineAkcssTrackingName = "BlackSilence.GeneratedInlineAkcss";
    private const string GeneratedProjectSourcesTrackingName = "BlackSilence.GeneratedProjectSources";

    private static readonly AnalyzerConfigOptionsProvider s_emptyOptionsProvider =
        new TestAnalyzerConfigOptionsProvider(string.Empty, string.Empty);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void GenerateSources_EmitsExactlyOneProjectHotReloadService(
        int componentCount)
    {
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(AkburaBlackSilenceGeneratorTests),
            Guid.NewGuid().ToString("N"));
        var files = new TestAdditionalText[componentCount];

        for (var i = 0; i < files.Length; i++)
        {
            files[i] = new TestAdditionalText(
                Path.Combine(projectDirectory, "View" + i + ".akbura"),
                SourceText.From(
                    "using Avalonia.Controls;\r\n" +
                    "<Border />\r\n"));
        }

        var options = new TestAnalyzerConfigOptionsProvider(
            "Demo",
            projectDirectory);
        var driver = CreateDriver(options, files);

        driver = driver.RunGenerators(CreateCompilation(string.Empty));

        var result = Assert.Single(driver.GetRunResult().Results);
        var service = GetHotReloadService(result);

        Assert.Null(result.Exception);
        Assert.Equal(
            componentCount,
            result.GeneratedSources.Count(static source =>
                source.HintName.StartsWith(
                    "Akbura.Component.",
                    StringComparison.Ordinal)));
        Assert.Equal(componentCount + 1, result.GeneratedSources.Length);
        Assert.Equal(
            IncrementalStepRunReason.New,
            Assert.Single(
                GetOutputReasons(
                    driver,
                    GeneratedProjectSourcesTrackingName)));

        var serviceText = service.SourceText.ToString();
        for (var i = 0; i < componentCount; i++)
        {
            Assert.Contains(
                "global::Demo.View" + i + ".__AkburaHotReloadApply();",
                serviceText,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GenerateSources_DebugCompilationRegistersHotReloadService()
    {
        var driver = CreateDebugDriver(s_emptyOptionsProvider);
        var compilation = CreateHotReloadCompilation();

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var result = Assert.Single(driver.GetRunResult().Results);
        GetHotReloadService(result);

        Assert.Null(result.Exception);
        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            static diagnostic =>
                diagnostic.Severity is DiagnosticSeverity.Warning or
                    DiagnosticSeverity.Error);

        var handlerType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            outputCompilation.GetTypeByMetadataName(
                "Akbura.Generated." +
                HotReloadServiceWriter.GetHandlerTypeName(
                    compilation.AssemblyName!)));
        var handlerAttribute = Assert.Single(
            outputCompilation.Assembly.GetAttributes(),
            static attribute =>
                attribute.AttributeClass?.ToDisplayString() ==
                "System.Reflection.Metadata.MetadataUpdateHandlerAttribute");
        var registeredType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            Assert.Single(handlerAttribute.ConstructorArguments).Value);

        Assert.True(
            SymbolEqualityComparer.Default.Equals(
                handlerType,
                registeredType));
    }

    [Fact]
    public void HotReloadService_ContinuesComponentTypesAndAggregatesFailures()
    {
        const string hostSource =
            """
            namespace Demo
            {
                public static class First
                {
                    public static int CallCount;
                    public static bool Throws;

                    internal static void __AkburaHotReloadApply()
                    {
                        CallCount++;
                        if (Throws)
                        {
                            throw new System.InvalidOperationException(
                                "First component type failed.");
                        }
                    }
                }

                public static class Second
                {
                    public static int CallCount;
                    public static bool Throws;

                    internal static void __AkburaHotReloadApply()
                    {
                        CallCount++;
                        if (Throws)
                        {
                            throw new System.InvalidOperationException(
                                "Second component type failed.");
                        }
                    }
                }

                public static class Third
                {
                    public static int CallCount;
                    public static bool Throws;

                    internal static void __AkburaHotReloadApply()
                    {
                        CallCount++;
                        if (Throws)
                        {
                            throw new System.InvalidOperationException(
                                "Third component type failed.");
                        }
                    }
                }
            }
            """;
        var assemblyName =
            "AkburaHotReloadIsolation_" + Guid.NewGuid().ToString("N");
        var generated = HotReloadServiceWriter.Generate(
            assemblyName,
            ["Demo.First", "Demo.Second", "Demo.Third"]);
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols("DEBUG");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(hostSource, parseOptions),
                CSharpSyntaxTree.ParseText(
                    generated.SourceText,
                    parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);

        Assert.True(emit.Success, FormatDiagnostics(emit.Diagnostics));

        var assembly = Assembly.Load(peStream.ToArray());
        var first = Assert.IsAssignableFrom<Type>(
            assembly.GetType("Demo.First"));
        var second = Assert.IsAssignableFrom<Type>(
            assembly.GetType("Demo.Second"));
        var third = Assert.IsAssignableFrom<Type>(
            assembly.GetType("Demo.Third"));
        var handler = Assert.IsAssignableFrom<Type>(
            assembly.GetType(
                "Akbura.Generated." +
                HotReloadServiceWriter.GetHandlerTypeName(assemblyName)));
        const BindingFlags flags =
            BindingFlags.Static |
            BindingFlags.Public |
            BindingFlags.NonPublic;
        var updateApplicationCore = Assert.IsAssignableFrom<MethodInfo>(
            handler.GetMethod("UpdateApplicationCore", flags));
        var reloadAll = Assert.IsAssignableFrom<MethodInfo>(
            handler.GetMethod("ReloadAll", flags));

        GetRequiredStaticField(first, "Throws").SetValue(null, true);

        var singleFailure = Assert.Throws<TargetInvocationException>(
            () => updateApplicationCore.Invoke(
                null,
                [new[] { first, second, third }]));

        Assert.Equal(
            "First component type failed.",
            Assert.IsType<InvalidOperationException>(
                singleFailure.InnerException).Message);
        Assert.Equal(1, GetStaticCallCount(first));
        Assert.Equal(1, GetStaticCallCount(second));
        Assert.Equal(1, GetStaticCallCount(third));

        foreach (var componentType in new[] { first, second, third })
        {
            GetRequiredStaticField(
                componentType,
                "CallCount").SetValue(null, 0);
        }

        GetRequiredStaticField(third, "Throws").SetValue(null, true);

        var multipleFailures = Assert.Throws<TargetInvocationException>(
            () => reloadAll.Invoke(null, parameters: null));
        var aggregate = Assert.IsType<AggregateException>(
            multipleFailures.InnerException);

        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Contains(
            aggregate.InnerExceptions,
            static exception =>
                exception.Message == "First component type failed.");
        Assert.Contains(
            aggregate.InnerExceptions,
            static exception =>
                exception.Message == "Third component type failed.");
        Assert.Equal(1, GetStaticCallCount(first));
        Assert.Equal(1, GetStaticCallCount(second));
        Assert.Equal(1, GetStaticCallCount(third));
    }

    [Fact]
    public void GenerateSources_DebugCompilationLinksComponentToHotReloadService()
    {
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(AkburaBlackSilenceGeneratorTests),
            Guid.NewGuid().ToString("N"));
        var component = new TestAdditionalText(
            Path.Combine(
                projectDirectory,
                "Components",
                "Preview.akbura"),
            SourceText.From(
                "using Avalonia.Controls;\r\n" +
                "<Border />\r\n"));
        var options = new TestAnalyzerConfigOptionsProvider(
            "Demo",
            projectDirectory);
        var driver = CreateDebugDriver(options, component);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            CreateCompilation(string.Empty),
            out var outputCompilation,
            out _);

        AssertGeneratedCompilation(driver, outputCompilation);

        var result = Assert.Single(driver.GetRunResult().Results);
        var componentSource = Assert.Single(
            result.GeneratedSources,
            static source =>
                source.HintName.StartsWith(
                    "Akbura.Component.",
                    StringComparison.Ordinal));
        var serviceSource = GetHotReloadService(result);

        Assert.Contains(
            "internal static void __AkburaHotReloadApply()",
            componentSource.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Demo.Components.Preview.__AkburaHotReloadApply();",
            serviceSource.SourceText.ToString(),
            StringComparison.Ordinal);

        var componentType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            outputCompilation.GetTypeByMetadataName(
                "Demo.Components.Preview"));
        var applyMethod = Assert.Single(
            componentType.GetMembers("__AkburaHotReloadApply")
                .OfType<IMethodSymbol>());

        Assert.True(applyMethod.IsStatic);
        Assert.Equal(Accessibility.Internal, applyMethod.DeclaredAccessibility);
        Assert.True(applyMethod.ReturnsVoid);
        Assert.Empty(applyMethod.Parameters);
    }

    [Fact]
    public void GenerateSources_DebugStructuralEdit_PreservesDeclaredFieldContract()
    {
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(AkburaBlackSilenceGeneratorTests),
            Guid.NewGuid().ToString("N"));
        var componentPath = Path.Combine(projectDirectory, "Page.akbura");
        var original = new TestAdditionalText(
            componentPath,
            SourceText.From(
                "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "<StackPanel>\r\n" +
                "    <TextBlock Text=\"Hello\" />\r\n" +
                "    <Border />\r\n" +
                "    <TextBlock Text=\"Hi\" />\r\n" +
                "</StackPanel>\r\n"));
        var edited = new TestAdditionalText(
            componentPath,
            SourceText.From(
                "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "<StackPanel>\r\n" +
                "    <Border />\r\n" +
                "    <TextBlock Text=\"Hi\" />\r\n" +
                "</StackPanel>\r\n"));
        var options = new TestAnalyzerConfigOptionsProvider(
            "Demo",
            projectDirectory);
        var baseCompilation = CreateCompilation(string.Empty);
        var driver = CreateDebugDriver(options, original);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            baseCompilation,
            out var initialCompilation,
            out _);

        AssertGeneratedCompilation(driver, initialCompilation);

        var initialContract = GetDeclaredFieldContract(
            initialCompilation,
            "Demo.Page");

        driver = driver.ReplaceAdditionalText(original, edited);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            baseCompilation,
            out var updatedCompilation,
            out _);

        AssertGeneratedCompilation(driver, updatedCompilation);

        var updatedContract = GetDeclaredFieldContract(
            updatedCompilation,
            "Demo.Page");

        Assert.Equal(initialContract, updatedContract);
        Assert.DoesNotContain(
            updatedContract,
            static field => field.StartsWith(
                "__element",
                StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateSources_DebugStructuralEdit_EmitsEditAndContinueDelta()
    {
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(AkburaBlackSilenceGeneratorTests),
            Guid.NewGuid().ToString("N"));
        var componentPath = Path.Combine(projectDirectory, "Page.akbura");
        var original = new TestAdditionalText(
            componentPath,
            SourceText.From(
                "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "<StackPanel>\r\n" +
                "    <Border />\r\n" +
                "</StackPanel>\r\n"));
        var edited = new TestAdditionalText(
            componentPath,
            SourceText.From(
                "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "<StackPanel>\r\n" +
                "    <TextBlock Text=\"Inserted\" />\r\n" +
                "    <Border />\r\n" +
                "</StackPanel>\r\n"));
        var options = new TestAnalyzerConfigOptionsProvider(
            "Demo",
            projectDirectory);
        var baseCompilation = CreateCompilation(string.Empty)
            .RemoveAllSyntaxTrees();
        var driver = CreateDebugDriver(options, original);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            baseCompilation,
            out var initialCompilation,
            out _);

        AssertGeneratedCompilation(driver, initialCompilation);
        var initialGeneratedSource = GetGeneratedComponentSource(driver);

        driver = driver.ReplaceAdditionalText(original, edited);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            baseCompilation,
            out var updatedCompilation,
            out _);

        AssertGeneratedCompilation(driver, updatedCompilation);
        var updatedGeneratedSource = GetGeneratedComponentSource(driver);
        Assert.False(
            initialGeneratedSource.SourceText.ContentEquals(
                updatedGeneratedSource.SourceText));

        var semanticEdits = GetChangedGeneratedMethodEdits(
            initialCompilation,
            updatedCompilation,
            "Demo.Page");
        Assert.NotEmpty(semanticEdits);

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var initialEmit = initialCompilation.Emit(
            peStream,
            pdbStream,
            options: new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb));

        Assert.True(
            initialEmit.Success,
            FormatDiagnostics(initialEmit.Diagnostics));

        var peImage = ImmutableArray.Create(peStream.ToArray());
        using var module = ModuleMetadata.CreateFromImage(peImage);
        using var peReader = new PEReader(peImage);
        var baseline = EmitBaseline.CreateInitialBaseline(
            initialCompilation,
            module,
            static _ => default,
            methodHandle => GetLocalSignature(peReader, methodHandle),
            hasPortableDebugInformation: true);
        using var metadataDelta = new MemoryStream();
        using var ilDelta = new MemoryStream();
        using var pdbDelta = new MemoryStream();

        var difference = updatedCompilation.EmitDifference(
            baseline,
            semanticEdits,
            static _ => false,
            metadataDelta,
            ilDelta,
            pdbDelta,
            CancellationToken.None);

        Assert.DoesNotContain(
            difference.Diagnostics,
            static diagnostic =>
                diagnostic.Id is "ENC0009" or "ENC0020" or "ENC0033");
        Assert.True(
            difference.Success,
            FormatDiagnostics(difference.Diagnostics));
        Assert.NotEmpty(difference.UpdatedMethods);
        Assert.True(metadataDelta.Length > 0);
        Assert.True(ilDelta.Length > 0);
        Assert.True(pdbDelta.Length > 0);
    }

    [Fact]
    public void GenerateSources_DebugStructuralOperationRemoval_EmitsEditAndContinueDelta()
    {
        const string eventHostSource =
            "using Avalonia.Interactivity;\r\n" +
            "\r\n" +
            "namespace Demo;\r\n" +
            "\r\n" +
            "public partial class EventPage\r\n" +
            "{\r\n" +
            "    private void OnClick(object? sender, RoutedEventArgs eventArgs)\r\n" +
            "    {\r\n" +
            "    }\r\n" +
            "}\r\n";

        AssertDebugEditAndContinueDelta(
            "EventPage.akbura",
            "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "<StackPanel>\r\n" +
                "    <Button Click={OnClick} />\r\n" +
                "</StackPanel>\r\n",
            "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "<StackPanel>\r\n" +
                "    <Button />\r\n" +
                "</StackPanel>\r\n",
            eventHostSource,
            "Demo.EventPage");

        AssertDebugEditAndContinueDelta(
            "EventExpressionPage.akbura",
            "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "state int count = 0;\r\n" +
                "\r\n" +
                "<StackPanel>\r\n" +
                "    <Button Click={count++} />\r\n" +
                "</StackPanel>\r\n",
            "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "state int count = 0;\r\n" +
                "\r\n" +
                "<StackPanel>\r\n" +
                "    <Button />\r\n" +
                "</StackPanel>\r\n",
            string.Empty,
            "Demo.EventExpressionPage");

        AssertDebugEditAndContinueDelta(
            "BindingPage.akbura",
            "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "<StackPanel>\r\n" +
                "    <TextBox x.Name=\"source\" Text=\"Initial\" />\r\n" +
                "    <TextBlock Text=${Binding #source.Text} />\r\n" +
                "</StackPanel>\r\n",
            "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "<StackPanel>\r\n" +
                "    <TextBox x.Name=\"source\" Text=\"Initial\" />\r\n" +
                "    <TextBlock />\r\n" +
                "</StackPanel>\r\n",
            string.Empty,
            "Demo.BindingPage");

        AssertDebugEditAndContinueDelta(
            "ReverseBindingPage.akbura",
            "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "state string value = \"Initial\";\r\n" +
                "\r\n" +
                "<TextBox x.Name=\"input\" bind:Text={value} />\r\n",
            "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "state string value = \"Initial\";\r\n" +
                "\r\n" +
                "<TextBox x.Name=\"input\" />\r\n",
            string.Empty,
            "Demo.ReverseBindingPage");

        AssertDebugEditAndContinueDelta(
            "CommandPage.akbura",
            "using Demo;\r\n" +
                "\r\n" +
                "command int Execute(int value);\r\n" +
                "\r\n" +
                "<Child Execute={value => value * 2} />\r\n",
            "using Demo;\r\n" +
                "\r\n" +
                "command int Execute(int value);\r\n" +
                "\r\n" +
                "<Child />\r\n",
            string.Empty,
            "Demo.CommandPage",
            "Child.akbura",
            "namespace Demo;\r\n" +
                "\r\n" +
                "command int Execute(int value);\r\n");
    }

    [Fact]
    public void GenerateSources_DebugAkcssEdit_PreservesDeclaredMemberContract()
    {
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(AkburaBlackSilenceGeneratorTests),
            Guid.NewGuid().ToString("N"));
        var componentPath = Path.Combine(projectDirectory, "StyledPage.akbura");
        var original = new TestAdditionalText(
            componentPath,
            SourceText.From(
                "using Avalonia.Controls;\r\n" +
                "using Demo.Extensions;\r\n" +
                "\r\n" +
                "@akcss {\r\n" +
                "    @using Avalonia.Controls;\r\n" +
                "    .card { Height: 20; }\r\n" +
                "    @utilities {\r\n" +
                "        Control.width-(double value) { Width: value; }\r\n" +
                "    }\r\n" +
                "}\r\n" +
                "\r\n" +
                "state double spacing = 4;\r\n" +
                "\r\n" +
                "<Border class=\"card\" width-${DirectPadding {spacing + 1}} />\r\n"));
        var edited = new TestAdditionalText(
            componentPath,
            SourceText.From(
                "using Avalonia.Controls;\r\n" +
                "using Demo.Extensions;\r\n" +
                "\r\n" +
                "@akcss {\r\n" +
                "    @using Avalonia.Controls;\r\n" +
                "    .accent { Opacity: 0.5; }\r\n" +
                "    .card { Height: 20; }\r\n" +
                "    @utilities {\r\n" +
                "        Control.height-(double value) { Height: value; }\r\n" +
                "        Control.width-(double value) { Width: value; }\r\n" +
                "    }\r\n" +
                "}\r\n" +
                "\r\n" +
                "state double spacing = 4;\r\n" +
                "\r\n" +
                "<Border class=\"card accent\"\r\n" +
                "        width-${DirectPadding {spacing + 2}}\r\n" +
                "        height-${DirectPadding {spacing + 3}} />\r\n"));
        var options = new TestAnalyzerConfigOptionsProvider(
            "Demo",
            projectDirectory);
        var baseCompilation = CreateCompilation(
            """
            namespace Demo.Extensions;

            public sealed class DirectPaddingExtension
            {
                public DirectPaddingExtension(double value)
                {
                }

                public double ProvideValue(System.IServiceProvider services) => 0;
            }
            """);
        var driver = CreateDebugDriver(options, original);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            baseCompilation,
            out var initialCompilation,
            out _);

        AssertGeneratedCompilation(driver, initialCompilation);

        var inlineModuleMetadataName =
            AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(
                "Demo",
                "StyledPage.akbura.inline.0.akcss")
            .Substring("global::".Length);
        var initialContract = GetDeclaredRuntimeMemberContract(
            initialCompilation,
            "Demo.StyledPage");
        var initialModuleContract = GetDeclaredRuntimeMemberContract(
            initialCompilation,
            inlineModuleMetadataName);
        var initialSource = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources,
            static source => source.HintName.StartsWith(
                "Akbura.Component.",
                StringComparison.Ordinal)).SourceText.ToString();
        var initialModuleSource = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources,
            static source => source.HintName.StartsWith(
                "Akbura.Akcss.",
                StringComparison.Ordinal)).SourceText.ToString();

        driver = driver.ReplaceAdditionalText(original, edited);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            baseCompilation,
            out var updatedCompilation,
            out _);

        AssertGeneratedCompilation(driver, updatedCompilation);

        var updatedContract = GetDeclaredRuntimeMemberContract(
            updatedCompilation,
            "Demo.StyledPage");
        var updatedModuleContract = GetDeclaredRuntimeMemberContract(
            updatedCompilation,
            inlineModuleMetadataName);
        var updatedSource = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources,
            static source => source.HintName.StartsWith(
                "Akbura.Component.",
                StringComparison.Ordinal)).SourceText.ToString();
        var updatedModuleSource = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources,
            static source => source.HintName.StartsWith(
                "Akbura.Akcss.",
                StringComparison.Ordinal)).SourceText.ToString();

        Assert.Equal(initialContract, updatedContract);
        Assert.Equal(
            initialModuleContract,
            updatedModuleContract);

        foreach (var generatedSource in new[] { initialSource, updatedSource })
        {
            Assert.DoesNotContain("s_akcssClass", generatedSource, StringComparison.Ordinal);
            Assert.DoesNotContain("s_akcssApplications", generatedSource, StringComparison.Ordinal);
            Assert.DoesNotContain("s_akcssValueProperty", generatedSource, StringComparison.Ordinal);
            Assert.DoesNotContain("__CreateAkcssValue", generatedSource, StringComparison.Ordinal);
            Assert.Contains(
                "." +
                AkcssModuleWriter.DebugStyleAccessorName +
                "(",
                generatedSource,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                ".Styles[",
                generatedSource,
                StringComparison.Ordinal);
        }

        foreach (var generatedSource in new[] { initialModuleSource, updatedModuleSource })
        {
            Assert.Contains(
                "internal static global::Akbura.Akcss.AkcssStyle " +
                AkcssModuleWriter.DebugStyleAccessorName +
                "(int index)",
                generatedSource,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GenerateSources_DebugAkcssApplicationRemoval_EmitsEditAndContinueDelta()
    {
        AssertDebugEditAndContinueDelta(
            "StyledPage.akbura",
            "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "@akcss {\r\n" +
                "    @using Avalonia.Controls;\r\n" +
                "    .card { Width: 20; }\r\n" +
                "}\r\n" +
                "\r\n" +
                "<Border class=\"card\" />\r\n",
            "using Avalonia.Controls;\r\n" +
                "\r\n" +
                "@akcss {\r\n" +
                "    @using Avalonia.Controls;\r\n" +
                "    .card { Width: 20; }\r\n" +
                "}\r\n" +
                "\r\n" +
                "<Border />\r\n",
            string.Empty,
            "Demo.StyledPage");
    }

    [Fact]
    public void GenerateSources_AddingAndRemovingComponentsUpdatesHotReloadService()
    {
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(AkburaBlackSilenceGeneratorTests),
            Guid.NewGuid().ToString("N"));
        var options = new TestAnalyzerConfigOptionsProvider(
            "Demo",
            projectDirectory);
        var first = new TestAdditionalText(
            Path.Combine(projectDirectory, "First.akbura"),
            SourceText.From(
                "using Avalonia.Controls;\r\n" +
                "<Border />\r\n"));
        var second = new TestAdditionalText(
            Path.Combine(projectDirectory, "Second.akbura"),
            SourceText.From(
                "using Avalonia.Controls;\r\n" +
                "<Border />\r\n"));
        var compilation = CreateCompilation(string.Empty);
        var driver = CreateDriver(options, first);

        driver = driver.RunGenerators(compilation);
        var initialText = GetHotReloadService(
            Assert.Single(driver.GetRunResult().Results))
            .SourceText
            .ToString();

        Assert.Contains(
            "global::Demo.First.__AkburaHotReloadApply();",
            initialText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "global::Demo.Second.__AkburaHotReloadApply();",
            initialText,
            StringComparison.Ordinal);

        driver = driver.AddAdditionalTexts([second]);
        driver = driver.RunGenerators(compilation);
        var addedText = GetHotReloadService(
            Assert.Single(driver.GetRunResult().Results))
            .SourceText
            .ToString();

        Assert.Contains(
            "global::Demo.First.__AkburaHotReloadApply();",
            addedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Demo.Second.__AkburaHotReloadApply();",
            addedText,
            StringComparison.Ordinal);
        Assert.Equal(
            IncrementalStepRunReason.Modified,
            Assert.Single(
                GetOutputReasons(
                    driver,
                    GeneratedProjectSourcesTrackingName)));

        driver = driver.RemoveAdditionalTexts([first]);
        driver = driver.RunGenerators(compilation);
        var removedResult = Assert.Single(driver.GetRunResult().Results);
        var removedText = GetHotReloadService(removedResult)
            .SourceText
            .ToString();

        Assert.DoesNotContain(
            "global::Demo.First.__AkburaHotReloadApply();",
            removedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Demo.Second.__AkburaHotReloadApply();",
            removedText,
            StringComparison.Ordinal);
        Assert.Equal(
            IncrementalStepRunReason.Modified,
            Assert.Single(
                GetOutputReasons(
                    driver,
                    GeneratedProjectSourcesTrackingName)));

        var fresh = CreateDriver(options, second)
            .RunGenerators(compilation);
        var freshText = GetHotReloadService(
            Assert.Single(fresh.GetRunResult().Results))
            .SourceText
            .ToString();

        Assert.Equal(freshText, removedText);
    }

    [Fact]
    public void UpdatingOneAdditionalFile_ReparsesOnlyThatFile()
    {
        // The parse cache is process-wide. Keep this project's syntax-tree
        // cache entries isolated from concurrent test projects.
        var projectDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            nameof(AkburaBlackSilenceGeneratorTests), Guid.NewGuid().ToString("N"));
        var a = new TestAdditionalText(
            System.IO.Path.Combine(projectDirectory, "A.akbura"),
            SourceText.From(
                """
                using Avalonia.Controls;

                <Border />
                """));

        var oldBText = SourceText.From(
            """
            using Avalonia.Controls;

            state double count = 0d;

            <Border Width={count} />
            """);

        var b = new TestAdditionalText(System.IO.Path.Combine(projectDirectory, "B.akbura"), oldBText);

        var c = new TestAdditionalText(
            System.IO.Path.Combine(projectDirectory, "C.akcss"),
            SourceText.From(
                """
                @using Avalonia.Controls;

                .button {
                    Width: 10;
                }
                """));

        const string csharpSource =
            """
            using Akbura;
            using Akbura.Engine;

            public partial class A : AkburaControl
            {
                public A()
                    : base(AkburaEngine.Empty)
                {
                }
            }

            public partial class B : AkburaControl
            {
                public B()
                    : base(AkburaEngine.Empty)
                {
                }
            }
            """;

        var compilation = CreateCompilation(csharpSource);
        var driver = CreateDriver(new TestAnalyzerConfigOptionsProvider(string.Empty, projectDirectory), a, b, c);

        driver = driver.RunGenerators(compilation);

        var initial = GetSyntaxTreeOutputs(driver);

        Assert.Equal(3, initial.Count);
        Assert.All(initial.Values, static output =>
            Assert.Equal(IncrementalStepRunReason.New, output.Reason));

        var initialComponentReasons = GetOutputReasons(driver, GeneratedComponentsTrackingName);
        var initialAkcssReasons = GetOutputReasons(driver, GeneratedExternalAkcssTrackingName);

        Assert.Equal(2, initialComponentReasons.Length);
        Assert.All(initialComponentReasons, static reason =>
            Assert.Equal(IncrementalStepRunReason.New, reason));

        Assert.Equal(
            IncrementalStepRunReason.New,
            Assert.Single(initialAkcssReasons));

        Assert.Equal(1, a.ReadCount);
        Assert.Equal(1, b.ReadCount);
        Assert.Equal(1, c.ReadCount);

        var changeStart = oldBText.ToString().IndexOf("0d", StringComparison.Ordinal);

        var newBText = oldBText.WithChanges(
            new TextChange(
                new TextSpan(changeStart, length: 1),
                "1"));

        var updatedB = new TestAdditionalText(b.Path, newBText);

        driver = driver.ReplaceAdditionalText(b, updatedB);
        driver = driver.RunGenerators(compilation);

        var afterAdditionalTextChange = GetSyntaxTreeOutputs(driver);

        Assert.Equal(
            IncrementalStepRunReason.Cached,
            afterAdditionalTextChange[a.Path].Reason);

        Assert.Equal(
            IncrementalStepRunReason.Modified,
            afterAdditionalTextChange[b.Path].Reason);

        Assert.Equal(
            IncrementalStepRunReason.Cached,
            afterAdditionalTextChange[c.Path].Reason);

        Assert.Same(
            initial[a.Path].SyntaxTree,
            afterAdditionalTextChange[a.Path].SyntaxTree);

        Assert.NotSame(
            initial[b.Path].SyntaxTree,
            afterAdditionalTextChange[b.Path].SyntaxTree);

        Assert.Same(
            initial[c.Path].SyntaxTree,
            afterAdditionalTextChange[c.Path].SyntaxTree);

        var oldBTree = Assert.IsType<ComponentSyntaxTree>(initial[b.Path].SyntaxTree);
        var newBTree = Assert.IsType<ComponentSyntaxTree>(afterAdditionalTextChange[b.Path].SyntaxTree);

        Assert.Equal(oldBText.ToString(), oldBTree.GetRoot().ToFullString());
        Assert.Equal(newBText.ToString(), newBTree.GetRoot().ToFullString());

        var oldState = Assert.Single(oldBTree.GetRoot().Members.OfType<StateDeclarationSyntax>());
        var newState = Assert.Single(newBTree.GetRoot().Members.OfType<StateDeclarationSyntax>());

        Assert.Equal("0d", oldState.Initializer.ToFullString());
        Assert.Equal("1d", newState.Initializer.ToFullString());

        var changedComponentReasons = GetOutputReasons(driver, GeneratedComponentsTrackingName);

        Assert.Equal(2, changedComponentReasons.Length);
        Assert.Equal(
            1,
            changedComponentReasons.Count(static reason =>
                reason == IncrementalStepRunReason.Modified));

        Assert.Equal(
            1,
            changedComponentReasons.Count(static reason =>
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));

        AssertReused(
            Assert.Single(
                GetOutputReasons(
                    driver,
                    GeneratedExternalAkcssTrackingName)));

        Assert.Equal(1, a.ReadCount);
        Assert.Equal(1, b.ReadCount);
        Assert.Equal(1, updatedB.ReadCount);
        Assert.Equal(1, c.ReadCount);

        var changedCompilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                "internal sealed class Changed { }",
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));

        driver = driver.RunGenerators(changedCompilation);

        var afterCompilationChange = GetSyntaxTreeOutputs(driver);

        Assert.All(afterCompilationChange.Values, static output =>
            Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));

        Assert.All(
            GetOutputReasons(driver, GeneratedComponentsTrackingName),
            AssertReused);

        Assert.All(
            GetOutputReasons(driver, GeneratedExternalAkcssTrackingName),
            AssertReused);

        Assert.Equal(1, a.ReadCount);
        Assert.Equal(1, b.ReadCount);
        Assert.Equal(1, updatedB.ReadCount);
        Assert.Equal(1, c.ReadCount);
    }

    [Fact]
    public void GenerateSources_EmitsComponentExternalAndInlineAkcssDocuments()
    {
        const string rootNamespace = "Demo";

        const string componentSource =
            """
            using Avalonia.Controls;
            using Demo.Styles.Shared.akcss;

            @akcss {
                @using Avalonia.Controls;

                .local {
                    Width: 10;
                }
            }

            <Border class="local shared" />
            """;

        const string externalAkcssSource =
            """
            @using Avalonia.Controls;

            .shared {
                Height: 20;
            }

            @utilities {
                .spacing-(double value) {
                    Width: value;
                }
            }
            """;

        const string csharpSource =
            """
            using Akbura;
            using Akbura.Engine;

            namespace Demo.Views;

            public partial class PlannerView : AkburaControl
            {
                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }
            }
            """;

        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            "AkburaBlackSilenceGeneratorTests");

        var componentPath = Path.Combine(
            projectDirectory,
            "Views",
            "PlannerView.akbura");

        var externalAkcssPath = Path.Combine(
            projectDirectory,
            "Styles",
            "Shared.akcss");

        var component = new TestAdditionalText(
            componentPath,
            SourceText.From(componentSource));

        var externalAkcss = new TestAdditionalText(
            externalAkcssPath,
            SourceText.From(externalAkcssSource));

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            rootNamespace,
            projectDirectory);

        var compilation = CreateCompilation(csharpSource);
        var driver = CreateDriver(optionsProvider, component, externalAkcss);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var result = Assert.Single(driver.GetRunResult().Results);

        Assert.Null(result.Exception);
        Assert.Equal(4, result.GeneratedSources.Length);
        GetHotReloadService(result);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        var componentGenerated = Assert.Single(
            result.GeneratedSources,
            static source =>
                source.HintName.StartsWith(
                    "Akbura.Component.",
                    StringComparison.Ordinal));

        var akcssGenerated = result.GeneratedSources
            .Where(static source =>
                source.HintName.StartsWith(
                    "Akbura.Akcss.",
                    StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, akcssGenerated.Length);

        var externalGenerated = Assert.Single(
            akcssGenerated,
            static source =>
                source.SourceText.ToString().Contains(
                    "SourcePath = \"Styles/Shared.akcss\";",
                    StringComparison.Ordinal));

        var inlineGenerated = Assert.Single(
            akcssGenerated,
            static source =>
                source.SourceText.ToString().Contains(
                    "SourcePath = \"Views/PlannerView.akbura\";",
                    StringComparison.Ordinal));

        var componentText = componentGenerated.SourceText.ToString();
        var externalText = externalGenerated.SourceText.ToString();
        var inlineText = inlineGenerated.SourceText.ToString();

        Assert.Contains(
            "SourcePath = \"Styles/Shared.akcss\",",
            externalText,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "SourcePath = \"" +
            AkcssGeneratedModuleNames.NormalizeSourcePath(
                externalAkcssPath) +
            "\"",
            externalText,
            StringComparison.Ordinal);

        Assert.Contains(
            "SourcePath = \"Views/PlannerView.akbura\",",
            inlineText,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "SourcePath = \"" +
            AkcssGeneratedModuleNames.NormalizeSourcePath(
                componentPath) +
            "\"",
            inlineText,
            StringComparison.Ordinal);

        Assert.Contains(
            "partial class PlannerView",
            componentText,
            StringComparison.Ordinal);

        Assert.Contains(
            "private sealed class Style_0 : global::Akbura.Akcss.AkcssClass",
            externalText,
            StringComparison.Ordinal);

        Assert.Contains(
            "private sealed class Style_1 : global::Akbura.Akcss.AkcssUtility<",
            externalText,
            StringComparison.Ordinal);

        Assert.Contains(
            "[global::Akbura.CompilerAnotations.InlinedStyleAttribute]",
            inlineText,
            StringComparison.Ordinal);

        var externalModuleType = AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(
            rootNamespace,
            "Styles/Shared.akcss");

        var inlineModuleType = AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(
            rootNamespace,
            "Views/PlannerView.akbura.inline.0.akcss");

        Assert.Contains(
            externalModuleType,
            componentText,
            StringComparison.Ordinal);

        Assert.Contains(
            inlineModuleType,
            componentText,
            StringComparison.Ordinal);

        var compilationDiagnostics = outputCompilation
            .GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            compilationDiagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                compilationDiagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            string.Join(
                Environment.NewLine + Environment.NewLine,
                result.GeneratedSources.Select(static source => source.SourceText.ToString())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingProjectOptions_UpdatesAkcssIdentityWithoutReadingTextAgain(bool changeProjectDirectory)
    {
        const string source =
            """
            @using Avalonia.Controls;

            Border.shared {
                Height: 20;
            }
            """;

        var projectDirectory = Path.Combine(Path.GetTempPath(), "BlackSilenceProjectOptionsTests");
        var sourcePath = Path.Combine(projectDirectory, "Styles", "Shared.akcss");
        var file = new TestAdditionalText(sourcePath, SourceText.From(source));
        var options = new TestAnalyzerConfigOptionsProvider("Demo", projectDirectory);
        var compilation = CreateCompilation(string.Empty);
        var driver = CreateDriver(options, file);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var initialCompilation, out _);
        AssertGeneratedCompilation(driver, initialCompilation);

        var initialTree = Assert.IsType<AkcssSyntaxTree>(GetSyntaxTreeOutputs(driver)[sourcePath].SyntaxTree);

        Assert.Equal("Demo.Styles.Shared.akcss", initialTree.LogicalName);
        Assert.Equal(1, file.ReadCount);

        var rootNamespace = changeProjectDirectory ? "Demo" : "Renamed";
        var updatedDirectory = changeProjectDirectory ? Path.Combine(projectDirectory, "Styles") : projectDirectory;
        var relativePath = changeProjectDirectory ? "Shared.akcss" : "Styles/Shared.akcss";

        driver = driver.WithUpdatedAnalyzerConfigOptions(
            new TestAnalyzerConfigOptionsProvider(rootNamespace, updatedDirectory));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        AssertGeneratedCompilation(driver, updatedCompilation);

        var updatedTree = Assert.IsType<AkcssSyntaxTree>(GetSyntaxTreeOutputs(driver)[sourcePath].SyntaxTree);

        Assert.NotSame(initialTree, updatedTree);
        Assert.Equal(AkcssGeneratedModuleNames.GetMetadataName(rootNamespace, relativePath), updatedTree.LogicalName);
        Assert.Equal(1, file.ReadCount);
        Assert.Equal(IncrementalStepRunReason.Cached, Assert.Single(GetOutputReasons(driver, SourceTextsTrackingName)));

        var generatedSource = Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources,
            static source =>
                source.HintName != HotReloadServiceWriter.HintName);
        var generatedText = generatedSource.SourceText.ToString();

        Assert.Contains("namespace " + rootNamespace + ".Generated", generatedText, StringComparison.Ordinal);
        Assert.Contains("SourcePath = \"" + relativePath + "\";", generatedText, StringComparison.Ordinal);
        Assert.Contains(updatedTree.LogicalName, generatedText, StringComparison.Ordinal);
        Assert.Equal(
            IncrementalStepRunReason.Modified,
            Assert.Single(GetOutputReasons(driver, GeneratedExternalAkcssTrackingName)));
    }

    [Fact]
    public void GenerateSources_PreservesBatchOrderAndContentAfterUnrelatedCompilationChanges()
    {
        const int componentCount = 4;
        var projectDirectory = Path.Combine(Path.GetTempPath(), "BlackSilenceBatchTests");
        var files = new TestAdditionalText[componentCount * 2];
        var declarations = new string[componentCount];

        for (var i = 0; i < componentCount; i++)
        {
            var componentSource =
                "using Avalonia.Controls;\r\n" +
                "using Demo.Styles.Shared" + i + ".akcss;\r\n" +
                "@akcss {\r\n" +
                "    @using Avalonia.Controls;\r\n" +
                "    .local { Width: " + (i + 10) + "; }\r\n" +
                "}\r\n" +
                "<Border class=\"local shared\" />\r\n";

            var akcssSource =
                "@using Avalonia.Controls;\r\n" +
                ".shared { Height: " + (i + 20) + "; }\r\n" +
                "@utilities { .width-(double value) { Width: value; } }\r\n";

            files[i * 2] = new TestAdditionalText(
                Path.Combine(projectDirectory, "Views", "View" + i + ".akbura"),
                SourceText.From(componentSource));

            files[i * 2 + 1] = new TestAdditionalText(
                Path.Combine(projectDirectory, "Styles", "Shared" + i + ".akcss"),
                SourceText.From(akcssSource));

            declarations[i] =
                "public partial class View" + i + " : global::Akbura.AkburaControl\r\n" +
                "{\r\n" +
                "    public View" + i + "() : base(global::Akbura.Engine.AkburaEngine.Empty) { }\r\n" +
                "}\r\n";
        }

        var compilation = CreateCompilation("namespace Demo.Views;\r\n" + string.Join("\r\n", declarations));
        var options = new TestAnalyzerConfigOptionsProvider("Demo", projectDirectory);
        var driver = CreateDriver(options, files);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var initialCompilation, out _);
        AssertGeneratedCompilation(driver, initialCompilation);

        var initialResult = Assert.Single(driver.GetRunResult().Results);
        var initialSources = initialResult.GeneratedSources;

        GetHotReloadService(initialResult);
        Assert.Equal(componentCount * 3 + 1, initialSources.Length);
        Assert.Equal(initialSources.Length, initialSources.Select(static source => source.HintName).Distinct().Count());
        Assert.Equal(componentCount, GetOutputReasons(driver, GeneratedComponentsTrackingName).Length);
        Assert.Equal(componentCount, GetOutputReasons(driver, GeneratedExternalAkcssTrackingName).Length);
        Assert.Equal(componentCount, GetOutputReasons(driver, GeneratedInlineAkcssTrackingName).Length);
        Assert.Equal(
            IncrementalStepRunReason.New,
            Assert.Single(
                GetOutputReasons(
                    driver,
                    GeneratedProjectSourcesTrackingName)));

        var changedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "internal sealed class Unrelated { }",
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));

        driver = driver.RunGeneratorsAndUpdateCompilation(changedCompilation, out var updatedCompilation, out _);
        AssertGeneratedCompilation(driver, updatedCompilation);

        var updatedSources = Assert.Single(driver.GetRunResult().Results).GeneratedSources;

        Assert.Equal(initialSources.Length, updatedSources.Length);

        for (var i = 0; i < initialSources.Length; i++)
        {
            Assert.Equal(initialSources[i].HintName, updatedSources[i].HintName);
            Assert.True(initialSources[i].SourceText.ContentEquals(updatedSources[i].SourceText));
        }

        Assert.All(GetOutputReasons(driver, GeneratedComponentsTrackingName), AssertReused);
        Assert.All(GetOutputReasons(driver, GeneratedExternalAkcssTrackingName), AssertReused);
        Assert.All(GetOutputReasons(driver, GeneratedInlineAkcssTrackingName), AssertReused);
        Assert.All(GetOutputReasons(driver, GeneratedProjectSourcesTrackingName), AssertReused);
        Assert.All(files, static file => Assert.Equal(1, file.ReadCount));
    }

    [Fact]
    public void GenerateSources_CanceledRunDoesNotReadFilesAndDriverCanRunAgain()
    {
        var file = new TestAdditionalText(
            "Canceled.akcss",
            SourceText.From("@using Avalonia.Controls; Border.shared { Height: 20; }"));

        var compilation = CreateCompilation(string.Empty);
        var driver = CreateDriver(s_emptyOptionsProvider, file);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => driver.RunGenerators(compilation, cancellation.Token));
        Assert.Equal(0, file.ReadCount);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        AssertGeneratedCompilation(driver, outputCompilation);

        var result = Assert.Single(driver.GetRunResult().Results);
        var generatedSources = result.GeneratedSources;

        Assert.Equal(2, generatedSources.Length);
        GetHotReloadService(result);
        Assert.Single(
            generatedSources,
            static source =>
                source.HintName != HotReloadServiceWriter.HintName);
        Assert.Equal(1, file.ReadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenerateSources_CompilesHooksAndTemplatesAcrossSourceComponents(bool reverseInputs)
    {
        const string contentSource =
            """
            using Avalonia.Controls;

            param string Title = "Preview";

            <TextBlock Text={Title} />
            """;

        const string hostSource =
            """
            using Akbura.Hooks;
            using Avalonia.Controls;
            using Avalonia.Controls.Templates;

            param IDataTemplate View;
            state Control? preview = null;

            useEffect(() =>
            {
                preview = View.Build(null);
            }, [View]);

            <ContentControl Content={preview} />
            """;

        const string hookSource =
            """
            using Akbura.Hooks;
            using Avalonia.Controls;
            using Demo.Components;

            param DataTemplates Target;
            state string title = useAvaloniaProperty(Target, Demo.Components.DataTemplates.TitleProperty);

            <TextBlock Text={title} />
            """;

        const string pageSource =
            """
            using Avalonia.Controls.Templates;
            using Demo.Components;

            <PreviewHost>
                <PreviewHost.View>
                    <DataTemplates Title="From template" />
                </PreviewHost.View>
            </PreviewHost>
            """;

        var projectDirectory = Path.Combine(Path.GetTempPath(), "BlackSilenceSourceComponentRegressionTests");
        var files = new TestAdditionalText[]
        {
            new(Path.Combine(projectDirectory, "Page.akbura"), SourceText.From(pageSource)),
            new(Path.Combine(projectDirectory, "Components", "PreviewHost.akbura"), SourceText.From(hostSource)),
            new(Path.Combine(projectDirectory, "Components", "HookReader.akbura"), SourceText.From(hookSource)),
            new(Path.Combine(projectDirectory, "Components", "DataTemplates.akbura"), SourceText.From(contentSource)),
        };

        if (reverseInputs)
        {
            Array.Reverse(files);
        }

        var compilation = CreateCompilation(string.Empty);
        var options = new TestAnalyzerConfigOptionsProvider("Demo", projectDirectory);
        var driver = CreateDriver(options, files);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        AssertGeneratedCompilation(driver, outputCompilation);

        var sources = Assert.Single(driver.GetRunResult().Results).GeneratedSources;

        Assert.Equal(5, sources.Length);
        Assert.Single(
            sources,
            static source =>
                source.HintName == HotReloadServiceWriter.HintName);
        Assert.Contains(sources, static source => source.SourceText.ToString().Contains(
            "global::Akbura.Hooks.EffectHooks.useEffect(",
            StringComparison.Ordinal));
        Assert.Contains(sources, static source => source.SourceText.ToString().Contains(
            "global::Akbura.Hooks.AvaloniaPropertyHooks.useAvaloniaProperty<global::Demo.Components.DataTemplates, string>(",
            StringComparison.Ordinal));
    }

    private static void AssertGeneratedCompilation(GeneratorDriver driver, Compilation compilation)
    {
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);

        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);

        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            string.Join(Environment.NewLine, result.GeneratedSources.Select(static source => source.SourceText.ToString())));
    }

    private static string[] GetDeclaredFieldContract(
        Compilation compilation,
        string metadataName)
    {
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName(metadataName));

        return type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(static field => !field.IsImplicitlyDeclared)
            .Select(static field =>
                field.MetadataName + "|" +
                field.Type.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat) + "|" +
                field.IsStatic + "|" +
                field.IsReadOnly + "|" +
                field.IsConst)
            .OrderBy(static field => field, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] GetDeclaredRuntimeMemberContract(
        Compilation compilation,
        string metadataName)
    {
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName(metadataName));

        return type.GetMembers()
            .Where(static member =>
                !member.IsImplicitlyDeclared &&
                member is IFieldSymbol or IMethodSymbol)
            .Select(static member => member switch
            {
                IFieldSymbol field =>
                    "F|" +
                    field.MetadataName + "|" +
                    field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" +
                    field.IsStatic + "|" +
                    field.IsReadOnly + "|" +
                    field.IsConst,
                IMethodSymbol method =>
                    "M|" +
                    method.MetadataName + "|" +
                    method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" +
                    method.IsStatic + "|" +
                    string.Join(
                        ",",
                        method.Parameters.Select(static parameter =>
                            parameter.Type.ToDisplayString(
                                SymbolDisplayFormat.FullyQualifiedFormat))),
                _ => throw new InvalidOperationException(),
            })
            .OrderBy(static member => member, StringComparer.Ordinal)
            .ToArray();
    }

    private static GeneratedSourceResult GetGeneratedComponentSource(
        GeneratorDriver driver)
    {
        return Assert.Single(
            Assert.Single(driver.GetRunResult().Results).GeneratedSources,
            static source => source.HintName.StartsWith(
                "Akbura.Component.",
                StringComparison.Ordinal));
    }

    private static SemanticEdit[] GetChangedGeneratedMethodEdits(
        Compilation initialCompilation,
        Compilation updatedCompilation,
        string metadataName)
    {
        var initialMethods = GetGeneratedMethods(
            initialCompilation,
            metadataName);
        var updatedMethods = GetGeneratedMethods(
            updatedCompilation,
            metadataName);

        Assert.Equal(
            initialMethods.Keys.OrderBy(
                static key => key,
                StringComparer.Ordinal),
            updatedMethods.Keys.OrderBy(
                static key => key,
                StringComparer.Ordinal));

        return updatedMethods
            .Where(pair => !GetMethodSyntax(initialMethods[pair.Key])
                .IsEquivalentTo(GetMethodSyntax(pair.Value)))
            .OrderBy(
                static pair => pair.Key,
                StringComparer.Ordinal)
            .Select(pair => new SemanticEdit(
                SemanticEditKind.Update,
                initialMethods[pair.Key],
                pair.Value))
            .ToArray();
    }

    private static Dictionary<string, IMethodSymbol> GetGeneratedMethods(
        Compilation compilation,
        string metadataName)
    {
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName(metadataName));

        return type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static method =>
                !method.IsImplicitlyDeclared &&
                !method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            .ToDictionary(GetMethodKey, StringComparer.Ordinal);
    }

    private static string GetMethodKey(IMethodSymbol method)
    {
        return method.MetadataName + "|" +
            method.Arity + "|" +
            method.IsStatic + "|" +
            method.ReturnType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat) + "|" +
            string.Join(
                ",",
                method.Parameters.Select(static parameter =>
                    parameter.RefKind + ":" +
                    parameter.Type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    private static SyntaxNode GetMethodSyntax(IMethodSymbol method)
    {
        return Assert.Single(
            method.DeclaringSyntaxReferences).GetSyntax();
    }

    private static void AssertDebugEditAndContinueDelta(
        string fileName,
        string originalSource,
        string editedSource,
        string hostSource,
        string componentMetadataName,
        string? supportingFileName = null,
        string? supportingSource = null)
    {
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(AkburaBlackSilenceGeneratorTests),
            Guid.NewGuid().ToString("N"));
        var componentPath = Path.Combine(projectDirectory, fileName);
        var original = new TestAdditionalText(
            componentPath,
            SourceText.From(originalSource));
        var edited = new TestAdditionalText(
            componentPath,
            SourceText.From(editedSource));
        var options = new TestAnalyzerConfigOptionsProvider(
            "Demo",
            projectDirectory);
        var baseCompilation = CreateCompilation(hostSource);
        var additionalTexts = supportingFileName == null
            ? new AdditionalText[] { original }
            :
            [
                original,
                new TestAdditionalText(
                    Path.Combine(projectDirectory, supportingFileName),
                    SourceText.From(supportingSource!)),
            ];
        var driver = CreateDebugDriver(options, additionalTexts);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            baseCompilation,
            out var initialCompilation,
            out _);
        AssertGeneratedCompilation(driver, initialCompilation);

        driver = driver.ReplaceAdditionalText(original, edited);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            baseCompilation,
            out var updatedCompilation,
            out _);
        AssertGeneratedCompilation(driver, updatedCompilation);

        var semanticEdits = GetChangedGeneratedMethodEdits(
            initialCompilation,
            updatedCompilation,
            componentMetadataName);
        Assert.NotEmpty(semanticEdits);

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var initialEmit = initialCompilation.Emit(
            peStream,
            pdbStream,
            options: new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(
            initialEmit.Success,
            FormatDiagnostics(initialEmit.Diagnostics));

        var peImage = ImmutableArray.Create(peStream.ToArray());
        using var module = ModuleMetadata.CreateFromImage(peImage);
        using var peReader = new PEReader(peImage);
        var baseline = EmitBaseline.CreateInitialBaseline(
            initialCompilation,
            module,
            static _ => default,
            methodHandle => GetLocalSignature(peReader, methodHandle),
            hasPortableDebugInformation: true);
        using var metadataDelta = new MemoryStream();
        using var ilDelta = new MemoryStream();
        using var pdbDelta = new MemoryStream();

        var difference = updatedCompilation.EmitDifference(
            baseline,
            semanticEdits,
            static _ => false,
            metadataDelta,
            ilDelta,
            pdbDelta,
            CancellationToken.None);

        Assert.DoesNotContain(
            difference.Diagnostics,
            static diagnostic =>
                diagnostic.Id is "ENC0009" or "ENC0020" or "ENC0033");
        Assert.True(
            difference.Success,
            FormatDiagnostics(difference.Diagnostics));
        Assert.NotEmpty(difference.UpdatedMethods);
        Assert.True(metadataDelta.Length > 0);
        Assert.True(ilDelta.Length > 0);
        Assert.True(pdbDelta.Length > 0);
    }

    private static StandaloneSignatureHandle GetLocalSignature(
        PEReader peReader,
        MethodDefinitionHandle methodHandle)
    {
        var method = peReader.GetMetadataReader()
            .GetMethodDefinition(methodHandle);

        return method.RelativeVirtualAddress == 0
            ? default
            : peReader.GetMethodBody(method.RelativeVirtualAddress)
                .LocalSignature;
    }

    private static string FormatDiagnostics(
        IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic => diagnostic.ToString()));
    }

    private static FieldInfo GetRequiredStaticField(
        Type type,
        string name)
    {
        const BindingFlags flags =
            BindingFlags.Static |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        return Assert.IsAssignableFrom<FieldInfo>(
            type.GetField(name, flags));
    }

    private static int GetStaticCallCount(Type type)
    {
        return Assert.IsType<int>(
            GetRequiredStaticField(type, "CallCount").GetValue(null));
    }

    private static GeneratorDriver CreateDriver(
        AnalyzerConfigOptionsProvider optionsProvider,
        params AdditionalText[] additionalTexts)
    {
        return CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaBlackSilenceGenerator().AsSourceGenerator(),
            ],
            additionalTexts: additionalTexts,
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            optionsProvider: optionsProvider,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));
    }

    private static GeneratorDriver CreateDebugDriver(
        AnalyzerConfigOptionsProvider optionsProvider,
        params AdditionalText[] additionalTexts)
    {
        return CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaBlackSilenceGenerator().AsSourceGenerator(),
            ],
            additionalTexts: additionalTexts,
            parseOptions: CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithPreprocessorSymbols("DEBUG"),
            optionsProvider: optionsProvider,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));
    }

    private static GeneratedSourceResult GetHotReloadService(
        GeneratorRunResult result)
    {
        return Assert.Single(
            result.GeneratedSources,
            static source =>
                source.HintName == HotReloadServiceWriter.HintName);
    }

    private static CSharpCompilation CreateHotReloadCompilation()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(
            LanguageVersion.Preview);
        var references = SymbolTests.CreateAvaloniaReferences()
            .Where(static reference =>
                reference.Display is not { } path ||
                !Path.GetFileName(path).StartsWith(
                    "Akbura",
                    StringComparison.OrdinalIgnoreCase));

        return CSharpCompilation.Create(
            "AkburaHotReloadServiceTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(string.Empty, parseOptions),
            ],
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        return CSharpCompilation.Create(
            "AkburaBlackSilenceGeneratorTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(source, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static Dictionary<string, (AkburaSyntaxTree SyntaxTree, IncrementalStepRunReason Reason)>
        GetSyntaxTreeOutputs(GeneratorDriver driver)
    {
        var generatorResult = Assert.Single(driver.GetRunResult().Results);

        Assert.Null(generatorResult.Exception);
        Assert.True(
            generatorResult.TrackedSteps.TryGetValue(
                SyntaxTreesTrackingName,
                out var steps));

        return steps
            .SelectMany(static step => step.Outputs)
            .Select(static output =>
                (
                    SyntaxTree: Assert.IsType<AkburaSyntaxTree>(
                        output.Value,
                        exactMatch: false),
                    output.Reason
                ))
            .ToDictionary(
                static output => output.SyntaxTree.FilePath,
                static output => output,
                StringComparer.Ordinal);
    }

    private static IncrementalStepRunReason[] GetOutputReasons(
        GeneratorDriver driver,
        string trackingName)
    {
        var generatorResult = Assert.Single(driver.GetRunResult().Results);

        Assert.Null(generatorResult.Exception);
        Assert.True(
            generatorResult.TrackedSteps.TryGetValue(
                trackingName,
                out var steps));

        return steps
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToArray();
    }

    private static void AssertReused(IncrementalStepRunReason reason)
    {
        Assert.True(
            reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected Cached or Unchanged, but received {reason}.");
    }

    private sealed class TestAdditionalText : AdditionalText
    {
        private readonly SourceText _sourceText;
        private int _readCount;

        public TestAdditionalText(string path, SourceText sourceText)
        {
            Path = path;
            _sourceText = sourceText;
        }

        public override string Path { get; }

        public int ReadCount => Volatile.Read(ref _readCount);

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);

            return _sourceText;
        }
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions s_emptyOptions =
            new TestAnalyzerConfigOptions(
                new Dictionary<string, string>());

        private readonly AnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(
            string rootNamespace,
            string projectDirectory)
        {
            _globalOptions = new TestAnalyzerConfigOptions(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["build_property.RootNamespace"] = rootNamespace,
                    ["build_property.ProjectDir"] = projectDirectory,
                });
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return s_emptyOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return s_emptyOptions;
        }
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public TestAnalyzerConfigOptions(
            IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (_values.TryGetValue(key, out var result))
            {
                value = result;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
