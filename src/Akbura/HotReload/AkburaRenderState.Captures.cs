namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    private const string ConditionalRenderCapturePrefix = "conditional/";
    private const string LocalRenderCapturePrefix = "render/local/";
    private Dictionary<string, object>? _renderCaptures;
    private AkburaRenderState? _renderCaptureParent;

    /// <summary>Connects an instance-local scope to its enclosing capture scope.</summary>
    public void SetRenderCaptureParent(AkburaRenderState parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (ReferenceEquals(_renderCaptureParent, parent))
        {
            return;
        }

        for (AkburaRenderState? ancestor = parent; ancestor != null; ancestor = ancestor._renderCaptureParent)
        {
            if (ReferenceEquals(ancestor, this))
            {
                throw new ArgumentException("Render capture scopes cannot form a cycle.", nameof(parent));
            }
        }

        _renderCaptureParent = parent;
    }

    /// <summary>Publishes a typed render local for retained template instances.</summary>
    public void SetRenderCapture<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_pendingRevision is { IsActivation: false } sourceRevision &&
            IsGeneratedRenderCaptureKey(key))
        {
            (sourceRevision.PublishedGeneratedCaptureKeys ??= new(StringComparer.Ordinal)).Add(key);
        }

        var captures = _renderCaptures ??= new(StringComparer.Ordinal);
        if (captures.TryGetValue(key, out var existing) && existing is RenderCapture<T> capture)
        {
            if (_pendingRevision != null)
            {
                _pendingRevision.TrackMutation(new RenderCaptureValueMutation<T>(capture, capture.Value));
            }

            capture.Value = value;
        }
        else
        {
            if (_pendingRevision != null)
            {
                _pendingRevision.TrackMutation(new RenderCaptureEntryMutation(captures, key, existing));
            }

            captures[key] = new RenderCapture<T>(value);
        }
    }

    /// <summary>Releases a capture owned by an inactive branch.</summary>
    public void RemoveRenderCapture(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_renderCaptures == null || !_renderCaptures.TryGetValue(key, out var baseline))
        {
            return;
        }

        _pendingRevision?.TrackMutation(new RenderCaptureEntryMutation(_renderCaptures, key, baseline));
        _renderCaptures.Remove(key);
    }

    private void CompleteConditionalRenderCaptureRevision(PendingRevision revision)
    {
        if (revision.IsActivation || _renderCaptures == null)
        {
            return;
        }

        List<string>? removed = null;
        foreach (var key in _renderCaptures.Keys)
        {
            if (IsGeneratedRenderCaptureKey(key) &&
                revision.PublishedGeneratedCaptureKeys?.Contains(key) != true)
            {
                (removed ??= []).Add(key);
            }
        }

        if (removed != null)
        {
            foreach (var key in removed)
            {
                _renderCaptures.Remove(key);
            }
        }
    }

    private static bool IsGeneratedRenderCaptureKey(string key) =>
        key.StartsWith(ConditionalRenderCapturePrefix, StringComparison.Ordinal) ||
        key.StartsWith(LocalRenderCapturePrefix, StringComparison.Ordinal);

    private sealed partial class PendingRevision
    {
        public HashSet<string>? PublishedGeneratedCaptureKeys { get; set; }
    }

    /// <summary>Reads the current parent's value rather than a factory's stale closure.</summary>
    public T GetRenderCapture<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        for (AkburaRenderState? scope = this; scope != null; scope = scope._renderCaptureParent)
        {
            if (scope._renderCaptures != null && scope._renderCaptures.TryGetValue(key, out var value))
            {
                if (value is RenderCapture<T> capture)
                {
                    return capture.Value;
                }

                break;
            }
        }

        throw new InvalidOperationException($"Render capture '{key}' has not been initialized with the expected type.");
    }

    private sealed class RenderCapture<T>(T value)
    {
        public T Value = value;
    }

    private sealed class RenderCaptureValueMutation<T>(RenderCapture<T> capture, T baseline) : RenderRevisionMutation
    {
        public override void Rollback() => capture.Value = baseline;
    }

    private sealed class RenderCaptureEntryMutation(Dictionary<string, object> captures, string key,
        object? baseline) : RenderRevisionMutation
    {
        public override void Rollback()
        {
            if (baseline == null)
            {
                captures.Remove(key);
            }
            else
            {
                captures[key] = baseline;
            }
        }
    }
}
