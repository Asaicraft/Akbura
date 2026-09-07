using Akbura.Diagnostics;
using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class AkburaDiagnosticCoreTests
{
    [Fact]
    public void SameIssueAcrossPublishers_HasOneIdentityAndMergedProvenance()
    {
        var generator = CreateRecord() with
        {
            Provenance = DiagnosticProvenance.BlackSilenceGenerator,
            DocumentVersion = 7,
        };
        var workspace = generator with
        {
            Provenance = DiagnosticProvenance.Workspaces,
            DocumentVersion = 8,
        };

        Assert.True(AkburaDiagnosticCanonicalComparer.Instance.Equals(generator, workspace));
        Assert.Equal(generator.LogicalId, workspace.LogicalId);
        var actual = Assert.Single(AkburaDiagnosticCollection.Deduplicate([generator, workspace]));
        Assert.Equal(DiagnosticProvenance.BlackSilenceGenerator | DiagnosticProvenance.Workspaces, actual.Provenance);
        Assert.Equal(8, actual.DocumentVersion);
        Assert.Equal(generator.Span, actual.Span);
    }

    [Fact]
    public void HashCollisions_DoNotHideDifferentMessagesLocationsOrSeverities()
    {
        var first = CreateRecord();
        AkburaDiagnosticRecord[] input =
        [
            first,
            first with { Message = "Different message" },
            first with { Span = new TextSpan(3, 1) },
            first with { Severity = AkburaDiagnosticSeverity.Warning },
            first with { Kind = AkburaDiagnosticKind.Infrastructure },
            first with { AdditionalLocations = [new("Other.akbura", new TextSpan(0, 1), default)] },
            first with { Properties = ImmutableDictionary<string, string?>.Empty.Add("member", "Other") },
            first with { Provenance = DiagnosticProvenance.Workspaces },
        ];

        var actual = AkburaDiagnosticCollection.Deduplicate(input, static _ => 0);

        Assert.Equal(input.Length - 1, actual.Length);
        Assert.Contains(actual, diagnostic => diagnostic.Message == "Different message");
        Assert.Contains(actual, diagnostic => diagnostic.Kind == AkburaDiagnosticKind.Infrastructure);
    }

    [Fact]
    public void DictionaryInsertionOrderAndTransportTags_DoNotChangeCanonicalIdentity()
    {
        var first = CreateRecord() with
        {
            Properties = ImmutableDictionary<string, string?>.Empty.Add("a", "1").Add("b", null),
        };
        var second = first with
        {
            Properties = ImmutableDictionary<string, string?>.Empty.Add("b", null).Add("a", "1")
                .Add("akbura.origin", "workspaces").Add("akbura.document-version", "12"),
        };

        Assert.True(AkburaDiagnosticCanonicalComparer.Instance.Equals(first, second));
        Assert.Equal(first.LogicalId, second.LogicalId);
        Assert.Equal(AkburaDiagnosticCanonicalComparer.Instance.GetHashCode(first),
            AkburaDiagnosticCanonicalComparer.Instance.GetHashCode(second));
        Assert.Single(AkburaDiagnosticCollection.Deduplicate([first, second]));
    }

    [Fact]
    public void FileIdentity_UsesHostPathCaseSensitivity()
    {
        var first = CreateRecord() with { FilePath = "Views/Page.akbura" };
        var second = first with { FilePath = "views/page.akbura" };
        var expectedEqual = Path.DirectorySeparatorChar == '\\';

        Assert.Equal(expectedEqual, AkburaDiagnosticCanonicalComparer.Instance.Equals(first, second));
        Assert.Equal(expectedEqual, first.LogicalId == second.LogicalId);
        if (expectedEqual)
        {
            Assert.True(AkburaDiagnosticCanonicalComparer.Instance.Equals(first,
                first with { FilePath = "Views\\Page.akbura" }));
        }
    }

    [Fact]
    public void RoslynAdapter_CachesDescriptorsWithoutFreezingMessagesOrMetadata()
    {
        var first = CreateRecord() with
        {
            Message = "First {literal} message",
            AdditionalLocations = [new("Other.akbura", new TextSpan(4, 2), new(new(1, 0), new(1, 2)))],
            Properties = ImmutableDictionary<string, string?>.Empty.Add("member", "Width"),
            Provenance = DiagnosticProvenance.BlackSilenceGenerator,
            DocumentVersion = 42,
        };
        var second = first with { Message = "Second message" };
        var left = AkburaDiagnosticAdapter.ToRoslyn(first);
        var right = AkburaDiagnosticAdapter.ToRoslyn(second);

        Assert.Same(left.Descriptor, right.Descriptor);
        Assert.Equal(first.Message, left.GetMessage());
        Assert.Equal(second.Message, right.GetMessage());
        Assert.Equal(first.Span, left.Location.SourceSpan);
        Assert.Equal(first.LineSpan, left.Location.GetLineSpan().Span);
        Assert.Equal("Other.akbura", Assert.Single(left.AdditionalLocations).GetLineSpan().Path);
        Assert.Equal("Width", left.Properties["member"]);
        Assert.Equal("black-silence", left.Properties["akbura.origin"]);
        Assert.Equal("semantic", left.Properties["akbura.kind"]);
        Assert.Equal("42", left.Properties["akbura.document-version"]);
        Assert.Equal(first.LogicalId, left.Properties["akbura.logical-id"]);
    }

    [Theory]
    [InlineData("// 😀 русский\r\n<Border Width=\"unterminated")]
    [InlineData("// 😀 русский\r\n<StackPanel<Button /></StackPanel>")]
    public void SyntaxCollector_MapsClampedUtf16LocationsFromExactSourceText(string source)
    {
        var tree = ComponentSyntaxTree.ParseText(SourceText.From(source), "Страница.akbura");
        var diagnostics = AkburaDiagnosticEngine.Collect(tree, includeSemantic: false);

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(AkburaDiagnosticKind.Syntax, diagnostic.Kind);
            Assert.Equal(tree.FilePath, diagnostic.FilePath);
            Assert.InRange(diagnostic.Span.Start, 0, source.Length);
            Assert.InRange(diagnostic.Span.End, diagnostic.Span.Start, source.Length);
            Assert.Equal(tree.Text.Lines.GetLinePositionSpan(diagnostic.Span), diagnostic.LineSpan);
            Assert.Equal(diagnostic.LineSpan, AkburaDiagnosticAdapter.ToRoslyn(diagnostic).Location.GetLineSpan().Span);
        });
    }

    [Theory]
    [InlineData("GlobalUsings.akbura", "using System;\r\n<object />")]
    [InlineData("GlobalUsings.akcss", "@using System;\r\n.invalid { }")]
    public void GlobalUsingValidation_DoesNotRequireSemanticModel(string path, string source)
    {
        AkburaSyntaxTree tree = path.EndsWith(".akcss", StringComparison.Ordinal)
            ? AkcssSyntaxTree.ParseText(SourceText.From(source), path)
            : ComponentSyntaxTree.ParseText(SourceText.From(source), path);

        var diagnostic = Assert.Single(AkburaDiagnosticEngine.Collect(tree, includeSemantic: false));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_GlobalUsingsFileContainsNonUsing, diagnostic.Id);
        Assert.Equal($"Global usings file '{path}' may contain only using directives", diagnostic.Message);
        Assert.Equal(AkburaDiagnosticKind.Semantic, diagnostic.Kind);
        Assert.Equal(tree.Text.Lines.GetLinePositionSpan(diagnostic.Span), diagnostic.LineSpan);
    }

    [Fact]
    public void SemanticCollector_ReportsInlineAkcssOnceAlongsideComponentDiagnostics()
    {
        const string source = "using Avalonia.Controls;\r\n" +
            "@akcss { @using Avalonia.Controls; Border.local { Missing: 1; } }\r\n" +
            "<Border class=\"local\" OtherMissing=\"1\" />\r\n";
        var tree = ComponentSyntaxTree.ParseText(SourceText.From(source), "View.akbura");
        var model = new AkburaCompilation(CreateCompilation(), [tree], "Demo").GetSemanticModel(tree);
        var actual = AkburaDiagnosticEngine.Collect(tree, model);

        Assert.Single(actual, diagnostic => diagnostic.Id == ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound);
        Assert.Single(actual, diagnostic => diagnostic.Id == ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound);
        foreach (var expected in model.GetSemanticDiagnostics(tree.GetRoot()))
        {
            Assert.Contains(actual, diagnostic => diagnostic.Id == expected.Code &&
                diagnostic.Message == expected.Message && diagnostic.Span == expected.Span);
        }
    }

    [Fact]
    public void ImportedSemanticDiagnostic_UsesOwningFileAndItsCrLfUnicodeLineMap()
    {
        var component = ComponentSyntaxTree.ParseText(SourceText.From("<object />"), "View.akbura");
        var external = AkcssSyntaxTree.ParseText(
            SourceText.From("// 😀 русский\r\nBorder.shared { Missing: 1; }\r\n"), "Shared.akcss");
        var compilation = new AkburaCompilation(CreateCompilation(), [component, external], "Demo");
        var model = compilation.GetSemanticModel(component);
        var syntax = Assert.Single(external.GetRoot().DescendantNodes().OfType<AkcssAssignmentSyntax>());
        var diagnostic = new AkburaSemanticDiagnostic(syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound, ["Missing", "Border"]);

        var actual = AkburaDiagnosticEngine.CreateSemanticRecord(component, model, diagnostic);

        Assert.Equal(external.FilePath, actual.FilePath);
        Assert.Equal(syntax.Span, actual.Span);
        Assert.Equal(external.Text.Lines.GetLinePositionSpan(syntax.Span), actual.LineSpan);
        Assert.Equal(1, actual.LineSpan.Start.Line);
        Assert.Equal("Shared.akcss", AkburaDiagnosticAdapter.ToRoslyn(actual).Location.GetLineSpan().Path);
    }

    [Fact]
    public void MalformedMessageArguments_FallBackToCodeWithoutRetainingArguments()
    {
        var tree = ComponentSyntaxTree.ParseText(SourceText.From("<object />"), "View.akbura");
        var model = new AkburaCompilation(CreateCompilation(), [tree]).GetSemanticModel(tree);
        var diagnostic = new AkburaSemanticDiagnostic(tree.GetRoot(),
            ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound, []);

        var actual = AkburaDiagnosticEngine.CreateSemanticRecord(tree, model, diagnostic);

        Assert.Equal(diagnostic.Code, actual.Message);
        Assert.Equal(diagnostic.Code, AkburaDiagnosticAdapter.ToRoslyn(actual).GetMessage());
    }

    [Fact]
    public void Collector_CancellationCannotReturnPartialResults()
    {
        var tree = ComponentSyntaxTree.ParseText(SourceText.From("<Border>"), "View.akbura");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AkburaDiagnosticEngine.Collect(tree, cancellationToken: cancellation.Token));
    }

    private static AkburaDiagnosticRecord CreateRecord() => new()
    {
        Id = "AKBURA_TEST",
        Severity = AkburaDiagnosticSeverity.Error,
        Message = "Example error",
        FilePath = "View.akbura",
        Span = new TextSpan(1, 2),
        LineSpan = new(new(0, 1), new(0, 3)),
        Kind = AkburaDiagnosticKind.Semantic,
    };

    private static CSharpCompilation CreateCompilation() => CSharpCompilation.Create(
        "AkburaDiagnosticCoreTests",
        references: SymbolTests.CreateAvaloniaReferences(),
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
