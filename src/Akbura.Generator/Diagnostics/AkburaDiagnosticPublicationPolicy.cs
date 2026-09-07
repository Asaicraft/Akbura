namespace Akbura.Diagnostics;

public enum AkburaDiagnosticPublisher
{
    Auto,
    Generator,
    Workspace,
    Both,
    None,
}

/// <summary>Ownership of live diagnostics is independent from build diagnostics.</summary>
public static class AkburaDiagnosticPublicationPolicy
{
    public static bool ShouldPublishGenerator(
        AkburaDiagnosticPublisher publisher,
        bool isDesignTimeBuild,
        bool workspaceDiagnosticsActive)
    {
        if (publisher == AkburaDiagnosticPublisher.None)
        {
            return false;
        }

        if (!isDesignTimeBuild)
        {
            return true;
        }

        return publisher switch
        {
            AkburaDiagnosticPublisher.Workspace => false,
            AkburaDiagnosticPublisher.Auto => !workspaceDiagnosticsActive,
            _ => true,
        };
    }

    public static bool ShouldPublishWorkspace(AkburaDiagnosticPublisher publisher)
    {
        return publisher is AkburaDiagnosticPublisher.Auto or
            AkburaDiagnosticPublisher.Workspace or AkburaDiagnosticPublisher.Both;
    }
}
