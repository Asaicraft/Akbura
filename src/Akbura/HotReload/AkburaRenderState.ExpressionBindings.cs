using Akbura.ComponentTree;
using Avalonia;

namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    public void ReconcileExpressionBinding<T>(int localId, string slot, string identity,
        Func<ExpressionBindingContext, T> evaluate, Action<T> write,
        AvaloniaObject? target = null, AvaloniaProperty? property = null, Func<Action>? captureBaseline = null,
        Func<ExpressionBinding<T>, IDisposable>? ownLifetime = null, bool preserveUnchanged = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentNullException.ThrowIfNull(write);
        if (_pendingRevision == null)
        {
            var node = GetNode(localId);
            if (node.TryGetAppliedOperation(slot, out var applied) &&
                applied.Resource is ExpressionBindingOperation<T> current)
            {
                current.Refresh(evaluate, write);
                return;
            }

            // Update-only assignments first run after the initial structural
            // revision has committed, just like dynamic event closures.
            var restore = CaptureExpressionBindingBaseline(target, property, captureBaseline);
            var resource = new ExpressionBindingOperation<T>(CreateExpressionBinding(ownLifetime), evaluate, write, restore);
            var previous = node.ReplaceDynamicOwnedOperation(slot, identity, resource);
            try
            {
                previous?.Resource?.Release();
                resource.Apply();
            }
            catch (Exception error)
            {
                List<Exception>? cleanupErrors = null;
                try { resource.Release(); }
                catch (Exception cleanupError) { (cleanupErrors ??= []).Add(cleanupError); }
                try { resource.Binding.Dispose(); }
                catch (Exception cleanupError) { (cleanupErrors ??= []).Add(cleanupError); }
                node.RestoreDynamicOwnedOperation(slot, previous);
                try { previous?.Resource?.Apply(); }
                catch (Exception cleanupError) { (cleanupErrors ??= []).Add(cleanupError); }
                if (cleanupErrors != null)
                {
                    cleanupErrors.Insert(0, error);
                    throw new AggregateException("Expression binding initialization and cleanup failed.", cleanupErrors);
                }
                throw;
            }
            return;
        }
        var pending = GetMutablePendingRevision();
        try
        {
            ValidateLocalId(localId, pending.Nodes.Length);
            var node = pending.Nodes[localId];
            if (preserveUnchanged)
            {
                if (!node.RegisterOwnedOperation(slot, identity)) return;
            }
            else node.RegisterOwnedOperationForReplacement(slot, identity);
            var key = CreateSlotKey(pending, localId, slot);
            ReleasePreviousProperty(pending, key);
            var applied = FindAppliedOperation(key)?.Resource;
            var previous = applied as ExpressionBindingOperation<T>;
            if (previous == null && applied != null) ReleasePreviousOwnedOperation(pending, key);
            var restore = previous?.Restore ?? CaptureExpressionBindingBaseline(target, property, captureBaseline);
            var replacement = new ExpressionBindingOperation<T>(previous?.Binding ?? CreateExpressionBinding(ownLifetime), evaluate, write, restore);
            node.SetOwnedOperationResource(slot, replacement);
            pending.TrackMutation(new ExpressionBindingMutation<T>(previous, replacement));
            // Transfer the existing coordinator without tearing down unchanged
            // subscriptions. The old callbacks live only in the rollback record.
            previous?.SetAppliedState(false);
            replacement.Apply();
        }
        catch (Exception error)
        {
            AbortPendingRevisionAfterFailure(pending, error);
            throw;
        }
    }

    private static ExpressionBinding<T> CreateExpressionBinding<T>(Func<ExpressionBinding<T>, IDisposable>? ownLifetime)
    {
        var binding = new ExpressionBinding<T>();
        try
        {
            if (ownLifetime != null) binding.SetLifetime(ownLifetime(binding));
            return binding;
        }
        catch
        {
            binding.Dispose();
            throw;
        }
    }

    private static Action? CaptureExpressionBindingBaseline(AvaloniaObject? target,
        AvaloniaProperty? property, Func<Action>? captureBaseline)
    {
        if (target == null || property == null) return captureBaseline?.Invoke();
        var baseline = CaptureAvaloniaPropertyBaseline(target, property);
        return () => baseline.Restore(target, property);
    }

    private sealed class ExpressionBindingOperation<T>(ExpressionBinding<T> binding,
        Func<ExpressionBindingContext, T> evaluate, Action<T> write, Action? restore) : RenderOperationResource, IDisposable
    {
        private Func<ExpressionBindingContext, T>? _evaluate = evaluate;
        private Action<T>? _write = write;
        public ExpressionBinding<T> Binding { get; } = binding;
        public Action? Restore { get; } = restore;
        public void Refresh(Func<ExpressionBindingContext, T> evaluate, Action<T> write)
        {
            Binding.Update(evaluate, write);
            _evaluate = evaluate;
            _write = write;
        }
        public (Func<ExpressionBindingContext, T> Evaluate, Action<T> Write) RetireCallbacks()
        {
            var callbacks = (_evaluate!, _write!);
            _evaluate = null;
            _write = null;
            return callbacks;
        }
        public void RestoreCallbacks((Func<ExpressionBindingContext, T> Evaluate, Action<T> Write) callbacks)
        {
            _evaluate = callbacks.Evaluate;
            _write = callbacks.Write;
        }
        protected override void ApplyCore() => Binding.Update(_evaluate!, _write!);
        protected override void ReleaseCore()
        {
            try { Binding.SuspendOwnership(); }
            finally { Restore?.Invoke(); }
        }
        public override void CompleteRelease()
        {
            try { Binding.Dispose(); }
            finally { RetireCallbacks(); }
        }
        public void Dispose()
        {
            try { Release(); }
            finally { CompleteRelease(); }
        }
    }

    private sealed class ExpressionBindingMutation<T>(ExpressionBindingOperation<T>? previous,
        ExpressionBindingOperation<T> replacement) : RenderRevisionMutation
    {
        // Applied nodes retain their previous operation metadata. Keep obsolete
        // closures only in this transient rollback record, never on that node.
        private readonly (Func<ExpressionBindingContext, T> Evaluate, Action<T> Write)? _previousCallbacks =
            previous?.RetireCallbacks();

        public override void Rollback()
        {
            if (previous == null)
            {
                try { replacement.Release(); }
                finally { replacement.Binding.Dispose(); }
            }
            else
            {
                replacement.SetAppliedState(false);
                previous.RestoreCallbacks(_previousCallbacks!.Value);
                previous.Apply();
            }
        }
    }
}
