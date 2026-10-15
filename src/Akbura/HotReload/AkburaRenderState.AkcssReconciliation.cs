using Akbura.Akcss;
using System.Collections.Immutable;

namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    private sealed class ReconciledAkcssStylesOperation : AkcssStylesOperation
    {
        private ImmutableArray<AkcssActivatorDescriptor> _descriptors;
        private Func<int, AkcssStyleActivator>? _factory;
        private AkcssRuntime.Reconciliation? _transition;
        private bool _releasePending;

        public AkcssRuntime.Reconciliation? Transition => _transition;

        public ReconciledAkcssStylesOperation(object target,
            ImmutableArray<AkcssActivatorDescriptor> descriptors, Func<int, AkcssStyleActivator> factory,
            AkcssStylesOperation? previous) : base(target, [], previous)
        {
            _descriptors = descriptors;
            _factory = factory;
        }

        protected override void ApplyCore()
        {
            if (_releasePending)
            {
                _transition!.Rollback();
                _transition = null;
                _releasePending = false;
                return;
            }
            if (_factory == null)
            {
                base.ApplyCore();
                AkcssRuntime.RestoreDescriptors(_target, _styles, _descriptors);
                return;
            }
            _transition = AkcssRuntime.Reconcile(_target, _descriptors, _factory);
            _styles = _transition.Styles;
            _descriptors = _transition.Descriptors;
            _factory = null;
        }

        public override void StageRelease()
        {
            if (!_baselineStyles.IsEmpty)
            {
                base.StageRelease();
                return;
            }
            SetAppliedState(false);
            try
            {
                _transition = AkcssRuntime.Reconcile(_target, [], static _ => throw new InvalidOperationException(), _baselineWasSet);
                _releasePending = true;
            }
            catch
            {
                SetAppliedState(true);
                throw;
            }
        }

        public override void CommitRelease()
        {
            if (_releasePending)
            {
                _transition!.PrepareCommit();
            }
        }

        public override void CompleteRelease()
        {
            if (_releasePending)
            {
                _transition!.Complete();
                _transition = null;
                _releasePending = false;
            }
        }

        public void CommitReconciliation()
        {
            _transition?.PrepareCommit();
        }

        public void CompleteReconciliation()
        {
            _transition?.Complete();
            _transition = null;
        }

        public void RollbackReconciliation()
        {
            try
            {
                _transition?.Rollback();
            }
            finally
            {
                _transition = null;
                SetAppliedState(false);
            }
        }
    }

    private sealed class AkcssReconciliationMutation(RenderOperationResource? previous, ReconciledAkcssStylesOperation replacement) : RenderRevisionMutation
    {
        // The mutation owns the rollback snapshot until the whole render revision
        // commits. The installed operation drops it after success, avoiding chains.
        private AkcssRuntime.Reconciliation? _transition;

        public override void Commit()
        {
            _transition = replacement.Transition;
            replacement.CommitReconciliation();
            previous?.SetAppliedState(false);
        }

        public override void Rollback()
        {
            try
            {
                _transition?.Rollback();
            }
            finally
            {
                try
                {
                    replacement.RollbackReconciliation();
                }
                finally
                {
                    previous?.SetAppliedState(true);
                }
            }
        }

        public override void CompleteCommit() => replacement.CompleteReconciliation();
    }
}
