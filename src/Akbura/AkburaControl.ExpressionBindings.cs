using Akbura.ComponentTree;
using Avalonia.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Akbura;

public abstract partial class AkburaControl
{
    // Per-owner ephemerons: a retired template target is not rooted by its
    // component merely because a binding once existed for that instance.
    private readonly ConditionalWeakTable<object, Dictionary<string, object>> _expressionBindings = new();

    [EditorBrowsable(EditorBrowsableState.Never)]
    protected void __AkburaBindExpression<T>(object target, string slot, Control lifetimeRoot,
        Func<ExpressionBindingContext, T> evaluate, Action<T> write)
    {
        var entries = _expressionBindings.GetOrCreateValue(target);
        var created = false;
        if (!entries.TryGetValue(slot, out var entry))
        {
            var binding = new ExpressionBinding<T>();
            var scope = (LocalRenderScope)__AkburaRegisterLocalRenderScope(lifetimeRoot, binding.Resume, binding.Suspend);
            binding.SetLifetime(scope);
            entry = new DirectExpressionBinding<T>(binding, scope);
            entries.Add(slot, entry);
            created = true;
        }
        if (entry is not DirectExpressionBinding<T> current)
            throw new InvalidOperationException("An expression binding changed type without replacing its owner.");
        current.Scope.MarkCurrentFrame();
        try { current.Binding.Update(evaluate, write); }
        catch (Exception error) when (created)
        {
            entries.Remove(slot);
            try { current.Binding.Dispose(); }
            catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
            throw;
        }
    }

    private sealed record DirectExpressionBinding<T>(ExpressionBinding<T> Binding, LocalRenderScope Scope);
}
