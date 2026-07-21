using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Akbura.Akcss;

internal static class AkcssRuntime
{
    private static readonly ConditionalWeakTable<Control, TargetRuntime> s_targets = new();

    public static void SetStyles(
        Control target,
        ImmutableArray<AkcssStyleActivator> styles)
    {
        ArgumentNullException.ThrowIfNull(target);

        s_targets.GetValue(target, static control => new TargetRuntime(control))
            .SetStyles(styles.IsDefault ? [] : styles);
    }

    public static void Refresh(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);

        s_targets.GetValue(target, static control => new TargetRuntime(control))
            .RequestApply();
    }

    private sealed class TargetRuntime : IObserver<object?>
    {
        private readonly Control _target;
        private readonly List<IDisposable> _subscriptions = [];
        private ImmutableArray<AkcssStyleActivator> _styles = [];
        private bool _isApplying;
        private bool _isChanging;
        private bool _isDetached;
        private bool _applyPending;

        public TargetRuntime(Control target)
        {
            _target = target;
            target.AttachedToVisualTree += OnAttachedToVisualTree;
            target.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        public void SetStyles(ImmutableArray<AkcssStyleActivator> styles)
        {
            _isChanging = true;
            _applyPending = false;
            try
            {
                DisposeSubscriptions();
                ResetStyles();
                _styles = styles;

                if (!_isDetached)
                {
                    Subscribe();
                }
            }
            catch
            {
                DisposeSubscriptions();
                throw;
            }
            finally
            {
                _isChanging = false;
            }

            if (!_isDetached)
            {
                RequestApply();
            }
        }

        public void RequestApply()
        {
            _applyPending = true;
            if (_isApplying || _isChanging || _isDetached)
            {
                return;
            }

            while (_applyPending && !_isDetached)
            {
                _applyPending = false;
                _isApplying = true;
                try
                {
                    ResetStyles();
                    ExecuteStyles();
                }
                finally
                {
                    _isApplying = false;
                }
            }
        }

        public void OnNext(object? value)
        {
            RequestApply();
        }

        public void OnError(Exception error)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        public void OnCompleted()
        {
        }

        private void Subscribe()
        {
            foreach (var style in _styles)
            {
                var signal = style.Watch(_target) ?? throw new InvalidOperationException(
                    $"AKCSS style '{style.Style.Name}' returned a null Watch signal.");
                _subscriptions.Add(signal.Subscribe(this));
            }
        }

        private void ResetStyles()
        {
            for (var index = _styles.Length - 1; index >= 0; index--)
            {
                _styles[index].Reset(_target);
            }
        }

        private void ExecuteStyles()
        {
            foreach (var style in _styles)
            {
                if (style.Condition)
                {
                    style.Execute(_target);
                }
            }
        }

        private void DisposeSubscriptions()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }

        private void OnAttachedToVisualTree(
            object? sender,
            VisualTreeAttachmentEventArgs args)
        {
            if (!_isDetached)
            {
                return;
            }

            _isDetached = false;
            _isChanging = true;
            try
            {
                Subscribe();
            }
            catch
            {
                DisposeSubscriptions();
                throw;
            }
            finally
            {
                _isChanging = false;
            }

            RequestApply();
        }

        private void OnDetachedFromVisualTree(
            object? sender,
            VisualTreeAttachmentEventArgs args)
        {
            _isDetached = true;
            _applyPending = false;
            DisposeSubscriptions();
        }
    }
}
