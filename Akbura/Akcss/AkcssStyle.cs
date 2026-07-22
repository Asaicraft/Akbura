using Akbura.CompilerAnotations;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Akbura.Akcss;

public abstract class AkcssStyle
{
    private readonly ConditionalWeakTable<object, SubscriptionCollection> _subscriptions = new();

    public string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NameCore))
            {
                return NameCore;
            }

            var type = this.GetType();
            var attributes = type.GetCustomAttributes(typeof(StyleNameAttribute), true);
            if (attributes.Length > 0)
            {
                var styleNameAttribute = (StyleNameAttribute)attributes[0];
                return (NameCore = styleNameAttribute.Name)!;
            }

            return (NameCore = type.Name)!;
        }
    }

    protected string? NameCore { get; set; }

    public bool IsInlined
    {
        get
        {
            if (IsInlinedCore.HasValue)
            {
                return IsInlinedCore.Value;
            }

            var type = this.GetType();
            var attributes = type.GetCustomAttributes(typeof(InlinedStyleAttribute), true);

            return (IsInlinedCore = attributes.Length > 0).Value;
        }
    }

    protected bool? IsInlinedCore { get; set; }

    /// <summary>
    /// Removes values previously written by this style.
    /// </summary>
    /// <remarks>
    /// Generated styles override this method and clear their values in reverse cascade order.
    /// Hand-written interceptors only need to override it when they retain values between updates.
    /// </remarks>
    public virtual void Reset(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        SubscriptionCollection? subscriptions;
        lock (_subscriptions)
        {
            if (!_subscriptions.TryGetValue(target, out subscriptions))
            {
                return;
            }

            _subscriptions.Remove(target);
        }

        subscriptions.Dispose();
    }

    /// <summary>
    /// Retains a generated style subscription until the style is reset for the target.
    /// </summary>
    protected void TrackSubscription(object target, IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(subscription);

        lock (_subscriptions)
        {
            _subscriptions.GetValue(
                target,
                static _ => new SubscriptionCollection()).Add(subscription);
        }
    }

    /// <summary>
    /// Returns a signal that this style should be applied again.
    /// </summary>
    /// <remarks>
    /// The default implementation observes properties declared with
    /// <see cref="ObservesPropertyAttribute"/>. Emitted values are signals only.
    /// </remarks>
    public virtual IObservable<object?> Watch(object target)
    {
        return AkcssObservedPropertySignal.Create(GetType(), target);
    }

    private sealed class SubscriptionCollection : IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];

        public void Add(IDisposable subscription)
        {
            _subscriptions.Add(subscription);
        }

        public void Dispose()
        {
            for (var index = _subscriptions.Count - 1; index >= 0; index--)
            {
                _subscriptions[index].Dispose();
            }

            _subscriptions.Clear();
        }
    }
}