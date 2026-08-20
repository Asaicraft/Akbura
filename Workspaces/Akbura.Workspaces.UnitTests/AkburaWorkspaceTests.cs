using Microsoft.CodeAnalysis.Text;
using System.Reflection;

namespace Akbura.Workspaces.UnitTests;

public sealed class AkburaWorkspaceTests
{
    [Fact]
    public async Task SnapshotReads_DoNotWaitForMutationGate()
    {
        using var workspace =
            new AkburaWorkspace();
        var uri = new Uri(
            Path.Combine(
                Path.GetTempPath(),
                nameof(AkburaWorkspaceTests),
                "Component.akbura"));
        var openedDocument =
            workspace.OpenDocument(
                workspace.DefaultProjectId,
                uri,
                SourceText.From("<Component/>"));
        var mutationGateField =
            typeof(AkburaWorkspace).GetField(
                "_mutationGate",
                BindingFlags.Instance |
                BindingFlags.NonPublic);
        var mutationGate = Assert.IsType<object>(
            mutationGateField?.GetValue(
                workspace));
        var gateEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseGate =
            new ManualResetEventSlim();
        var gateTask = Task.Factory.StartNew(
            () =>
            {
                lock (mutationGate)
                {
                    gateEntered.SetResult();
                    releaseGate.Wait();
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        try
        {
            await gateEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5));

            var result = await Task.Run(
                () =>
                {
                    var solution =
                        workspace.CurrentSolution;
                    var foundById =
                        workspace.TryGetDocument(
                            openedDocument.Id,
                            out var documentById);
                    var foundByUri =
                        workspace.TryGetDocument(
                            uri,
                            out var documentByUri);

                    return (
                        Solution: solution,
                        FoundById: foundById,
                        DocumentById: documentById,
                        FoundByUri: foundByUri,
                        DocumentByUri: documentByUri);
                }).WaitAsync(
                    TimeSpan.FromSeconds(5));

            Assert.Same(
                workspace.CurrentSolution,
                result.Solution);
            Assert.True(result.FoundById);
            Assert.Same(openedDocument, result.DocumentById);
            Assert.True(result.FoundByUri);
            Assert.Same(openedDocument, result.DocumentByUri);
        }
        finally
        {
            releaseGate.Set();
            await gateTask.WaitAsync(
                TimeSpan.FromSeconds(5));
        }
    }
}
