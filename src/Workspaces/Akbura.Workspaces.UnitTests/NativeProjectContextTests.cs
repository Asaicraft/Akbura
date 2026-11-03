using Akbura.VisualStudio.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Threading.Channels;

namespace Akbura.Workspaces.UnitTests;

public sealed class NativeProjectContextTests
{
    private const string Source = "param MissingDependency Value;\nparam MissingForever Broken;";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialContextRecoversAutomaticallyWithoutChangingText(bool retainSourceError)
    {
        using var fixture = new Fixture();
        var text = SourceText.From(retainSourceError ? Source : "param MissingDependency Value;");
        using var buffer = fixture.Open("View.akbura", text);
        var initial = await buffer.NextPublication();
        Assert.Contains(initial.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingDependency"));
        Assert.True(fixture.Workspace.CurrentSolution.TryGetProject(initial.Context.Project.Id, out var partial));
        Assert.NotEmpty(partial.CSharpCompilation.References); // Successful initialization, not the zero-reference retry.
        var identity = initial.Context.Document.Id;
        var initialRequest = initial.Request;

        fixture.AddType("MissingDependency");

        // The Roslyn event must drive publication. No edit, reopen, explicit
        // refresh or direct diagnostic request is made after changing context.
        var recovered = await buffer.NextPublication();
        Assert.Same(text, buffer.Text);
        Assert.Equal(0, buffer.TextVersion);
        Assert.True(recovered.Request > initialRequest);
        Assert.Equal(identity, recovered.Context.Document.Id);
        Assert.DoesNotContain(recovered.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingDependency"));
        if (retainSourceError)
        {
            Assert.Contains(recovered.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingForever"));
        }
        else
        {
            Assert.Empty(recovered.Diagnostics);
        }
        Assert.Equal(2, fixture.LoadCount);
    }

    [Fact]
    public async Task NewContextDuringSynchronizationCatchesUpAndSharesWork()
    {
        var entered = Barrier();
        var release = Barrier();
        using var fixture = new Fixture(async (manager, project, token, count) =>
        {
            var loaded = await manager.LoadProjectAsync(project, token);
            if (count == 1)
            {
                entered.SetResult();
                await release.Task;
            }
            return loaded;
        });
        using var first = fixture.Open("First.akbura", SourceText.From(Source));
        await entered.Task.WaitAsync(Timeout);
        using var second = fixture.Open("Second.akbura", SourceText.From(Source));
        var invalidated = first.NextInvalidation();

        fixture.AddType("MissingDependency");
        await invalidated;
        release.SetResult();

        foreach (var buffer in new[] { first, second })
        {
            var publication = await buffer.NextPublication();
            Assert.DoesNotContain(publication.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingDependency"));
            Assert.Contains(publication.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingForever"));
        }
        Assert.Equal(2, fixture.LoadCount);
    }

    [Fact]
    public async Task OlderAnalysisCompletingLastCannotReplaceCurrentPublication()
    {
        using var fixture = new Fixture();
        using var buffer = fixture.Open("View.akbura", SourceText.From(Source));
        var older = await buffer.NextPublication();
        var entered = Barrier();
        var release = Barrier();
        var oldAnalysis = Task.Run(async () =>
        {
            entered.SetResult();
            await release.Task;
            return buffer.Context.TryPublishSemantic(older.Request, () => buffer.Record(older));
        });
        await entered.Task.WaitAsync(Timeout);
        fixture.AddType("MissingDependency");
        var current = await buffer.NextPublication();
        release.SetResult();

        Assert.False(await oldAnalysis.WaitAsync(Timeout));
        Assert.Same(current, buffer.Latest);
        Assert.DoesNotContain(buffer.Latest!.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingDependency"));
    }

    [Fact]
    public async Task MultipleBuffersPreserveEditsMadeDuringLoadingAndOpenResources()
    {
        var entered = Barrier();
        var release = Barrier();
        using var fixture = new Fixture(async (manager, project, token, count) =>
        {
            var loaded = await manager.LoadProjectAsync(project, token);
            if (count == 2)
            {
                entered.SetResult();
                await release.Task;
            }
            return loaded;
        });
        using var first = fixture.Open("First.akbura", SourceText.From(Source));
        using var second = fixture.Open("Second.akbura", SourceText.From(Source));
        using var styles = fixture.Open("Styles.akcss", SourceText.From("@utilities { }"));
        using var resource = fixture.Manager.RegisterOpenText(fixture.PathFor("Theme.axaml"),
            SourceText.From("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"/>"));
        await first.NextPublication();
        var previous = await second.NextPublication();
        await styles.NextPublication();
        var originalIdentity = previous.Context.Document.Id;
        var unsaved = SourceText.From("param MissingDependency Unsaved;");
        second.Edit(unsaved);
        await second.NextPublication();

        fixture.AddType("MissingDependency");
        await entered.Task.WaitAsync(Timeout);
        var latestText = SourceText.From("param MissingDependency LatestUnsaved;");
        second.Edit(latestText);
        var latestStyles = SourceText.From("// unsaved styles\n@utilities { }");
        styles.Edit(latestStyles);
        var latestResource = SourceText.From("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"><!-- unsaved --></ResourceDictionary>");
        resource.Update(latestResource);
        release.SetResult();

        var firstResult = await first.NextPublication();
        var secondResult = await second.NextPublication();
        var stylesResult = await styles.NextPublication();
        Assert.DoesNotContain(firstResult.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingDependency"));
        Assert.Empty(secondResult.Diagnostics);
        Assert.Equal(originalIdentity, secondResult.Context.Document.Id);
        Assert.Same(latestText, secondResult.Context.Document.Text);
        Assert.Equal(2, fixture.LoadCount);
        var project = fixture.Workspace.CurrentSolution.GetRequiredProject(firstResult.Context.Project.Id);
        Assert.True(project.TryGetDocument(new Uri(second.Context.FilePath), out var other));
        Assert.Same(latestText, other.Text);
        Assert.Same(latestStyles, stylesResult.Context.Document.Text);
        Assert.Same(latestResource, Assert.Single(project.ResourceDocuments.Values).Text);
    }

    [Fact]
    public async Task UnrelatedProjectDoesNotRefreshButTransitiveDependencyDoes()
    {
        using var fixture = new Fixture(withDependency: true);
        using var buffer = fixture.Open("View.akbura", SourceText.From("param MiddleType Value; param MissingForever Broken;"));
        var initial = await buffer.NextPublication();
        Assert.Empty(GetTransitiveCompilation(initial).GetTypeByMetadataName("BaseType")!.GetMembers("Recovered"));
        var originalLoadCount = fixture.LoadCount;
        var unrelated = fixture.Roslyn.CurrentSolution.Projects.Single(project => project.Name == "Unrelated");
        await fixture.ChangeAndDrainEvents(unrelated.AddDocument("Other.cs", "public class Other {} ").Project.Solution);
        Assert.Equal(originalLoadCount, fixture.LoadCount);
        Assert.Same(initial, buffer.Latest);
        Assert.Equal(0, buffer.InvalidationCount);

        var dependency = fixture.Roslyn.CurrentSolution.Projects.Single(project => project.Name == "Dependency");
        var sourceDocument = dependency.Documents.Single();
        fixture.Apply(dependency.Solution.WithDocumentText(sourceDocument.Id,
            SourceText.From("public class BaseType { public int Recovered { get; } }")));
        var recovered = await buffer.NextPublication();
        Assert.Single(GetTransitiveCompilation(recovered).GetTypeByMetadataName("BaseType")!.GetMembers("Recovered"));
        Assert.Contains(recovered.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingForever"));
        Assert.Equal(1, buffer.InvalidationCount);
    }

    private static Compilation GetTransitiveCompilation(Publication publication) =>
        publication.Context.Project.CSharpCompilation.References.OfType<CompilationReference>()
            .Single(reference => reference.Compilation.AssemblyName == "Middle").Compilation
            .References.OfType<CompilationReference>()
            .Single(reference => reference.Compilation.AssemblyName == "Dependency").Compilation;

    [Fact]
    public async Task OrdinaryTypingReusesCurrentProjectSynchronization()
    {
        using var fixture = new Fixture();
        using var buffer = fixture.Open("View.akbura", SourceText.From("param string Before;"));
        await buffer.NextPublication();
        buffer.Edit(SourceText.From("param string After;"));
        var edited = await buffer.NextPublication();
        Assert.Empty(edited.Diagnostics);
        Assert.Equal(1, fixture.LoadCount);
        Assert.Equal(0, buffer.InvalidationCount);
    }

    [Theory]
    [InlineData("MetadataReference")]
    [InlineData("CompilationOptions")]
    [InlineData("DocumentMembership")]
    public async Task SemanticProjectInputsRefreshUnchangedBuffers(string input)
    {
        using var fixture = new Fixture();
        using var buffer = fixture.Open("View.akbura", SourceText.From(Source));
        var before = await buffer.NextPublication();
        var project = fixture.Roslyn.CurrentSolution.GetProject(fixture.ProjectId)!;
        var solution = input switch
        {
            "MetadataReference" => project.AddMetadataReference(
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)).Solution,
            "CompilationOptions" => project.WithCompilationOptions(
                ((CSharpCompilationOptions)project.CompilationOptions!).WithAllowUnsafe(true)).Solution,
            _ => project.AddAdditionalDocument("Extra.akcss", SourceText.From("@utilities { }"),
                filePath: fixture.PathFor("Extra.akcss")).Project.Solution,
        };
        fixture.Apply(solution);
        var after = await buffer.NextPublication();
        Assert.Equal(0, buffer.TextVersion);
        Assert.NotSame(before.Context.Project.Context, after.Context.Project.Context);
        if (input == "DocumentMembership")
        {
            Assert.True(after.Context.Project.TryGetDocument(new Uri(fixture.PathFor("Extra.akcss")), out _));
        }
        Assert.Contains(after.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingForever"));
        Assert.Equal(2, fixture.LoadCount);
    }

    [Fact]
    public async Task AddedProjectReferenceRecoversMissingDependencyAutomatically()
    {
        using var fixture = new Fixture();
        using var buffer = fixture.Open("View.akbura", SourceText.From(Source));
        await buffer.NextPublication();
        var libraryId = ProjectId.CreateNewId();
        var solution = fixture.Roslyn.CurrentSolution.AddProject(ProjectInfo.Create(libraryId, VersionStamp.Create(),
            "Library", "Library", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));
        solution = solution.AddDocument(DocumentId.CreateNewId(libraryId), "Library.cs",
            SourceText.From("public class MissingDependency {}"))
            .AddProjectReference(fixture.ProjectId, new ProjectReference(libraryId));
        fixture.Apply(solution);
        var after = await buffer.NextPublication();
        Assert.DoesNotContain(after.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingDependency"));
        Assert.Contains(after.Diagnostics, diagnostic => diagnostic.Message.Contains("MissingForever"));
        Assert.Equal(0, buffer.TextVersion);
    }

    [Fact]
    public async Task SolutionClearInvalidatesOldIdentityAndRebindsOnAvailability()
    {
        using var fixture = new Fixture();
        using var buffer = fixture.Open("View.akbura", SourceText.From(Source));
        var before = await buffer.NextPublication();
        var invalidated = buffer.NextInvalidation();
        fixture.Roslyn.ClearSolution();
        await invalidated;
        await buffer.LastWork.WaitAsync(Timeout);
        Assert.False(buffer.Context.TryPublishSemantic(before.Request, () => buffer.Record(before)));
        fixture.ReplaceProject();
        var after = await buffer.NextPublication();
        Assert.NotEqual(before.Context.Project.Id, after.Context.Project.Id);
    }

    [Fact]
    public async Task DisposingOneWaiterDoesNotCancelSharedWorkOrPublishToDisposedBuffer()
    {
        var entered = Barrier();
        var release = Barrier();
        using var fixture = new Fixture(async (manager, project, token, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return await manager.LoadProjectAsync(project, token);
        });
        using var first = fixture.Open("First.akbura", SourceText.From(Source));
        await entered.Task.WaitAsync(Timeout);
        using var second = fixture.Open("Second.akbura", SourceText.From(Source));
        first.Dispose();
        release.SetResult();
        await second.NextPublication();
        await first.LastWork.WaitAsync(Timeout);
        Assert.Null(first.Latest);
        Assert.Equal(1, fixture.LoadCount);
    }

    [Fact]
    public async Task ResourceClosedDuringLoadingDoesNotRestoreDiscardedUnsavedText()
    {
        var entered = Barrier();
        var release = Barrier();
        using var fixture = new Fixture(async (manager, project, token, _) =>
        {
            var loaded = await manager.LoadProjectAsync(project, token);
            entered.SetResult();
            await release.Task;
            return loaded;
        });
        using var resource = fixture.Manager.RegisterOpenText(fixture.PathFor("Theme.axaml"),
            SourceText.From("<ResourceDictionary><!-- discarded unsaved text --></ResourceDictionary>"));
        using var buffer = fixture.Open("View.akbura", SourceText.From(Source));
        await entered.Task.WaitAsync(Timeout);
        resource.Dispose();
        release.SetResult();
        var publication = await buffer.NextPublication();
        var project = fixture.Workspace.CurrentSolution.GetRequiredProject(publication.Context.Project.Id);
        Assert.Equal("<ResourceDictionary/>", Assert.Single(project.ResourceDocuments.Values).Text.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCanceledSynchronizationIsRetryable(bool canceled)
    {
        using var fixture = new Fixture((manager, project, token, count) => count == 1
            ? canceled ? Task.FromCanceled<AkburaLoadedProject?>(new CancellationToken(true))
                : Task.FromException<AkburaLoadedProject?>(new IOException("Controlled failure"))
            : manager.LoadProjectAsync(project, token));
        using var buffer = fixture.Manager.OpenBuffer(fixture.PathFor("View.akbura"), SourceText.From(Source));
        await Assert.ThrowsAnyAsync<Exception>(() => buffer.SynchronizeAsync(CancellationToken.None));
        Assert.NotNull(await buffer.SynchronizeAsync(CancellationToken.None).WaitAsync(Timeout));
        Assert.Equal(2, fixture.LoadCount);
    }

    [Fact]
    public async Task ProjectRemovalAndRediscoveryDoNotKeepObsoleteIdentity()
    {
        using var fixture = new Fixture();
        using var buffer = fixture.Open("View.akbura", SourceText.From(Source));
        var original = await buffer.NextPublication();
        var invalidated = buffer.NextInvalidation();
        fixture.Apply(fixture.Roslyn.CurrentSolution.RemoveProject(fixture.ProjectId));
        await invalidated;
        await buffer.LastWork.WaitAsync(Timeout);
        Assert.Null(buffer.Context.ProjectId);
        Assert.False(buffer.Context.TryPublish(original.Request, () => throw new InvalidOperationException()));

        fixture.ReplaceProject();
        var reloaded = await buffer.NextPublication();
        Assert.NotEqual(original.Context.Project.Id, reloaded.Context.Project.Id);
        Assert.NotNull(buffer.Context.ProjectId);
    }

    [Fact]
    public async Task BufferOpenedBeforeProjectDiscoveryRefreshesOnProjectAvailability()
    {
        using var fixture = new Fixture();
        await fixture.ChangeAndDrainEvents(fixture.Roslyn.CurrentSolution.RemoveProject(fixture.ProjectId));
        using var buffer = fixture.Open("View.akbura", SourceText.From(Source));
        await buffer.LastWork.WaitAsync(Timeout);
        Assert.Null(buffer.Latest);
        fixture.ReplaceProject();
        await buffer.NextPublication();
        Assert.Equal(1, fixture.LoadCount);
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static TaskCompletionSource Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Fixture : IDisposable
    {
        internal AdhocWorkspace Roslyn { get; } = new();
        internal AkburaWorkspace Workspace { get; } = new();
        internal AkburaProjectContextManager Manager { get; }
        internal ProjectId ProjectId { get; private set; } = ProjectId.CreateNewId();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), nameof(NativeProjectContextTests), Guid.NewGuid().ToString("N"));
        internal int LoadCount;

        internal Fixture(Func<AkburaProjectContextManager, Project, CancellationToken, int, Task<AkburaLoadedProject?>>? load = null,
            bool withDependency = false)
        {
            var solution = AddProject(Roslyn.CurrentSolution, ProjectId, "App");
            if (withDependency)
            {
                var middle = ProjectId.CreateNewId();
                var dependency = ProjectId.CreateNewId();
                solution = AddProject(AddProject(AddProject(solution, middle, "Middle"), dependency, "Dependency"),
                    ProjectId.CreateNewId(), "Unrelated");
                solution = solution.AddProjectReference(ProjectId, new ProjectReference(middle))
                    .AddProjectReference(middle, new ProjectReference(dependency));
                solution = solution.AddDocument(DocumentId.CreateNewId(middle), "Middle.cs", SourceText.From("public class MiddleType : BaseType {}"))
                    .AddDocument(DocumentId.CreateNewId(dependency), "Dependency.cs", SourceText.From("public class BaseType {}"));
            }
            Apply(solution);
            Manager = new AkburaProjectContextManager(Roslyn, Workspace,
                path => Roslyn.CurrentSolution.Projects.FirstOrDefault(project => project.Name == "App" &&
                    project.AdditionalDocuments.Any(document => document.FilePath == path)),
                (project, token) =>
                {
                    var count = Interlocked.Increment(ref LoadCount);
                    return load == null ? Manager!.LoadProjectAsync(project, token) : load(Manager!, project, token, count);
                });
        }

        private Solution AddProject(Solution solution, ProjectId id, string name)
        {
            solution = solution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), name, name, LanguageNames.CSharp,
                filePath: PathFor(name + ".csproj"), compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));
            if (name == "App")
            {
                foreach (var fileName in new[] { "View.akbura", "First.akbura", "Second.akbura", "Styles.akcss", "Theme.axaml" })
                {
                    solution = solution.AddAdditionalDocument(DocumentId.CreateNewId(id), fileName,
                        SourceText.From(fileName.EndsWith("axaml") ? "<ResourceDictionary/>" : string.Empty), filePath: PathFor(fileName));
                }
            }
            return solution;
        }

        internal string PathFor(string fileName) => Path.Combine(_directory, fileName);
        internal TestBuffer Open(string fileName, SourceText text) => new(this, Manager.OpenBuffer(PathFor(fileName), text), text);
        internal void Apply(Solution solution) => Assert.True(Roslyn.TryApplyChanges(solution));
        internal void AddType(string name) => Apply(Roslyn.CurrentSolution.AddDocument(DocumentId.CreateNewId(ProjectId),
            name + ".cs", SourceText.From("public class " + name + " {} ")).GetProject(ProjectId)!.Solution);
        internal void ReplaceProject()
        {
            ProjectId = ProjectId.CreateNewId();
            Apply(AddProject(Roslyn.CurrentSolution, ProjectId, "App"));
        }

        // Roslyn notifications are queued. A handler appended after the manager
        // provides a deterministic barrier, including for a negative assertion.
        internal async Task ChangeAndDrainEvents(Solution solution)
        {
            var delivered = Barrier();
            void Changed(WorkspaceChangeEventArgs args)
            {
                if (ReferenceEquals(args.NewSolution, Roslyn.CurrentSolution))
                {
                    delivered.TrySetResult();
                }
            }
            using var registration = Roslyn.RegisterWorkspaceChangedHandler(Changed);
            Apply(solution);
            await delivered.Task.WaitAsync(Timeout);
        }

        public void Dispose()
        {
            Manager.Dispose();
            Workspace.Dispose();
            Roslyn.Dispose();
        }
    }

    private sealed record Publication(long Request, AkburaDocumentContext Context, ImmutableArray<AkburaDiagnosticSpan> Diagnostics);

    // A small editor adapter: the production component owns invalidation,
    // shared synchronization and publication acceptance. Only text snapshots
    // and the VS/UI notification consumer are substituted here.
    private sealed class TestBuffer : IDisposable
    {
        private readonly Fixture _fixture;
        private readonly Channel<Publication> _publications = Channel.CreateUnbounded<Publication>();
        private readonly Channel<bool> _invalidations = Channel.CreateUnbounded<bool>();
        private readonly CancellationTokenSource _disposal = new();
        internal AkburaEditorProjectContext Context { get; }
        internal SourceText Text { get; private set; }
        internal int TextVersion { get; private set; }
        internal int InvalidationCount;
        internal Task LastWork { get; private set; } = Task.CompletedTask;
        internal Publication? Latest;

        internal TestBuffer(Fixture fixture, AkburaEditorProjectContext context, SourceText text)
        {
            _fixture = fixture;
            Context = context;
            Text = text;
            Context.Changed += OnChanged;
            Refresh();
        }

        private void OnChanged()
        {
            Interlocked.Increment(ref InvalidationCount);
            Refresh();
            _invalidations.Writer.TryWrite(true);
        }

        internal Task NextInvalidation() => _invalidations.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        internal Task<Publication> NextPublication() => _publications.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        internal void Edit(SourceText text)
        {
            Text = text;
            TextVersion++;
            Refresh();
        }

        private void Refresh()
        {
            var text = Text;
            var request = Context.Request(text);
            LastWork = AnalyzeAsync(request, text);
        }

        private async Task AnalyzeAsync(long request, SourceText text)
        {
            try
            {
                var projectId = await Context.SynchronizeAsync(_disposal.Token);
                if (projectId == null)
                {
                    return;
                }
                if (!Context.IsCurrent(request))
                {
                    return;
                }
                var context = await Context.OpenDocumentAsync(request, new Uri(Context.FilePath), text, _disposal.Token);
                if (context == null)
                {
                    return;
                }
                var diagnostics = _fixture.Workspace.LanguageServices.Diagnostics.GetDiagnostics(context, new TextSpan(0, text.Length));
                Context.TryPublishSemantic(request, () => Record(new Publication(request, context, diagnostics)));
            }
            catch (OperationCanceledException) when (_disposal.IsCancellationRequested)
            {
            }
        }

        internal void Record(Publication publication)
        {
            Latest = publication;
            _publications.Writer.TryWrite(publication);
        }

        public void Dispose()
        {
            Context.Changed -= OnChanged;
            Context.Dispose();
            _disposal.Cancel();
        }
    }
}
