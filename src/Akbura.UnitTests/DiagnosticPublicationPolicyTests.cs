using Akbura.Diagnostics;

namespace Akbura.UnitTests;

public sealed class DiagnosticPublicationPolicyTests
{
    [Theory]
    [InlineData(AkburaDiagnosticPublisher.Auto, false, false, true)]
    [InlineData(AkburaDiagnosticPublisher.Auto, false, true, true)]
    [InlineData(AkburaDiagnosticPublisher.Auto, true, false, true)]
    [InlineData(AkburaDiagnosticPublisher.Auto, true, true, false)]
    [InlineData(AkburaDiagnosticPublisher.Generator, false, true, true)]
    [InlineData(AkburaDiagnosticPublisher.Generator, true, true, true)]
    [InlineData(AkburaDiagnosticPublisher.Workspace, false, false, true)]
    [InlineData(AkburaDiagnosticPublisher.Workspace, false, true, true)]
    [InlineData(AkburaDiagnosticPublisher.Workspace, true, false, false)]
    [InlineData(AkburaDiagnosticPublisher.Workspace, true, true, false)]
    [InlineData(AkburaDiagnosticPublisher.Both, false, true, true)]
    [InlineData(AkburaDiagnosticPublisher.Both, true, true, true)]
    [InlineData(AkburaDiagnosticPublisher.None, false, false, false)]
    [InlineData(AkburaDiagnosticPublisher.None, true, true, false)]
    public void GeneratorOwnership_DistinguishesBuildFromLiveEditor(
        AkburaDiagnosticPublisher publisher,
        bool designTime,
        bool workspaceActive,
        bool expected)
    {
        Assert.Equal(expected, AkburaDiagnosticPublicationPolicy.ShouldPublishGenerator(publisher, designTime, workspaceActive));
    }

    [Theory]
    [InlineData(AkburaDiagnosticPublisher.Auto, true)]
    [InlineData(AkburaDiagnosticPublisher.Generator, false)]
    [InlineData(AkburaDiagnosticPublisher.Workspace, true)]
    [InlineData(AkburaDiagnosticPublisher.Both, true)]
    [InlineData(AkburaDiagnosticPublisher.None, false)]
    public void WorkspaceOwnership_AffectsPresentationOnly(AkburaDiagnosticPublisher publisher, bool expected)
    {
        Assert.Equal(expected, AkburaDiagnosticPublicationPolicy.ShouldPublishWorkspace(publisher));
    }
}
