using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Benchmarks;

public enum FeatureGalleryIncrementalScenario
{
    ComponentValueEdit,
    ComponentBindingEdit,
    ComponentContractEdit,
    SharedAkcssValueEdit,
    ReferencedCSharpRename,
}

internal sealed record FeatureGalleryIncrementalInputs(
    CSharpCompilation OriginalCompilation,
    CSharpCompilation ModifiedCompilation,
    ImmutableArray<AdditionalText> OriginalFiles,
    ImmutableArray<AdditionalText> ModifiedFiles,
    FeatureGalleryBenchmarkSnapshot Snapshot);

/// <summary>
/// Two valid revisions over the complete real FeatureGallery. The first two
/// scenarios edit existing gallery components; dependency scenarios add small,
/// explicitly named virtual fixtures without modifying files on disk.
/// </summary>
internal static class FeatureGalleryIncrementalScenarioFactory
{
    public static FeatureGalleryIncrementalInputs Create(
        FeatureGalleryBenchmarkProject project,
        FeatureGalleryIncrementalScenario scenario)
    {
        var snapshot = project.CreateSnapshot();
        var originalFiles = snapshot.AdditionalTexts;
        var modifiedFiles = originalFiles;
        var originalCompilation = project.Compilation;
        var modifiedCompilation = originalCompilation;
        var fixtureNamespace = project.RootNamespace + ".IncrementalBenchmarks";

        switch (scenario)
        {
            case FeatureGalleryIncrementalScenario.ComponentValueEdit:
            {
                var original = FindGalleryFile(snapshot, "Pages/AkcssPage.akbura");
                var modified = Replace(original, "Text=\"AKCSS\"", "Text=\"AKCSS styles\"");
                modifiedFiles = originalFiles.Replace(original, modified);
                break;
            }

            case FeatureGalleryIncrementalScenario.ComponentBindingEdit:
            {
                var original = FindGalleryFile(snapshot, "Components/MarkupExtensionPrefixDemo.akbura");
                var modified = Replace(
                    original,
                    "${Binding #MyToggle.IsChecked}:p-10",
                    "${Binding #MyToggle.IsEnabled}:p-10");
                modifiedFiles = originalFiles.Replace(original, modified);
                break;
            }

            case FeatureGalleryIncrementalScenario.ComponentContractEdit:
            {
                var view = Fixture(
                    "ContractView.akbura",
                    "using Avalonia.Controls;\r\n" +
                    "param string Caption = \"initial\";\r\n" +
                    "<TextBlock Text={Caption} />\r\n");
                var consumer = Fixture(
                    "ContractConsumer.akbura",
                    "using " + fixtureNamespace + ";\r\n" +
                    "<ContractView Caption=\"consumer\" />\r\n");
                AddFixtures(view, consumer);

                // Rename the exported parameter and both its local expression
                // and consumer together: neither revision has a broken binding.
                var modifiedView = Replace(view, "param string Caption", "param string Title");
                modifiedView = Replace(modifiedView, "Text={Caption}", "Text={Title}");
                var modifiedConsumer = Replace(consumer, "Caption=\"consumer\"", "Title=\"consumer\"");
                modifiedFiles = originalFiles.Replace(view, modifiedView).Replace(consumer, modifiedConsumer);
                break;
            }

            case FeatureGalleryIncrementalScenario.SharedAkcssValueEdit:
            {
                const string baseName = "AkburaBenchmarkSharedStyleBaseModule";
                const string importedName = "AkburaBenchmarkSharedStyleImportedModule";
                const string consumerName = "AkburaBenchmarkSharedStyleConsumer";
                VerifyIsolatedFixtureNames(project, snapshot, fixtureNamespace, baseName, importedName, consumerName);

                var basic = Fixture(
                    baseName + ".akcss",
                    "@using Avalonia.Controls;\r\n" +
                    "Border.incremental-base { Width: 10; }\r\n");
                var imported = Fixture(
                    importedName + ".akcss",
                    "@using " + fixtureNamespace + "." + baseName + ".akcss;\r\n" +
                    ".incremental-imported { @apply incremental-base; }\r\n");
                var consumer = Fixture(
                    consumerName + ".akbura",
                    "using Avalonia.Controls;\r\n" +
                    "using " + fixtureNamespace + "." + importedName + ".akcss;\r\n" +
                    "<Border class=\"incremental-imported\" />\r\n");
                AddFixtures(basic, imported, consumer);

                // Base module -> imported @apply class -> component. The other
                // two documents are unchanged inputs but remain real consumers.
                var modified = Replace(basic, "Width: 10;", "Width: 20;");
                modifiedFiles = originalFiles.Replace(basic, modified);
                break;
            }

            case FeatureGalleryIncrementalScenario.ReferencedCSharpRename:
            {
                const string probeName = "IncrementalProbe";
                if (originalCompilation.GetTypeByMetadataName(fixtureNamespace + "." + probeName) != null)
                {
                    throw new InvalidOperationException("The incremental benchmark probe type already exists.");
                }

                var probePath = FixturePath(probeName + ".cs");
                var probeText = SourceText.From(
                    "namespace " + fixtureNamespace + ";\r\n" +
                    "internal static class " + probeName + "\r\n" +
                    "{\r\n" +
                    "    public static string Caption => \"probe value\";\r\n" +
                    "}\r\n");
                var probe = CSharpSyntaxTree.ParseText(probeText, project.ParseOptions, probePath);
                var modifiedProbeText = Replace(probeText, probePath, "string Caption", "string Title");
                var modifiedProbe = probe.WithChangedText(modifiedProbeText);
                originalCompilation = originalCompilation.AddSyntaxTrees(probe);
                modifiedCompilation = originalCompilation.ReplaceSyntaxTree(probe, modifiedProbe);

                var consumer = Fixture(
                    "ProbeConsumer.akbura",
                    "using Avalonia.Controls;\r\n" +
                    "using " + fixtureNamespace + ";\r\n" +
                    "<TextBlock Text={IncrementalProbe.Caption} />\r\n");
                AddFixtures(consumer);
                var modifiedConsumer = Replace(consumer, "IncrementalProbe.Caption", "IncrementalProbe.Title");
                modifiedFiles = originalFiles.Replace(consumer, modifiedConsumer);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown incremental scenario.");
        }

        return new FeatureGalleryIncrementalInputs(
            originalCompilation, modifiedCompilation, originalFiles, modifiedFiles, snapshot);

        string FixturePath(string name)
        {
            var path = Path.Combine(snapshot.ProjectDirectory, "IncrementalBenchmarks", name);
            if (originalFiles.Any(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                originalCompilation.SyntaxTrees.Any(tree => string.Equals(tree.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("The incremental benchmark fixture path already exists: " + path);
            }

            return path;
        }

        BenchmarkAdditionalText Fixture(string name, string source)
        {
            return new BenchmarkAdditionalText(FixturePath(name), SourceText.From(source));
        }

        void AddFixtures(params AdditionalText[] files)
        {
            originalFiles = originalFiles.AddRange(files);
            modifiedFiles = originalFiles;
        }
    }

    private static BenchmarkAdditionalText FindGalleryFile(FeatureGalleryBenchmarkSnapshot snapshot, string relativePath)
    {
        var matches = snapshot.AdditionalTexts.Where(file => string.Equals(
            Path.GetRelativePath(snapshot.ProjectDirectory, file.Path).Replace('\\', '/'),
            relativePath,
            StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || matches[0] is not BenchmarkAdditionalText match)
        {
            throw new InvalidOperationException("Expected exactly one real FeatureGallery input: " + relativePath);
        }

        return match;
    }

    private static void VerifyIsolatedFixtureNames(
        FeatureGalleryBenchmarkProject project,
        FeatureGalleryBenchmarkSnapshot snapshot,
        string fixtureNamespace,
        params string[] names)
    {
        // Dependency candidates include words in markup literals, not only actual
        // imports. A generic module name such as Imported also matches gallery UI.
        foreach (var file in snapshot.AdditionalTexts)
        {
            var text = file.GetText() ??
                throw new InvalidOperationException("Cannot check benchmark fixture isolation: " + file.Path);
            VerifySource(file.Path, text.ToString());
        }

        foreach (var tree in project.Compilation.SyntaxTrees)
        {
            VerifySource(tree.FilePath, tree.GetText().ToString());
        }

        foreach (var name in names)
        {
            if (project.Compilation.GetTypeByMetadataName(fixtureNamespace + "." + name) != null)
            {
                throw new InvalidOperationException("The isolated benchmark fixture type already exists: " + name);
            }
        }

        void VerifySource(string filePath, string source)
        {
            foreach (var name in names)
            {
                if (filePath.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                    source.Contains(name, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Benchmark fixture name '{name}' already occurs in '{filePath}'.");
                }
            }

            // ValueText also detects escaped C# identifiers, unlike a raw-text search.
            foreach (var token in SyntaxFactory.ParseTokens(source))
            {
                if (names.Contains(token.ValueText, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Benchmark fixture name '{token.ValueText}' already occurs in '{filePath}'.");
                }
            }
        }
    }

    private static BenchmarkAdditionalText Replace(BenchmarkAdditionalText file, string before, string after)
    {
        return new BenchmarkAdditionalText(file.Path, Replace(file.SourceText, file.Path, before, after));
    }

    private static SourceText Replace(SourceText source, string filePath, string before, string after)
    {
        var text = source.ToString();
        var start = text.IndexOf(before, StringComparison.Ordinal);
        if (before.Length == 0 || before == after || start < 0 ||
            text.IndexOf(before, start + before.Length, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("Expected one nontrivial incremental edit anchor in '" + filePath + "': " + before);
        }

        return source.WithChanges(new TextChange(new TextSpan(start, before.Length), after));
    }
}
