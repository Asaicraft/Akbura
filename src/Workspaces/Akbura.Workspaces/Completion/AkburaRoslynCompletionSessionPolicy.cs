namespace Akbura.Workspaces.Completion;

internal sealed class AkburaRoslynCompletionSessionPolicy
{
    private readonly object _gate = new();
    private int _snapshotVersion = -1;
    private AkburaCSharpCompletionContextKind _contextKind;
    private Akbura.Language.Syntax.SyntaxKind _ownerKind;
    private int _ownerStart = -1;
    private int _hostStart = -1;
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
                context.Kind != _contextKind ||
                context.OwnerKind != _ownerKind ||
                context.OwnerSpan.Start != _ownerStart ||
                context.HostSpan.Start != _hostStart)
            {
                _allowNonTrigger = false;
                _requestPending = false;
            }

            var allowNonTrigger = _allowNonTrigger || _requestPending;
            _snapshotVersion = snapshotVersion;
            _contextKind = context.Kind;
            _ownerKind = context.OwnerKind;
            _ownerStart = context.OwnerSpan.Start;
            _hostStart = context.HostSpan.Start;
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
                context.Kind != _contextKind ||
                context.OwnerKind != _ownerKind ||
                context.OwnerSpan.Start != _ownerStart ||
                context.HostSpan.Start != _hostStart)
            {
                return;
            }

            _allowNonTrigger = value;
            _requestPending = false;
        }
    }
}
