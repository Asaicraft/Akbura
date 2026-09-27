namespace Akbura.Workspaces.AutomaticPairing;

internal sealed class AkburaMarkupAutoCloseSessionLifecycle
{
    private bool _isPending;
    private bool _isTypeCharCommandInProgress;
    private bool _isMarkupContextArmed;
    private bool _isFinished;
    private bool _isConsumed;
    private bool _isCancelled;

    public bool IsPending => _isPending;

    public bool IsTypeCharCommandInProgress =>
        _isTypeCharCommandInProgress;

    public bool IsMarkupContextArmed => _isMarkupContextArmed;

    public bool TryBeginTypeCharCommand(bool isAtGeneratedClosingAngle)
    {
        if (!isAtGeneratedClosingAngle ||
            _isConsumed ||
            _isCancelled ||
            _isFinished && !_isMarkupContextArmed)
        {
            return false;
        }

        _isTypeCharCommandInProgress = true;
        _isMarkupContextArmed = false;
        return true;
    }

    public bool TryPublishPending(bool successfulAngleOvertype)
    {
        if (!successfulAngleOvertype ||
            _isFinished ||
            _isConsumed ||
            _isCancelled)
        {
            return false;
        }

        _isPending = true;
        return true;
    }

    public bool Finish(bool preserveMarkupContext = false)
    {
        _isFinished = true;
        if (_isPending || _isTypeCharCommandInProgress)
        {
            return true;
        }

        if (_isConsumed)
        {
            return false;
        }

        if (preserveMarkupContext)
        {
            _isMarkupContextArmed = true;
            return true;
        }

        _isCancelled = true;
        return false;
    }

    public bool TryConsume()
    {
        if (_isCancelled || _isConsumed)
        {
            return false;
        }

        _isConsumed = true;
        _isPending = false;
        _isTypeCharCommandInProgress = false;
        _isMarkupContextArmed = false;
        return true;
    }

    public void Cancel()
    {
        _isCancelled = true;
        _isPending = false;
        _isTypeCharCommandInProgress = false;
        _isMarkupContextArmed = false;
    }
}
