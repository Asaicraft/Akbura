namespace Akbura.Workspaces.Completion;

internal enum AkburaCSharpCompletionRejectionReason
{
    None = 0,
    ContextKindChanged,
    LogicalSlotChanged,
    OwnerKindChanged,
    OwnerChanged,
    HostSpanChanged,
    TextOutsideHostSpanChanged,
    OwnerSpanTranslationFailed,
    HostSpanTranslationFailed,
    NoCurrentCSharpContext,
}
