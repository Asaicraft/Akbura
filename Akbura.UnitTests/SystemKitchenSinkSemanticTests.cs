using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using CSharpSymbolKind = Microsoft.CodeAnalysis.SymbolKind;

namespace Akbura.UnitTests;

public sealed class SystemKitchenSinkSemanticTests
{
    [Fact]
    public void SystemKitchenSink_ResolvesStateTypesAndCSharpSymbols()
    {
        const string code =
            """
            using System;
            using System.Collections.Concurrent;
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            using System.Globalization;
            using System.Linq;
            using System.Text.Json;
            using System.Text.RegularExpressions;
            using System.Xml.Linq;

            using Avalonia.Controls;
            using Akbura.Hooks;

            using Microsoft.Extensions.Logging;

            namespace Demo.Pages;

            inject ILogger<SystemKitchenSink> logger;

            param int UserId = 1;
            param bind string Search = "";

            state bool showSystemInfo = true;
            state int effectRuns = 0;
            state string lastJson = "";
            state ObservableCollection<string> logs = new ObservableCollection<string>();
            state ObservableCollection<string> matches = new ObservableCollection<string>();
            state ConcurrentDictionary<string, int> counters = new ConcurrentDictionary<string, int>();
            state Dictionary<string, string> metadata = new Dictionary<string, string>();
            state List<string> history = new List<string>();

            useEffect(() => {
                effectRuns++;

                var query = Search ?? string.Empty;

                var numbers = Enumerable
                    .Range(1, 12)
                    .Where(x => x % 2 == 0)
                    .Select(x => x * UserId)
                    .ToArray();

                var regex = new Regex(
                    @"[\p{L}\p{Nd}_]+",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                );

                var found = regex
                    .Matches(query)
                    .Select(x => x.Value)
                    .DefaultIfEmpty("<no matches>")
                    .ToArray();

                matches.Clear();

                foreach(var item in found) {
                    matches.Add(item);
                }

                var xml = new XElement(
                    "effect",
                    new XAttribute("userId", UserId),
                    new XElement("query", query),
                    new XElement("sum", numbers.Sum())
                );

                var payload = new {
                    UserId,
                    Search = query,
                    Numbers = numbers,
                    Matches = found,
                    Xml = xml.ToString(SaveOptions.DisableFormatting)
                };

                var json = JsonSerializer.Serialize(payload);

                logs.Insert(0, json);
                logger.LogInformation("Created {Count}", numbers.Length);
            }, [UserId, Search]);

            if(showSystemInfo)
            {
                var startupMessage = $"Running on {Environment.MachineName} / {Environment.OSVersion}";

                Console.WriteLine(startupMessage);

                <TextBlock class="muted" Text={startupMessage}/>
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Pages/SystemKitchenSink.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();

        Assert.Equal(code.Length, root.FullWidth);
        AssertStateType(semanticModel, root, "logs", "System.Collections.ObjectModel.ObservableCollection<string>");
        AssertStateType(semanticModel, root, "matches", "System.Collections.ObjectModel.ObservableCollection<string>");
        AssertStateType(semanticModel, root, "counters", "System.Collections.Concurrent.ConcurrentDictionary<string, int>");
        AssertStateType(semanticModel, root, "metadata", "System.Collections.Generic.Dictionary<string, string>");
        AssertStateType(semanticModel, root, "history", "System.Collections.Generic.List<string>");

        var useEffect = root.Members.OfType<CSharpStatementSyntax>()
            .Single(static statement => statement.Tokens.ToFullString().TrimStart().StartsWith("useEffect", StringComparison.Ordinal));
        CSharpStatementSyntax[] effectStatements = [useEffect];

        var numbersReferences = GetReferences(semanticModel, effectStatements, "var numbers = Enumerable");
        AssertNamedType(numbersReferences, "Enumerable", "System.Linq.Enumerable");
        AssertMethod(numbersReferences, "Range", "System.Linq.Enumerable", "Range");
        AssertMethod(numbersReferences, "Where", "System.Linq.Enumerable", "Where");
        AssertMethod(numbersReferences, "Select", "System.Linq.Enumerable", "Select");
        AssertMethod(numbersReferences, "ToArray", "System.Linq.Enumerable", "ToArray");
        AssertAkburaReference<IParamSymbol>(numbersReferences, "UserId");

        var regexReferences = GetReferences(semanticModel, effectStatements, "var regex = new Regex");
        AssertNamedType(regexReferences, "Regex", "System.Text.RegularExpressions.Regex");
        AssertNamedType(regexReferences, "RegexOptions", "System.Text.RegularExpressions.RegexOptions");
        AssertField(regexReferences, "IgnoreCase", "System.Text.RegularExpressions.RegexOptions", "IgnoreCase");
        AssertField(regexReferences, "CultureInvariant", "System.Text.RegularExpressions.RegexOptions", "CultureInvariant");

        var foundReferences = GetReferences(semanticModel, effectStatements, "var found = regex");
        AssertLocal(foundReferences, "regex");
        AssertMethod(foundReferences, "Matches", "System.Text.RegularExpressions.Regex", "Matches");
        AssertMethod(foundReferences, "Select", "System.Linq.Enumerable", "Select");
        AssertMethod(foundReferences, "DefaultIfEmpty", "System.Linq.Enumerable", "DefaultIfEmpty");
        AssertMethod(foundReferences, "ToArray", "System.Linq.Enumerable", "ToArray");

        var xmlReferences = GetReferences(semanticModel, effectStatements, "var xml = new XElement");
        AssertNamedType(xmlReferences, "XElement", "System.Xml.Linq.XElement");
        AssertNamedType(xmlReferences, "XAttribute", "System.Xml.Linq.XAttribute");
        AssertAkburaReference<IParamSymbol>(xmlReferences, "UserId");
        AssertLocal(xmlReferences, "query");
        AssertLocal(xmlReferences, "numbers");
        AssertMethod(xmlReferences, "Sum", "System.Linq.Enumerable", "Sum");

        var jsonReferences = GetReferences(semanticModel, effectStatements, "var json = JsonSerializer.Serialize");
        AssertNamedType(jsonReferences, "JsonSerializer", "System.Text.Json.JsonSerializer");
        AssertMethod(jsonReferences, "Serialize", "System.Text.Json.JsonSerializer", "Serialize");
        AssertLocal(jsonReferences, "payload");

        var logReferences = GetReferences(semanticModel, effectStatements, "logger.LogInformation");
        AssertAkburaReference<IInjectSymbol>(logReferences, "logger");
        AssertMethod(logReferences, "LogInformation", "Microsoft.Extensions.Logging.ILogger<Demo.Pages.SystemKitchenSink>", "LogInformation");
        AssertLocal(logReferences, "numbers");

        var conditional = root.Members.OfType<CSharpStatementSyntax>()
            .Single(static statement => statement.Tokens.ToFullString().TrimStart().StartsWith("if", StringComparison.Ordinal));
        var conditionalStatements = conditional.Body!.Tokens.OfType<CSharpStatementSyntax>().ToArray();

        var startupReferences = GetReferences(semanticModel, conditionalStatements, "var startupMessage");
        AssertNamedType(startupReferences, "Environment", "System.Environment");
        AssertProperty(startupReferences, "MachineName", "System.Environment", "MachineName");
        AssertProperty(startupReferences, "OSVersion", "System.Environment", "OSVersion");

        var consoleReferences = GetReferences(semanticModel, conditionalStatements, "Console.WriteLine");
        AssertNamedType(consoleReferences, "Console", "System.Console");
        AssertMethod(consoleReferences, "WriteLine", "System.Console", "WriteLine");
        AssertLocal(consoleReferences, "startupMessage");

        var markupRoot = Assert.IsType<MarkupRootSyntax>(
            Assert.Single(conditional.Body.Tokens.OfType<MarkupRootSyntax>()));
        var textBlockSymbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(
            semanticModel.GetSymbolInfo(markupRoot.Element).Symbol);
        Assert.Equal("TextBlock", textBlockSymbol.Name);
        Assert.Equal("Avalonia.Controls.TextBlock", textBlockSymbol.CSharpDefinition.Symbol?.ToDisplayString());
    }

    private static void AssertStateType(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSyntax root,
        string stateName,
        string expectedType)
    {
        var state = root.Members
            .OfType<StateDeclarationSyntax>()
            .Single(state => state.Name.Identifier.ValueText == stateName);
        var symbol = Assert.IsAssignableFrom<IStateSymbol>(
            semanticModel.GetSymbolInfo(state).Symbol);

        Assert.Equal(expectedType, symbol.Type.Symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
    }

    private static ImmutableArray<CSharpSymbolReference> GetReferences(
        AkburaSemanticModel semanticModel,
        IEnumerable<CSharpStatementSyntax> statements,
        string statementText)
    {
        var statement = statements.Single(statement => statement.ToFullString().Contains(statementText));
        return semanticModel.GetCSharpSymbolReferences(statement);
    }

    private static void AssertAkburaReference<TSymbol>(
        ImmutableArray<CSharpSymbolReference> references,
        string name)
        where TSymbol : AkburaSymbol
    {
        Assert.Contains(references, reference =>
            reference.Name == name &&
            reference.AkburaSymbol is TSymbol &&
            reference.CSharpDefinition.Symbol != null);
    }

    private static void AssertNamedType(
        ImmutableArray<CSharpSymbolReference> references,
        string name,
        string expectedType)
    {
        Assert.Contains(references, reference =>
            reference.Name == name &&
            reference.CSharpDefinition.Symbol is INamedTypeSymbol symbol &&
            symbol.Kind == CSharpSymbolKind.NamedType &&
            symbol.ToDisplayString() == expectedType);
    }

    private static void AssertMethod(
        ImmutableArray<CSharpSymbolReference> references,
        string name,
        string expectedContainingType,
        string expectedMetadataName)
    {
        var methods = references
            .Where(reference => reference.Name == name)
            .Select(reference => reference.CSharpDefinition.Symbol)
            .OfType<IMethodSymbol>()
            .ToArray();

        Assert.True(methods.Any(method =>
            method.MetadataName == expectedMetadataName &&
            method.ContainingType.ToDisplayString() == expectedContainingType),
            "Expected method " + expectedContainingType + "." + expectedMetadataName +
            " for reference '" + name + "'. Actual references: " +
            string.Join(", ", references.Select(reference =>
                reference.Name + "=" + (reference.CSharpDefinition.Symbol?.ToDisplayString() ?? "<null>"))));
    }

    private static void AssertField(
        ImmutableArray<CSharpSymbolReference> references,
        string name,
        string expectedContainingType,
        string expectedMetadataName)
    {
        var field = Assert.Single(references, reference =>
            reference.Name == name &&
            reference.CSharpDefinition.Symbol is IFieldSymbol field &&
            field.MetadataName == expectedMetadataName &&
            field.ContainingType.ToDisplayString() == expectedContainingType);
        Assert.NotNull(field.CSharpDefinition.Symbol);
    }

    private static void AssertProperty(
        ImmutableArray<CSharpSymbolReference> references,
        string name,
        string expectedContainingType,
        string expectedMetadataName)
    {
        var property = Assert.Single(references, reference =>
            reference.Name == name &&
            reference.CSharpDefinition.Symbol is Microsoft.CodeAnalysis.IPropertySymbol property &&
            property.MetadataName == expectedMetadataName &&
            property.ContainingType.ToDisplayString() == expectedContainingType);
        Assert.NotNull(property.CSharpDefinition.Symbol);
    }

    private static void AssertLocal(
        ImmutableArray<CSharpSymbolReference> references,
        string name)
    {
        Assert.Contains(references, reference =>
            reference.Name == name &&
            reference.CSharpDefinition.Symbol is ILocalSymbol local &&
            local.Name == name);
    }

    private static AkburaSemanticModel CreateSemanticModel(AkburaSyntaxTree syntaxTree)
    {
        return new AkburaCompilation(
                CreateCSharpCompilation(),
                [syntaxTree],
                rootNamespace: "Demo",
                projectDirectory: Environment.CurrentDirectory)
            .GetSemanticModel(syntaxTree);
    }

    private static CSharpCompilation CreateCSharpCompilation()
    {
        const string csharpCode =
            """
            namespace Microsoft.Extensions.Logging;

            public interface ILogger<T>
            {
                void LogInformation(string message);

                void LogInformation(string message, params object[] args);
            }
            """;

        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var references = trustedPlatformAssemblies
            .Concat([
                MetadataReference.CreateFromFile(typeof(Avalonia.Controls.TextBlock).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Akbura.AkburaControl).Assembly.Location),
            ])
            .GroupBy(static reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        return CSharpCompilation.Create(
            "SystemKitchenSinkSemanticTests",
            [CSharpSyntaxTree.ParseText(csharpCode)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
