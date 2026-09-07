using Akbura.BlackSilence;
using Akbura.Diagnostics;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.UnitTests;

public sealed class BlackSilenceDiagnosticSnapshotTests
{
    [Fact]
    public void FailureAfterFirstDiagnostic_DoesNotPublishPartialDiagnosticsOrSourcesAndCanRetry()
    {
        var project = new SnapshotProject();
        var initial = project.Warm();
        var first = new ControlledSourceText("using System;\r\n<object />\r\n");
        var second = new ControlledSourceText("using System;\r\n<string />\r\n");
        var request = project.CreateRequest(first, second, width: 20);
        var firstReads = 0;
        first.OnRead = () => firstReads++;
        second.OnRead = () =>
        {
            Assert.True(firstReads > 0);
            throw new InvalidOperationException("Synthetic source snapshot read failed.");
        };

        var failed = BlackSilenceDocumentBatch.GenerateSafely(request, CancellationToken.None);

        var failure = Assert.Single(failed.Diagnostics);
        Assert.Equal("AKBURA_GENERATOR_FAILURE", failure.Id);
        Assert.Equal(AkburaDiagnosticKind.Infrastructure, failure.Kind);
        Assert.Contains("Synthetic source snapshot read failed.", failure.Message, StringComparison.Ordinal);
        Assert.Empty(failed.Components);
        Assert.Same(initial, project.State.TryGetSnapshot(project.Options));
        Assert.Empty(initial.DiagnosticEntries[project.FirstGlobalPath].Diagnostics);
        Assert.Single(initial.Entries);

        first.OnRead = null;
        second.OnRead = null;
        var retried = BlackSilenceDocumentBatch.GenerateSafely(request, CancellationToken.None);
        var current = Assert.IsType<BlackSilenceProjectSnapshot>(project.State.TryGetSnapshot(project.Options));

        Assert.NotSame(initial, current);
        Assert.Equal(2, retried.Diagnostics.Length);
        Assert.All(retried.Diagnostics, diagnostic =>
            Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_GlobalUsingsFileContainsNonUsing, diagnostic.Id));
        Assert.Single(retried.Components);
        Assert.NotSame(initial.Entries["component:Page.akbura"], current.Entries["component:Page.akbura"]);
        Assert.Equal(2, current.DiagnosticEntries.Values.Sum(static entry => entry.Diagnostics.Length));
    }

    [Fact]
    public void CancellationDuringDiagnosticCollection_PropagatesWithoutPublishingPartialSnapshot()
    {
        var project = new SnapshotProject();
        var initial = project.Warm();
        var first = new ControlledSourceText("using System;\r\n<object />\r\n");
        var second = new ControlledSourceText("using System;\r\n<string />\r\n");
        var request = project.CreateRequest(first, second, width: 20);
        using var cancellation = new CancellationTokenSource();
        var firstReads = 0;
        first.OnRead = () => firstReads++;
        second.OnRead = cancellation.Cancel;

        Assert.ThrowsAny<OperationCanceledException>(() =>
            BlackSilenceDocumentBatch.GenerateSafely(request, cancellation.Token));

        Assert.True(firstReads > 0);
        Assert.Same(initial, project.State.TryGetSnapshot(project.Options));
        Assert.Empty(initial.DiagnosticEntries[project.FirstGlobalPath].Diagnostics);

        first.OnRead = null;
        second.OnRead = null;
        var retried = BlackSilenceDocumentBatch.Generate(request, CancellationToken.None);
        Assert.Equal(2, retried.Diagnostics.Length);
        Assert.Single(retried.Components);
        Assert.NotSame(initial, project.State.TryGetSnapshot(project.Options));
    }

    [Fact]
    public void OlderCompletedBranch_ReturnsOwnDiagnosticsButCannotOverwriteNewerSnapshot()
    {
        var project = new SnapshotProject();
        _ = project.Warm();
        var older = project.CreateRequest(SourceText.From("using System;\r\n<object />\r\n"), null, width: 20);
        var newer = project.CreateRequest(SourceText.From("using System;\r\n"), null, width: 30);
        Assert.True(newer.Version > older.Version);

        var newBatch = BlackSilenceDocumentBatch.Generate(newer, CancellationToken.None);
        var published = Assert.IsType<BlackSilenceProjectSnapshot>(project.State.TryGetSnapshot(project.Options));
        var oldBatch = BlackSilenceDocumentBatch.Generate(older, CancellationToken.None);

        Assert.Empty(newBatch.Diagnostics);
        Assert.Single(oldBatch.Diagnostics);
        Assert.Same(published, project.State.TryGetSnapshot(project.Options));
        Assert.Equal(newer.Version, published.Version);
        Assert.Empty(published.DiagnosticEntries[project.FirstGlobalPath].Diagnostics);
        Assert.NotEqual(Assert.Single(newBatch.Components).SourceText.ToString(),
            Assert.Single(oldBatch.Components).SourceText.ToString());
        Assert.Equal(Assert.Single(newBatch.Components).SourceText.ToString(),
            published.Entries["component:Page.akbura"].Source.SourceText.ToString());
    }

    private sealed class SnapshotProject
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "BlackSilenceDiagnosticSnapshotTests");

        public SnapshotProject()
        {
            Options = new GeneratorProjectOptions("Demo", _directory);
            State = new BlackSilenceProjectState(CSharpCompilation.Create(
                "BlackSilenceDiagnosticSnapshotTests",
                references: SymbolTests.CreateAvaloniaReferences(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
        }

        public GeneratorProjectOptions Options { get; }
        public BlackSilenceProjectState State { get; }
        public string FirstGlobalPath => Path.Combine(_directory, "A", "GlobalUsings.akbura");

        public BlackSilenceProjectSnapshot Warm()
        {
            var request = CreateRequest(SourceText.From("using System;\r\n"), null, width: 10);
            var batch = BlackSilenceDocumentBatch.Generate(request, CancellationToken.None);
            Assert.Empty(batch.Diagnostics);
            Assert.Single(batch.Components);
            return Assert.IsType<BlackSilenceProjectSnapshot>(State.TryGetSnapshot(Options));
        }

        public BlackSilenceGenerationRequest CreateRequest(SourceText first, SourceText? second, int width)
        {
            var documents = ImmutableArray.CreateBuilder<DocumentSyntaxVersion>();
            documents.Add(DocumentSyntaxVersion.Create(ComponentSyntaxTree.ParseText(first, FirstGlobalPath)));
            if (second != null)
            {
                documents.Add(DocumentSyntaxVersion.Create(ComponentSyntaxTree.ParseText(second,
                    Path.Combine(_directory, "B", "GlobalUsings.akbura"))));
            }

            var component = ComponentSyntaxTree.ParseText(SourceText.From(
                $"using Avalonia.Controls;\r\n<Border Width=\"{width}\" />\r\n"), Path.Combine(_directory, "Page.akbura"));
            documents.Add(DocumentSyntaxVersion.Create(component));
            return Assert.IsType<BlackSilenceGenerationRequest>(BlackSilenceGenerationRequestBuilder.Create(
                documents.ToImmutable(), State, Options, CancellationToken.None));
        }
    }

    private sealed class ControlledSourceText(string text) : SourceText
    {
        private readonly SourceText _text = SourceText.From(text);

        public Action? OnRead { get; set; }
        public override Encoding? Encoding => _text.Encoding;
        public override int Length
        {
            get
            {
                OnRead?.Invoke();
                return _text.Length;
            }
        }

        public override char this[int position] => _text[position];

        public override void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count) =>
            _text.CopyTo(sourceIndex, destination, destinationIndex, count);
    }
}
