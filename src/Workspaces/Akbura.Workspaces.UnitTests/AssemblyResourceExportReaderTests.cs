using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using System.Xml.Linq;

namespace Akbura.Workspaces.UnitTests;

public sealed class AssemblyResourceExportReaderTests
{
    private const string AttributeSource = """
        using System;

        namespace Akbura.CompilerAnotations;

        [AttributeUsage(
            AttributeTargets.Assembly,
            AllowMultiple = true,
            Inherited = false)]
        public sealed class ExportResourceForAkburaCompletionAttribute :
            Attribute
        {
            public ExportResourceForAkburaCompletionAttribute(
                string dictionaryPath,
                string key,
                Type resourceType)
            {
            }
        }
        """;

    [Fact]
    public void Read_ReturnsExportsFromReferencedAssembly()
    {
        var contract = EmitReference("Akbura", AttributeSource);
        var theme = EmitReference(
            "Acme.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "/Styles.axaml",
                "CardBackgroundBrush",
                typeof(string))]
            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "CardPadding",
                typeof(double))]
            [assembly: ExportResourceForAkburaCompletion(
                @"Themes\Compact.axaml",
                "CompactSpacing",
                typeof(int))]
            """,
            contract);
        var compilation = CreateCompilation(
            "Consumer",
            contract,
            theme);

        var exports = AssemblyResourceExportReader.Read(compilation);

        Assert.Equal(3, exports.Length);
        Assert.All(
            exports,
            export => Assert.Equal(
                "Acme.Theme",
                export.Dictionary.Assembly.Name));
        Assert.Equal(
            ["CardBackgroundBrush", "CardPadding"],
            exports
                .Where(export =>
                    export.Dictionary.Path.Value == "Styles.axaml")
                .Select(export => export.Key)
                .OrderBy(static key => key));

        var compact = Assert.Single(
            exports,
            export => export.Key == "CompactSpacing");
        Assert.Equal(
            "Themes/Compact.axaml",
            compact.Dictionary.Path.Value);
        Assert.Equal(
            "int",
            compact.ResourceType?.ToDisplayString());
    }

    [Fact]
    public void Read_PreservesExportsInMetadataOnlyReferenceAssembly()
    {
        var contract = EmitReference("Akbura", AttributeSource);
        var referenceAssembly = EmitReference(
            "ReferenceOnly.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Themes/Main.axaml",
                "ReferenceOnlyBrush",
                typeof(string))]
            """,
            metadataOnly: true,
            contract);
        var compilation = CreateCompilation(
            "Consumer",
            contract,
            referenceAssembly);

        var export = Assert.Single(
            AssemblyResourceExportReader.Read(compilation));

        Assert.Equal("ReferenceOnlyBrush", export.Key);
        Assert.Equal(
            "ReferenceOnly.Theme",
            export.Dictionary.Assembly.Name);
        Assert.Equal(
            "Themes/Main.axaml",
            export.Dictionary.Path.Value);
    }

    [Fact]
    public void Read_ReturnsExportsFromCurrentSourceAssembly()
    {
        var contract = EmitReference("Akbura", AttributeSource);
        var compilation = CreateCompilation(
            "Source.Theme",
            contract,
            source: """
                using Akbura.CompilerAnotations;

                [assembly: ExportResourceForAkburaCompletion(
                    "Styles.axaml",
                    "SourceBrush",
                    typeof(string))]
                """);

        var export = Assert.Single(
            AssemblyResourceExportReader.Read(compilation));

        Assert.Equal("SourceBrush", export.Key);
        Assert.Equal(
            "Source.Theme",
            export.Dictionary.Assembly.Name);
    }

    [Fact]
    public void Read_ReturnsExportsFromCompilationReference()
    {
        var contract = EmitReference("Akbura", AttributeSource);
        var themeCompilation = CreateCompilation(
            "SourceReference.Theme",
            contract,
            source: """
                using Akbura.CompilerAnotations;

                [assembly: ExportResourceForAkburaCompletion(
                    "Styles.axaml",
                    "ProjectReferenceBrush",
                    typeof(string))]
                """);
        var compilation = CreateCompilation(
            "Consumer",
            contract,
            themeCompilation.ToMetadataReference());

        var export = Assert.Single(
            AssemblyResourceExportReader.Read(compilation));

        Assert.Equal("ProjectReferenceBrush", export.Key);
        Assert.Equal(
            "SourceReference.Theme",
            export.Dictionary.Assembly.Name);
    }

    [Fact]
    public void Read_DoesNotExecuteUserCodeWhileReadingExports()
    {
        var contract = EmitReference(
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
                    throw new System.InvalidOperationException(
                        "Attribute constructors must not execute.");
                }
            }
            """);
        var theme = EmitReference(
            "Canary.Theme",
            """
            using Akbura.CompilerAnotations;
            using System.Runtime.CompilerServices;

            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "CanaryResource",
                typeof(Canary.ExplosiveResource))]

            namespace Canary;

            public sealed class ExplosiveResource
            {
                public ExplosiveResource()
                {
                    throw new System.InvalidOperationException(
                        "Resource constructors must not execute.");
                }
            }

            internal static class ModuleCanary
            {
                [ModuleInitializer]
                internal static void Initialize()
                {
                    throw new System.InvalidOperationException(
                        "Module initializers must not execute.");
                }
            }
            """,
            contract);
        var compilation = CreateCompilation(
            "Consumer",
            contract,
            theme);

        var export = Assert.Single(
            AssemblyResourceExportReader.Read(compilation));

        Assert.Equal("CanaryResource", export.Key);
        Assert.Equal(
            "Canary.ExplosiveResource",
            export.ResourceType?.ToDisplayString());
    }

    [Fact]
    public void Read_PreservesKeyWhenExportedTypeCannotBeResolved()
    {
        var contract = EmitReference("Akbura", AttributeSource);
        var resourceTypes = EmitReference(
            "Missing.ResourceTypes",
            "namespace Missing.ResourceTypes; public sealed class Brush { }");
        var theme = EmitReference(
            "MissingType.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "UnknownBrush",
                typeof(Missing.ResourceTypes.Brush))]
            """,
            contract,
            resourceTypes);
        var compilation = CreateCompilation(
            "Consumer",
            contract,
            theme);

        var export = Assert.Single(
            AssemblyResourceExportReader.Read(compilation));

        Assert.Equal("UnknownBrush", export.Key);
        Assert.Null(export.ResourceType);
    }

    [Fact]
    public void Read_SkipsMalformedExportsWithoutDroppingValidExports()
    {
        var contract = EmitReference("Akbura", AttributeSource);
        var theme = EmitReference(
            "Malformed.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "",
                "EmptyPath",
                typeof(string))]
            [assembly: ExportResourceForAkburaCompletion(
                "../Outside.axaml",
                "Traversal",
                typeof(string))]
            [assembly: ExportResourceForAkburaCompletion(
                "C:/Absolute.axaml",
                "DrivePath",
                typeof(string))]
            [assembly: ExportResourceForAkburaCompletion(
                "avares://Other/Styles.axaml",
                "UriPath",
                typeof(string))]
            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                " ",
                typeof(string))]
            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "ValidBrush",
                typeof(string))]
            """,
            contract);
        var compilation = CreateCompilation(
            "Consumer",
            contract,
            theme);

        var export = Assert.Single(
            AssemblyResourceExportReader.Read(compilation));

        Assert.Equal("ValidBrush", export.Key);
    }

    [Fact]
    public void Read_RejectsSameNamedAttributeFromAnotherAssembly()
    {
        var contract = EmitReference("Akbura", AttributeSource);
        var counterfeitContract = EmitReference(
            "Counterfeit.Attributes",
            AttributeSource);
        var counterfeitTheme = EmitReference(
            "Counterfeit.Theme",
            """
            using Akbura.CompilerAnotations;

            [assembly: ExportResourceForAkburaCompletion(
                "Styles.axaml",
                "CounterfeitBrush",
                typeof(string))]
            """,
            counterfeitContract);
        var compilation = CreateCompilation(
            "Consumer",
            contract,
            counterfeitContract,
            counterfeitTheme);

        var exports = AssemblyResourceExportReader.Read(compilation);

        Assert.Empty(exports);
    }

    [Fact]
    public void ResourceIdentity_UsesFullAssemblyIdentityAndNormalizedPath()
    {
        var firstAssembly = ResourceAssemblyIdentity.Create(
            new AssemblyIdentity(
                "Acme.Theme",
                new Version(1, 0, 0, 0)));
        var sameAssembly = ResourceAssemblyIdentity.Create(
            new AssemblyIdentity(
                "Acme.Theme",
                new Version(1, 0, 0, 0)));
        var updatedAssembly = ResourceAssemblyIdentity.Create(
            new AssemblyIdentity(
                "Acme.Theme",
                new Version(2, 0, 0, 0)));

        Assert.Equal(firstAssembly, sameAssembly);
        Assert.NotEqual(firstAssembly, updatedAssembly);
        Assert.True(firstAssembly.HasSimpleName("acme.theme"));

        Assert.True(ResourcePath.TryCreateExportPath(
            "/Themes/./Compact.axaml",
            out var rootedPath));
        Assert.True(ResourcePath.TryCreateExportPath(
            @"Themes\Shared\..\Compact.axaml",
            out var backslashPath));
        Assert.Equal("Themes/Compact.axaml", rootedPath.Value);
        Assert.Equal(rootedPath, backslashPath);

        var firstDictionary = new ResourceDictionaryIdentity(
            firstAssembly,
            rootedPath);
        var sameDictionary = new ResourceDictionaryIdentity(
            sameAssembly,
            backslashPath);
        var updatedDictionary = new ResourceDictionaryIdentity(
            updatedAssembly,
            rootedPath);
        Assert.Equal(firstDictionary, sameDictionary);
        Assert.NotEqual(firstDictionary, updatedDictionary);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/")]
    [InlineData("//server/Styles.axaml")]
    [InlineData(@"\server\Styles.axaml")]
    [InlineData("../Styles.axaml")]
    [InlineData("Themes/../../Styles.axaml")]
    [InlineData("C:/Styles.axaml")]
    [InlineData("avares://Theme/Styles.axaml")]
    [InlineData("Styles.axaml?variant=dark")]
    [InlineData("Styles.axaml#theme")]
    public void ResourcePath_RejectsForbiddenExportPath(string path)
    {
        Assert.False(ResourcePath.TryCreateExportPath(path, out _));
    }

    [Fact]
    public void BuiltInHints_AreSmallTypedAndUnique()
    {
        Assert.InRange(BuiltInAvaloniaResourceHints.All.Length, 1, 32);
        Assert.Equal(
            BuiltInAvaloniaResourceHints.All.Length,
            BuiltInAvaloniaResourceHints.All
                .Select(static hint => hint.Key)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            BuiltInAvaloniaResourceHints.All,
            static hint =>
            {
                Assert.Equal(
                    "Avalonia.Media.Color",
                    hint.ResourceTypeMetadataName);
                Assert.Equal(
                    "Built-in Avalonia resource hint",
                    hint.SourceDescription);
            });
        Assert.Contains(
            BuiltInAvaloniaResourceHints.All,
            static hint => hint.Key == "SystemAccentColor");
    }

    [Fact]
    public void AkburaManifest_MatchesEveryResourceInStylesExactly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var stylesPath = Path.Combine(
            repositoryRoot,
            "src",
            "Akbura",
            "Styles.axaml");
        var manifestPath = Path.Combine(
            repositoryRoot,
            "src",
            "Akbura",
            "ResourceCompletionExports.cs");

        var document = XDocument.Load(stylesPath);
        var xamlNamespace =
            XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var expected = document.Root!
            .Elements()
            .Where(element =>
                element.Attribute(xamlNamespace + "Key") != null)
            .Select(element => new
            {
                Key = (string?)element.Attribute(xamlNamespace + "Key"),
                Type = GetManifestType(element.Name.LocalName),
            })
            .ToDictionary(
                static entry => entry.Key!,
                static entry => entry.Type,
                StringComparer.Ordinal);

        var manifestText = File.ReadAllText(manifestPath);
        var actual = ReadManifest(manifestText);

        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
        {
            Assert.True(
                actual.TryGetValue(pair.Key, out var actualType),
                $"Resource '{pair.Key}' is missing from the export manifest.");
            Assert.Equal(pair.Value, actualType);
        }
    }

    private static IReadOnlyDictionary<string, string> ReadManifest(string source)
    {
        var result = new Dictionary<string, string>(
            StringComparer.Ordinal);
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();

        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            if (attribute.Parent is not AttributeListSyntax
                {
                    Target.Identifier.ValueText: "assembly",
                } ||
                attribute.Name.ToString() != "ExportResource" ||
                attribute.ArgumentList?.Arguments is not { Count: 3 }
                    arguments ||
                arguments[0].Expression is not LiteralExpressionSyntax
                    dictionaryLiteral ||
                dictionaryLiteral.Token.ValueText != "Styles.axaml" ||
                arguments[1].Expression is not LiteralExpressionSyntax
                    keyLiteral ||
                arguments[2].Expression is not TypeOfExpressionSyntax
                    typeOfExpression)
            {
                continue;
            }

            var key = keyLiteral.Token.ValueText;
            Assert.True(
                result.TryAdd(key, typeOfExpression.Type.ToString()),
                $"Resource '{key}' is exported more than once.");
        }

        return result;
    }

    private static string GetManifestType(string xamlType)
    {
        return xamlType switch
        {
            "Double" => "double",
            "FontFamily" => "global::Avalonia.Media.FontFamily",
            "FontWeight" => "global::Avalonia.Media.FontWeight",
            "CornerRadius" => "global::Avalonia.CornerRadius",
            "BoxShadows" => "global::Avalonia.Media.BoxShadows",
            "SolidColorBrush" =>
                "global::Avalonia.Media.SolidColorBrush",
            _ => throw new InvalidOperationException(
                $"Resource type '{xamlType}' is not mapped by the manifest test."),
        };
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, params MetadataReference[] references)
    {
        return CreateCompilation(
            assemblyName,
            references,
            source: string.Empty);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, MetadataReference reference, string source)
    {
        return CreateCompilation(
            assemblyName,
            [reference],
            source);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, IReadOnlyCollection<MetadataReference> references, string source)
    {
        return CSharpCompilation.Create(
            assemblyName,
            string.IsNullOrEmpty(source)
                ? []
                : [CSharpSyntaxTree.ParseText(source)],
            [GetPlatformReference(), .. references],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static PortableExecutableReference EmitReference(string assemblyName, string source, params MetadataReference[] references)
    {
        return EmitReference(
            assemblyName,
            source,
            metadataOnly: false,
            references);
    }

    private static PortableExecutableReference EmitReference(string assemblyName, string source, bool metadataOnly, params MetadataReference[] references)
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
                metadataOnly: metadataOnly,
                includePrivateMembers: !metadataOnly));
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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Akbura.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Akbura repository root.");
    }
}
