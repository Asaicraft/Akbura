using Akbura.Diagnostics;
using Akbura.VisualStudio.Editor;

namespace Akbura.VisualStudio.Diagnostics;

internal static class AkburaDiagnosticPresentation
{
    public static bool ShouldPublish(
        AkburaTextBufferContext bufferContext,
        AkburaClassifiedBufferState state)
    {
        var publisher = state is AkburaParsedBufferState parsedState
            ? parsedState.Project.Context.DiagnosticPublisher
            : bufferContext.TryGetLatestDocumentContext(out var context, out _)
                ? context.Project.Context.DiagnosticPublisher
                : AkburaDiagnosticPublisher.Auto;

        return AkburaDiagnosticPublicationPolicy.ShouldPublishWorkspace(publisher);
    }
}
