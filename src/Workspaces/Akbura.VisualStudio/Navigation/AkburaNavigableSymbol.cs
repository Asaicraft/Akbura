using Akbura.Workspaces;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using System.Text;

using VsTextSpan =
    Microsoft.VisualStudio.TextManager.Interop.TextSpan;

namespace Akbura.VisualStudio.Navigation;

internal sealed class AkburaNavigableSymbol :
    INavigableSymbol
{
    private static readonly
        INavigableRelationship[]
        DefinitionRelationships =
        [
            PredefinedNavigableRelationships
                .Definition,
        ];

    private readonly AkburaDefinition
        _definition;

    private readonly IServiceProvider
        _serviceProvider;

    private readonly AkburaVisualStudioWorkspace
        _workspaceHost;

    public AkburaNavigableSymbol(
        SnapshotSpan symbolSpan,
        AkburaDefinition definition,
        AkburaVisualStudioWorkspace workspaceHost,
        IServiceProvider serviceProvider)
    {
        SymbolSpan = symbolSpan;

        _definition =
            definition ??
            throw new ArgumentNullException(
                nameof(definition));

        _workspaceHost =
            workspaceHost ??
            throw new ArgumentNullException(
                nameof(workspaceHost));

        _serviceProvider =
            serviceProvider ??
            throw new ArgumentNullException(
                nameof(serviceProvider));
    }

    public SnapshotSpan SymbolSpan { get; }

    public IEnumerable<INavigableRelationship>
        Relationships =>
        DefinitionRelationships;

    public void Navigate(
        INavigableRelationship relationship)
    {
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Navigation,
            $"Navigate requested: " +
            $"relationship='{relationship.GetType().FullName}'.");

        if (!ReferenceEquals(
                relationship,
                PredefinedNavigableRelationships
                    .Definition))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Navigation,
                "Navigate ignored: unsupported relationship.");
            return;
        }

        ThreadHelper.JoinableTaskFactory
            .Run(NavigateAsync);
    }

    private async Task NavigateAsync()
    {
        try
        {
            await ThreadHelper
                .JoinableTaskFactory
                .SwitchToMainThreadAsync();

            NavigateCore();
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Navigation,
                "Navigation failed.",
                exception);
        }
    }

    private void NavigateCore()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var hasPhysicalProjectSource =
            _workspaceHost.TryResolveProjectSource(
                _definition,
                out var projectSourcePath);

        var filePath = hasPhysicalProjectSource
            ? projectSourcePath
            : _definition.TargetFilePath;

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Navigation,
            $"Navigation target selected: " +
            $"path='{filePath}', " +
            $"physicalProjectSource={hasPhysicalProjectSource}, " +
            $"materialize={!hasPhysicalProjectSource && _definition.TargetText != null}.");

        MaterializeTargetSource(
            filePath,
            hasPhysicalProjectSource
                ? null
                : _definition.TargetText);

        if (!File.Exists(filePath))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Navigation,
                $"Navigate failed: target file '{filePath}' " +
                $"does not exist.");

            return;
        }

        VsShellUtilities.OpenDocument(
            _serviceProvider,
            filePath,
            VSConstants.LOGVIEWID_TextView,
            out _,
            out _,
            out var windowFrame,
            out var textView);

        ErrorHandler.ThrowOnFailure(
            windowFrame.Show());

        var start =
            _definition.TargetLineSpan.Start;

        var end =
            _definition.TargetLineSpan.End;

        ErrorHandler.ThrowOnFailure(
            textView.SetSelection(
                start.Line,
                start.Character,
                end.Line,
                end.Character));

        var visibleSpan =
            new VsTextSpan
            {
                iStartLine =
                    start.Line,

                iStartIndex =
                    start.Character,

                iEndLine =
                    end.Line,

                iEndIndex =
                    end.Character,
            };

        ErrorHandler.ThrowOnFailure(
            textView.EnsureSpanVisible(
                visibleSpan));

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Navigation,
            $"Navigate completed: " +
            $"path='{filePath}', " +
            $"selection={start.Line}:{start.Character}.." +
            $"{end.Line}:{end.Character}.");
    }

    private static void MaterializeTargetSource(
        string filePath,
        Microsoft.CodeAnalysis.Text.SourceText? text)
    {
        if (text == null)
        {
            return;
        }

        var content = text.ToString();
        if (File.Exists(filePath) &&
            string.Equals(
                File.ReadAllText(filePath),
                content,
                StringComparison.Ordinal))
        {
            return;
        }

        var directory =
            Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            filePath,
            content,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true));
    }
}
