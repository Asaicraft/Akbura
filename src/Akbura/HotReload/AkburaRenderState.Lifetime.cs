namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    /// <summary>Releases generated ownership; a later render may start a fresh session.</summary>
    public void Dispose()
    {
        List<Exception>? failures = null;
        var pending = _pendingRevision;
        _pendingRevision = null;
        if (pending != null)
        {
            try
            {
                pending.Abort(throwOnFailure: true);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        ReleaseAllLocalScopeLifetimes(ref failures);
        ReleaseConditionalNameScopes();

        foreach (var node in _nodes)
        {
            foreach (var operation in node.GetAppliedOperations().Values)
            {
                try
                {
                    operation.Resource?.Release();
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
        }

        foreach (var property in _properties.Values)
        {
            try
            {
                property.RestoreBaseline();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        foreach (var collection in _collections.Values)
        {
            try
            {
                collection.RemoveOwnedItemsAndCreateEmptyState();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        _nodes = [];
        _properties.Clear();
        _collections.Clear();
        _conditionalDefinitions = [];
        _conditionalRegions = [];
        _nodeFactory = null;
        _renderCaptures?.Clear();
        _renderCaptureParent = null;
        _revision = null;
        if (failures != null)
        {
            throw new AggregateException("Generated render ownership could not be fully released.", failures);
        }
    }
}
