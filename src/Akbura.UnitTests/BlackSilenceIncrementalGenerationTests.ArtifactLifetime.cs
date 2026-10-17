using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    [Fact]
    public void DetachedArtifacts_RoslynRequestDoesNotStronglyRetainPreviousSnapshot()
    {
        var (request, weak) = CaptureCurrentRequestAndObsoleteContext();
        AssertCollected(weak);
        Assert.Null(request.PreviousSnapshot);
        Assert.NotNull(request.State.TryGetSnapshot(request.Options));
        GC.KeepAlive(request);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (BlackSilenceGenerationRequest Request, List<(string Kind, WeakReference Reference)> Weak) CaptureCurrentRequestAndObsoleteContext()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border Width=\"10\" />"));
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), project.Compilation);
        var model = initial.Request.Index.Compilation.GetSemanticModel(initial.Request.Documents[0].SyntaxTree);
        var weak = new List<(string Kind, WeakReference Reference)>
        {
            ("PreviousSnapshot", new WeakReference(initial.Snapshot)),
            ("PreviousIndex", new WeakReference(initial.Request.Index)),
            ("PreviousAkburaCompilation", new WeakReference(initial.Request.Index.Compilation)),
            ("PreviousSemanticModel", new WeakReference(model)),
            ("PreviousProbeContext", new WeakReference(model.BindingSession.ProbeContext)),
        };
        var changed = file.Replace("10", "20");
        var current = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(file, changed), project.Compilation);
        return (current.Request, weak);
    }

    [Fact]
    public void DetachedArtifacts_RemovedDocumentsAreAbsentAndOldArtifactGenerationIsCollectible()
    {
        var (owner, old, removed) = RemoveArtifactDocument();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.False(old.IsAlive);
        Assert.False(removed.IsAlive);
        Assert.Single(owner.Latest!.Entries);
        Assert.Single(owner.Latest.Documents);
        GC.KeepAlive(owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (BoundedArtifactStore Owner, WeakReference OldArtifacts, WeakReference RemovedText) RemoveArtifactDocument()
    {
        var project = new TestProject(publishDiagnostics: true);
        var first = project.File("Page.akbura", Component("<Border />"));
        var removed = project.File("Removed.akbura", Component("<TextBlock />"));
        var initial = ArtifactRun(project, CreateDriver(project.Options, first, removed), project.Compilation);
        var artifacts = initial.Request.State.Artifacts.Latest!;
        var text = artifacts.Entries.Single(pair => pair.Key.EndsWith("Removed.akbura")).Value.Source;
        var current = ArtifactRun(project, initial.Driver.RemoveAdditionalTexts([removed]), project.Compilation);
        AssertArtifactsEqualToFresh(project, current, project.Compilation, first);
        return (current.Request.State.Artifacts, new WeakReference(artifacts), new WeakReference(text));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void DetachedArtifacts_OnlyNewestStoreAliveDoesNotRetainAnyCompilerGeneration(int revisions)
    {
        var result = RunArtifactLifetime(revisions);
        AssertCollected(result.WeakObjects);
        Assert.Single(result.Owner.Latest!.Entries);
        Assert.Single(result.Owner.Latest.Documents);
        AssertDetachedGraph(result.Owner.Latest);
        GC.KeepAlive(result.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactLifetimeResult RunArtifactLifetime(int revisions)
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("state int value = 1; <Border Width={value + 1} />"));
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, project.Options.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var current = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var weak = new List<(string Kind, WeakReference Reference)>();
        CaptureArtifactWeakObjects(current, weak);
        for (var index = 1; index <= revisions; index++)
        {
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(
                ArtifactLeaf.Replace("=> 1", "=> " + (index + 1)), project.Options.ParseOptions, tree.FilePath));
            var driver = current.Driver;
            if (index % 13 == 0)
            {
                var replacement = project.File("Page.akbura", Component("state int value = " + (index + 1) + "; <Border Width={value + 1} />"));
                driver = driver.ReplaceAdditionalText(file, replacement);
                file = replacement;
            }
            current = ArtifactRun(project, driver, compilation);
            AssertArtifactsEqualToFresh(project, current, compilation, file);
            CaptureArtifactWeakObjects(current, weak);
            Assert.Single(current.Request.State.Artifacts.Latest!.Entries);
        }
        return new ArtifactLifetimeResult(current.Request.State.Artifacts, weak);
    }

    private static void CaptureArtifactWeakObjects(TestRun run, List<(string Kind, WeakReference Reference)> weak)
    {
        weak.Add(("CSharpCompilation", new WeakReference(run.Request.State.CSharpCompilation)));
        weak.Add(("AkburaCompilation", new WeakReference(run.Request.Index.Compilation)));
        weak.Add(("ProjectIndex", new WeakReference(run.Request.Index)));
        weak.Add(("ProjectState", new WeakReference(run.Request.State)));
        weak.Add(("ProjectSnapshot", new WeakReference(run.Snapshot)));
        weak.Add(("DetachedSnapshot", new WeakReference(run.Request.State.Artifacts.Latest)));
        var tree = run.Request.Documents[0].SyntaxTree;
        if (run.Request.Index.Compilation.HasCreatedSemanticModel(tree))
        {
            var model = run.Request.Index.Compilation.GetSemanticModel(tree);
            var probes = run.Request.Index.Compilation.CSharpProbeCompilation;
            weak.Add(("AkburaSemanticModel", new WeakReference(model)));
            weak.Add(("ProbeContext", new WeakReference(model.BindingSession.ProbeContext)));
            weak.Add(("ProbeCompilation", new WeakReference(probes)));
            weak.Add(("RoslynSemanticModel", new WeakReference(probes.GetSemanticModel(probes.SyntaxTrees.Last()))));
        }
    }

    private static void AssertCollected(IEnumerable<(string Kind, WeakReference Reference)> objects)
    {
        // The caller intentionally retains the newest cache owner, not a driver.
        var expected = objects.Where(static item => item.Kind != "DetachedSnapshot").ToArray();
        for (var attempt = 0; attempt < 6 && expected.Any(static item => item.Reference.IsAlive); attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            Thread.Yield();
        }
        var alive = expected.Where(static item => item.Reference.IsAlive).GroupBy(static item => item.Kind)
            .Select(static group => group.Key + ": " + group.Count()).ToArray();
        Assert.True(alive.Length == 0, "Unexpected roots with only the detached store retained: " + string.Join(", ", alive));
        var snapshots = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var item in objects.Where(static item => item.Kind == "DetachedSnapshot"))
        {
            if (item.Reference.Target is { } snapshot)
            {
                snapshots.Add(snapshot);
            }
        }
        Assert.InRange(snapshots.Count, 0, 1);
    }

    private static void AssertDetachedGraph(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var value = pending.Pop();
            if (!visited.Add(value))
            {
                continue;
            }
            var type = value.GetType();
            Assert.False(value is Compilation or SemanticModel or SyntaxTree or SyntaxNode or Microsoft.CodeAnalysis.ISymbol or
                IOperation or AkburaCompilation or AkburaSemanticModel or AkburaProjectIndex or SemanticModelState or
                AkburaSyntaxTree or ComponentPlan or DocumentSyntaxVersion or DocumentGenerationVersion or DocumentDiagnosticVersion or BlackSilenceProjectSnapshot or Delegate,
                "Compilation-bound object in detached graph: " + type.FullName);
            if (value is SourceText)
            {
                Assert.Equal(SourceText.From(string.Empty).GetType(), type);
                continue;
            }
            if (type.IsPrimitive || type.IsEnum || value is string or decimal)
            {
                continue;
            }
            if (value is IEnumerable sequence)
            {
                foreach (var item in sequence)
                {
                    if (item != null)
                    {
                        pending.Push(item);
                    }
                }
                continue;
            }
            // Inspect our records and scalar structs, not private Roslyn internals.
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.GetValue(value) is { } child)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private sealed record ArtifactLifetimeResult(BoundedArtifactStore Owner, List<(string Kind, WeakReference Reference)> WeakObjects);
}
