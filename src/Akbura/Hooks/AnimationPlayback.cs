using Avalonia;
using Avalonia.Animation;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Akbura.Hooks;

internal static class AnimationPlayback
{
    internal static HashSet<AvaloniaProperty> GetProperties(Animation animation)
    {
        if (animation.IterationCount == IterationCount.Infinite)
        {
            throw new NotSupportedException("RunAsync hooks support only finite animations.");
        }

        var properties = new HashSet<AvaloniaProperty>();
        foreach (var frame in animation.Children)
        {
            foreach (var setter in frame.Setters)
            {
                if (setter is not Setter propertySetter)
                {
                    throw new NotSupportedException("Animation hooks support keyframes with Avalonia setters.");
                }

                var property = propertySetter.Property ?? throw new InvalidOperationException(
                    "An animation setter must specify a property.");
                properties.Add(property);
            }
        }

        return properties;
    }

    internal static async Task RunAsync(
        Animatable target,
        Animation animation,
        CancellationToken cancellationToken,
        Func<Animation, Animatable, CancellationToken, Task>? runner = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        GetProperties(animation);
        using var cancellation = new CancellationTokenSource();
        var detached = false;
        var disposed = false;
        void Cancel()
        {
            if (!disposed)
            {
                cancellation.Cancel();
            }
        }

        void CancelOnDispatcher()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Cancel();
            }
            else
            {
                Dispatcher.UIThread.Post(Cancel);
            }
        }

        void OnVisualDetached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            detached = true;
            Cancel();
        }

        void OnLogicalDetached(object? sender, LogicalTreeAttachmentEventArgs args)
        {
            detached = true;
            Cancel();
        }

        var visual = target as Visual;
        var styled = target as StyledElement;
        if (visual is not null)
        {
            visual.DetachedFromVisualTree += OnVisualDetached;
        }

        if (styled is not null)
        {
            styled.DetachedFromLogicalTree += OnLogicalDetached;
        }

        using var registration = cancellationToken.Register(CancelOnDispatcher);
        try
        {
            var task = runner is null
                ? animation.RunAsync(target, cancellation.Token)
                : runner(animation, target, cancellation.Token);
            if (task is null)
            {
                throw new InvalidOperationException("The animation runner returned null.");
            }

            await task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            cancellation.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (detached && !cancellationToken.IsCancellationRequested)
        {
            throw new AnimationTargetDetachedException(cancellation.Token);
        }
        finally
        {
            void Dispose()
            {
                disposed = true;
                if (visual is not null)
                {
                    visual.DetachedFromVisualTree -= OnVisualDetached;
                }

                if (styled is not null)
                {
                    styled.DetachedFromLogicalTree -= OnLogicalDetached;
                }
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Dispose();
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(Dispose);
            }
        }
    }
}

internal sealed class AnimationTargetDetachedException(CancellationToken cancellationToken)
    : OperationCanceledException("The animation target was detached.", cancellationToken);
