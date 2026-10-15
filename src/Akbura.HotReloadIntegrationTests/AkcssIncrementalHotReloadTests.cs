using Akbura.Akcss;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Reflection;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Akbura.HotReloadIntegrationTests;

public sealed class AkcssIncrementalHotReloadTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task OwnedModuleChanges_SwitchScalarResourceAndConditionalPublicationSafely() =>
        Scenario(nameof(OwnedModuleChanges_SwitchScalarResourceAndConditionalPublicationSafely),
            "${Owned}:w-12 h-24", (source, reload, owner, target, window) =>
        {
            target.Resources["size"] = 2d;
            var resource = Styles.Replace("Border.w-(double value) { Width: value; }",
                "Border.w-(double value) { Width: Amx.DynamicResource<double>(\"size\") * value; }", StringComparison.Ordinal);
            reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", resource));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Equal(24, target.Width);
            target.Resources["size"] = 3d;
            Assert.Equal(36, target.Width);
            var conditional = Styles.Replace("Border.w-(double value) { Width: value; }",
                "Border.w-(double value) { @if(IsEnabled) { Width: value; } }", StringComparison.Ordinal);
            reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", conditional));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Equal(12, target.Width);
            target.Resources["size"] = 5d;
            Assert.Equal(12, target.Width);
            target.IsEnabled = false;
            AkburaControl.RefreshAkcssStylesForHotReload(target);
            Assert.True(double.IsNaN(target.Width));
            reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", Styles));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Equal(12, target.Width);
            Assert.Equal(24, target.Height);
            Assert.Same(target, owner.Child);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task OwnedReactiveScalar_RemainsSynchronousAndBatchesAfterRealDelta() =>
        Scenario(nameof(OwnedReactiveScalar_RemainsSynchronousAndBatchesAfterRealDelta),
            "${Owned}:w-${Stream} h-24", (source, reload, owner, target, window) =>
        {
            target.Width = 73;
            var original = AkburaControl.GetAkcssStyles(target)[0];
            var subscriptions = Count(reload, "Subscriptions");
            Update(source, reload, "${Owned}:w-${Stream} h-25");
            Assert.Same(original, AkburaControl.GetAkcssStyles(target)[0]);
            var writes = 0;
            target.PropertyChanged += (_, args) =>
            {
                if (args.Property == Control.WidthProperty)
                {
                    writes++;
                }
            };
            for (var value = 42; value < 52; value++)
            {
                Publish(reload, "Publish", (double)value);
                Assert.Equal(value, target.Width);
            }
            Assert.Equal(10, writes);
            using (AkburaControl.BeginAkcssUpdate(target))
            {
                Publish(reload, "Publish", 61d);
                Publish(reload, "Publish", 62d);
                Publish(reload, "Publish", 63d);
                Assert.Equal(51, target.Width);
            }
            Assert.Equal(63, target.Width);
            Assert.Equal(11, writes);
            Assert.Equal(subscriptions, Count(reload, "Subscriptions"));
            Update(source, reload, "h-25");
            Assert.Equal(73, target.Width);
            Publish(reload, "PublishStale", 999d);
            Assert.Equal(73, target.Width);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task FiftyDeltas_ReorderRemoveAndRetainLiveOwnedAndReactiveCandidates() =>
        Scenario(nameof(FiftyDeltas_ReorderRemoveAndRetainLiveOwnedAndReactiveCandidates),
            "${Owned}:w-12 ${Owned}:w-13 h-${Stream}", (source, reload, owner, target, window) =>
        {
            var height = AkburaControl.GetAkcssStyles(target)[2];
            var subscriptions = Count(reload, "Subscriptions");
            var retired = new List<WeakReference>();
            long allocated = 0;
            long ticks = 0;
            var collections = GC.CollectionCount(0);
            for (var index = 0; index < 50; index++)
            {
                var previousSlot = index == 0 ? 0 : (index - 1) % 2;
                retired.Add(new WeakReference(AkburaControl.GetAkcssStyles(target)[previousSlot]));
                var value = index + 20;
                var declarations = index % 2 == 0
                    ? "${Owned}:w-" + value + " ${Owned}:w-13 h-${Stream}"
                    : "${Owned}:w-13 ${Owned}:w-" + value + " h-${Stream}";
                var updated = source.Update(Component(declarations));
                reload.ApplyUpdate(updated);
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                reload.DispatchMetadataUpdateHandlers();
                if (index >= 5)
                {
                    ticks += Stopwatch.GetTimestamp() - start;
                    allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                }
                Assert.Same(target, owner.Child);
                Assert.Equal(index % 2 == 0 ? 13 : value, target.Width);
                Assert.Equal(3, AkburaControl.GetAkcssStyles(target).Length);
                Assert.Same(height, AkburaControl.GetAkcssStyles(target)[2]);
                Assert.Equal(subscriptions, Count(reload, "Subscriptions"));
                Assert.Equal(1, Count(reload, "Subscribers"));
            }
            output.WriteLine($"CLR handler only, 45 measured of 50 deltas: {allocated / 45} B/update; " +
                $"{ticks * 1_000_000d / Stopwatch.Frequency / 45:F2} us/update; Gen0 delta (includes compilation)={GC.CollectionCount(0) - collections}");
            Update(source, reload, "h-${Stream}");
            Assert.True(double.IsNaN(target.Width));
            Update(source, reload, "${Owned}:w-31");
            Assert.Equal(0, Count(reload, "Subscribers"));
            Publish(reload, "PublishStale", 999d);
            Assert.True(double.IsNaN(target.Height));
            Assert.Equal(31, target.Width);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.True(retired.Count(reference => reference.IsAlive) <= 2,
                "Retired activators must not accumulate in the live runtime snapshot.");
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task ModifyOneUtility_RetainsNeighborsAndOnlyTouchesItsProperty() =>
        Scenario(nameof(ModifyOneUtility_RetainsNeighborsAndOnlyTouchesItsProperty), "w-12 h-24 pad-4", (source, reload, owner, target, window) =>
        {
            var old = AkburaControl.GetAkcssStyles(target);
            Assert.All(old, activator => Assert.All(((AkcssUtility)activator.Style).Operations,
                operation => Assert.True(operation.SupportsHotReloadReconciliation, operation.GetType().FullName)));
            Reset(reload);
            var oldDescriptor = DescriptorText(source.Compilation, "h");
            var updated = source.Update(Component("w-13 h-24 pad-4"));
            Assert.Equal(oldDescriptor, DescriptorText(updated, "h"));
            reload.ApplyUpdate(updated);
            reload.DispatchMetadataUpdateHandlers();
            var current = AkburaControl.GetAkcssStyles(target);
            Assert.Same(target, owner.Child);
            Assert.NotSame(old[0], current[0]);
            Assert.Same(old[1], current[1]);
            Assert.Same(old[2], current[2]);
            Assert.Equal(1, Count(reload, "Constructions"));
            Assert.Equal(13, target.Width);
            Assert.Equal(24, target.Height);
            Assert.Equal(new Thickness(4), target.Padding);
            AssertKeys(reload, "property:Width");
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task AddBeforeUtilities_ConstructsOnlyTheNewDeclaration() =>
        Scenario(nameof(AddBeforeUtilities_ConstructsOnlyTheNewDeclaration), "w-12 h-24", (source, reload, owner, target, window) =>
        {
            var old = AkburaControl.GetAkcssStyles(target);
            Reset(reload);
            Update(source, reload, "pad-4 w-12 h-24");
            var current = AkburaControl.GetAkcssStyles(target);
            Assert.Same(old[0], current[1]);
            Assert.Same(old[1], current[2]);
            Assert.Equal(1, Count(reload, "Constructions"));
            Assert.Equal(new Thickness(4), target.Padding);
            AssertKeys(reload, "property:Padding");
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task RemoveUtility_DetachesItsSourceAndRejectsDelayedCallbacks() =>
        Scenario(nameof(RemoveUtility_DetachesItsSourceAndRejectsDelayedCallbacks), "w-${Stream} h-24", (source, reload, owner, target, window) =>
        {
            var height = AkburaControl.GetAkcssStyles(target)[1];
            Reset(reload);
            Update(source, reload, "h-24");
            Assert.Same(height, Assert.Single(AkburaControl.GetAkcssStyles(target)));
            Assert.Equal(0, Count(reload, "Subscribers"));
            Assert.Equal(0, Count(reload, "Constructions"));
            Assert.True(double.IsNaN(target.Width));
            Publish(reload, "PublishStale", 500d);
            Assert.True(double.IsNaN(target.Width));
            Assert.Equal(24, target.Height);
            AssertKeys(reload, "property:Width");
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task ReorderConflicts_UpdatesPrecedenceAndRetainsUnchangedInstances() =>
        Scenario(nameof(ReorderConflicts_UpdatesPrecedenceAndRetainsUnchangedInstances), "w-12 h-24 w-13", (source, reload, owner, target, window) =>
        {
            var old = AkburaControl.GetAkcssStyles(target);
            Assert.Equal(13, target.Width);
            Reset(reload);
            Update(source, reload, "w-13 w-12 h-24");
            var current = AkburaControl.GetAkcssStyles(target);
            Assert.Same(old[2], current[0]);
            Assert.Same(old[0], current[1]);
            Assert.Same(old[1], current[2]);
            Assert.Equal(0, Count(reload, "Constructions"));
            Assert.Equal(12, target.Width);
            AssertKeys(reload, "property:Width");
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task ReactiveVariant_SurvivesAnUnrelatedUtilityEdit() =>
        Scenario(nameof(ReactiveVariant_SurvivesAnUnrelatedUtilityEdit), "w-12 ${Hover}:w-30 h-24", (source, reload, owner, target, window) =>
        {
            Publish(reload, "Hover", true);
            Assert.Equal(30, target.Width);
            var old = AkburaControl.GetAkcssStyles(target);
            var subscriptions = Count(reload, "HoverSubscriptions");
            Reset(reload);
            Update(source, reload, "w-12 ${Hover}:w-30 h-25");
            Assert.Same(old[0], AkburaControl.GetAkcssStyles(target)[0]);
            Assert.Same(old[1], AkburaControl.GetAkcssStyles(target)[1]);
            Assert.Equal(subscriptions, Count(reload, "HoverSubscriptions"));
            Assert.Equal(1, Count(reload, "HoverSubscribers"));
            Assert.Equal(1, Count(reload, "Constructions"));
            AssertKeys(reload, "property:Height");
            Publish(reload, "Hover", false);
            Assert.Equal(12, target.Width);
            Publish(reload, "Hover", true);
            Assert.Equal(30, target.Width);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task ModuleOnlyDelta_ReplacesOnlyTheAffectedDefinition() =>
        Scenario(nameof(ModuleOnlyDelta_ReplacesOnlyTheAffectedDefinition), "w-12 h-${Stream} pad-4", (source, reload, owner, target, window) =>
        {
            var old = AkburaControl.GetAkcssStyles(target);
            var subscriptions = Count(reload, "Subscriptions");
            Reset(reload);
            reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", Styles.Replace("Width: value;", "Width: value * 2;", StringComparison.Ordinal)));
            Assert.DoesNotContain("Demo.Counter", reload.UpdatedTypeNames);
            reload.DispatchMetadataUpdateHandlers();
            Assert.Same(target, owner.Child);
            Assert.NotSame(old[0], AkburaControl.GetAkcssStyles(target)[0]);
            Assert.Same(old[1], AkburaControl.GetAkcssStyles(target)[1]);
            Assert.Same(old[2], AkburaControl.GetAkcssStyles(target)[2]);
            Assert.Equal(subscriptions, Count(reload, "Subscriptions"));
            Assert.Equal(1, Count(reload, "Constructions"));
            Assert.Equal(24, target.Width);
            AssertKeys(reload, "property:Width");
            Publish(reload, "Publish", 51d);
            Assert.Equal(51, target.Height);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task MultiPropertyUtility_RecalculatesEveryAffectedKeyButNotPadding() =>
        Scenario(nameof(MultiPropertyUtility_RecalculatesEveryAffectedKeyButNotPadding), "pair-12 pad-4", (source, reload, owner, target, window) =>
        {
            var padding = AkburaControl.GetAkcssStyles(target)[1];
            Reset(reload);
            Update(source, reload, "pair-13 pad-4");
            Assert.Same(padding, AkburaControl.GetAkcssStyles(target)[1]);
            Assert.Equal(13, target.Width);
            Assert.Equal(26, target.Height);
            Assert.Equal(1, Count(reload, "Constructions"));
            AssertKeys(reload, "property:Width", "property:Height");
            Reset(reload);
            Update(source, reload, "pad-4");
            Assert.True(double.IsNaN(target.Width));
            Assert.True(double.IsNaN(target.Height));
            AssertKeys(reload, "property:Width", "property:Height");
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task RemoveWinningCandidate_ExposesTheOriginalShadowedWinner() =>
        Scenario(nameof(RemoveWinningCandidate_ExposesTheOriginalShadowedWinner), "w-12 ${Hover}:w-30 h-24", (source, reload, owner, target, window) =>
        {
            Publish(reload, "Hover", true);
            Assert.Equal(30, target.Width);
            var old = AkburaControl.GetAkcssStyles(target);
            Reset(reload);
            Update(source, reload, "w-12 h-24");
            Assert.Same(old[0], AkburaControl.GetAkcssStyles(target)[0]);
            Assert.Same(old[2], AkburaControl.GetAkcssStyles(target)[1]);
            Assert.Equal(12, target.Width);
            Assert.Equal(0, Count(reload, "HoverSubscribers"));
            Assert.Equal(0, Count(reload, "Constructions"));
            Publish(reload, "HoverStale", false);
            Assert.Equal(12, target.Width);
            AssertKeys(reload, "property:Width");
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task FailedReplacement_PreservesTheWorkingCascadeAndAllowsAValidDelta() =>
        Scenario(nameof(FailedReplacement_PreservesTheWorkingCascadeAndAllowsAValidDelta), "w-${Stream} h-24", (source, reload, owner, target, window) =>
        {
            var old = AkburaControl.GetAkcssStyles(target);
            var subscriptions = Count(reload, "Subscriptions");
            Reset(reload);
            reload.ApplyUpdate(source.Update(Component("w-${ThrowingStream} h-24")));
            Assert.Contains("incremental failure", Assert.Throws<TargetInvocationException>(reload.DispatchMetadataUpdateHandlers).ToString(), StringComparison.Ordinal);
            Assert.True(old == AkburaControl.GetAkcssStyles(target));
            Assert.Equal(subscriptions, Count(reload, "Subscriptions"));
            Assert.Equal(1, Count(reload, "Subscribers"));
            AssertKeys(reload, "property:Width");
            Publish(reload, "Publish", 19d);
            Assert.Equal(19, target.Width);
            Update(source, reload, "w-20 h-24");
            Assert.Equal(20, target.Width);
            Assert.Same(old[1], AkburaControl.GetAkcssStyles(target)[1]);
            Assert.Equal(0, Count(reload, "Subscribers"));
            Publish(reload, "PublishStale", 600d);
            Assert.Equal(20, target.Width);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task TwentyDeltas_KeepUnchangedReactiveActivatorsAndSubscriptions() =>
        Scenario(nameof(TwentyDeltas_KeepUnchangedReactiveActivatorsAndSubscriptions), "w-12 h-${Stream} pad-4", (source, reload, owner, target, window) =>
        {
            var height = AkburaControl.GetAkcssStyles(target)[1];
            var padding = AkburaControl.GetAkcssStyles(target)[2];
            var subscriptions = Count(reload, "Subscriptions");
            for (var index = 0; index < 20; index++)
            {
                Reset(reload);
                Update(source, reload, "w-" + (20 + index) + " h-${Stream} pad-4");
                Assert.Same(target, owner.Child);
                Assert.Same(height, AkburaControl.GetAkcssStyles(target)[1]);
                Assert.Same(padding, AkburaControl.GetAkcssStyles(target)[2]);
                Assert.Equal(3, AkburaControl.GetAkcssStyles(target).Length);
                Assert.Equal(1, Count(reload, "Subscribers"));
                Assert.Equal(subscriptions, Count(reload, "Subscriptions"));
                Assert.Equal(1, Count(reload, "Constructions"));
                AssertKeys(reload, "property:Width");
                Publish(reload, "Publish", (double)(100 + index));
                Publish(reload, "PublishStale", 800d);
                Assert.Equal(100 + index, target.Height);
            }
            Assert.Equal(20, reload.AppliedUpdates);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task MixedCascade_UsesTheDocumentedFullReplacementFallback() =>
        Scenario(nameof(MixedCascade_UsesTheDocumentedFullReplacementFallback), "class=\"card\" w-12 h-${Stream}", (source, reload, owner, target, window) =>
        {
            var old = AkburaControl.GetAkcssStyles(target);
            var subscriptions = Count(reload, "Subscriptions");
            Update(source, reload, "class=\"card\" w-13 h-${Stream}");
            Assert.Same(target, owner.Child);
            Assert.NotSame(old[0], AkburaControl.GetAkcssStyles(target)[0]);
            Assert.NotSame(old[1], AkburaControl.GetAkcssStyles(target)[1]);
            Assert.NotSame(old[2], AkburaControl.GetAkcssStyles(target)[2]);
            Assert.Equal(subscriptions + 1, Count(reload, "Subscriptions"));
            Assert.Equal(new CornerRadius(7), target.CornerRadius);
            Assert.Equal(13, target.Width);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task DetachedDelta_DoesNotSubscribeUntilTheLatestCascadeReattaches() =>
        Scenario(nameof(DetachedDelta_DoesNotSubscribeUntilTheLatestCascadeReattaches), "w-12 h-${Stream}", (source, reload, owner, target, window) =>
        {
            var subscriptions = Count(reload, "Subscriptions");
            window.Content = null;
            Assert.Equal(0, Count(reload, "Subscribers"));
            Update(source, reload, "w-13 h-${Stream}");
            Assert.Equal(0, Count(reload, "Subscribers"));
            Assert.Equal(subscriptions, Count(reload, "Subscriptions"));
            window.Content = owner;
            Assert.Same(target, owner.Child);
            Assert.Equal(13, target.Width);
            Assert.Equal(1, Count(reload, "Subscribers"));
            Assert.Equal(subscriptions + 1, Count(reload, "Subscriptions"));
            Publish(reload, "Publish", 26d);
            Publish(reload, "PublishStale", 700d);
            Assert.Equal(26, target.Height);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task DuplicatesAndModuleIndexInsertion_MatchDeterministicallyAndUseCurrentFactories() =>
        Scenario(nameof(DuplicatesAndModuleIndexInsertion_MatchDeterministicallyAndUseCurrentFactories), "w-12 w-12 h-${Stream}", (source, reload, owner, target, window) =>
        {
            var old = AkburaControl.GetAkcssStyles(target);
            Reset(reload);
            Update(source, reload, "pad-4 w-12 w-12 h-${Stream}");
            var current = AkburaControl.GetAkcssStyles(target);
            Assert.NotSame(old[0], current[1]);
            Assert.NotSame(old[1], current[2]);
            Assert.Same(old[2], current[3]);
            Assert.Equal(3, Count(reload, "Constructions"));
            Assert.Equal(1, Count(reload, "Subscribers"));
            Update(source, reload, "h-${Stream} w-12 pad-4");
            Assert.Same(old[2], AkburaControl.GetAkcssStyles(target)[0]);
            var inserted = Styles.Replace("@utilities {", "@utilities { Border.before-(double value) { MinWidth: value; }", StringComparison.Ordinal);
            reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", inserted));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Same(target, owner.Child);
            Assert.Equal(12, target.Width);
            Assert.Equal(new Thickness(4), target.Padding);
            Publish(reload, "Publish", 28d);
            Assert.Equal(28, target.Height);
        });

    private static string Component(string attributes) =>
        "using Avalonia.Controls; using Demo; using Demo.Styles.akcss; namespace Demo; <Border " + attributes + " />";

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task ModuleOnlyConflictKeyChange_ClearsTheOriginalPropertyUsingCapturedResetMetadata() =>
        Scenario(nameof(ModuleOnlyConflictKeyChange_ClearsTheOriginalPropertyUsingCapturedResetMetadata),
            "pair-12 pad-4 h-${Stream}", (source, reload, owner, target, window) =>
            {
                var old = AkburaControl.GetAkcssStyles(target);
                var subscriptions = Count(reload, "Subscriptions");
                Reset(reload);
                var changed = Styles.Replace("Width: value; Height: value * 2;",
                    "MinWidth: value; MinHeight: value * 2;", StringComparison.Ordinal);
                reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", changed));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Same(target, owner.Child);
                Assert.NotSame(old[0], AkburaControl.GetAkcssStyles(target)[0]);
                Assert.Same(old[1], AkburaControl.GetAkcssStyles(target)[1]);
                Assert.Same(old[2], AkburaControl.GetAkcssStyles(target)[2]);
                Assert.True(double.IsNaN(target.Width));
                Assert.Equal(12, target.MinWidth);
                Assert.Equal(24, target.MinHeight);
                Assert.Equal(12, target.Height);
                Assert.Equal(1, Count(reload, "Constructions"));
                Assert.Equal(subscriptions, Count(reload, "Subscriptions"));
                AssertKeys(reload, "property:Width", "property:Height", "property:MinWidth", "property:MinHeight");
                Publish(reload, "Publish", 30d);
                Assert.Equal(30, target.Height);
            });

    private static string DescriptorText(Microsoft.CodeAnalysis.Compilation compilation, string family) =>
        compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantNodes())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>()
            .First(node => node.Type.ToString().EndsWith("AkcssActivatorDescriptor", StringComparison.Ordinal) &&
                node.ArgumentList!.Arguments[1].ToString() == "\"" + family + "\"").ToString();

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task NeighborInsertion_RetainsFactoriesWhenStructuralLocalIdsMove()
    {
        const string attributes = "x.Name=\"styled\" w-12 h-${Stream}";
        return Scenario(nameof(NeighborInsertion_RetainsFactoriesWhenStructuralLocalIdsMove), attributes,
            (source, reload, owner, target, window) =>
            {
                var root = Assert.IsType<StackPanel>(owner.Child);
                var old = AkburaControl.GetAkcssStyles(target);
                var updated = Component(attributes).Replace("<Border ",
                    "<StackPanel><TextBlock Text=\"before\" /><Border ", StringComparison.Ordinal) + "</StackPanel>";
                reload.ApplyUpdate(source.Update(updated));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Same(root, owner.Child);
                Assert.Same(target, root.Children[1]);
                Assert.Same(old[0], AkburaControl.GetAkcssStyles(target)[0]);
                var currentSubscriptions = Count(reload, "Subscriptions");
                window.Content = null;
                window.Content = owner;
                Assert.Equal(currentSubscriptions + 1, Count(reload, "Subscriptions"));
                Assert.Equal(1, Count(reload, "Subscribers"));
                Publish(reload, "Publish", 29d);
                Assert.Equal(12, target.Width);
                Assert.Equal(29, target.Height);
            }, wrap: true);
    }

    private static void Update(AkburaCompilationSession source, ClrHotReloadSession reload, string attributes)
    {
        reload.ApplyUpdate(source.Update(Component(attributes)));
        reload.DispatchMetadataUpdateHandlers();
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task OwnedPropertyMigration_PreservesExternalBindingAndReleasesOriginalProperty() =>
        Scenario(nameof(OwnedPropertyMigration_PreservesExternalBindingAndReleasesOriginalProperty), "${Owned}:w-12 h-${Stream}", (source, reload, owner, target, window) =>
        {
            using var external = target.Bind(Control.WidthProperty, new Avalonia.Data.Binding(nameof(Border.Height)) { Source = target });
            Publish(reload, "Publish", 73d);
            Assert.Equal(12, target.Width);
            var old = AkburaControl.GetAkcssStyles(target);
            var updated = Styles.Replace("Width: value;", "MinWidth: value;", StringComparison.Ordinal);
            reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", updated));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Same(target, owner.Child);
            Assert.Same(old[1], AkburaControl.GetAkcssStyles(target)[1]);
            Assert.Equal(73, target.Width);
            Assert.Equal(12, target.MinWidth);
            Publish(reload, "Publish", 81d);
            Assert.Equal(81, target.Width);
            Update(source, reload, "h-${Stream}");
            Assert.False(target.IsSet(Control.MinWidthProperty));
            Publish(reload, "Publish", 92d);
            Assert.Equal(92, target.Width);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task OwnedMultiPropertyFailure_RestoresCapturedContributionsAndAllowsRetry() =>
        Scenario(nameof(OwnedMultiPropertyFailure_RestoresCapturedContributionsAndAllowsRetry), "${Owned}:pair-12 pad-4", (source, reload, owner, target, window) =>
        {
            var old = AkburaControl.GetAkcssStyles(target);
            var failing = Styles.Replace("Width: value; Height: value * 2;", "Width: value + 1; Height: Demo.Probe.FailValue();", StringComparison.Ordinal);
            reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", failing));
            Assert.ThrowsAny<Exception>(() => reload.DispatchMetadataUpdateHandlers());
            Assert.Same(target, owner.Child);
            Assert.True(old == AkburaControl.GetAkcssStyles(target));
            Assert.Equal(12, target.Width);
            Assert.Equal(24, target.Height);
            Assert.Equal(new Thickness(4), target.Padding);
            reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", Styles));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Equal(12, target.Width);
            Assert.Equal(24, target.Height);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task OwnedTwentyDeltas_RetainOtherContributionsAndRestoreExternalValueOnRemoval() =>
        Scenario(nameof(OwnedTwentyDeltas_RetainOtherContributionsAndRestoreExternalValueOnRemoval), "${Owned}:w-12 ${Owned}:round-7 h-${Stream}", (source, reload, owner, target, window) =>
        {
            target.Width = 73;
            var old = AkburaControl.GetAkcssStyles(target);
            var subscriptions = Count(reload, "Subscriptions");
            for (var index = 0; index < 20; index++)
            {
                Update(source, reload, "${Owned}:w-" + (index + 20) + " ${Owned}:round-7 h-${Stream}");
                Assert.Equal(index + 20, target.Width);
                Assert.Same(old[1], AkburaControl.GetAkcssStyles(target)[1]);
                Assert.Same(old[2], AkburaControl.GetAkcssStyles(target)[2]);
                Assert.Equal(subscriptions, Count(reload, "Subscriptions"));
            }
            Update(source, reload, "h-${Stream}");
            Assert.Equal(73, target.Width);
            Assert.False(target.IsSet(Border.CornerRadiusProperty));
            Publish(reload, "PublishStale", 999d);
            Assert.Equal(73, target.Width);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task OwnedDetachedUpdate_ActivatesOnlyTheLatestCascadeOnReattachment() =>
        Scenario(nameof(OwnedDetachedUpdate_ActivatesOnlyTheLatestCascadeOnReattachment), "${Owned}:w-12 h-${Stream}", (source, reload, owner, target, window) =>
        {
            target.Width = 73;
            window.Content = null;
            Assert.Equal(73, target.Width);
            Update(source, reload, "${Owned}:w-25 h-${Stream}");
            Update(source, reload, "${Owned}:w-31 h-${Stream}");
            Assert.Equal(0, Count(reload, "Subscribers"));
            Assert.Equal(73, target.Width);
            window.Content = owner;
            Assert.Equal(31, target.Width);
            Assert.Equal(1, Count(reload, "Subscribers"));
            Update(source, reload, "h-${Stream}");
            Assert.Equal(73, target.Width);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task OwnedDynamicResource_ReplacementRollbackAndRemovalPreserveLiveSources() =>
        Scenario(nameof(OwnedDynamicResource_ReplacementRollbackAndRemovalPreserveLiveSources), "${Owned}:resource-3 pad-4", (source, reload, owner, target, window) =>
        {
            target.Resources["size"] = 4d;
            Assert.Equal(12, target.Width);
            Update(source, reload, "${Owned}:resource-4 pad-4");
            Assert.Equal(16, target.Width);
            var old = AkburaControl.GetAkcssStyles(target);
            var failed = Styles.Replace("Width: Amx.DynamicResource<double>(\"size\") * value;",
                "Width: Amx.DynamicResource<double>(\"size\") * value; Height: Demo.Probe.FailValue();", StringComparison.Ordinal);
            reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", failed));
            Assert.ThrowsAny<Exception>(() => reload.DispatchMetadataUpdateHandlers());
            Assert.True(old == AkburaControl.GetAkcssStyles(target));
            Assert.Equal(16, target.Width);
            target.Resources["size"] = 5d;
            Assert.Equal(20, target.Width);
            reload.ApplyUpdate(source.UpdateAkcss("Styles.akcss", Styles));
            reload.DispatchMetadataUpdateHandlers();
            Update(source, reload, "pad-4");
            Assert.False(target.IsSet(Control.WidthProperty));
            target.Resources["size"] = 9d;
            Assert.False(target.IsSet(Control.WidthProperty));
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task OwnedPaddingEdit_RetainsUnrelatedBackgroundAndCornerRadiusContributions() =>
        Scenario(nameof(OwnedPaddingEdit_RetainsUnrelatedBackgroundAndCornerRadiusContributions), "${Owned}:bg-1 ${Owned}:round-7 ${Owned}:pad-4 h-${Stream}", (source, reload, owner, target, window) =>
        {
            var old = AkburaControl.GetAkcssStyles(target);
            var background = target.Background;
            var subscriptions = Count(reload, "Subscriptions");
            Reset(reload);
            Update(source, reload, "${Owned}:bg-1 ${Owned}:round-7 ${Owned}:pad-6 h-${Stream}");
            var current = AkburaControl.GetAkcssStyles(target);
            Assert.Same(old[0], current[0]);
            Assert.Same(old[1], current[1]);
            Assert.Same(old[3], current[3]);
            Assert.Same(background, target.Background);
            Assert.Equal(new CornerRadius(7), target.CornerRadius);
            Assert.Equal(new Thickness(6), target.Padding);
            Assert.Equal(subscriptions, Count(reload, "Subscriptions"));
            Assert.Equal(1, Count(reload, "Constructions"));
            AssertKeys(reload, "property:Padding");
        });

    private static int Count(ClrHotReloadSession reload, string name) =>
        Assert.IsType<int>(reload.Assembly.GetType("Demo.Probe")!.GetProperty(name)!.GetValue(null));

    private static void Publish(ClrHotReloadSession reload, string method, object value) =>
        reload.Assembly.GetType("Demo.Probe")!.GetMethod(method)!.Invoke(null, [value]);

    private static void Reset(ClrHotReloadSession reload) =>
        reload.Assembly.GetType("Demo.Probe")!.GetMethod("Reset")!.Invoke(null, null);

    private static void AssertKeys(ClrHotReloadSession reload, params string[] expected)
    {
        var keys = Assert.IsType<string[]>(reload.Assembly.GetType("Demo.Probe")!.GetProperty("Keys")!.GetValue(null));
        Assert.Equal(expected.Order(StringComparer.Ordinal), keys.Order(StringComparer.Ordinal));
    }

    private async Task Scenario(string name, string attributes,
        Action<AkburaCompilationSession, ClrHotReloadSession, AkburaControl, Border, Window> action, bool wrap = false)
    {
        var testName = typeof(AkcssIncrementalHotReloadTests).FullName + "." + name;
        if (!HotReloadChildProcess.IsChild(testName))
        {
            var childOutput = await HotReloadChildProcess.RunAsync(testName,
                detailedOutput: name == nameof(FiftyDeltas_ReorderRemoveAndRetainLiveOwnedAndReactiveCandidates));
            if (name == nameof(FiftyDeltas_ReorderRemoveAndRetainLiveOwnedAndReactiveCandidates))
            {
                output.WriteLine(childOutput);
            }
            return;
        }
        var component = Component(attributes);
        if (wrap)
        {
            component = component.Replace("<Border ", "<StackPanel><Border ", StringComparison.Ordinal) + "</StackPanel>";
        }
        var source = new AkburaCompilationSession(component, OwnerSource,
            new Dictionary<string, string> { ["Styles.akcss"] = Styles }, instrumentAkcss: true);
        using var reload = new ClrHotReloadSession(source.Compilation, "Demo.Counter", includeAkcssTypes: true);
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AkcssIncrementalHotReloadTests).Assembly).Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var target = wrap ? Assert.IsType<StackPanel>(owner.Child).Children[0] : owner.Child;
                action(source, reload, owner, Assert.IsType<Border>(target), window);
            }
            finally
            {
                window.Close();
            }
            Assert.Equal(0, Count(reload, "Subscribers"));
            Assert.Equal(0, Count(reload, "HoverSubscribers"));
        }, CancellationToken.None);
    }

    private const string Styles =
        "@using Akbura; @using Avalonia; @using Avalonia.Controls; Border.card { CornerRadius: new CornerRadius(7); } @utilities { " +
        "Border.w-(double value) { Width: value; } Border.h-(double value) { Height: value; } " +
        "Border.pad-(double value) { Padding: new Thickness(value); } Border.round-(double value) { CornerRadius: new CornerRadius(value); } " +
        "Border.pair-(double value) { Width: value; Height: value * 2; } " +
        "Border.resource-(double value) { Width: Amx.DynamicResource<double>(\"size\") * value; } " +
        "Border.bg-(double value) { Background: Avalonia.Media.Brushes.Red; } }";

    private const string OwnerSource =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Akbura;
        using Akbura.Engine;
        namespace Demo;
        public partial class Counter : AkburaControl
        {
            public Counter() : base(AkburaEngine.Empty) { }
        }
        public sealed class StreamExtension
        {
            public IObservable<double> ProvideValue(IServiceProvider services) => Probe.Source;
        }
        public sealed class HoverExtension
        {
            public IObservable<bool> ProvideValue(IServiceProvider services) => Probe.HoverSource;
        }
        public sealed class ThrowingStreamExtension
        {
            public IObservable<double> ProvideValue(IServiceProvider services) => throw new InvalidOperationException("incremental failure");
        }
        [Akbura.Markup.UtilityBindingPriority(Priority = Avalonia.Data.BindingPriority.Animation)]
        public sealed class OwnedExtension
        {
            public bool ProvideValue(IServiceProvider services) => true;
        }
        public static class Probe
        {
            private static readonly HashSet<string> _keys = new(StringComparer.Ordinal);
            public static Signal<double> Source { get; } = new(12);
            public static Signal<bool> HoverSource { get; } = new(false);
            public static int Constructions { get; set; }
            public static int Resets { get; private set; }
            public static int Executions { get; private set; }
            public static string[] Keys => _keys.ToArray();
            public static int Subscribers => Source.Subscribers;
            public static int Subscriptions => Source.Subscriptions;
            public static int HoverSubscribers => HoverSource.Subscribers;
            public static int HoverSubscriptions => HoverSource.Subscriptions;
            public static void Record(string key, string action)
            {
                _keys.Add(key);
                if (action == "reset")
                {
                    Resets++;
                }
                else
                {
                    Executions++;
                }
            }
            public static void Reset()
            {
                _keys.Clear();
                Constructions = Resets = Executions = 0;
            }
            public static void Publish(double value) => Source.Publish(value);
            public static void PublishStale(double value) => Source.PublishStale(value);
            public static void Hover(bool value) => HoverSource.Publish(value);
            public static void HoverStale(bool value) => HoverSource.PublishStale(value);
            public static double FailValue() => throw new InvalidOperationException("property application failed");
        }
        public sealed class Signal<T>(T initial) : IObservable<T>
        {
            private readonly List<IObserver<T>> _current = new();
            private readonly List<IObserver<T>> _retired = new();
            private T _value = initial;
            public int Subscribers => _current.Count;
            public int Subscriptions { get; private set; }
            public IDisposable Subscribe(IObserver<T> observer)
            {
                _current.Add(observer);
                Subscriptions++;
                observer.OnNext(_value);
                return new Subscription(() =>
                {
                    _current.Remove(observer);
                    _retired.Add(observer);
                });
            }
            public void Publish(T value)
            {
                _value = value;
                foreach (var observer in _current.ToArray())
                {
                    observer.OnNext(value);
                }
            }
            public void PublishStale(T value)
            {
                foreach (var observer in _retired.ToArray())
                {
                    observer.OnNext(value);
                }
            }
            private sealed class Subscription(Action release) : IDisposable
            {
                private Action? _release = release;
                public void Dispose() => System.Threading.Interlocked.Exchange(ref _release, null)?.Invoke();
            }
        }
        """;
}
