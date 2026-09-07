using Akbura.BlackSilence;
using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using FuriosoGenerator = Akbura.Furioso.AkburaCsGenerator;
using LanguageDiagnostic = Akbura.Language.Syntax.AkburaDiagnostic;
using Location = Microsoft.CodeAnalysis.Location;
using LocationKind = Microsoft.CodeAnalysis.LocationKind;

namespace Akbura.UnitTests;

public sealed class BlackSilenceDiagnosticTests
{
    [Theory]
    [InlineData("Page.akbura", "using Avalonia.Controls;\r\n<Button Missing=\"1\" />", ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound)]
    [InlineData("Shared.akcss", "@using Avalonia.Controls;\r\nBorder.shared { Missing: 1; }", ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound)]
    [InlineData("Page.akbura", "using Avalonia.Controls;\r\n@akcss { @using Avalonia.Controls; Border.local { Missing: 1; } }\r\n<Border class=\"local\" />", ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound)]
    public void SemanticDiagnostics_MatchFuriosoIncludingExactLocationsAndMetadata(
        string relativePath,
        string source,
        string expectedCode)
    {
        var project = new DiagnosticProject();
        var file = project.File(relativePath, source);
        var expected = Run(project, new FuriosoGenerator(), file);
        var actual = Run(project, new AkburaBlackSilenceGenerator(), file);

        Assert.Contains(expected.Diagnostics, diagnostic => diagnostic.Id == expectedCode);
        AssertSourceLocations(file, expected.Diagnostics);
        AssertDiagnosticParity(expected.Diagnostics, actual.Diagnostics);
    }

    [Theory]
    [InlineData("GlobalUsings.akbura", "using System;\r\n<object />")]
    [InlineData("GlobalUsings.akcss", "@using System;\r\n.invalid { }")]
    public void InvalidGlobalUsings_ReportsDiagnosticsEvenWithoutGeneratedSources(string path, string source)
    {
        var project = new DiagnosticProject();
        var file = project.File(path, source);
        var expected = Run(project, new FuriosoGenerator(), file);
        var actual = Run(project, new AkburaBlackSilenceGenerator(), file);

        var diagnostic = Assert.Single(expected.Diagnostics);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_GlobalUsingsFileContainsNonUsing, diagnostic.Id);
        Assert.Empty(expected.GeneratedSources);
        Assert.Empty(actual.GeneratedSources);
        AssertSourceLocations(file, expected.Diagnostics);
        AssertDiagnosticParity(expected.Diagnostics, actual.Diagnostics);
    }

    [Theory]
    [InlineData("Page.akbura", "using Avalonia.Controls;\r\n<StackPanel<Button /></StackPanel>")]
    [InlineData("Shared.akcss", "@using Avalonia.Controls;\r\nBorder.local { Width: 10; }\r\n/* unfinished")]
    [InlineData("Page.akbura", "using Avalonia.Controls;\r\n@akcss { @using Avalonia.Controls; Border.local { Width: 10; } /* unfinished")]
    [InlineData("GlobalUsings.akbura", "using Demo.Empty.akcss\r\nusing Avalonia.Controls;")]
    [InlineData("GlobalUsings.akcss", "@using System;\r\n/* unfinished")]
    public void SyntaxDiagnostics_ExtendFuriosoUsingExistingParserLocations(string path, string source)
    {
        var project = new DiagnosticProject();
        var file = project.File(path, source);
        var companion = project.File("Empty.akcss", string.Empty);
        var syntax = GetExpectedSyntaxDiagnostics(file);
        var furioso = Run(project, new FuriosoGenerator(), file, companion);
        var blackSilence = Run(project, new AkburaBlackSilenceGenerator(), file, companion);

        // Furioso intentionally has no parser-diagnostic publication. The new
        // syntax coverage is checked against the existing Workspace mapping
        // contract, while preserving every diagnostic Furioso already reports.
        Assert.NotEmpty(syntax);
        AssertSourceLocations(file, syntax);
        AssertDiagnosticParity(syntax.AddRange(furioso.Diagnostics), blackSilence.Diagnostics);
    }

    [Fact]
    public void CrLfAndNonAsciiText_PreserveUtf16DiagnosticPositions()
    {
        var project = new DiagnosticProject();
        var file = project.File(
            "Примеры/Page.akbura",
            "using Avalonia.Controls;\r\n<StackPanel>\r\n<TextBlock Text=\"Привет 😀\" /><Button Missing=\"1\" />\r\n</StackPanel>");
        var expected = Run(project, new FuriosoGenerator(), file);
        var actual = Run(project, new AkburaBlackSilenceGenerator(), file);

        var diagnostic = Assert.Single(expected.Diagnostics);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound, diagnostic.Id);
        Assert.Equal(2, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.True(diagnostic.Location.SourceSpan.Start > file.Text.ToString().IndexOf("😀", StringComparison.Ordinal));
        AssertSourceLocations(file, expected.Diagnostics);
        AssertDiagnosticParity(expected.Diagnostics, actual.Diagnostics);
    }

    [Fact]
    public void InlineAkcssError_IsNotDuplicatedByItsContainingComponent()
    {
        var project = new DiagnosticProject();
        var file = project.File(
            "Page.akbura",
            "using Avalonia.Controls;\r\n@akcss { @using Avalonia.Controls; Border.local { Missing: 1; } }\r\n<Border class=\"local\" />");
        var expected = Run(project, new FuriosoGenerator(), file);
        var actual = Run(project, new AkburaBlackSilenceGenerator(), file);

        Assert.Single(expected.Diagnostics, static diagnostic => diagnostic.Id == ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound);
        AssertDiagnosticParity(expected.Diagnostics, actual.Diagnostics);
        Assert.Single(actual.Diagnostics, static diagnostic => diagnostic.Id == ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SemanticError_AddFixAndReintroduce_MatchesFreshWithoutStaleDiagnostics(bool akcss)
    {
        var project = new DiagnosticProject();
        var valid = project.File(
            akcss ? "Shared.akcss" : "Page.akbura",
            akcss ? "@using Avalonia.Controls;\r\nBorder.shared { Width: 10; }" : "using Avalonia.Controls;\r\n<Button Width=\"10\" />");
        var invalid = valid.Replace("Width", "Missing");
        var initial = RunState(project, CreateDriver(project, new AkburaBlackSilenceGenerator(), valid));
        Assert.Empty(initial.Result.Diagnostics);
        var added = RunState(project, initial.Driver.ReplaceAdditionalText(valid, invalid));

        Assert.NotEmpty(added.Result.Diagnostics);
        AssertFreshParity(project, added, invalid);
        var fixedRun = RunState(project, added.Driver.ReplaceAdditionalText(invalid, valid));

        Assert.Empty(fixedRun.Result.Diagnostics);
        AssertFreshParity(project, fixedRun, valid);
        var reintroduced = RunState(project, fixedRun.Driver.ReplaceAdditionalText(valid, invalid));

        AssertDiagnosticParity(added.Result.Diagnostics, reintroduced.Result.Diagnostics);
        AssertFreshParity(project, reintroduced, invalid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyntaxError_AddAndFix_RemovesThePreviousDiagnosticBatch(bool akcss)
    {
        var project = new DiagnosticProject();
        var valid = project.File(
            akcss ? "Shared.akcss" : "Page.akbura",
            akcss ? "@using Avalonia.Controls;\r\nBorder.shared { Width: 10; }" : "using Avalonia.Controls;\r\n<Button Width=\"10\" />");
        var invalid = valid.Append("\r\n/* unfinished");
        var initial = RunState(project, CreateDriver(project, new AkburaBlackSilenceGenerator(), valid));
        var added = RunState(project, initial.Driver.ReplaceAdditionalText(valid, invalid));

        Assert.NotEmpty(GetExpectedSyntaxDiagnostics(invalid));
        Assert.NotEmpty(added.Result.Diagnostics);
        AssertFreshParity(project, added, invalid);
        var fixedRun = RunState(project, added.Driver.ReplaceAdditionalText(invalid, valid));

        Assert.Empty(fixedRun.Result.Diagnostics);
        AssertFreshParity(project, fixedRun, valid);
    }

    [Fact]
    public void InitiallyUnterminatedComment_FixedInComponentWithImportedAndInlineStyles_MatchesFreshGeneration()
    {
        var project = new DiagnosticProject();
        var styles = project.File(
            "Shared.akcss",
            "@using Avalonia.Controls;\r\n@utilities { StackPanel.page { Width: 10; } StackPanel.page-desktop { Width: 20; } }");
        var valid = project.File(
            "Page.akbura",
            "using Avalonia.Controls;\r\nusing Akbura.Markup;\r\nusing Demo.Shared.akcss;\r\n" +
            "@akcss { @using Demo.Shared.akcss; .local { @apply page; } }\r\n" +
            "<StackPanel page\r\n            ${md}:page-desktop>\r\n" +
            "<StackPanel class=\"local\" /><TextBlock Text=\"value\" /></StackPanel>");
        var invalid = valid.Append("\r\n/* diagnostic benchmark");
        // No valid driver runs first: recovery must not rely on an older valid
        // parse/semantic snapshot surviving in a process-wide cache.
        var initial = RunState(project, CreateDriver(project, new AkburaBlackSilenceGenerator(), invalid, styles));
        Assert.NotEmpty(initial.Result.Diagnostics);
        var updated = RunState(project, initial.Driver.ReplaceAdditionalText(invalid, valid));

        Assert.Empty(updated.Result.Diagnostics);
        AssertFreshParity(project, updated, valid, styles);
    }

    [Fact]
    public void RemovedErrorDocument_StaysAbsentOnTheNextCachedRunAndReorder()
    {
        var project = new DiagnosticProject();
        var first = project.File("First.akbura", "using Avalonia.Controls;\r\n<Button />");
        var removedFile = project.File("Removed.akbura", "using Avalonia.Controls;\r\n<Button Missing=\"1\" />");
        var last = project.File("Last.akbura", "using Avalonia.Controls;\r\n<Border />");
        var initial = RunState(project, CreateDriver(project, new AkburaBlackSilenceGenerator(), first, removedFile, last));
        Assert.NotEmpty(initial.Result.Diagnostics);
        var removed = RunState(project, initial.Driver.RemoveAdditionalTexts([removedFile]));
        var unchanged = RunState(project, removed.Driver);
        var reordered = RunState(project, removed.Driver.ReplaceAdditionalTexts([last, first]));

        Assert.Empty(removed.Result.Diagnostics);
        Assert.Empty(unchanged.Result.Diagnostics);
        Assert.Empty(reordered.Result.Diagnostics);
        AssertFreshParity(project, removed, first, last);
        AssertFreshParity(project, unchanged, first, last);
        AssertFreshParity(project, reordered, last, first);
    }

    [Fact]
    public void RenamedErrorDocument_ReplacesTheDiagnosticLocation()
    {
        var project = new DiagnosticProject();
        var original = project.File("Before.akbura", "using Avalonia.Controls;\r\n<Button Missing=\"1\" />");
        var renamed = project.File("After.akbura", original.Text.ToString());
        var initial = RunState(project, CreateDriver(project, new AkburaBlackSilenceGenerator(), original));
        Assert.NotEmpty(initial.Result.Diagnostics);
        var updated = RunState(project, initial.Driver.ReplaceAdditionalTexts([renamed]));

        Assert.NotEmpty(updated.Result.Diagnostics);
        Assert.All(updated.Result.Diagnostics, diagnostic => Assert.Equal(renamed.Path, diagnostic.Location.GetLineSpan().Path));
        AssertFreshParity(project, updated, renamed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoChangesAndEofWhitespace_ReuseDiagnosticEntries(bool akcss)
    {
        var project = new DiagnosticProject();
        var original = project.File(
            akcss ? "Shared.akcss" : "Page.akbura",
            akcss ? "@using Avalonia.Controls;\r\nBorder.shared { Missing: 1; }" : "using Avalonia.Controls;\r\n<Button Missing=\"1\" />");
        var initial = RunState(project, CreateDriver(project, new AkburaBlackSilenceGenerator(), original));
        var initialSnapshot = GetSnapshot(initial);
        var unchanged = RunState(project, initial.Driver);
        var edited = original.Append("\r\n \t\r\n");
        var whitespace = RunState(project, initial.Driver.ReplaceAdditionalText(original, edited));

        Assert.NotEmpty(initial.Result.Diagnostics);
        Assert.Same(initialSnapshot.DiagnosticEntries[original.Path], GetSnapshot(unchanged).DiagnosticEntries[original.Path]);
        Assert.Same(initialSnapshot.DiagnosticEntries[original.Path], GetSnapshot(whitespace).DiagnosticEntries[original.Path]);
        AssertDiagnosticParity(initial.Result.Diagnostics, unchanged.Result.Diagnostics);
        AssertDiagnosticParity(initial.Result.Diagnostics, whitespace.Result.Diagnostics);
        AssertFreshParity(project, whitespace, edited);
    }

    [Fact]
    public void ShadowToPublishAndBack_ChangesOnlyPublicationAndReusesTheComputedEntry()
    {
        var project = new DiagnosticProject();
        var file = project.File("Page.akbura", "using Avalonia.Controls;\r\n<Button Missing=\"1\" />");
        var shadowDriver = CreateDriver(project, new AkburaBlackSilenceGenerator(), file)
            .WithUpdatedAnalyzerConfigOptions(new DiagnosticOptions(project.Directory, "Shadow"));
        var shadow = RunState(project, shadowDriver);
        var shadowSnapshot = GetSnapshot(shadow);

        Assert.Empty(shadow.Result.Diagnostics);
        Assert.NotEmpty(shadowSnapshot.DiagnosticEntries[file.Path].Diagnostics);
        var published = RunState(project, shadow.Driver.WithUpdatedAnalyzerConfigOptions(project.Options));

        Assert.NotEmpty(published.Result.Diagnostics);
        Assert.Same(shadowSnapshot.DiagnosticEntries[file.Path], GetSnapshot(published).DiagnosticEntries[file.Path]);
        Assert.Same(shadowSnapshot.Entries["component:Page.akbura"], GetSnapshot(published).Entries["component:Page.akbura"]);
        AssertFreshParity(project, published, file);
        var hidden = RunState(project, published.Driver.WithUpdatedAnalyzerConfigOptions(new DiagnosticOptions(project.Directory, "Shadow")));

        Assert.Empty(hidden.Result.Diagnostics);
        Assert.Same(shadowSnapshot.DiagnosticEntries[file.Path], GetSnapshot(hidden).DiagnosticEntries[file.Path]);
    }

    [Fact]
    public void OffMode_DoesNotComputeDiagnostics_AndEnablingPublishFindsExistingErrors()
    {
        var project = new DiagnosticProject();
        var file = project.File("Page.akbura", "using Avalonia.Controls;\r\n<Button Missing=\"1\" />");
        var driver = CreateDriver(project, new AkburaBlackSilenceGenerator(), file)
            .WithUpdatedAnalyzerConfigOptions(new DiagnosticOptions(project.Directory, "Off"));
        var disabled = RunState(project, driver);

        Assert.Empty(disabled.Result.Diagnostics);
        Assert.Empty(GetSnapshot(disabled).DiagnosticEntries);
        var enabled = RunState(project, disabled.Driver.WithUpdatedAnalyzerConfigOptions(project.Options));

        Assert.NotEmpty(enabled.Result.Diagnostics);
        AssertFreshParity(project, enabled, file);
    }

    [Fact]
    public void ComponentSurfaceEdit_ReevaluatesOnlyItsDiagnosticDependents()
    {
        var project = new DiagnosticProject();
        var component = project.File("ValueView.akbura", "using Avalonia.Controls;\r\nparam double Value = 0d;\r\n<Border />");
        var consumer = project.File("Consumer.akbura", "using Demo;\r\n<ValueView Value=\"10\" />");
        var unrelated = project.File("Other.akbura", "using Avalonia.Controls;\r\n<Button />");
        var initial = RunState(project, CreateDriver(project, new AkburaBlackSilenceGenerator(), component, consumer, unrelated));
        var snapshot = GetSnapshot(initial);
        var changed = component.Replace("Value =", "Renamed =");
#if STATS
        using var measurement = BlackSilenceGenerationStatistics.BeginMeasurement();
#endif
        var updated = RunState(project, initial.Driver.ReplaceAdditionalText(component, changed));
#if STATS
        Assert.Equal(2, measurement.GetSnapshot().DiagnosticDocumentEvaluatedCount);
#endif
        var updatedSnapshot = GetSnapshot(updated);
        Assert.NotSame(snapshot.DiagnosticEntries[component.Path], updatedSnapshot.DiagnosticEntries[component.Path]);
        Assert.NotSame(snapshot.DiagnosticEntries[consumer.Path], updatedSnapshot.DiagnosticEntries[consumer.Path]);
        Assert.Same(snapshot.DiagnosticEntries[unrelated.Path], updatedSnapshot.DiagnosticEntries[unrelated.Path]);
        Assert.Contains(updated.Result.Diagnostics, static diagnostic => diagnostic.Id == ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound);
        AssertFreshParity(project, updated, changed, consumer, unrelated);
    }

    [Fact]
    public void AkcssBodyEdit_ReevaluatesTransitiveImportsButReusesUnrelatedDiagnostics()
    {
        var project = new DiagnosticProject();
        var basic = project.File("Base.akcss", "@using Avalonia.Controls;\r\nBorder.basic { Width: 10; }");
        var top = project.File("Top.akcss", "@using Demo.Base.akcss;\r\n.top { @apply basic; }");
        var page = project.File("Page.akbura", "using Avalonia.Controls;\r\nusing Demo.Top.akcss;\r\n<Border class=\"top\" />");
        var unrelated = project.File("Other.akbura", "using Avalonia.Controls;\r\n<Button />");
        var initial = RunState(project, CreateDriver(project, new AkburaBlackSilenceGenerator(), basic, top, page, unrelated));
        var snapshot = GetSnapshot(initial);
        var changed = basic.Replace("Width", "Missing");
#if STATS
        using var measurement = BlackSilenceGenerationStatistics.BeginMeasurement();
#endif
        var updated = RunState(project, initial.Driver.ReplaceAdditionalText(basic, changed));
#if STATS
        Assert.Equal(3, measurement.GetSnapshot().DiagnosticDocumentEvaluatedCount);
#endif
        var updatedSnapshot = GetSnapshot(updated);
        Assert.NotSame(snapshot.DiagnosticEntries[basic.Path], updatedSnapshot.DiagnosticEntries[basic.Path]);
        Assert.NotSame(snapshot.DiagnosticEntries[top.Path], updatedSnapshot.DiagnosticEntries[top.Path]);
        Assert.NotSame(snapshot.DiagnosticEntries[page.Path], updatedSnapshot.DiagnosticEntries[page.Path]);
        Assert.Same(snapshot.DiagnosticEntries[unrelated.Path], updatedSnapshot.DiagnosticEntries[unrelated.Path]);
        Assert.Contains(updated.Result.Diagnostics, static diagnostic => diagnostic.Id == ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound);
        AssertFreshParity(project, updated, changed, top, page, unrelated);
    }

    [Fact]
    public void FixingGlobalUsingsDiagnostic_DoesNotRequireAnyGeneratedSource()
    {
        var project = new DiagnosticProject();
        var invalid = project.File("GlobalUsings.akcss", "@using System;\r\n.invalid { }");
        var valid = project.File("GlobalUsings.akcss", "@using System;");
        var initial = RunState(project, CreateDriver(project, new AkburaBlackSilenceGenerator(), invalid));
        var initialSnapshot = GetSnapshot(initial);
        Assert.NotEmpty(initial.Result.Diagnostics);
        Assert.Empty(initial.Result.GeneratedSources);
        var updated = RunState(project, initial.Driver.ReplaceAdditionalText(invalid, valid));

        Assert.Empty(updated.Result.Diagnostics);
        Assert.Empty(updated.Result.GeneratedSources);
        Assert.NotSame(initialSnapshot.DiagnosticEntries[invalid.Path], GetSnapshot(updated).DiagnosticEntries[valid.Path]);
        AssertFreshParity(project, updated, valid);
    }

#if STATS
    [Theory]
    [InlineData("NoChanges")]
    [InlineData("ComponentEof")]
    [InlineData("AkcssEof")]
    [InlineData("UnrelatedCSharp")]
    public void DiagnosticFastPaths_PerformNoSemanticOrDiagnosticAllocationWork(string scenario)
    {
        var project = new DiagnosticProject();
        var component = project.File("Page.akbura", "using Avalonia.Controls;\r\n<Button Missing=\"1\" />");
        var styles = project.File("Shared.akcss", "@using Avalonia.Controls;\r\nBorder.shared { Missing: 1; }");
        var compilation = project.Compilation;
        if (scenario == "UnrelatedCSharp")
        {
            // A new namespace can change lookup and is intentionally conservative.
            // This fixture changes only an unused type in an existing namespace.
            compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                "namespace Unrelated; internal sealed class Existing { }",
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));
        }

        var initial = RunState(project, CreateDriver(project, new AkburaBlackSilenceGenerator(), component, styles), compilation);
        var snapshot = GetSnapshot(initial);
        var driver = initial.Driver;
        if (scenario == "ComponentEof")
        {
            driver = driver.ReplaceAdditionalText(component, component.Append("\r\n \t\r\n"));
        }
        else if (scenario == "AkcssEof")
        {
            driver = driver.ReplaceAdditionalText(styles, styles.Append("\r\n \t\r\n"));
        }
        else if (scenario == "UnrelatedCSharp")
        {
            compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                "namespace Unrelated; internal sealed class Unused { }",
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));
        }

        using var measurement = BlackSilenceGenerationStatistics.BeginMeasurement();
        var updated = RunState(project, driver, compilation);
        var statistics = measurement.GetSnapshot();

        Assert.Equal(0, statistics.DiagnosticDocumentEvaluatedCount);
        Assert.Equal(0, statistics.DiagnosticBatchCreatedCount);
        Assert.Equal(0, statistics.DiagnosticSemanticModelCreatedCount);
        Assert.Equal(0, statistics.SemanticModelCreatedCount);
        Assert.Equal(0, statistics.GeneratedDiagnosticCreatedCount);
        Assert.Equal(0, statistics.RoslynDiagnosticCreatedCount);
        Assert.Equal(0, statistics.DiagnosticDescriptorCreatedCount);
        Assert.Equal(0, statistics.DiagnosticPublishedCount);
        Assert.Equal(0, statistics.GeneratedSourceTextCreatedCount);
        Assert.Equal(TimeSpan.Zero, statistics.DiagnosticSemanticElapsed);
        Assert.Equal(TimeSpan.Zero, statistics.DiagnosticBatchElapsed);
        Assert.Same(snapshot.DiagnosticEntries[component.Path], GetSnapshot(updated).DiagnosticEntries[component.Path]);
        Assert.Same(snapshot.DiagnosticEntries[styles.Path], GetSnapshot(updated).DiagnosticEntries[styles.Path]);
        AssertDiagnosticParity(initial.Result.Diagnostics, updated.Result.Diagnostics);
    }
#endif

    private static BlackSilenceProjectSnapshot GetSnapshot(DiagnosticRun run)
    {
        var request = Assert.IsType<BlackSilenceGenerationRequest>(Assert.Single(
            run.Result.TrackedSteps["BlackSilence.GenerationRequests"].SelectMany(static step => step.Outputs)).Value);
        return Assert.IsType<BlackSilenceProjectSnapshot>(request.State.TryGetSnapshot(request.Options));
    }

    private static void AssertFreshParity(DiagnosticProject project, DiagnosticRun incremental, params DiagnosticFile[] files)
    {
        var fresh = Run(project, new AkburaBlackSilenceGenerator(), files);
        AssertDiagnosticParity(fresh.Diagnostics, incremental.Result.Diagnostics);
        var expectedSources = fresh.GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal).ToArray();
        var actualSources = incremental.Result.GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedSources.Length, actualSources.Length);
        for (var i = 0; i < expectedSources.Length; i++)
        {
            Assert.Equal(expectedSources[i].HintName, actualSources[i].HintName);
            Assert.Equal(expectedSources[i].SourceText.ToString(), actualSources[i].SourceText.ToString());
        }
    }

    private static ImmutableArray<Diagnostic> GetExpectedSyntaxDiagnostics(DiagnosticFile file)
    {
        AkburaSyntaxTree tree = file.Path.EndsWith(".akcss", StringComparison.Ordinal)
            ? AkcssSyntaxTree.ParseText(file.Text, file.Path, "Demo." + Path.GetFileName(file.Path))
            : ComponentSyntaxTree.ParseText(file.Text, file.Path);
        var diagnostics = new List<Diagnostic>();
        foreach (var nodeOrToken in tree.GetRootSyntax().DescendantNodesAndTokensAndSelf())
        {
            Add(nodeOrToken.GetDiagnostics(), nodeOrToken.SpanStart, nodeOrToken.Span);
            if (nodeOrToken.IsToken)
            {
                var token = nodeOrToken.AsToken();
                foreach (var trivia in token.LeadingTrivia)
                {
                    Add(trivia.GetDiagnostics(), trivia.SpanStart, trivia.Span);
                }

                foreach (var trivia in token.TrailingTrivia)
                {
                    Add(trivia.GetDiagnostics(), trivia.SpanStart, trivia.Span);
                }
            }
        }

        return diagnostics.GroupBy(
            static diagnostic => DescribeDiagnostics([diagnostic])[0],
            StringComparer.Ordinal).Select(static group => group.First()).ToImmutableArray();

        void Add(IEnumerable<LanguageDiagnostic> values, int spanStart, TextSpan fallback)
        {
            foreach (var diagnostic in values)
            {
                var start = diagnostic is SyntaxDiagnosticInfo syntax ? (long)spanStart + syntax.Position : fallback.Start;
                var width = diagnostic is SyntaxDiagnosticInfo syntaxInfo ? Math.Max(0, syntaxInfo.Width) : fallback.Length;
                var clampedStart = (int)Math.Clamp(start, 0, file.Text.Length);
                var clampedEnd = (int)Math.Clamp(start + width, clampedStart, file.Text.Length);
                var span = TextSpan.FromBounds(clampedStart, clampedEnd);
                var descriptor = new DiagnosticDescriptor(
                    diagnostic.Code,
                    diagnostic.Code,
                    GetSyntaxDiagnosticMessage(diagnostic),
                    "Akbura",
                    (DiagnosticSeverity)diagnostic.Severity,
                    isEnabledByDefault: true);
                diagnostics.Add(Diagnostic.Create(
                    descriptor,
                    Location.Create(file.Path, span, file.Text.Lines.GetLinePositionSpan(span))));
            }
        }
    }

    private static string GetSyntaxDiagnosticMessage(LanguageDiagnostic diagnostic)
    {
        try
        {
            return diagnostic.Message;
        }
        catch (FormatException)
        {
            // Existing Workspace syntax diagnostics preserve a malformed resource
            // as its stable code instead of allowing formatting to break the host.
            return diagnostic.Code;
        }
    }

    private static GeneratorRunResult Run(
        DiagnosticProject project,
        IIncrementalGenerator generator,
        params DiagnosticFile[] files)
    {
        return RunState(project, CreateDriver(project, generator, files)).Result;
    }

    private static GeneratorDriver CreateDriver(DiagnosticProject project, IIncrementalGenerator generator, params DiagnosticFile[] files)
    {
        return CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: files,
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            optionsProvider: project.Options,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    }

    private static DiagnosticRun RunState(
        DiagnosticProject project,
        GeneratorDriver driver,
        CSharpCompilation? compilation = null)
    {
        driver = driver.RunGenerators(compilation ?? project.Compilation);
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Id is "CS8784" or "CS8785");
        return new DiagnosticRun(driver, result);
    }

    private static void AssertSourceLocations(DiagnosticFile file, ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.NotEmpty(diagnostics);
        foreach (var diagnostic in diagnostics)
        {
            var location = diagnostic.Location;
            Assert.Equal(LocationKind.ExternalFile, location.Kind);
            Assert.Equal(file.Path, location.GetLineSpan().Path);
            Assert.InRange(location.SourceSpan.Start, 0, file.Text.Length);
            Assert.InRange(location.SourceSpan.End, location.SourceSpan.Start, file.Text.Length);
            Assert.Equal(file.Text.Lines.GetLinePositionSpan(location.SourceSpan), location.GetLineSpan().Span);
            Assert.Equal(location.GetLineSpan(), location.GetMappedLineSpan());
        }
    }

    private static void AssertDiagnosticParity(ImmutableArray<Diagnostic> expected, ImmutableArray<Diagnostic> actual)
    {
        Assert.Equal(DescribeDiagnostics(expected), DescribeDiagnostics(actual));
    }

    private static string[] DescribeDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Select(static diagnostic => JsonSerializer.Serialize(new
        {
            diagnostic.Id,
            diagnostic.Severity,
            diagnostic.WarningLevel,
            diagnostic.IsSuppressed,
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            Title = diagnostic.Descriptor.Title.ToString(CultureInfo.InvariantCulture),
            diagnostic.Descriptor.Category,
            Description = diagnostic.Descriptor.Description.ToString(CultureInfo.InvariantCulture),
            diagnostic.Descriptor.HelpLinkUri,
            diagnostic.Descriptor.DefaultSeverity,
            diagnostic.Descriptor.IsEnabledByDefault,
            CustomTags = diagnostic.Descriptor.CustomTags.ToArray(),
            Location = DescribeLocation(diagnostic.Location),
            AdditionalLocations = diagnostic.AdditionalLocations.Select(DescribeLocation).ToArray(),
            // Furioso has no transport metadata. Ignore only the four documented
            // publisher fields, never an entire prefix or arbitrary properties.
            Properties = diagnostic.Properties.Where(static pair => !IsTransportProperty(pair.Key))
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray(),
        })).OrderBy(static diagnostic => diagnostic, StringComparer.Ordinal).ToArray();
    }

    private static bool IsTransportProperty(string key)
    {
        return key is "akbura.origin" or "akbura.kind" or "akbura.logical-id" or "akbura.document-version";
    }

    private static object DescribeLocation(Location location)
    {
        var original = location.GetLineSpan();
        var mapped = location.GetMappedLineSpan();
        return new
        {
            location.Kind,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            Original = DescribeLineSpan(original),
            Mapped = DescribeLineSpan(mapped),
        };
    }

    private static object DescribeLineSpan(FileLinePositionSpan span)
    {
        return new
        {
            span.IsValid,
            span.Path,
            span.HasMappedPath,
            StartLine = span.StartLinePosition.Line,
            StartCharacter = span.StartLinePosition.Character,
            EndLine = span.EndLinePosition.Line,
            EndCharacter = span.EndLinePosition.Character,
        };
    }

    private sealed class DiagnosticProject
    {
        public DiagnosticProject()
        {
            Directory = Path.Combine(Path.GetTempPath(), "BlackSilenceDiagnosticTests", Guid.NewGuid().ToString("N"));
            Options = new DiagnosticOptions(Directory);
            Compilation = CSharpCompilation.Create(
                "BlackSilenceDiagnosticTests",
                references: SymbolTests.CreateAvaloniaReferences(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        public string Directory { get; }
        public DiagnosticOptions Options { get; }
        public CSharpCompilation Compilation { get; }

        public DiagnosticFile File(string path, string source)
        {
            return new DiagnosticFile(Path.Combine(Directory, path.Replace('/', Path.DirectorySeparatorChar)), SourceText.From(source));
        }
    }

    private sealed record DiagnosticRun(GeneratorDriver Driver, GeneratorRunResult Result);

    private sealed class DiagnosticFile(string path, SourceText text) : AdditionalText
    {
        public override string Path { get; } = path;
        public SourceText Text { get; } = text;
        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Text;
        }

        public DiagnosticFile Append(string text)
        {
            return new DiagnosticFile(Path, Text.WithChanges(new TextChange(new TextSpan(Text.Length, 0), text)));
        }

        public DiagnosticFile Replace(string oldText, string newText)
        {
            var index = Text.ToString().IndexOf(oldText, StringComparison.Ordinal);
            Assert.True(index >= 0);
            return new DiagnosticFile(Path, Text.WithChanges(new TextChange(new TextSpan(index, oldText.Length), newText)));
        }
    }

    private sealed class DiagnosticOptions(string directory, string mode = "Publish") : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions s_empty = new Values(new Dictionary<string, string>());
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = "Demo",
            ["build_property.ProjectDir"] = directory,
            ["build_property.AkburaBlackSilenceDiagnostics"] = mode,
        });

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => s_empty;
    }

    private sealed class Values(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            return values.TryGetValue(key, out value!);
        }
    }
}
