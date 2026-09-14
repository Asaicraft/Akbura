using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

[Collection(nameof(WorkspaceUtilityIncrementalCollection))]
public sealed class WorkspaceUtilityIncrementalTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task RootUtilityInsertion_WithNestedConditionalUtilities_RoundTripsAndMatchesFreshWorkspace(
        bool replaceWholeOpeningTag,
        bool preserveTextChanges)
    {
        const string originalOpeningTag = "<Border>";
        const string utilities = " px-4 py-3 rounded-2xl {IsActive}:bg-white";
        const string editedOpeningTag = "<Border" + utilities + ">";
        var directory = Path.Combine(Path.GetTempPath(), nameof(WorkspaceUtilityIncrementalTests), Guid.NewGuid().ToString("N"));
        var compilation = CreateCompilation(directory);
        using var workspace = CreateWorkspace(compilation, directory);
        var uri = new Uri(Path.Combine(directory, "Page.akbura"));
        var otherUri = new Uri(Path.Combine(directory, "Other.akbura"));
        var originalText = SourceText.From(ComponentSource.ReplaceLineEndings("\r\n"));
        var initial = workspace.OpenOrChangeDocumentContext(uri, originalText);
        var initialServices = await GetServicesAsync(workspace, initial, expectedUtilityCount: 7);
        Assert.True(initial.Project.TryGetDocument(otherUri, out var other));

        var openingTagStart = originalText.ToString().IndexOf(originalOpeningTag, StringComparison.Ordinal);
        Assert.True(openingTagStart >= 0);
        var editStart = replaceWholeOpeningTag ? openingTagStart : openingTagStart + "<Border".Length;
        var originalValue = replaceWholeOpeningTag ? originalOpeningTag : string.Empty;
        var editedValue = replaceWholeOpeningTag ? editedOpeningTag : utilities;

        // Change only the root opening tag. The nested controls and binding paths remain untouched.
        // Publishers may supply either tracked edits or new SourceText without change history.
        var editedText = originalText.WithChanges(new TextChange(new TextSpan(editStart, originalValue.Length), editedValue));
        if (!preserveTextChanges)
        {
            editedText = SourceText.From(editedText.ToString());
        }

        var updated = workspace.OpenOrChangeDocumentContext(uri, editedText);
        AssertSameDocumentIdentity(initial, updated);
        Assert.True(updated.Project.TryGetDocument(otherUri, out var retainedOther));
        Assert.Same(other, retainedOther);
        var updatedServices = await AssertMatchesFreshAsync(workspace, updated, compilation, directory, expectedUtilityCount: 11);
        Assert.NotEqual(initialServices.Classifications, updatedServices.Classifications);

        var restoredText = editedText.WithChanges(new TextChange(new TextSpan(editStart, editedValue.Length), originalValue));
        if (!preserveTextChanges)
        {
            restoredText = SourceText.From(restoredText.ToString());
        }

        var reverted = workspace.OpenOrChangeDocumentContext(uri, restoredText);
        AssertSameDocumentIdentity(updated, reverted);
        Assert.True(reverted.Project.TryGetDocument(otherUri, out retainedOther));
        Assert.Same(other, retainedOther);
        var revertedServices = await AssertMatchesFreshAsync(workspace, reverted, compilation, directory, expectedUtilityCount: 7);

        Assert.True(originalText.ContentEquals(restoredText));
        AssertServicesEqual(initialServices, revertedServices);

        // Old contexts must still use their own text, semantic model and source coordinates.
        Assert.True(initial.Document.Text.ContentEquals(originalText));
        AssertServicesEqual(initialServices, await GetServicesAsync(workspace, initial, expectedUtilityCount: 7));
        AssertServicesEqual(updatedServices, await GetServicesAsync(workspace, updated, expectedUtilityCount: 11));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ViewboxWidthUtility_TypedCharacterByCharacter_MatchesFreshDiagnosticsAndClearsMissingIdentifier(
        bool preserveTextChanges)
    {
        const string componentName = "Viewbox";
        const string utilities = " w-30";
        var directory = Path.Combine(Path.GetTempPath(), nameof(WorkspaceUtilityIncrementalTests), Guid.NewGuid().ToString("N"));
        var compilation = CreateCompilation(directory);
        using var workspace = CreateWorkspace(compilation, directory);
        var uri = new Uri(Path.Combine(directory, "NavIcon.akbura"));
        var originalText = SourceText.From(
            "using System.Collections.ObjectModel;\r\n" +
            "using Avalonia.Controls;\r\n" +
            "using Avalonia.Media;\r\n" +
            "using Akbura.Styles.akcss;\r\n\r\n" +
            "namespace PurityUIDashboard;\r\n\r\n" +
            "param bool IsActive = false;\r\n" +
            "param ObservableCollection<StreamGeometry> Content;\r\n\r\n" +
            "<" + componentName + ">\r\n\r\n</" + componentName + ">\r\n");
        var initial = workspace.OpenOrChangeDocumentContext(uri, originalText);
        Assert.Empty(AssertTypingDiagnosticsMatchFresh(workspace, initial, compilation, directory));
        var current = initial;
        var text = originalText;
        var position = FindAnchor(originalText.ToString(), "<" + componentName + ">") +
            componentName.Length + 1;
        var typed = string.Empty;
        var failures = new List<string>();

        // Use the actual spelling as supplied, not an implicit casing correction.
        // Each character must produce the same diagnostics as a new workspace;
        // a missing utility segment is temporary and clears after its first digit.
        foreach (var character in utilities)
        {
            var changedText = text.WithChanges(new TextChange(new TextSpan(position, 0), character.ToString()));
            text = preserveTextChanges ? changedText : SourceText.From(changedText.ToString());
            var changed = workspace.OpenOrChangeDocumentContext(uri, text);
            AssertSameDocumentIdentity(current, changed);
            current = changed;
            position++;
            typed += character;

            var failure = Record.Exception(() =>
                AssertTypingStage(AssertTypingDiagnosticsMatchFresh(workspace, current, compilation, directory), typed));
            if (failure != null)
            {
                failures.Add($"After inserting '{typed}': {failure.Message}");
            }
        }

        Assert.Contains("<" + componentName + " w-30>", text.ToString(), StringComparison.Ordinal);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        var completed = current;

        foreach (var _ in utilities)
        {
            position--;
            var changedText = text.WithChanges(new TextChange(new TextSpan(position, 1), string.Empty));
            text = preserveTextChanges ? changedText : SourceText.From(changedText.ToString());
            var changed = workspace.OpenOrChangeDocumentContext(uri, text);
            AssertSameDocumentIdentity(current, changed);
            current = changed;
            typed = typed[..^1];
            AssertTypingStage(AssertTypingDiagnosticsMatchFresh(workspace, current, compilation, directory), typed);
        }

        Assert.True(originalText.ContentEquals(text));
        Assert.True(initial.Document.Text.ContentEquals(originalText));
        Assert.Empty(AssertTypingDiagnosticsMatchFresh(workspace, initial, compilation, directory));
        Assert.Empty(AssertTypingDiagnosticsMatchFresh(workspace, completed, compilation, directory));
        Assert.Empty(AssertTypingDiagnosticsMatchFresh(workspace, current, compilation, directory));

        static void AssertTypingStage(AkburaDiagnosticSpan[] diagnostics, string typed)
        {
            Assert.Equal(typed == " w-", diagnostics.Any(static diagnostic => diagnostic.Code == ErrorCodes.ERR_IdentifierExpected));
            if (typed is "" or " " or " w-3" or " w-30")
            {
                Assert.Empty(diagnostics);
            }
        }
    }

    private static AkburaDiagnosticSpan[] AssertTypingDiagnosticsMatchFresh(
        AkburaWorkspace workspace,
        AkburaDocumentContext context,
        CSharpCompilation compilation,
        string directory)
    {
        var actual = GetDiagnostics(workspace, context);
        using var freshWorkspace = CreateWorkspace(compilation, directory);
        var freshContext = freshWorkspace.OpenOrChangeDocumentContext(context.Document.Uri,
            SourceText.From(context.Document.Text.ToString()));
        Assert.Equal(GetDiagnostics(freshWorkspace, freshContext), actual);
        return actual;

        static AkburaDiagnosticSpan[] GetDiagnostics(AkburaWorkspace workspace, AkburaDocumentContext context)
        {
            var text = context.Document.Text;
            Assert.Equal(text.ToString(), context.Document.SyntaxTree.GetRootSyntax().ToFullString());
            return workspace.LanguageServices.Diagnostics.GetDiagnostics(context, new TextSpan(0, text.Length))
                .OrderBy(static diagnostic => diagnostic.Span.Start)
                .ThenBy(static diagnostic => diagnostic.Span.Length)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Severity)
                .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private static void AssertSameDocumentIdentity(AkburaDocumentContext previous, AkburaDocumentContext current)
    {
        Assert.Equal(previous.Document.Id, current.Document.Id);
        Assert.Equal(previous.Project.Id, current.Project.Id);
        Assert.NotEqual(previous.Document.Version, current.Document.Version);
        Assert.NotSame(previous.Document, current.Document);
        Assert.NotSame(previous.Document.SyntaxTree, current.Document.SyntaxTree);
    }

    private static async Task<ServiceSnapshot> AssertMatchesFreshAsync(
        AkburaWorkspace workspace,
        AkburaDocumentContext context,
        CSharpCompilation compilation,
        string directory,
        int expectedUtilityCount)
    {
        var actual = await GetServicesAsync(workspace, context, expectedUtilityCount);
        using var freshWorkspace = CreateWorkspace(compilation, directory);
        var freshContext = freshWorkspace.OpenOrChangeDocumentContext(
            context.Document.Uri,
            SourceText.From(context.Document.Text.ToString()));
        var expected = await GetServicesAsync(freshWorkspace, freshContext, expectedUtilityCount);

        AssertServicesEqual(expected, actual);
        return actual;
    }

    private static async Task<ServiceSnapshot> GetServicesAsync(
        AkburaWorkspace workspace,
        AkburaDocumentContext context,
        int expectedUtilityCount)
    {
        var text = context.Document.Text;
        var source = text.ToString();
        var root = context.Document.SyntaxTree.GetRootSyntax();
        var fullSpan = new TextSpan(0, text.Length);
        Assert.False(root.ContainsDiagnostics);
        Assert.Equal(source, root.ToFullString());
        var semanticModel = context.Project.Compilation.GetSemanticModel(context.Document.SyntaxTree);
        var attributes = root.DescendantNodes().OfType<TailwindAttributeSyntax>().ToArray();

        Assert.Equal(expectedUtilityCount, attributes.Length);
        foreach (var attribute in attributes)
        {
            var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(semanticModel.GetOperation(attribute));
            Assert.NotNull(operation.Utility);
            Assert.False(operation.HasErrors, attribute.ToFullString());
        }

        var diagnostics = workspace.LanguageServices.Diagnostics.GetDiagnostics(context, fullSpan).ToArray();
        Assert.Empty(diagnostics);
        var classifications = workspace.LanguageServices.Classification.GetClassifications(context, fullSpan)
            .OrderBy(static classification => classification.Span.Start)
            .ThenBy(static classification => classification.Span.Length)
            .ThenBy(static classification => classification.Kind)
            .ToArray();
        Assert.NotEmpty(classifications);
        Assert.All(classifications, classification => Assert.True(fullSpan.Contains(classification.Span)));

        // These references move when root utilities are inserted, including both positive and negated conditions.
        foreach (var (anchor, name) in new[]
        {
            ("{IsActive}:bg-teal-300", "IsActive"),
            ("{!IsActive}:bg-white", "IsActive"),
            ("{IsCollapsed}:hidden", "IsCollapsed"),
            ("{Icon}", "Icon"),
            ("Text={Text}", "Text"),
        })
        {
            var referenceStart = FindAnchor(source, anchor) + anchor.LastIndexOf(name, StringComparison.Ordinal);
            Assert.Contains(classifications, classification =>
                classification.Span == new TextSpan(referenceStart, name.Length) &&
                classification.Kind == AkburaClassificationKind.PropertyName);
        }

        var document = AkburaSyntacticDocument.Create(context.Document);
        var utilityCompletions = new List<AkburaCompletionResult>();
        foreach (var (anchor, utility, expectedDisplayText) in new[]
        {
            ("{IsActive}:bg-teal-300", "bg-teal-300", "bg-(string color)-(int shade)"),
            ("{IsActive}:text-grey-700", "text-grey-700", "text-(string color)-(int shade)"),
            ("rounded-xl", "rounded-xl", "rounded-xl"),
        })
        {
            var utilityStart = FindAnchor(source, anchor) + anchor.IndexOf(utility, StringComparison.Ordinal);
            var position = utilityStart + 2;
            var result = workspace.LanguageServices.Completion.GetCompletions(document, context, position);
            var item = Assert.Single(result.Items, item => item.DisplayText == expectedDisplayText);
            Assert.Equal(AkburaCompletionKind.TailwindUtility, item.Kind);
            Assert.Equal(utilityStart, result.ApplicableSpan.Start);
            Assert.True(result.ApplicableSpan.End >= position);
            Assert.True(result.ApplicableSpan.End <= utilityStart + utility.Length);
            utilityCompletions.Add(result);
        }

        var bindingCompletions = new List<AkburaProjectedCompletionResult>();
        foreach (var (anchor, name) in new[]
        {
            ("{IsActive}:bg-teal-300", "IsActive"),
            ("Text={Text}", "Text"),
        })
        {
            var referenceStart = FindAnchor(source, anchor) + anchor.LastIndexOf(name, StringComparison.Ordinal);
            var result = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
                document, context, referenceStart + 2, new(true, false, '\0'));
            Assert.NotNull(result);
            var item = Assert.Single(result.Value.Items, item => item.DisplayText == name);
            Assert.Equal(referenceStart, item.SourceSpan.Start);
            Assert.True(item.SourceSpan.Length >= 2);
            Assert.True(item.SourceSpan.Length <= name.Length);
            bindingCompletions.Add(result.Value);
        }

        return new(diagnostics, classifications, utilityCompletions.ToArray(), bindingCompletions.ToArray());
    }

    private static void AssertServicesEqual(ServiceSnapshot expected, ServiceSnapshot actual)
    {
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        Assert.Equal(expected.Classifications, actual.Classifications);
        Assert.Equal(expected.UtilityCompletions.Length, actual.UtilityCompletions.Length);
        for (var i = 0; i < expected.UtilityCompletions.Length; i++)
        {
            var expectedCompletion = expected.UtilityCompletions[i];
            var actualCompletion = actual.UtilityCompletions[i];
            Assert.Equal(expectedCompletion.ApplicableSpan, actualCompletion.ApplicableSpan);
            Assert.Equal(expectedCompletion.IsIncomplete, actualCompletion.IsIncomplete);
            Assert.Equal(
                GetUtilityCompletionItems(expectedCompletion),
                GetUtilityCompletionItems(actualCompletion));
        }

        Assert.Equal(expected.BindingCompletions.Length, actual.BindingCompletions.Length);
        for (var i = 0; i < expected.BindingCompletions.Length; i++)
        {
            var expectedCompletion = expected.BindingCompletions[i];
            var actualCompletion = actual.BindingCompletions[i];
            Assert.Equal(expectedCompletion.IsIncomplete, actualCompletion.IsIncomplete);
            Assert.Equal(
                GetBindingCompletionItems(expectedCompletion),
                GetBindingCompletionItems(actualCompletion));
        }
    }

    private static object[] GetUtilityCompletionItems(AkburaCompletionResult result)
    {
        return result.Items.OrderBy(static item => item.SortText, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayText, StringComparer.Ordinal)
            .Select(static item => (object)(item.Kind, item.DisplayText, item.InsertText, item.FilterText,
                item.SortText, item.Suffix, item.Description, item.Priority, item.CaretOffsetFromEnd,
                item.TriggerCompletionAfterInsert, item.NamespaceImport, item.ResolveKey))
            .ToArray();
    }

    private static object[] GetBindingCompletionItems(AkburaProjectedCompletionResult result)
    {
        // Roslyn resolve keys belong to a session; compare the UI data and mapped replacement spans.
        return result.Items.OrderBy(static item => item.SortText, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayText, StringComparer.Ordinal)
            .Select(static item => (object)(item.Kind, item.DisplayText, item.InsertText, item.FilterText,
                item.SortText, item.Detail, item.SourceSpan))
            .ToArray();
    }

    private static int FindAnchor(string source, string anchor)
    {
        var start = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(start >= 0, anchor);
        Assert.Equal(-1, source.IndexOf(anchor, start + anchor.Length, StringComparison.Ordinal));
        return start;
    }

    private static AkburaWorkspace CreateWorkspace(CSharpCompilation compilation, string directory)
    {
        var project = new ProjectContext(ProjectId.CreateNewId(), string.Empty, directory, "Akbura", compilation,
            ImmutableArray<ProjectReference>.Empty);
        var workspace = new AkburaWorkspace(project);
        var styles = workspace.OpenOrChangeDocumentContext(new Uri(Path.Combine(directory, "Styles.akcss")),
            SourceText.From(StylesSource.ReplaceLineEndings("\r\n")));
        Assert.Empty(workspace.LanguageServices.Diagnostics.GetDiagnostics(styles, new TextSpan(0, styles.Document.Text.Length)));
        workspace.OpenOrChangeDocumentContext(new Uri(Path.Combine(directory, "Other.akbura")),
            SourceText.From("using Avalonia.Controls;\r\n<Border />"));
        return workspace;
    }

    private static CSharpCompilation CreateCompilation(string directory)
    {
        var platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];
        var compilation = CSharpCompilation.Create(nameof(WorkspaceUtilityIncrementalTests),
            [CSharpSyntaxTree.ParseText(Definitions.ReplaceLineEndings("\r\n"), path: Path.Combine(directory, "Definitions.cs"))],
            platform.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.DoesNotContain(compilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return compilation;
    }

    private sealed record ServiceSnapshot(
        AkburaDiagnosticSpan[] Diagnostics,
        AkburaClassifiedSpan[] Classifications,
        AkburaCompletionResult[] UtilityCompletions,
        AkburaProjectedCompletionResult[] BindingCompletions);

    private const string ComponentSource = """
        using Avalonia.Controls;
        using Akbura.Markup;
        using Akbura.Styles.akcss;

        param bool IsActive = true;
        param bool IsCollapsed = false;
        param Control Icon = new TextBlock();
        param string Text = "Item";

        <Border>
            <Grid ColumnDefinitions="Auto, *">
                <Border {IsActive}:bg-teal-300 {!IsActive}:bg-white rounded-xl>
                    {Icon}
                </Border>
                <Border pr-3 {IsCollapsed}:hidden>
                    <TextBlock {IsActive}:text-grey-700 {!IsActive}:text-grey-400 Text={Text} />
                </Border>
            </Grid>
        </Border>
        """;

    // A self-contained resolution fixture: the same utility syntax without requiring installed Avalonia packages.
    private const string StylesSource = """
        @using Avalonia.Controls;

        @utilities {
            Control.w-(double value) { Width: value; }
            Border.px-(double value) { Padding: value; }
            Border.py-(double value) { Padding: value; }
            Border.pr-(double value) { Padding: value; }
            Border.rounded-xl { CornerRadius: 12; }
            Border.rounded-2xl { CornerRadius: 16; }
            Border.bg-(string color) { Background: color; }
            Border.bg-(string color)-(int shade) { Background: color + "-" + shade; }
            Border.hidden { IsVisible: false; }
            TextBlock.text-(string color)-(int shade) { Foreground: color + "-" + shade; }
        }
        """;

    private const string Definitions = """
        using System;
        using System.Collections.Generic;

        namespace Avalonia.Metadata
        {
            [AttributeUsage(AttributeTargets.Property)]
            public sealed class ContentAttribute : Attribute { }
        }

        namespace Avalonia.Controls
        {
            public class Control
            {
                public bool IsVisible { get; set; } = true;
                public double Width { get; set; }
            }

            public sealed class Viewbox : Control
            {
                [Avalonia.Metadata.Content]
                public Control? Child { get; set; }
            }

            public sealed class Border : Control
            {
                [Avalonia.Metadata.Content]
                public Control? Child { get; set; }
                public double Padding { get; set; }
                public double CornerRadius { get; set; }
                public string Background { get; set; } = "";
            }

            public sealed class Grid : Control
            {
                [Avalonia.Metadata.Content]
                public List<Control> Children { get; } = new();
                public string ColumnDefinitions { get; set; } = "";
            }

            public sealed class TextBlock : Control
            {
                public string Text { get; set; } = "";
                public string Foreground { get; set; } = "";
            }
        }

        namespace Akbura.Markup
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class UtilityVariantAttribute : Attribute { }
        }

        namespace Avalonia.Media
        {
            public sealed class StreamGeometry { }
        }

        namespace Akbura
        {
            public class AkburaControl : Avalonia.Controls.Control { }
            public partial class Page : AkburaControl { }
        }

        namespace PurityUIDashboard
        {
            public partial class NavIcon : Akbura.AkburaControl { }
        }
        """;
}

// Roslyn's cached import-completion items have mutable spans shared across sessions.
// https://github.com/dotnet/roslyn/blob/release/dev17.11/src/Features/Core/Portable/Completion/Providers/ImportCompletionProvider/AbstractTypeImportCompletionProvider.cs#L117
[CollectionDefinition(nameof(WorkspaceUtilityIncrementalCollection), DisableParallelization = true)]
public sealed class WorkspaceUtilityIncrementalCollection
{
}
