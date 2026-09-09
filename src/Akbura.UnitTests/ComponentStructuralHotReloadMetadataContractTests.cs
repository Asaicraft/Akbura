using Akbura.BlackSilence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class ComponentStructuralHotReloadMetadataContractTests
{
    [Fact]
    public void DebugStructural_EagerChildInsertionAndRemoval_PreserveCompleteMetadataContract()
    {
        const string original =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "<StackPanel>\r\n" +
            "    <TextBlock x.Name=\"title\" Text=\"Title\" />\r\n" +
            "    <Button Content=\"Save\" />\r\n" +
            "</StackPanel>\r\n";
        const string edited =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "<StackPanel>\r\n" +
            "    <Border Width=\"24\" />\r\n" +
            "    <TextBlock x.Name=\"title\" Text=\"Title\" />\r\n" +
            "    <Button Content=\"Save\" />\r\n" +
            "</StackPanel>\r\n";

        var initialContract = GenerateEmitAndReadContract(
            "Page.akbura",
            original,
            string.Empty,
            "Demo.Page");
        var updatedContract = GenerateEmitAndReadContract(
            "Page.akbura",
            edited,
            string.Empty,
            "Demo.Page");

        AssertContractEqual(initialContract, updatedContract);
    }

    [Fact]
    public void DebugStructural_AkcssValueEdit_WithStableOperationShape_PreservesCompleteMetadataContract()
    {
        const string original =
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
            "<Border class=\"card\" width-${DirectPadding {spacing + 1}} />\r\n";
        const string edited =
            "using Avalonia.Controls;\r\n" +
            "using Demo.Extensions;\r\n" +
            "\r\n" +
            "@akcss {\r\n" +
            "    @using Avalonia.Controls;\r\n" +
            "    .card { Height: 24; }\r\n" +
            "    @utilities {\r\n" +
            "        Control.width-(double value) { Width: value; }\r\n" +
            "    }\r\n" +
            "}\r\n" +
            "\r\n" +
            "state double spacing = 4;\r\n" +
            "\r\n" +
            "<Border class=\"card\" width-${DirectPadding {spacing + 2}} />\r\n";
        const string hostSource =
            "namespace Demo.Extensions;\r\n" +
            "\r\n" +
            "public sealed class DirectPaddingExtension\r\n" +
            "{\r\n" +
            "    public DirectPaddingExtension(double value)\r\n" +
            "    {\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    public double ProvideValue(System.IServiceProvider services) => 0;\r\n" +
            "}\r\n";

        var initialContract = GenerateEmitAndReadContract(
            "StyledPage.akbura",
            original,
            hostSource,
            "Demo.StyledPage");
        var updatedContract = GenerateEmitAndReadContract(
            "StyledPage.akbura",
            edited,
            hostSource,
            "Demo.StyledPage");

        AssertContractEqual(initialContract, updatedContract);
    }

    [Fact]
    public void DebugStructural_EventChangeAndRemoval_PreserveCompleteMetadataContract()
    {
        const string original =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "<StackPanel>\r\n" +
            "    <Button Click={OnPrimaryClick} />\r\n" +
            "</StackPanel>\r\n";
        const string changed =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "<StackPanel>\r\n" +
            "    <Button Click={OnSecondaryClick} />\r\n" +
            "</StackPanel>\r\n";
        const string removed =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "<StackPanel>\r\n" +
            "    <Button />\r\n" +
            "</StackPanel>\r\n";
        const string hostSource =
            "using Avalonia.Interactivity;\r\n" +
            "\r\n" +
            "namespace Demo;\r\n" +
            "\r\n" +
            "public partial class EventPage\r\n" +
            "{\r\n" +
            "    private void OnPrimaryClick(object? sender, RoutedEventArgs eventArgs)\r\n" +
            "    {\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    private void OnSecondaryClick(object? sender, RoutedEventArgs eventArgs)\r\n" +
            "    {\r\n" +
            "    }\r\n" +
            "}\r\n";

        var initialContract = GenerateEmitAndReadContract(
            "EventPage.akbura",
            original,
            hostSource,
            "Demo.EventPage");
        var changedContract = GenerateEmitAndReadContract(
            "EventPage.akbura",
            changed,
            hostSource,
            "Demo.EventPage");
        var removedContract = GenerateEmitAndReadContract(
            "EventPage.akbura",
            removed,
            hostSource,
            "Demo.EventPage");

        AssertContractEqual(initialContract, changedContract);
        AssertContractEqual(initialContract, removedContract);
    }

    [Fact]
    public void DebugStructural_BindingRemoval_PreservesCompleteMetadataContract()
    {
        const string original =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "<StackPanel>\r\n" +
            "    <TextBox x.Name=\"source\" Text=\"Initial\" />\r\n" +
            "    <TextBlock Text=${Binding #source.Text} />\r\n" +
            "</StackPanel>\r\n";
        const string removed =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "<StackPanel>\r\n" +
            "    <TextBox x.Name=\"source\" Text=\"Initial\" />\r\n" +
            "    <TextBlock />\r\n" +
            "</StackPanel>\r\n";

        var initialContract = GenerateEmitAndReadContract(
            "BindingPage.akbura",
            original,
            string.Empty,
            "Demo.BindingPage");
        var removedContract = GenerateEmitAndReadContract(
            "BindingPage.akbura",
            removed,
            string.Empty,
            "Demo.BindingPage");

        AssertContractEqual(initialContract, removedContract);
    }

    private static string[] GenerateEmitAndReadContract(
        string fileName,
        string componentSource,
        string hostSource,
        string componentMetadataName)
    {
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(ComponentStructuralHotReloadMetadataContractTests),
            Guid.NewGuid().ToString("N"));
        var component = new TestAdditionalText(
            Path.Combine(projectDirectory, fileName),
            SourceText.From(componentSource));
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols("DEBUG");
        var compilation = CSharpCompilation.Create(
            "StructuralHotReloadContract",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(hostSource, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaBlackSilenceGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                component,
            ],
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(
                "Demo",
                projectDirectory));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out _);

        AssertGeneratedCompilation(driver, outputCompilation);

        using var image = new MemoryStream();
        var emitResult = outputCompilation.Emit(image);
        Assert.True(
            emitResult.Success,
            string.Join(
                Environment.NewLine,
                emitResult.Diagnostics.Select(static diagnostic => diagnostic.ToString())));

        var metadataReference = MetadataReference.CreateFromImage(image.ToArray());
        var metadataCompilation = CSharpCompilation.Create(
            "StructuralHotReloadContractReader",
            references: SymbolTests.CreateAvaloniaReferences().Append(metadataReference),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                metadataImportOptions: MetadataImportOptions.All));
        var assembly = Assert.IsAssignableFrom<IAssemblySymbol>(
            metadataCompilation.GetAssemblyOrModuleSymbol(metadataReference));
        var componentType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            assembly.GetTypeByMetadataName(componentMetadataName));

        var contract = new List<string>();
        AppendTypeContract(componentType, componentMetadataName, contract);

        return contract
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertContractEqual(
        string[] expected,
        string[] actual)
    {
        var removed = expected
            .Except(actual, StringComparer.Ordinal)
            .ToArray();
        var added = actual
            .Except(expected, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            "Removed metadata:\r\n" +
            string.Join("\r\n", removed) +
            "\r\n\r\nAdded metadata:\r\n" +
            string.Join("\r\n", added));
    }

    private static void AppendTypeContract(
        INamedTypeSymbol type,
        string path,
        List<string> contract)
    {
        contract.Add(
            "T|" +
            path + "|" +
            type.TypeKind + "|" +
            type.DeclaredAccessibility + "|" +
            type.IsStatic + "|" +
            type.IsAbstract + "|" +
            type.IsSealed + "|" +
            TypeName(type.BaseType) + "|" +
            string.Join(
                ",",
                type.Interfaces.Select(TypeName).OrderBy(static item => item, StringComparer.Ordinal)));

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field:
                    contract.Add(
                        "F|" +
                        path + "|" +
                        field.MetadataName + "|" +
                        TypeName(field.Type) + "|" +
                        field.DeclaredAccessibility + "|" +
                        field.IsStatic + "|" +
                        field.IsReadOnly + "|" +
                        field.IsConst + "|" +
                        FormatConstant(field.ConstantValue));
                    break;

                case IMethodSymbol method:
                    contract.Add(
                        "M|" +
                        path + "|" +
                        method.MetadataName + "|" +
                        method.MethodKind + "|" +
                        method.DeclaredAccessibility + "|" +
                        method.IsStatic + "|" +
                        method.IsAbstract + "|" +
                        method.IsVirtual + "|" +
                        method.IsOverride + "|" +
                        method.IsSealed + "|" +
                        method.RefKind + "|" +
                        TypeName(method.ReturnType) + "|" +
                        string.Join(
                            ",",
                            method.TypeParameters.Select(FormatTypeParameter)) + "|" +
                        string.Join(
                            ",",
                            method.Parameters.Select(FormatParameter)));
                    break;

                case IPropertySymbol property:
                    contract.Add(
                        "P|" +
                        path + "|" +
                        property.MetadataName + "|" +
                        TypeName(property.Type) + "|" +
                        property.DeclaredAccessibility + "|" +
                        property.IsStatic + "|" +
                        property.IsReadOnly + "|" +
                        property.IsWriteOnly + "|" +
                        property.RefKind + "|" +
                        string.Join(
                            ",",
                            property.Parameters.Select(FormatParameter)));
                    break;

                case IEventSymbol eventSymbol:
                    contract.Add(
                        "E|" +
                        path + "|" +
                        eventSymbol.MetadataName + "|" +
                        TypeName(eventSymbol.Type) + "|" +
                        eventSymbol.DeclaredAccessibility + "|" +
                        eventSymbol.IsStatic);
                    break;
            }
        }

        foreach (var nestedType in type.GetTypeMembers())
        {
            AppendTypeContract(
                nestedType,
                path + "+" + nestedType.MetadataName,
                contract);
        }
    }

    private static string FormatParameter(IParameterSymbol parameter)
    {
        return parameter.MetadataName + ":" +
            parameter.RefKind + ":" +
            TypeName(parameter.Type) + ":" +
            parameter.IsParams + ":" +
            parameter.IsOptional + ":" +
            (parameter.HasExplicitDefaultValue
                ? FormatConstant(parameter.ExplicitDefaultValue)
                : "<none>");
    }

    private static string FormatTypeParameter(ITypeParameterSymbol parameter)
    {
        return parameter.MetadataName + ":" +
            parameter.Variance + ":" +
            parameter.HasReferenceTypeConstraint + ":" +
            parameter.HasValueTypeConstraint + ":" +
            parameter.HasUnmanagedTypeConstraint + ":" +
            parameter.HasNotNullConstraint + ":" +
            parameter.HasConstructorConstraint + ":" +
            string.Join(
                ",",
                parameter.ConstraintTypes.Select(TypeName).OrderBy(static item => item, StringComparer.Ordinal));
    }

    private static string TypeName(ITypeSymbol? type)
    {
        return type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "<null>";
    }

    private static string FormatConstant(object? value)
    {
        return value switch
        {
            null => "<null>",
            string text => "string:" + text,
            char character => "char:" + (int)character,
            _ => value.GetType().FullName + ":" + value,
        };
    }

    private static void AssertGeneratedCompilation(
        GeneratorDriver driver,
        Compilation compilation)
    {
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);

        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    private sealed class TestAdditionalText : AdditionalText
    {
        private readonly SourceText _sourceText;

        public TestAdditionalText(string path, SourceText sourceText)
        {
            Path = path;
            _sourceText = sourceText;
        }

        public override string Path { get; }

        public override SourceText GetText(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
