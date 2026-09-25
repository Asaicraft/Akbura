namespace Akbura.Workspaces.Completion;

internal sealed class AkburaRoslynCompletionSessionPolicy
{
    internal static bool ShouldPublishSupplementalBeforeRoslyn(AkburaCompletionContextKind syntacticKind, bool hasSupplementalItems, bool roslynCompleted)
    {
        return syntacticKind ==
                AkburaCompletionContextKind.DeclarationModifier &&
            hasSupplementalItems &&
            !roslynCompleted;
    }

    private readonly object _gate = new();
    private int _snapshotVersion = -1;
    private AkburaCSharpCompletionContext _context;
    private bool _hasContext;
    private bool _allowNonTrigger;
    private bool _requestPending;

    public bool BeginRequest(
        int snapshotVersion,
        AkburaCSharpCompletionContext context)
    {
        lock (_gate)
        {
            if (snapshotVersion < _snapshotVersion ||
                snapshotVersion > _snapshotVersion + 1 ||
                !_hasContext ||
                !AkburaCSharpCompletionContextFacts
                    .HasSameLogicalSlot(_context, context))
            {
                _allowNonTrigger = false;
                _requestPending = false;
            }

            var allowNonTrigger = _allowNonTrigger || _requestPending;
            _snapshotVersion = snapshotVersion;
            _context = context;
            _hasContext = true;
            _requestPending = true;
            return allowNonTrigger;
        }
    }

    public void SetAllowNonTrigger(
        int snapshotVersion,
        AkburaCSharpCompletionContext context,
        bool value)
    {
        lock (_gate)
        {
            if (snapshotVersion != _snapshotVersion ||
                !_hasContext ||
                !AkburaCSharpCompletionContextFacts
                    .HasSameLogicalSlot(_context, context))
            {
                return;
            }

            _allowNonTrigger = value;
            _requestPending = false;
        }
    }
}
