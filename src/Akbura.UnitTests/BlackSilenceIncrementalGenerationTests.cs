using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Akbura.UnitTests;

public sealed class BlackSilenceIncrementalGenerationTests
{
    private const string RequestsTrackingName = "BlackSilence.GenerationRequests";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EofWhitespace_ReusesEveryCompletedEntryAndMatchesFreshGeneration(bool editAkcss)
    {
        var project = new TestProject();
        var files = new[]
        {
            project.File("Page.akbura", Component("<Border Width=\"10\" />")),
            project.File("Other.akbura", Component("<Border />")),
            project.File("Styles/Shared.akcss", "@using Avalonia.Controls;\r\nBorder.shared { Height: 20; }"),
        };
        var initial = Run(project, CreateDriver(project.Options, files));
        var editedIndex = editAkcss ? 2 : 0;
        var original = files[editedIndex];
        files[editedIndex] = original.WithText(original.Text.WithChanges(new TextChange(new TextSpan(original.Text.Length, 0), "\r\n \t\r\n")));
        var updated = Run(project, initial.Driver.ReplaceAdditionalText(original, files[editedIndex]));

        AssertReusedExcept(initial, updated);
        Assert.Same(initial.Request.Index, updated.Request.Index);
        AssertParityWithFresh(project, updated, project.Options, files);
    }

    [Fact]
    public void LocalMarkupEdit_ReplacesOnlyItsOwnEntryIncludingWhenAnotherComponentUsesIt()
    {
        var project = new TestProject();
        var files = new[]
        {
            project.File("Page.akbura", Component("<Border Width=\"10\" />")),
            project.File("Consumer.akbura", "using Demo;\r\n<Page />"),
            project.File("Other.akbura", Component("<Border />")),
        };
        var initial = Run(project, CreateDriver(project.Options, files));
        var original = files[0];
        files[0] = original.Replace("10", "20");
        var updated = Run(project, initial.Driver.ReplaceAdditionalText(original, files[0]));

        AssertReusedExcept(initial, updated, "component:Page.akbura");
        AssertParityWithFresh(project, updated, project.Options, files);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UtilityPrefixedNestedTextEdit_RoundTripsAndMatchesFreshGeneration(bool preserveTextChanges)
    {
        const string originalAttribute = "Text=\"AKCSS\"";
        const string editedAttribute = "Text=\"AKCSS styles\"";
        var project = new TestProject(publishDiagnostics: true);
        var original = project.File("Page.akbura", Component(
            "using Akbura.Markup;\r\nusing Demo.Shared.akcss;\r\n" +
            "<StackPanel page\r\n            ${md}:page-desktop>\r\n" +
            "    <TextBlock Text=\"AKCSS\"\r\n" +
            "               page-title\r\n" +
            "               ${md}:page-title-desktop/>\r\n" +
            "</StackPanel>"));
        var styles = project.File("Shared.akcss",
            "@using Avalonia.Controls;\r\n" +
            "@utilities {\r\n" +
            "    StackPanel.page { Spacing: 10; }\r\n" +
            "    StackPanel.page-desktop { Spacing: 20; }\r\n" +
            "    TextBlock.page-title { FontSize: 24; }\r\n" +
            "    TextBlock.page-title-desktop { FontSize: 32; }\r\n" +
            "}");
        var initial = Run(project, CreateDriver(project.Options, original, styles));
        Assert.False(Assert.Single(initial.Request.Index.ComponentDescriptors).Root.ContainsDiagnostics);
        var initialSource = initial.Snapshot.Entries["component:Page.akbura"].Source.SourceText.ToString();
        var attributeStart = original.Text.ToString().IndexOf(originalAttribute, StringComparison.Ordinal);
        Assert.True(attributeStart >= 0);

        // Replace the full benchmark anchor while preserving both utility-prefix boundaries.
        // AdditionalText publishers may return equivalent text without its change history.
        var editedText = original.Text.WithChanges(
            new TextChange(new TextSpan(attributeStart, originalAttribute.Length), editedAttribute));
        var edited = original.WithText(preserveTextChanges ? editedText : SourceText.From(editedText.ToString()));
        var updated = Run(project, initial.Driver.ReplaceAdditionalText(original, edited));
        Assert.False(Assert.Single(updated.Request.Index.ComponentDescriptors).Root.ContainsDiagnostics);
        var updatedSource = updated.Snapshot.Entries["component:Page.akbura"].Source.SourceText.ToString();

        Assert.NotEqual(initialSource, updatedSource);
        Assert.Contains("\"AKCSS styles\"", updatedSource, StringComparison.Ordinal);
        AssertParityWithFresh(project, updated, project.Options, edited, styles);

        var restoredText = edited.Text.WithChanges(
            new TextChange(new TextSpan(attributeStart, editedAttribute.Length), originalAttribute));
        var restored = edited.WithText(preserveTextChanges ? restoredText : SourceText.From(restoredText.ToString()));
        var reverted = Run(project, updated.Driver.ReplaceAdditionalText(edited, restored));
        Assert.False(Assert.Single(reverted.Request.Index.ComponentDescriptors).Root.ContainsDiagnostics);
        var revertedSource = reverted.Snapshot.Entries["component:Page.akbura"].Source.SourceText.ToString();

        Assert.Equal(original.Text.ToString(), restored.Text.ToString());
        Assert.NotEqual(updatedSource, revertedSource);
        Assert.Equal(initialSource, revertedSource);
        AssertParityWithFresh(project, reverted, project.Options, restored, styles);
    }

    [Theory]
    [InlineData("Height: 24;", "Height: 48;")]
    [InlineData("double value", "float value")]
    public void RepeatedUtilityApply_AkcssEditsRoundTripWithoutReusingOldLookupData(string before, string after)
    {
        var project = new TestProject(publishDiagnostics: true);
        var page = project.File("Page.akbura", Component(
            "using Demo.Styles.Shared.akcss;\r\n" +
            "<StackPanel><Border class=\"first\" /><Border class=\"second\" /></StackPanel>"));
        var other = project.File("Other.akbura", Component("<Border />"));
        var original = project.File("Styles/Shared.akcss",
            "@using Avalonia.Controls;\r\n" +
            "@utilities {\r\n" +
            "    Border.enabled { Height: 24; }\r\n" +
            "    Border.scale-(double value) { Opacity: value; }\r\n" +
            "}\r\n" +
            "Border.first { @apply enabled scale-1; }\r\n" +
            "Border.second { @apply enabled scale-1; }\r\n");
        var initial = Run(project, CreateDriver(project.Options, page, other, original));
        var initialSource = initial.Snapshot.Entries["akcss:Styles/Shared.akcss"].Source.SourceText.ToString();
        AssertParityWithFresh(project, initial, project.Options, page, other, original);

        var edited = original.Replace(before, after);
        var updated = Run(project, initial.Driver.ReplaceAdditionalText(original, edited));
        var updatedSource = updated.Snapshot.Entries["akcss:Styles/Shared.akcss"].Source.SourceText.ToString();
        Assert.NotEqual(initialSource, updatedSource);
        AssertReusedExcept(initial, updated, "component:Page.akbura", "akcss:Styles/Shared.akcss");
        AssertParityWithFresh(project, updated, project.Options, page, other, edited);

        var restored = edited.Replace(after, before);
        var reverted = Run(project, updated.Driver.ReplaceAdditionalText(edited, restored));
        Assert.Equal(original.Text.ToString(), restored.Text.ToString());
        Assert.Equal(initialSource, reverted.Snapshot.Entries["akcss:Styles/Shared.akcss"].Source.SourceText.ToString());
        AssertReusedExcept(updated, reverted, "component:Page.akbura", "akcss:Styles/Shared.akcss");
        AssertParityWithFresh(project, reverted, project.Options, page, other, restored);
    }

    [Fact]
    public void ParameterTypeEdit_ReplacesComponentAndConsumerButNotUnrelatedEntries()
    {
        var project = new TestProject();
        var files = new[]
        {
            project.File("ValueView.akbura", Component("param double Value = 0d;\r\n<Border />")),
            project.File("Consumer.akbura", "using Demo;\r\n<ValueView Value=\"10\" />"),
            project.File("Other.akbura", Component("<Border />")),
        };
        var initial = Run(project, CreateDriver(project.Options, files));
        var original = files[0];
        files[0] = original.Replace("param double Value = 0d;", "param string Value = \"initial\";");
        var updated = Run(project, initial.Driver.ReplaceAdditionalText(original, files[0]));

        AssertReusedExcept(initial, updated, "component:ValueView.akbura", "component:Consumer.akbura");
        AssertParityWithFresh(project, updated, project.Options, files);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppliedModuleBodyOrLineEdit_ReplacesTransitiveDependentsAndMatchesFreshGeneration(bool changeLine)
    {
        var project = new TestProject();
        var files = new[]
        {
            project.File("Page.akbura", Component("using Demo.Styles.Top.akcss;\r\n<Border class=\"top\" />")),
            project.File("Other.akbura", Component("<Border />")),
            project.File("Styles/Top.akcss", "@using Demo.Styles.Base.akcss;\r\n.top { @apply basic; }"),
            project.File("Styles/Base.akcss", "@using Avalonia.Controls;\r\nBorder.basic { Width: 10; }"),
        };
        var initial = Run(project, CreateDriver(project.Options, files));
        var original = files[3];
        files[3] = changeLine
            ? original.WithText(original.Text.WithChanges(new TextChange(new TextSpan(0, 0), "\r\n")))
            : original.Replace("10", "20");
        var updated = Run(project, initial.Driver.ReplaceAdditionalText(original, files[3]));

        AssertReusedExcept(initial, updated, "component:Page.akbura", "akcss:Styles/Top.akcss", "akcss:Styles/Base.akcss");
        AssertParityWithFresh(project, updated, project.Options, files);
    }

    [Fact]
    public void IsolatedSharedStyleEdit_RoundTripsWithoutInvalidatingUnrelatedText()
    {
        const string fixtureNamespace = "Demo.IncrementalBenchmarks";
        const string baseName = "AkburaBenchmarkSharedStyleBaseModule";
        const string importedName = "AkburaBenchmarkSharedStyleImportedModule";
        const string consumerName = "AkburaBenchmarkSharedStyleConsumer";
        const string baseIdentity = "akcss:IncrementalBenchmarks/" + baseName + ".akcss";
        var project = new TestProject(publishDiagnostics: true);
        var basic = project.File("IncrementalBenchmarks/" + baseName + ".akcss",
            "@using Avalonia.Controls;\r\nBorder.incremental-base { Width: 10; }\r\n");
        var imported = project.File("IncrementalBenchmarks/" + importedName + ".akcss",
            "@using " + fixtureNamespace + "." + baseName + ".akcss;\r\n" +
            ".incremental-imported { @apply incremental-base; }\r\n");
        var consumer = project.File("IncrementalBenchmarks/" + consumerName + ".akbura", Component(
            "using " + fixtureNamespace + "." + importedName + ".akcss;\r\n" +
            "<Border class=\"incremental-imported\" />\r\n"));
        var unrelated = project.File("Page.akbura", Component("<TextBlock Text=\"Imported modules\" />"));
        var changedIdentities = new[]
        {
            baseIdentity,
            "akcss:IncrementalBenchmarks/" + importedName + ".akcss",
            "component:IncrementalBenchmarks/" + consumerName + ".akbura",
        };
        var initial = Run(project, CreateDriver(project.Options, basic, imported, consumer, unrelated));
        Assert.Equal(4, initial.Snapshot.Entries.Count);
        foreach (var identity in changedIdentities)
        {
            Assert.True(initial.Snapshot.Entries.ContainsKey(identity), identity);
        }

        Assert.True(initial.Snapshot.Entries.ContainsKey("component:Page.akbura"));
        var initialSource = initial.Snapshot.Entries[baseIdentity].Source.SourceText.ToString();
        var edited = basic.Replace("Width: 10;", "Width: 20;");
        var updated = Run(project, initial.Driver.ReplaceAdditionalText(basic, edited));
        var updatedSource = updated.Snapshot.Entries[baseIdentity].Source.SourceText.ToString();

        Assert.NotEqual(initialSource, updatedSource);
        AssertReusedExcept(initial, updated, changedIdentities);
        AssertParityWithFresh(project, updated, project.Options, edited, imported, consumer, unrelated);

        var restored = edited.Replace("Width: 20;", "Width: 10;");
        var reverted = Run(project, updated.Driver.ReplaceAdditionalText(edited, restored));
        var revertedSource = reverted.Snapshot.Entries[baseIdentity].Source.SourceText.ToString();

        Assert.NotEqual(updatedSource, revertedSource);
        Assert.Equal(initialSource, revertedSource);
        AssertReusedExcept(updated, reverted, changedIdentities);
        AssertParityWithFresh(project, reverted, project.Options, restored, imported, consumer, unrelated);
    }

    [Fact]
    public void GlobalUsingEditInOrdinaryComponent_RegeneratesEveryAffectedDocument()
    {
        var project = new TestProject();
        var files = new[]
        {
            project.File("Imports.akbura", "global using Avalonia.Controls;\r\n<Border />"),
            project.File("Page.akbura", "<Border />"),
        };
        var initial = Run(project, CreateDriver(project.Options, files));
        var original = files[0];
        files[0] = original.Replace("<Border />", "global using System;\r\n<Border />");
        var updated = Run(project, initial.Driver.ReplaceAdditionalText(original, files[0]));

        AssertReusedExcept(initial, updated, initial.Snapshot.Entries.Keys.ToArray());
        AssertParityWithFresh(project, updated, project.Options, files);
    }

    [Fact]
    public void InsertingInlineModule_UpdatesShiftedIdentitiesWithoutReusingWrongModule()
    {
        const string originalBody = "@akcss { @using Avalonia.Controls; .local { Width: 10; } }\r\n<Border class=\"local\" />";
        const string insertedBlock = "@akcss { @using Avalonia.Controls; .added { Height: 20; } }\r\n";
        var project = new TestProject();
        var files = new[]
        {
            project.File("Page.akbura", Component(originalBody)),
            project.File("Other.akbura", Component("<Border />")),
        };
        var initial = Run(project, CreateDriver(project.Options, files));
        var original = files[0];
        files[0] = original.Replace(originalBody, insertedBlock + originalBody);
        var updated = Run(project, initial.Driver.ReplaceAdditionalText(original, files[0]));

        AssertReusedExcept(initial, updated, "component:Page.akbura", "akcss:Page.akbura.inline.0.akcss");
        Assert.Contains("akcss:Page.akbura.inline.1.akcss", updated.Snapshot.Entries.Keys);
        Assert.Equal(initial.Snapshot.Entries.Count + 1, updated.Snapshot.Entries.Count);
        AssertParityWithFresh(project, updated, project.Options, files);
    }

    [Fact]
    public void AddedRemovedAndReorderedDocuments_KeepOutputSetEqualToFreshGeneration()
    {
        var project = new TestProject();
        var first = project.File("First.akbura", Component("<Border />"));
        var second = project.File("Second.akbura", Component("<Border />"));
        var third = project.File("Third.akbura", Component("<Border />"));
        var initial = Run(project, CreateDriver(project.Options, first, second));
        var addedFiles = new[] { first, second, third };
        var added = Run(project, initial.Driver.AddAdditionalTexts([third]));

        Assert.Equal(3, added.Snapshot.Entries.Count);
        AssertParityWithFresh(project, added, project.Options, addedFiles);

        var remainingFiles = new[] { first, third };
        var removed = Run(project, added.Driver.RemoveAdditionalTexts([second]));

        Assert.DoesNotContain("component:Second.akbura", removed.Snapshot.Entries.Keys);
        AssertParityWithFresh(project, removed, project.Options, remainingFiles);

        // The first cached run after a removal must preserve every surviving slot.
        var unchanged = Run(project, removed.Driver);

        AssertReusedExcept(removed, unchanged);
        AssertParityWithFresh(project, unchanged, project.Options, remainingFiles);

        Array.Reverse(remainingFiles);
        var reordered = Run(project, removed.Driver.ReplaceAdditionalTexts(remainingFiles.Cast<AdditionalText>().ToImmutableArray()));

        AssertParityWithFresh(project, reordered, project.Options, remainingFiles);
    }

    [Fact]
    public void RequestBuilder_CanonicalizesPermutationsBeforeIndexingAndReusesCompletedEntries()
    {
        var project = new TestProject();
        var firstFile = project.File("First/Item.akbura", Component("<Border Width=\"10\" />"));
        var secondFile = project.File("Second/Item.akbura", Component("<Border Height=\"20\" />"));
        var styleFile = project.File("Shared.akcss", "@using Avalonia.Controls;\r\nBorder.shared { Width: 10; }");
        var first = DocumentSyntaxVersion.Create(ComponentSyntaxTree.ParseText(firstFile.Text, firstFile.Path));
        var second = DocumentSyntaxVersion.Create(ComponentSyntaxTree.ParseText(secondFile.Text, secondFile.Path));
        var style = DocumentSyntaxVersion.Create(AkcssSyntaxTree.ParseText(styleFile.Text, styleFile.Path, "Demo.Shared.akcss"));
        var state = new BlackSilenceProjectState(project.Compilation);
        var options = new GeneratorProjectOptions("Demo", project.Directory);
        var initial = Assert.IsType<BlackSilenceGenerationRequest>(BlackSilenceGenerationRequestBuilder.Create(
            [style, second, first], state, options, CancellationToken.None));

        Assert.Equal(new[] { first, second, style }, initial.Documents.ToArray());
        Assert.Equal(
            new[] { "Demo.First.Item", "Demo.Second.Item" },
            initial.Index.ComponentDescriptors.Select(static component => component.ComponentMetadataName).ToArray());
        BlackSilenceDocumentBatch.Generate(initial, CancellationToken.None);
        var snapshot = Assert.IsType<BlackSilenceProjectSnapshot>(state.TryGetSnapshot(options));
        var reordered = Assert.IsType<BlackSilenceGenerationRequest>(BlackSilenceGenerationRequestBuilder.Create(
            [second, style, first], state, options, CancellationToken.None));

        Assert.Same(initial.Index, reordered.Index);
        Assert.Same(initial.DeclarationEnvironment, reordered.DeclarationEnvironment);
        Assert.All(reordered.Components, request => Assert.Same(
            snapshot.Entries["component:" + request.Descriptor.SourcePath], request.Previous));
        Assert.All(reordered.ExternalAkcss, request => Assert.Same(
            snapshot.Entries["akcss:" + request.Descriptor.ModuleIdentity], request.Previous));
    }

    [Fact]
    public void ReorderedSameShortNameComponents_KeepCanonicalDeclarationsAndMatchFreshGeneration()
    {
        var project = new TestProject();
        var first = project.File("First/Item.akbura", Component("<Border Width=\"10\" />"));
        var second = project.File("Second/Item.akbura", Component("<Border Height=\"20\" />"));
        var page = project.File("Page.akbura", Component("<StackPanel><Demo.First.Item /><Demo.Second.Item /></StackPanel>"));
        var initial = Run(project, CreateDriver(project.Options, second, page, first));
        var files = new[] { first, second, page };
        var reordered = Run(project, initial.Driver.ReplaceAdditionalTexts(files.Cast<AdditionalText>().ToImmutableArray()));

        Assert.Equal(
            new[] { "First/Item.akbura", "Page.akbura", "Second/Item.akbura" },
            initial.Request.Index.ComponentDescriptors.Select(static component => component.SourcePath).ToArray());
        AssertReusedExcept(initial, reordered);
        AssertParityWithFresh(project, reordered, project.Options, files);
        AssertParityWithFresh(project, reordered, project.Options, page, second, first);
    }

    [Fact]
    public async Task ConcurrentBranchesFromOneWarmDriver_KeepTheirOwnOutputsAndMatchFreshGeneration()
    {
        var project = new TestProject();
        var first = project.File("First.akbura", Component("<Border Width=\"10\" />"));
        var second = project.File("Second.akbura", Component("<Border Height=\"10\" />"));
        var baseline = Run(project, CreateDriver(project.Options, first, second));
        var changedFirst = first.Replace("10", "20");
        var changedSecond = second.Replace("10", "30");
        using var start = new ManualResetEventSlim();
        var leftTask = Task.Run(() =>
        {
            start.Wait();
            return RunOutput(project, baseline.Driver.ReplaceAdditionalText(first, changedFirst));
        });
        var rightTask = Task.Run(() =>
        {
            start.Wait();
            return RunOutput(project, baseline.Driver.ReplaceAdditionalText(second, changedSecond));
        });

        start.Set();
        await Task.WhenAll(leftTask, rightTask);
        var left = await leftTask;
        var right = await rightTask;

        // A shared state's latest snapshot can belong to either racing branch.
        // Assert each driver's own outputs, not whichever snapshot won publication.
        AssertParityWithFresh(project, left.Driver, left.Output, project.Options, changedFirst, second);
        AssertParityWithFresh(project, right.Driver, right.Output, project.Options, first, changedSecond);

        var resumedFirst = changedFirst.Replace("20", "40");
        var resumed = Run(project, left.Driver.ReplaceAdditionalText(changedFirst, resumedFirst));
        AssertParityWithFresh(project, resumed, project.Options, resumedFirst, second);
    }

    [Fact]
    public void TwoBranchesFromOneWarmDriver_DoNotPublishStaleResultsIntoEachOther()
    {
        var project = new TestProject();
        var first = project.File("First.akbura", Component("<Border Width=\"10\" />"));
        var second = project.File("Second.akbura", Component("<Border Height=\"10\" />"));
        var baseline = Run(project, CreateDriver(project.Options, first, second));
        var changedFirst = first.Replace("10", "20");
        var left = Run(project, baseline.Driver.ReplaceAdditionalText(first, changedFirst));
        var changedSecond = second.Replace("10", "30");
        var right = Run(project, baseline.Driver.ReplaceAdditionalText(second, changedSecond));

        Assert.Same(left.Request.State, right.Request.State);
        AssertParityWithFresh(project, left, project.Options, changedFirst, second);
        AssertParityWithFresh(project, right, project.Options, first, changedSecond);
        Assert.NotSame(left.Snapshot, right.Snapshot);

        // Resume the old branch after the other branch has published a newer snapshot.
        var resumedFirst = changedFirst.Replace("20", "40");
        var resumed = Run(project, left.Driver.ReplaceAdditionalText(changedFirst, resumedFirst));

        AssertParityWithFresh(project, resumed, project.Options, resumedFirst, second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectOptionEdit_UpdatesIdentitiesWithoutRereadingAdditionalTexts(bool changeDirectory)
    {
        var project = new TestProject();
        var files = new[]
        {
            project.File("Views/Page.akbura", Component("<Border />")),
            project.File("Views/Shared.akcss", "@using Avalonia.Controls;\r\nBorder.shared { Width: 10; }"),
        };
        var initial = Run(project, CreateDriver(project.Options, files));
        var options = new TestOptions(
            changeDirectory ? "Demo" : "Renamed",
            changeDirectory ? Path.Combine(project.Directory, "Views") : project.Directory);
        var updated = Run(project, initial.Driver.WithUpdatedAnalyzerConfigOptions(options));

        Assert.All(files, static file => Assert.Equal(1, file.ReadCount));
        AssertParityWithFresh(project, updated, options, files);
    }

    [Fact]
    public void CanceledPreparedBatch_LeavesPublishedSnapshotIntactAndCanBeRetried()
    {
        var project = new TestProject();
        var original = project.File("Page.akbura", Component("<Border Width=\"10\" />"));
        var initial = Run(project, CreateDriver(project.Options, original));
        var changed = original.Replace("10", "20");
        var syntaxTree = Assert.IsType<ComponentSyntaxTree>(Assert.Single(initial.Request.Documents).SyntaxTree);
        var version = DocumentSyntaxVersion.Create(syntaxTree.WithChangedText(changed.Text));
        var request = Assert.IsType<BlackSilenceGenerationRequest>(BlackSilenceGenerationRequestBuilder.Create(
            [version], initial.Request.State, initial.Request.Options, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => BlackSilenceDocumentBatch.Generate(request, cancellation.Token));
        Assert.Same(initial.Snapshot, initial.Request.State.TryGetSnapshot(initial.Request.Options));

        var batch = BlackSilenceDocumentBatch.Generate(request, CancellationToken.None);
        Assert.Single(batch.Components);
        Assert.NotSame(initial.Snapshot, initial.Request.State.TryGetSnapshot(initial.Request.Options));

        var updated = Run(project, initial.Driver.ReplaceAdditionalText(original, changed));
        AssertParityWithFresh(project, updated, project.Options, changed);
    }

    private static string Component(string body) => "using Avalonia.Controls;\r\n" + body;

    private static TestRun Run(TestProject project, GeneratorDriver driver)
    {
        var run = RunOutput(project, driver);
        var result = Assert.Single(run.Driver.GetRunResult().Results);
        Assert.True(result.TrackedSteps.TryGetValue(RequestsTrackingName, out var steps));
        var request = Assert.IsType<BlackSilenceGenerationRequest>(Assert.Single(steps.SelectMany(static step => step.Outputs)).Value);
        var snapshot = Assert.IsType<BlackSilenceProjectSnapshot>(request.State.TryGetSnapshot(request.Options));

        return new TestRun(run.Driver, run.Output, request, snapshot);
    }

    private static (GeneratorDriver Driver, Compilation Output) RunOutput(TestProject project, GeneratorDriver driver)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(project.Compilation, out var output, out var driverDiagnostics);
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        AssertNoErrors(driverDiagnostics);
        AssertNoErrors(result.Diagnostics);
        AssertNoErrors(output.GetDiagnostics());

        return (driver, output);
    }

    private static void AssertReusedExcept(TestRun previous, TestRun current, params string[] changedIdentities)
    {
        foreach (var pair in previous.Snapshot.Entries)
        {
            Assert.True(current.Snapshot.Entries.TryGetValue(pair.Key, out var entry), pair.Key);

            if (changedIdentities.Contains(pair.Key, StringComparer.Ordinal))
            {
                Assert.NotSame(pair.Value, entry);
                Assert.NotSame(pair.Value.Source.SourceText, entry.Source.SourceText);
            }
            else
            {
                // Read the pre-output cache: a downstream ContentEquals comparer can
                // otherwise hide a complete regeneration of identical C# text.
                Assert.Same(pair.Value, entry);
                Assert.Same(pair.Value.Source.SourceText, entry.Source.SourceText);
            }
        }
    }

    private static void AssertParityWithFresh(TestProject project, TestRun incremental, TestOptions options, params TestFile[] files)
    {
        AssertParityWithFresh(project, incremental.Driver, incremental.Output, options, files);
    }

    private static void AssertParityWithFresh(
        TestProject project,
        GeneratorDriver incrementalDriver,
        Compilation incrementalOutput,
        TestOptions options,
        params TestFile[] files)
    {
        var freshFiles = files.Select(static file => new TestFile(file.Path, file.Text)).ToArray();
        var fresh = Run(project, CreateDriver(options, freshFiles));
        var incrementalResult = Assert.Single(incrementalDriver.GetRunResult().Results);
        var freshResult = Assert.Single(fresh.Driver.GetRunResult().Results);
        var expected = freshResult.GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal).ToArray();
        var actual = incrementalResult.GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].HintName, actual[i].HintName);
            Assert.Equal(expected[i].SourceText.ToString(), actual[i].SourceText.ToString());
        }

        Assert.Equal(GetDiagnostics(freshResult.Diagnostics), GetDiagnostics(incrementalResult.Diagnostics));
        Assert.Equal(GetDiagnostics(fresh.Output.GetDiagnostics()), GetDiagnostics(incrementalOutput.GetDiagnostics()));
    }

    private static string[] GetDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Select(static diagnostic =>
        {
            var span = diagnostic.Location.GetMappedLineSpan();
            return diagnostic.Id + "|" + diagnostic.Severity + "|" + diagnostic.GetMessage(CultureInfo.InvariantCulture) + "|" +
                span.Path + "|" + span.StartLinePosition + "|" + span.EndLinePosition + "|" + diagnostic.Location.SourceSpan;
        }).OrderBy(static diagnostic => diagnostic, StringComparer.Ordinal).ToArray();
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
    }

    private static GeneratorDriver CreateDriver(TestOptions options, params TestFile[] files)
    {
        return CSharpGeneratorDriver.Create(
            generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: files,
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            optionsProvider: options,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    }

    private sealed class TestRun(
        GeneratorDriver driver,
        Compilation output,
        BlackSilenceGenerationRequest request,
        BlackSilenceProjectSnapshot snapshot)
    {
        public GeneratorDriver Driver { get; } = driver;
        public Compilation Output { get; } = output;
        public BlackSilenceGenerationRequest Request { get; } = request;
        public BlackSilenceProjectSnapshot Snapshot { get; } = snapshot;
    }

    private sealed class TestProject
    {
        public TestProject(bool publishDiagnostics = false)
        {
            Directory = Path.Combine(Path.GetTempPath(), "BlackSilenceIncrementalTests", Guid.NewGuid().ToString("N"));
            Options = new TestOptions("Demo", Directory, publishDiagnostics);
            Compilation = CSharpCompilation.Create(
                "BlackSilenceIncrementalTests",
                syntaxTrees: [CSharpSyntaxTree.ParseText(string.Empty, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
                references: SymbolTests.CreateAvaloniaReferences(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        }

        public string Directory { get; }
        public TestOptions Options { get; }
        public CSharpCompilation Compilation { get; }

        public TestFile File(string relativePath, string source)
        {
            return new TestFile(Path.Combine(Directory, relativePath.Replace('/', Path.DirectorySeparatorChar)), SourceText.From(source));
        }
    }

    private sealed class TestFile(string path, SourceText text) : AdditionalText
    {
        private int _readCount;

        public override string Path { get; } = path;
        public SourceText Text { get; } = text;
        public int ReadCount => Volatile.Read(ref _readCount);

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return Text;
        }

        public TestFile WithText(SourceText updated) => new(Path, updated);

        public TestFile Replace(string oldValue, string newValue)
        {
            var start = Text.ToString().IndexOf(oldValue, StringComparison.Ordinal);
            Assert.True(start >= 0, oldValue);
            return WithText(Text.WithChanges(new TextChange(new TextSpan(start, oldValue.Length), newValue)));
        }
    }

    private sealed class TestOptions(string rootNamespace, string projectDirectory, bool publishDiagnostics = false) : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions s_empty = new Values(new Dictionary<string, string>());

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = rootNamespace,
            ["build_property.ProjectDir"] = projectDirectory,
            ["build_property.AkburaBlackSilenceDiagnostics"] = publishDiagnostics ? "Publish" : "Shadow",
        });

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => s_empty;
    }

    private sealed class Values(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var result))
            {
                value = result;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
