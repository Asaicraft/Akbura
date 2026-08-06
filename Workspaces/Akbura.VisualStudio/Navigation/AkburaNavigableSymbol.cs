using Akbura.Workspaces;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using System.Diagnostics;

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

    public AkburaNavigableSymbol(
        SnapshotSpan symbolSpan,
        AkburaDefinition definition,
        IServiceProvider serviceProvider)
    {
        SymbolSpan = symbolSpan;

        _definition =
            definition ??
            throw new ArgumentNullException(
                nameof(definition));

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
        if (!ReferenceEquals(
                relationship,
                PredefinedNavigableRelationships
                    .Definition))
        {
            return;
        }

        _ = ThreadHelper.JoinableTaskFactory
            .RunAsync(NavigateAsync);
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
            Debug.WriteLine(
                $"[Akbura] Navigation failed: " +
                $"{exception}");
        }
    }

    private void NavigateCore()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var filePath =
            _definition.TargetFilePath;

        if (!File.Exists(filePath))
        {
            Debug.WriteLine(
                $"[Akbura] Definition file was not found: " +
                $"'{filePath}'.");

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
    }
}